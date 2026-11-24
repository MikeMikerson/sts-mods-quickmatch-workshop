using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Emotes;
using QuickMatch.Lobby;
using QuickMatch.Transport;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

// What the vote prompt shows: a running vote, or the result of the last one.
internal readonly record struct VoteView(ulong Target, int Yes, int Needed, double SecondsLeft, bool CanAnswer, bool? Passed, bool InRun = false);

// Two protections in a quick-match lobby, beside the block list's checks in the Matchmaker: the vote to remove a
// client (KickVote, over lobby chat so Steam vouches for each voter), and the host's join-and-leave record
// (JoinLeaveRecord, kept by SeenStore). In a quick-match run the same vote runs over the run's Steam lobby chat among
// the present players, the host can be voted out, and a passed vote bans the player from the run (RunKickPolicy).
// The same queue hands the Matchmaker its question whether to start short (StartShortWire), and in a run hands
// EmoteRoom the players' emotes (EmoteWire).
// Runs on the game's thread; lobby events may arrive on any thread and wait in a queue for the next frame.
internal static partial class LobbyGuard
{
    // A voter waits this long after a vote passes for the host to remove the player (its 3 s, then 5 s) before
    // leaving itself.
    private const double HostRemovesSeconds = 8;
    private const double ResultSeconds = 8;

    private static readonly KickVote Vote = new() { IsVoter = IsQuickMatchPeer };
    private static readonly ConcurrentQueue<(ulong Sender, byte[] Data)> Inbox = new();
    // As host: when each client joined this lobby, in Unix seconds.
    private static readonly Dictionary<ulong, long> JoinedAt = new();
    // As host: players whose leaving we caused (sent away, moved in a merge), or who asked us to send a blocked player
    // away.
    private static readonly HashSet<ulong> NotCounted = new();

    private static ulong _lobby;
    private static bool _inRun;
    private static SceneTree? _tree;
    private static int _epoch;
    private static bool _votedInLast;
    private static (ulong Target, bool Passed, double At)? _result;

    public static bool Active => _lobby != 0;

    public static bool IsGuest(ulong player) => Regroup.InQuickMatchRun
        ? RunRecord.Current?.GuestPlayers?.Contains(player) == true : Matchmaker.GuestPlayers.Contains(player);

    public static bool IsQuickMatchPeer(ulong player) => _inRun
        ? RunRecord.Current?.IsQuickMatchPlayer(player) == true && RunManager.Instance.RunLobby?.PlayerIds.Contains(player) == true
        : Matchmaker.IsQuickMatchPeer(player);

    public static void Begin(ulong steamLobbyId)
    {
        End();
        Start(steamLobbyId, inRun: false);
    }

    public static void BeginRun(INetGameService netService)
    {
        EndRun();
        if (LobbyLinks.ParseLobbyId(netService.GetRawLobbyIdentifier()) is not { } lobby)
        {
            MainFile.Logger.Info("run: no Steam lobby, so no vote to remove a player");
            return;
        }
        End();
        Start(lobby, inRun: true);
        MainFile.Logger.Info($"run: votes to remove a player go over lobby {lobby}");
    }

    public static void EndRun()
    {
        if (_inRun)
        {
            End();
        }
    }

    public static void EndLobby()
    {
        if (!_inRun)
        {
            End();
        }
    }

    private static void Start(ulong steamLobbyId, bool inRun)
    {
        _lobby = steamLobbyId;
        _inRun = inRun;
        Vote.HostCanBeRemoved = inRun;
        if (inRun)
        {
            EmoteRoom.NewRun();
        }
        _epoch++;
        LobbyBackend.Current.Changed += OnLobbyEvent;
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            _tree = tree;
            tree.ProcessFrame += Tick;
        }
        // Players already here when a client's session begins are not on our record: only a host keeps one.
        if (!inRun && IsHost())
        {
            long now = BlockStore.Now();
            foreach (ulong player in Members().Where(p => p != Self()))
            {
                JoinedAt[player] = now;
            }
        }
    }

    private static void End()
    {
        if (_lobby == 0)
        {
            return;
        }
        LobbyBackend.Current.Changed -= OnLobbyEvent;
        if (_tree != null)
        {
            _tree.ProcessFrame -= Tick;
            _tree = null;
        }
        _lobby = 0;
        _inRun = false;
        _epoch++;
        Vote.Reset();
        JoinedAt.Clear();
        NotCounted.Clear();
        _result = null;
        while (Inbox.TryDequeue(out _))
        {
        }
        KickVotePrompt.Hide();
    }

    public static void PlayerJoined(ulong player)
    {
        if (Active && !_inRun && IsHost() && player != Self() && player != 0)
        {
            JoinedAt[player] = BlockStore.Now();
        }
    }

    // A short stay that ends on their own counts on the join-and-leave record.
    public static void PlayerLeft(ulong player)
    {
        if (!Active || _inRun || !IsHost() || !JoinedAt.Remove(player, out long joinedAt))
        {
            return;
        }
        if (NotCounted.Contains(player) || Matchmaker.State == MatchState.Merging)
        {
            return;
        }
        SeenStore.NoteLeave(player, joinedAt);
    }

    public static void NoteHostCaused(ulong player) => NotCounted.Add(player);

    // This member asked us to send a blocked player away, so they may leave over that block.
    public static void NoteAskedUs(ulong player) => NotCounted.Add(player);

    public static void NoteMovingEveryone()
    {
        foreach (ulong player in Members())
        {
            NotCounted.Add(player);
        }
    }

    public static bool Refuses(ulong joiner) => joiner != 0 && SeenStore.Refuses(joiner);

    private static string Where() => _inRun ? $"run lobby {_lobby}" : $"lobby {_lobby}";

    private static double Clock() => Time.GetTicksMsec() / 1000.0;

    private static INetGameService? Service() => _inRun ? RunManager.Instance.NetService : Matchmaker.Lobby?.NetService;

    private static ulong Self() => Service()?.NetId ?? 0;

    private static bool IsHost() => Service()?.Type == NetGameType.Host;

    private static ulong HostId()
    {
        if (IsHost())
        {
            return Self();
        }
        if (_inRun)
        {
            // The game's own idea of whom we are connected to; Steam's lobby owner can lag a migration.
            return Service() is NetClientGameService { IsConnected: true } client ? client.HostNetId : 0;
        }
        return LobbyBackend.Current.Owner(_lobby);
    }

    // In a run, the players present in it: the game's run lobby, the same on every peer.
    private static IReadOnlyCollection<ulong> Members()
    {
        if (_inRun)
        {
            if (RunManager.Instance.RunLobby is not { } run)
            {
                return new HashSet<ulong>();
            }
            HashSet<ulong> present = run.PlayerIds.Where(id => id != 0).ToHashSet();
            if (Self() is var self and not 0)
            {
                present.Add(self);
            }
            return present;
        }
        return Matchmaker.Lobby?.Players.Select(p => p.id).Where(id => id != 0).ToHashSet() ?? new HashSet<ulong>();
    }
}
