using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Localization;
using QuickMatch.Steam;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    private const double RejoinPollSeconds = 5;
    private const double RejoinTimeoutSeconds = 180;

    // The game's load lobby takes over once joined; it only admits players who are in the save.
    private static async Task RejoinAsync(RunRecord record, CancellationToken ct, double? hostAfter, SerializableRun? hostRun, LostHost? lost)
    {
        ulong started = Time.GetTicksMsec();
        // A question asked at our turn has its minute to be answered before the search may time out.
        double timeout = MigrationPolicy.SearchTimeoutSeconds(RejoinTimeoutSeconds, hostAfter + (lost?.Asks == true ? MigrationPolicy.AnswerSeconds : 0));
        var skipped = new HashSet<ulong>();
        TakeOverQuestion? question = null;
        double askedAt = 0;
        try
        {
            while (true)
            {
                double elapsed = (Time.GetTicksMsec() - started) / 1000.0;
                SetState(MatchState.Rejoining, "waiting for the host");
                QuickMatchHub.ShowStatus(RejoinStatus(lost, elapsed));
                foreach (ulong lobbyId in await LobbySearch.FindResumeAsync(record.Key, record.Banned, ct))
                {
                    if (skipped.Contains(lobbyId))
                    {
                        continue;
                    }
                    // An open question stays until the join is made (then it closes as we leave), so a failed join keeps our turn.
                    if (await TryJoinResumeAsync(lobbyId, ct, skipped))
                    {
                        return;
                    }
                    SetState(MatchState.Rejoining, "join failed");
                }
                elapsed = (Time.GetTicksMsec() - started) / 1000.0;
                if (MigrationPolicy.HostsNow(elapsed, hostAfter))
                {
                    TakeOverStep step = TakeOverStep.Host;
                    if (lost?.Asks == true)
                    {
                        if (question == null)
                        {
                            MainFile.Logger.Info($"no lobby for run {record.Key} found; asking whether to reopen it as its host");
                            question = RegroupPrompt.AskTakeOver(PlatformUtil.GetPlayerName(PlatformType.Steam, lost.OldHost),
                                MigrationPolicy.AnswerSeconds, MigrationPolicy.HasNext(lost.Order, RunRecord.LocalPlayerId()));
                            askedAt = elapsed;
                        }
                        step = MigrationPolicy.TakeOverNow(question.Given, elapsed - askedAt);
                    }
                    if (step == TakeOverStep.Host && await ReopenRunAsync(record, hostRun, ct))
                    {
                        return;
                    }
                    if (step != TakeOverStep.Wait)
                    {
                        if (step == TakeOverStep.Join && question != null)
                        {
                            bool answered = question.Given != null;
                            question.Close();
                            MainFile.Logger.Info($"{(answered ? "passed" : "no answer in time")}; searching on for the run as a joiner");
                        }
                        question = null;
                        hostAfter = null;
                    }
                }
                if (elapsed > timeout)
                {
                    break;
                }
                // An answer to the question cuts the wait short.
                Task delay = GodotDelay.Seconds(RejoinPollSeconds, ct);
                await (question != null ? Task.WhenAny(delay, question.Answer) : delay);
                ct.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            question?.Close();
        }

        Reset("rejoin timed out");
        if (await RegroupPrompt.AskKeepWaitingAsync())
        {
            if (QuickMatchHub.IsOnTop())
            {
                StartRejoin();
            }
        }
        else
        {
            RunRecord.Clear("forgotten by the player");
            if (QuickMatchHub.IsOnTop())
            {
                Stack()?.Pop();
            }
            if (Stack()?.GetSubmenuType<NMultiplayerSubmenu>() is { } submenu)
            {
                QuickMatchButton.Refresh(submenu);
            }
        }
    }

    private static string RejoinStatus(LostHost? lost, double elapsed)
    {
        ulong turn = MigrationPolicy.WaitingFor(lost?.Order, elapsed, RunRecord.LocalPlayerId());
        if (turn == 0)
        {
            return Loc.Get("QUICKMATCH.rejoin_waiting");
        }
        return Loc.With("QUICKMATCH.rejoin_waiting_for", "host", PlatformUtil.GetPlayerName(PlatformType.Steam, lost!.OldHost),
            "player", PlatformUtil.GetPlayerName(PlatformType.Steam, turn));
    }

    private static async Task<bool> ReopenRunAsync(RunRecord record, SerializableRun? hostRun, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SerializableRun? run = hostRun ?? HostCopy.Load(record.Key, RunRecord.LocalPlayerId());
        if (run == null)
        {
            MainFile.Logger.Warn($"no run {record.Key} to reopen; searching on");
            return false;
        }
        MainFile.Logger.Info($"no lobby for run {record.Key} found; reopening it {(hostRun != null ? "from our save" : "from our copy, as the new host")}");
        Reset("reopening the run");
        QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.rejoin_reopening"));
        await Regroup.HostSavedRunAsync(run, record.Key);
        return true;
    }

    private static async Task<bool> TryJoinResumeAsync(ulong lobbyId, CancellationToken ct, HashSet<ulong> skipped)
    {
        ct.ThrowIfCancellationRequested();
        QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.joining"));
        if (await ConnectAsync(lobbyId, ct, rejoin: true) is not { } connected)
        {
            return false;
        }
        (JoinFlow flow, JoinResult result, RejoinClient? watcher) = connected;

        try
        {
            ct.ThrowIfCancellationRequested();
            // Steam names a lobby's owner only to its members, so a host voted out of the run is found once connected.
            if (flow.NetService is NetClientGameService { IsConnected: true } client
                && RunKickPolicy.SkipsHost(client.HostNetId, RunRecord.Current?.Banned ?? []))
            {
                MainFile.Logger.Info($"rejoin {lobbyId}: its host was voted out of the run; skipping it");
                skipped.Add(lobbyId);
                watcher?.Dispose();
                flow.NetService.Disconnect(NetError.Quit);
                return false;
            }
            if (result.sessionState == RunSessionState.Running && StartScreens.FromGame(result.gameMode, GameVersions.IsCustom(result)) != null
                && result.rejoinResponse is { } answer && watcher != null)
            {
                if (RunRejoin.Refusal(flow.NetService, answer, watcher) is { } why)
                {
                    MainFile.Logger.Info($"rejoin {lobbyId}: answer not taken ({why})");
                    watcher.Dispose();
                    flow.NetService.SetBufferMessages(false);
                    flow.NetService.Disconnect(NetError.Quit);
                    return false;
                }
                Reset("rejoining the run under way");
                await RunRejoin.EnterAsync(flow.NetService, answer, watcher);
                return true;
            }
            watcher?.Dispose();
            if (result.sessionState != RunSessionState.InLoadedLobby || StartScreens.FromGame(result.gameMode, GameVersions.IsCustom(result)) == null || !result.loadJoinResponse.HasValue)
            {
                MainFile.Logger.Info($"rejoin {lobbyId}: not a loaded quick-match lobby (state={result.sessionState} mode={result.gameMode} custom={GameVersions.IsCustom(result)})");
                flow.NetService.Disconnect(NetError.Quit);
                return false;
            }
            if (BlockPolicy.FirstBlockedInRun(result.loadJoinResponse.Value.serializableRun.Players.Select(p => p.NetId), flow.NetService.NetId, BlockStore.IsBlocked, RunRecord.Current?.Stayed ?? []) != 0)
            {
                MainFile.Logger.Info($"rejoin {lobbyId}: a player in the saved run is blocked; not rejoining");
                flow.NetService.Disconnect(NetError.Quit);
                return false;
            }
            NMainMenuSubmenuStack stack = Stack() ?? throw new InvalidOperationException("main menu is gone");
            NMultiplayerLoadGameScreen screen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
            screen.InitializeAsClient(flow.NetService, result.loadJoinResponse.Value);
            Reset("rejoined");
            stack.Push(screen);
            MainFile.Logger.Info($"rejoined saved run in lobby {lobbyId}");
            return true;
        }
        catch
        {
            if (flow.NetService.IsConnected)
            {
                flow.NetService.Disconnect(NetError.Quit);
            }
            throw;
        }
    }
}
