using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Transport;

namespace QuickMatch.Matchmaking;

// A player who dropped comes back into a quick-match run under way (the rules are in RejoinPolicy).
//
// Host:
// - While a player is absent, the run's Steam lobby is listed under the run's key, so the Rejoin search finds it.
// - The game's rejoin request of a returning QuickMatch player is held (they are told so) until the party's next room
//   starts. Then, in the room's sync, the host sends them what the game's answer lacks, lets the game answer (the run's
//   save and combat state; the game adds them to the connected players and tells the others), and passes on the room's
//   sync data that the other clients sent before the returning player could get their messages.
//
// Returning player (the client half, which the game does not have):
// - Says hello when the host says the run is under way, waits while held, and once answered builds the run as a load
//   does (a load lobby made from the answer, RunManager.SetUpSavedMultiplayer), with the counters every peer keeps in
//   step set to the host's, as the game's combat replay sets them. It then enters the room the party is entering
//   through the same sync, and takes part from there.
//
// The game's private members are reached by plain reflection; a test checks that each name in RejoinPolicy.GameMembers
// exists in the installed game.
internal static partial class RunRejoin
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static RunState? _state;
    private static NetHostGameService? _host;
    private static readonly HashSet<ulong> SaidHello = [];
    private static readonly List<ulong> Held = [];
    private static bool _releasing;
    private static bool _listed;

    private static bool IsHost => _host != null && _state != null;

    public static void OnRunLaunched(RunState state, bool quickMatch)
    {
        OnRunCleanUp();
        if (!quickMatch || RunManager.Instance.NetService is not NetHostGameService host)
        {
            return;
        }
        _state = state;
        _host = host;
        host.RegisterMessageHandler<QuickMatchRejoinHelloMessage>(OnHello);
        host.ClientDisconnected += OnClientDisconnected;
        // A run loaded with a player missing, or reopened after a drop, lists itself at once.
        Safely(UpdateListing, "list the run");
    }

    // Called before the run is torn down.
    public static void OnRunCleanUp()
    {
        if (_host != null)
        {
            _host.UnregisterMessageHandler<QuickMatchRejoinHelloMessage>(OnHello);
            _host.ClientDisconnected -= OnClientDisconnected;
        }
        _host = null;
        _state = null;
        SaidHello.Clear();
        Held.Clear();
        _releasing = false;
        _listed = false;
    }

    public static void OnPlayerDropped() => Safely(UpdateListing, "list the run");

    private static void OnHello(QuickMatchRejoinHelloMessage message, ulong sender)
    {
        if (IsHost)
        {
            SaidHello.Add(sender);
        }
    }

    private static void OnClientDisconnected(ulong player, NetErrorInfo info)
    {
        SaidHello.Remove(player);
        if (Held.Remove(player))
        {
            MainFile.Logger.Info($"held rejoin of {player} gone ({info.GetReason()})");
        }
    }

    // Returns false when QuickMatch answers it: held for the next room, or refused.
    public static bool OnRejoinRequest(RunLobby lobby, ulong sender)
    {
        if (_releasing || !IsHost || lobby != RunManager.Instance.RunLobby)
        {
            return true;
        }
        bool inRun = _state!.Players.Any(p => p.NetId == sender);
        bool refused = inRun && RunKickPolicy.HostRefusesRejoin(sender, BlockStore.IsBlocked, RunRecord.Current?.Stayed ?? [], RunRecord.Current?.Banned ?? []);
        switch (RejoinPolicy.OnRequest(inRun, SaidHello.Contains(sender), refused))
        {
            case RejoinPolicy.Answer.NotQuickMatch:
                MainFile.Logger.Info($"rejoin of {sender} refused: not a QuickMatch client");
                _host!.DisconnectClient(sender, NetError.RunInProgress);
                return false;
            case RejoinPolicy.Answer.Refused:
                MainFile.Logger.Info($"rejoin of {sender} refused: blocked, or voted out of the run");
                _host!.DisconnectClient(sender, NetError.InvalidJoin);
                return false;
            case RejoinPolicy.Answer.Hold:
                if (!Held.Contains(sender))
                {
                    Held.Add(sender);
                }
                MainFile.Logger.Info($"rejoin of {sender} held until the party's next room");
                _host!.SendMessage(new QuickMatchRejoinHeldMessage(), sender);
                return false;
            default:
                return true;
        }
    }

    // On the host as each room's sync starts, before StandIn tells everyone who is left out of the fight: lets the held
    // players in when this is a room entered from the map.
    public static void BeforeRoomSync()
    {
        if (!IsHost || RunManager.Instance.RunLobby is not { } lobby)
        {
            return;
        }
        Held.RemoveAll(id => !_host!.ConnectedPeers.Any(p => p.peerId == id));
        if (!RejoinPolicy.ReleasesAt(_state!.CurrentRoomCount, Held.Count))
        {
            return;
        }
        List<ulong> releasing = [.. Held];
        Held.Clear();
        Safely(() => Release(lobby, releasing), "let the returning players in");
        Safely(UpdateListing, "list the run");
    }

    private static void Release(RunLobby lobby, List<ulong> releasing)
    {
        RunManager run = RunManager.Instance;
        NetHostGameService host = _host!;
        List<ulong> fightAbsent = RejoinPolicy.AbsentAfter(_state!.Players.Select(p => p.NetId), lobby.PlayerIds, releasing);
        // The room's sync data the other clients sent before the returning players could get their messages.
        var sent = new List<(ulong Sender, SerializablePlayer Player)>();
        foreach (DictionaryEntry entry in (IDictionary)Field(run.CombatStateSynchronizer, "_syncData")!)
        {
            sent.Add(((ulong)entry.Key, (SerializablePlayer)entry.Value!));
        }
        MethodInfo answer = typeof(RunLobby).GetMethod("HandleClientRejoinRequestMessage", Members)
            ?? throw new MissingMethodException(nameof(RunLobby), "HandleClientRejoinRequestMessage");
        MethodInfo forward = (typeof(NetHostGameService).GetMethod("SendMessageToClientInternal", Members)
            ?? throw new MissingMethodException(nameof(NetHostGameService), "SendMessageToClientInternal")).MakeGenericMethod(typeof(SyncPlayerDataMessage));
        foreach (ulong player in releasing)
        {
            // Connected as the game will have it once it answers this player; a player let in after them in this loop
            // reaches them through the game's own "player rejoined" message.
            List<ulong> connected = RejoinPolicy.PlayersAfter(lobby.PlayerIds, [player]);
            var info = new QuickMatchRejoinInfoMessage
            {
                connected = connected,
                modded = connected.Select(id => lobby.Players.FirstOrDefault(p => p.id == id) is { id: not 0 } known
                    ? known.isModded
                    : host.GetVersionInfoForPeer(id)?.IsModded() ?? false).ToList(),
                fightAbsent = fightAbsent,
                nextActionId = run.ActionQueueSet.NextActionId,
                nextHookId = run.ActionQueueSynchronizer.NextHookId,
                nextChecksumId = run.ChecksumTracker.NextId,
                mapGenerationCount = run.MapSelectionSynchronizer.MapGenerationCount,
            };
            MainFile.Logger.Info($"letting {player} back into the run: {connected.Count} players connected, absent from the fight [{string.Join(",", fightAbsent)}], {sent.Count} sync messages passed on");
            host.SendMessage(info, player);
            _releasing = true;
            try
            {
                answer.Invoke(lobby, [default(ClientRejoinRequestMessage), player]);
            }
            finally
            {
                _releasing = false;
            }
            foreach ((ulong sender, SerializablePlayer data) in sent)
            {
                if (sender != player && sender != host.NetId)
                {
                    var message = new SyncPlayerDataMessage { player = data };
                    forward.Invoke(host, [message, player, message.Mode.ToChannelId(), (ulong?)sender]);
                }
            }
        }
    }

    private static void UpdateListing()
    {
        if (!IsHost || RunManager.Instance.RunLobby is not { } lobby || _host is not QuickMatchHostService service
            || service.LobbyId is not { } steamLobby || RunRecord.Current is not { } record)
        {
            return;
        }
        // A player voted out of the run is absent for good: the run is not listed for them.
        int absent = RejoinPolicy.Awaited(_state!.Players.Select(p => p.NetId), lobby.PlayerIds.ToHashSet(), record.Banned).Count;
        bool list = RejoinPolicy.ListsRun(absent);
        if (list == _listed)
        {
            return;
        }
        _listed = list;
        if (list)
        {
            QuickMatchLobby.PublishResume(steamLobby, record.Key);
            LobbyBackend.Current.SetJoinable(steamLobby, true);
            MainFile.Logger.Info($"run lobby {steamLobby} listed for {absent} absent player(s)");
        }
        else
        {
            QuickMatchLobby.UnpublishResume(steamLobby);
            LobbyBackend.Current.SetKind(steamLobby, LobbyKind.Private);
            MainFile.Logger.Info($"run lobby {steamLobby} private again: no one absent");
        }
    }

    private static object? Field(object instance, string name) =>
        (instance.GetType().GetField(name, Members) ?? throw new MissingFieldException(instance.GetType().Name, name)).GetValue(instance);

    private static void Safely(Action step, string what)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"could not {what}: {e}");
        }
    }
}
