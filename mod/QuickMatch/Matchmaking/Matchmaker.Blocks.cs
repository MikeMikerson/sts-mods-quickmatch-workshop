using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    private const double ExpelGraceSeconds = 3;
    private const double HostAnswerSeconds = 5;

    public static bool RefusesJoin(StartRunLobby lobby, ulong joiner)
    {
        if (Lobby == null || lobby != Lobby)
        {
            return false;
        }
        if (BlockPolicy.HostRefuses(joiner, BlockStore.IsBlocked, RefusedHere))
        {
            return true;
        }
        if (LobbyGuard.Refuses(joiner))
        {
            MainFile.Logger.Info($"lobby {SteamLobbyId}: refused a player who joined and left too often");
            return true;
        }
        return false;
    }

    public static bool RefusesRejoin(SerializableRun run, ulong joiner) =>
        RunRecord.Current is { } record && RunRecord.KeyOf(run) == record.Key && RunKickPolicy.HostRefusesRejoin(joiner, BlockStore.IsBlocked, record.Stayed, record.Banned);

    private static void CheckBlocked()
    {
        if (Lobby == null || _cts == null || _merging)
        {
            return;
        }
        ulong self = Lobby.NetService.NetId;
        ulong host = Lobby.NetService.Type == NetGameType.Host ? self : LobbyBackend.Current.Owner(SteamLobbyId);
        foreach (ulong player in Lobby.Players.Select(p => p.id).ToList())
        {
            if (player == self || !BlockStore.IsBlocked(player))
            {
                continue;
            }
            switch (BlockPolicy.InLobby(player, self, host))
            {
                case BlockAction.Expel:
                    Expel(player, "blocked by the host");
                    break;
                case BlockAction.Leave:
                    LeaveBlockedLobby("the host is blocked");
                    return;
                case BlockAction.AskHost:
                    if (!AskedHost.Add(player))
                    {
                        break;
                    }
                    MainFile.Logger.Info($"lobby {SteamLobbyId}: a blocked player is here; asking the host to send them away");
                    Lobby.NetService.SendMessage(new QuickMatchBlockedHereMessage { player = player });
                    _ = LeaveIfStillHereAsync(player, _generation, Lobby);
                    break;
            }
        }
    }

    internal static void Expel(ulong player, string reason, byte why = LeaveReasons.GroupGone)
    {
        if (Lobby?.NetService is not INetHostGameService host || !RefusedHere.Add(player))
        {
            return;
        }
        LobbyGuard.NoteHostCaused(player);
        MainFile.Logger.Info($"lobby {SteamLobbyId}: sending a player away ({reason})");
        host.SendMessage(new QuickMatchLeaveMessage { reason = why }, player);
        _ = DisconnectIfStillHereAsync(host, player, _generation, Lobby);
    }

    private static async Task DisconnectIfStillHereAsync(INetHostGameService host, ulong player, int gen, StartRunLobby lobby)
    {
        try
        {
            await GodotDelay.Seconds(ExpelGraceSeconds, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (gen == _generation && Lobby == lobby && GameVersions.ConnectedPeers(host).Any(p => p.peerId == player))
        {
            MainFile.Logger.Info($"lobby {SteamLobbyId}: the player sent away stayed; disconnecting them");
            host.DisconnectClient(player, NetError.Kicked);
        }
    }

    private static async Task LeaveIfStillHereAsync(ulong player, int gen, StartRunLobby lobby)
    {
        try
        {
            await GodotDelay.Seconds(HostAnswerSeconds, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (gen == _generation && Lobby == lobby && lobby.Players.Any(p => p.id == player))
        {
            LeaveBlockedLobby("the host did not send a blocked player away");
        }
    }

    private static void OnBlockedHereMessage(QuickMatchBlockedHereMessage message, ulong senderId)
    {
        if (Lobby == null || Lobby.NetService.Type != NetGameType.Host)
        {
            return;
        }
        // The sender may leave by themselves if we do not act: that leave is not theirs to count.
        LobbyGuard.NoteAskedUs(senderId);
        if (!BlockPolicy.HostHonours(senderId, message.player, Lobby.NetService.NetId, Lobby.Players.Select(p => p.id).ToList()))
        {
            MainFile.Logger.Info($"lobby {SteamLobbyId}: ignored a request from {senderId} to send a player away");
            return;
        }
        Expel(message.player, "blocked by a member");
    }

    private static void OnLeaveMessage(QuickMatchLeaveMessage message, ulong senderId)
    {
        if (State != MatchState.InLobbyClient || Lobby == null || senderId != LobbyBackend.Current.Owner(SteamLobbyId))
        {
            return;
        }
        switch (message.reason)
        {
            case LeaveReasons.Idle:
                _idleStreak = MatchPolicy.IdleStreak(_idleStreak, IdleEvent.SentBack);
                if (MatchPolicy.IdleStops(_idleStreak))
                {
                    StopIdleClient();
                }
                else
                {
                    LeaveBlockedLobby("we did not press Embark in time", notice: "QUICKMATCH.NOTICE.idle_out");
                }
                break;
            case LeaveReasons.HostIdle:
                LeaveBlockedLobby("the host did not press Embark in time", notice: "QUICKMATCH.NOTICE.host_idle", keepReady: true);
                break;
            case LeaveReasons.Voted:
                LeaveBlockedLobby("the others voted to remove us", voted: true);
                break;
            default:
                LeaveBlockedLobby("the host sent us away", voted: LobbyGuard.VoteAgainstSelf);
                break;
        }
    }

    // The line names a vote or the countdown, otherwise only that the group is gone: it never names a block.
    internal static void LeaveBlockedLobby(string reason, bool voted = false, string? notice = null, bool keepReady = false)
    {
        if (_cts == null || Lobby == null || State != MatchState.InLobbyClient)
        {
            return;
        }
        ulong lobbyId = SteamLobbyId;
        MainFile.Logger.Info($"leaving lobby {lobbyId}: {reason}");
        Notice.Show(Loc.Get(notice ?? (voted ? "QUICKMATCH.NOTICE.voted_out" : "QUICKMATCH.NOTICE.group_gone")));
        MarkFailed(lobbyId, BlockedLobbyCooldownMs);
        SetState(MatchState.Searching, "left a lobby we were sent away from");
        CancellationToken ct = _cts.Token;
        int gen = _generation;
        // Run outside the message dispatch: leaving tears down the connection that delivered it.
        Callable.From(() =>
        {
            if (gen == _generation)
            {
                _ = RunAsync(gen, ct, () => LeaveAndSearchAsync(ct, keepReady), "leave");
            }
        }).CallDeferred();
    }

    private static async Task LeaveAndSearchAsync(CancellationToken ct, bool keepReady = false)
    {
        _pendingCharacter = Lobby?.LocalPlayer.character;
        // A different group: the player presses Embark there themselves, unless the group itself was sent on (an idle host).
        _pendingReady = keepReady && (Lobby?.LocalPlayer.isReady ?? false);
        _merging = true;
        try
        {
            CloseCharacterSelect();
        }
        finally
        {
            _merging = false;
        }
        await SearchJoinOrHostAsync(ct);
    }
}
