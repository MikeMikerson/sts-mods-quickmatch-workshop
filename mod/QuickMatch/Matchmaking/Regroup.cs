using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Localization;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

// Gets a quick-match group back into the same run after someone drops. The game cannot rejoin a run in
// progress, but it can reload the host's save, so a regroup is: the host reopens the saved run, and every
// other player finds that lobby by the run key and joins it. A host whose client drops goes on without them
// (StandIn), and a host left alone is asked whether to wait for them; a client that loses the host goes to
// the rejoin search instead of the game's error popup.
internal static partial class Regroup
{
    private const ulong PendingRunMaxAgeMs = 120_000;

    private static RunLobby? _trackedLobby;
    private static RunLobby? _presentLobby;
    private static bool _promptOpen;
    private static bool _regrouping;

    // The game clears its run lobby's list before it tells anyone the connection to the host is lost,
    // so the host order is worked out from this copy, kept from the lobby's own leave and rejoin events.
    private static List<ulong> _present = [];

    // A host voted out of the run that we are leaving on purpose: our own disconnect then starts a migration.
    private static ulong _leavingVotedHost;

    public static bool InQuickMatchRun { get; private set; }

    public static void OnRunLaunched(RunState state)
    {
        RunManager run = RunManager.Instance;
        if (Matchmaker.PendingRunStart is { } pending)
        {
            Matchmaker.PendingRunStart = null;
            // A run launches a few seconds after embarking; an old marker belongs to a start that failed.
            if (Godot.Time.GetTicksMsec() - pending.At < PendingRunMaxAgeMs)
            {
                RunRecord.Save(state, pending.HostId, pending.Size, pending.Voice);
            }
        }
        InQuickMatchRun = RunRecord.Matches(state);
        if (InQuickMatchRun)
        {
            MatchVoice.Set(MatchVoice.ForRun(RunRecord.Current!.Voice, MatchChoice.Voice));
            NoteHost(run, RunRecord.Current.Key);
        }
        _promptOpen = false;
        _regrouping = false;
        _leavingVotedHost = 0;
        TrackPresent(InQuickMatchRun ? run.RunLobby : null);
        if (InQuickMatchRun)
        {
            LobbyGuard.BeginRun(run.NetService);
        }
        StandIn.OnRunLaunched(state, InQuickMatchRun);
        RunRejoin.OnRunLaunched(state, InQuickMatchRun);
        MainFile.Logger.Info($"run launched: quick match={InQuickMatchRun} key={RunRecord.KeyOf(state)} as {run.NetService.Type}{(InQuickMatchRun ? " " + MatchVoice.Text(MatchVoice.On) : "")}");
        if (InQuickMatchRun && run.NetService.Type == NetGameType.Host && run.RunLobby != null)
        {
            _trackedLobby = run.RunLobby;
            _trackedLobby.RemotePlayerDisconnected += OnRemotePlayerDisconnected;
        }
    }

    // A host keeps the game's save and needs no copy; a client back in a run it once hosted moves its old
    // save of the run aside, since the run has gone on without it.
    private static void NoteHost(RunManager run, string key)
    {
        try
        {
            if (run.NetService.Type == NetGameType.Host)
            {
                RunRecord.SetHost(RunRecord.LocalPlayerId());
                HostCopy.Delete();
            }
            else if (run.NetService is NetClientGameService client)
            {
                RunRecord.SetHost(client.HostNetId);
                HostCopy.MoveOldSaveAside(key);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not note the run's host: {e.Message}");
        }
    }

    public static void OnRunCleanUp()
    {
        LobbyGuard.EndRun();
        TrackPresent(null);
        StandIn.OnRunCleanUp();
        RunRejoin.OnRunCleanUp();
        if (InQuickMatchRun && RunManager.Instance.IsAbandoned)
        {
            RunRecord.Clear("run abandoned");
        }
        if (_trackedLobby != null)
        {
            _trackedLobby.RemotePlayerDisconnected -= OnRemotePlayerDisconnected;
            _trackedLobby = null;
        }
        InQuickMatchRun = false;
    }

    private static void TrackPresent(RunLobby? lobby)
    {
        if (_presentLobby != null)
        {
            _presentLobby.RemotePlayerDisconnected -= OnPresentLeft;
            _presentLobby.PlayerRejoined -= OnPresentRejoined;
        }
        _presentLobby = lobby;
        if (lobby != null)
        {
            lobby.RemotePlayerDisconnected += OnPresentLeft;
            lobby.PlayerRejoined += OnPresentRejoined;
            _present = lobby.PlayerIds.ToList();
        }
    }

    private static void OnPresentChanged()
    {
        if (_presentLobby != null)
        {
            _present = _presentLobby.PlayerIds.ToList();
        }
    }

    // Not for a player voted out, whose vote said so, nor on a host left alone, which is asked what to do instead.
    private static void OnPresentLeft(ulong player)
    {
        OnPresentChanged();
        try
        {
            RunManager run = RunManager.Instance;
            if (!InQuickMatchRun || run.IsAbandoned || RunRecord.Current?.Banned.Contains(player) == true)
            {
                return;
            }
            if (run.NetService.Type == NetGameType.Host && DropPolicy.AskHostAlone(_presentLobby?.Players.Count ?? 1))
            {
                return;
            }
            Notice.Show(Loc.With("QUICKMATCH.NOTICE.dropped", "player", PlatformUtil.GetPlayerName(PlatformType.Steam, player)));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not say that a player left: {e.Message}");
        }
    }

    private static void OnPresentRejoined(RunLobbyPlayer player)
    {
        OnPresentChanged();
        try
        {
            if (InQuickMatchRun && player.id != RunRecord.LocalPlayerId())
            {
                Notice.Show(Loc.With("QUICKMATCH.NOTICE.back", "player", PlatformUtil.GetPlayerName(PlatformType.Steam, player.id)));
                NetworkProblemMark.Recheck(player.id);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not say that a player is back: {e.Message}");
        }
    }

    public static void OnRunEnded()
    {
        if (InQuickMatchRun)
        {
            RunRecord.Clear("run ended");
        }
    }

    private static async Task<NMainMenu> MainMenuAsync()
    {
        for (int i = 0; i < 50; i++)
        {
            if (NGame.Instance?.MainMenu is { } menu && menu.IsNodeReady() && menu.SubmenuStack != null)
            {
                return menu;
            }
            await GodotDelay.Seconds(0.1, CancellationToken.None);
        }
        throw new InvalidOperationException("main menu did not appear");
    }
}
