using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    private static async Task CountdownLoopAsync(int gen, int epoch, CancellationToken ct)
    {
        var seen = new HashSet<ulong>();
        try
        {
            while (true)
            {
                await GodotDelay.Seconds(0.5, ct);
                if (!Hosting(gen, epoch) || Lobby == null)
                {
                    return;
                }
                // Players already sent away are on their way out and count for nothing.
                List<StartRunLobbyPlayer> present = Lobby.Players.Where(p => !RefusedHere.Contains(p.id)).ToList();
                bool joined = false;
                foreach (StartRunLobbyPlayer player in present)
                {
                    joined |= seen.Add(player.id);
                }
                seen.IntersectWith(present.Select(p => p.id));
                int ready = present.Count(p => p.isReady);
                // A player still connecting is about to join: the countdown waits for them, then starts again.
                bool settled = SettledGroupSize() == Lobby.Players.Count;
                bool runs = settled && MatchPolicy.CountdownRuns(present.Count, ready, StartSize, AnySize);
                uint now = LobbyBackend.Current.ServerTime;
                uint deadline = !runs ? 0 : _deadline == 0 || joined ? now + (uint)MatchPolicy.CountdownSeconds : _deadline;
                if (deadline != _deadline)
                {
                    string what = deadline == 0 ? "cleared"
                        : _deadline == 0 ? $"of {MatchPolicy.CountdownSeconds:0}s started"
                        : $"of {MatchPolicy.CountdownSeconds:0}s started again (a player joined)";
                    _deadline = deadline;
                    QuickMatchLobby.SetDeadline(SteamLobbyId, deadline);
                    MainFile.Logger.Info($"lobby {SteamLobbyId}: countdown {what} ({ready}/{present.Count} ready)");
                    RefreshStatus();
                }
                if (!runs || now < _deadline)
                {
                    continue;
                }
                _deadline = 0;
                QuickMatchLobby.SetDeadline(SteamLobbyId, 0);
                if (!Lobby.LocalPlayer.isReady)
                {
                    await HostIdleAsync(ct);
                    return;
                }
                foreach (StartRunLobbyPlayer idle in present.Where(p => !p.isReady))
                {
                    Expel(idle.id, "did not press Embark in time", LeaveReasons.Idle);
                }
                RefreshStatus();
            }
        }
        finally
        {
            if (gen == _generation && epoch == _hostEpoch && _deadline != 0)
            {
                _deadline = 0;
                if (SteamLobbyId != 0)
                {
                    QuickMatchLobby.SetDeadline(SteamLobbyId, 0);
                }
            }
        }
    }

    // A host cannot be sent away from its own lobby, so its players are, and it leaves: a host who
    // walked away holds nobody.
    private static async Task HostIdleAsync(CancellationToken ct)
    {
        MainFile.Logger.Info($"lobby {SteamLobbyId}: we did not press Embark in time; sending our players to search again and leaving");
        LobbyBackend.Current.SetJoinable(SteamLobbyId, false);
        if (Lobby?.NetService is NetHostGameService host && host.ConnectedPeers.Count > 0)
        {
            LobbyGuard.NoteMovingEveryone();
            foreach (ulong peer in host.ConnectedPeers.Select(p => p.peerId).ToList())
            {
                if (IsQuickMatchPeer(peer))
                {
                    host.SendMessage(new QuickMatchLeaveMessage { reason = LeaveReasons.HostIdle }, peer);
                }
            }
            await WaitForClientsToLeaveAsync(host, ct);
        }
        StopIdle("we did not press Embark in time", "QUICKMATCH.NOTICE.idle_host_self");
    }

    private static void StopIdleClient()
    {
        if (_cts == null || Lobby == null || State != MatchState.InLobbyClient)
        {
            return;
        }
        ulong lobbyId = SteamLobbyId;
        MainFile.Logger.Info($"leaving lobby {lobbyId}: we did not press Embark in time, {_idleStreak} lobbies in a row; stopping the search");
        MarkFailed(lobbyId, BlockedLobbyCooldownMs);
        int gen = _generation;
        // Run outside the message dispatch: leaving tears down the connection that delivered it.
        Callable.From(() =>
        {
            if (gen == _generation)
            {
                StopIdle($"we did not press Embark in time in {_idleStreak} lobbies in a row", "QUICKMATCH.NOTICE.idle_stopped");
            }
        }).CallDeferred();
    }

    // The line stays until its button is pressed or a new search starts: a player who walked away
    // does not see a line that goes by itself.
    private static void StopIdle(string reason, string lineKey)
    {
        NSubmenu? screen = _screen;
        Reset(reason);
        // Popped as its Back button does, which tears the lobby down; the session is already over.
        if (Stack() is { } stack && screen != null && stack.Peek() == screen)
        {
            stack.Pop();
        }
        int id = 0;
        id = Notice.ShowWithButton(Loc.Get(lineKey), Loc.Get("QUICKMATCH.NOTICE.ok"), () => Notice.Hide(id));
        _idleNotice = id;
    }

    private static void SearchStartedByPlayer()
    {
        _idleStreak = MatchPolicy.IdleStreak(_idleStreak, IdleEvent.SearchStarted);
        Notice.Hide(_idleNotice);
        _idleNotice = 0;
    }

    public static void OnEmbarkPressed(NSubmenu screen)
    {
        if (Lobby != null && StartScreens.LobbyOf(screen) == Lobby && _idleStreak != 0)
        {
            MainFile.Logger.Info($"embark pressed: the idle count of {_idleStreak} starts again");
            _idleStreak = MatchPolicy.IdleStreak(_idleStreak, IdleEvent.Embarked);
        }
    }

    private static void RefreshStatus()
    {
        if (_screen != null && GodotObject.IsInstanceValid(_screen))
        {
            LobbyStatusPanel.Refresh(_screen);
        }
    }
}
