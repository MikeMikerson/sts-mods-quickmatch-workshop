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
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Transport;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    private const double RejoinPollSeconds = 5;
    private const double RejoinTimeoutSeconds = 180;

    // The game's load lobby takes over once joined; it only admits players who are in the save.
    private static async Task RejoinAsync(RunRecord record, CancellationToken ct, double? hostAfter, SerializableRun? hostRun, LostHost? lost)
    {
        if (record.HasGuests)
        {
            hostAfter = null;
            lost = null;
        }
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
                    if (await TryJoinResumeAsync(lobbyId, record.Key, record.HasGuests, ct, skipped))
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
                            question = RegroupPrompt.AskTakeOver(LobbyBackend.NameOf(lost.OldHost),
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
        return Loc.With("QUICKMATCH.rejoin_waiting_for", "host", LobbyBackend.NameOf(lost!.OldHost),
            "player", LobbyBackend.NameOf(turn));
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

    public static void StartGuestSaveLink(ulong lobbyId, string key, bool voice)
    {
        if (!IsAvailable || State != MatchState.Idle || key.Length == 0)
        {
            QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.LINK.failed"));
            return;
        }
        MatchVoice.Set(MatchVoice.ForRun(voice, MatchChoice.Voice));
        LobbyLinks.Remember(lobbyId, offersSockets: true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetState(MatchState.Rejoining, "invite to saved run");
        _ = RunAsync(_generation, ct, async () =>
        {
            if (!await TryJoinResumeAsync(lobbyId, key, true, ct, []))
            {
                Reset("could not join saved run");
                QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.LINK.failed"));
            }
        }, "invite to saved run");
    }

    private static async Task<bool> TryJoinResumeAsync(ulong lobbyId, string key, bool guestSave, CancellationToken ct, HashSet<ulong> skipped)
    {
        ct.ThrowIfCancellationRequested();
        QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.joining"));
        if (await ConnectAsync(lobbyId, ct, rejoin: !guestSave) is not { } connected)
        {
            return false;
        }
        (JoinFlow flow, JoinResult result, RejoinClient? watcher) = connected;

        try
        {
            ct.ThrowIfCancellationRequested();
            var record = RunRecord.Current;
            if (record?.Key != key)
            {
                record = null;
            }
            // Steam names a lobby's owner only to its members, so a host voted out of the run is found once connected.
            if (flow.NetService is NetClientGameService { IsConnected: true } client
                && (RunKickPolicy.SkipsHost(client.HostNetId, record?.Banned ?? [])
                    || record is { HasGuests: true } && client.HostNetId != record.HostId))
            {
                MainFile.Logger.Info($"rejoin {lobbyId}: its host cannot reopen this run; skipping it");
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
            var save = result.loadJoinResponse.Value.serializableRun;
            List<ulong> players = save.Players.Select(p => p.NetId).ToList();
            if (!RejoinPolicy.AcceptsAnswer(RunRecord.KeyOf(save), key, players, flow.NetService.NetId)
                || BlockPolicy.FirstBlockedInRun(players, flow.NetService.NetId, BlockStore.IsBlocked, record?.Stayed ?? []) != 0)
            {
                MainFile.Logger.Info($"rejoin {lobbyId}: saved run or players do not match; not rejoining");
                flow.NetService.Disconnect(NetError.Quit);
                return false;
            }
            if (guestSave)
            {
                ulong host = ((NetClientGameService)flow.NetService).HostNetId;
                var guests = ResumeLobby.ReadGuests(LobbyBackend.Current.GetData(lobbyId, ResumeLobby.Guests), players, host);
                if (guests == null)
                {
                    flow.NetService.Disconnect(NetError.Quit);
                    return false;
                }
                bool voice = LobbyBackend.Current.GetData(lobbyId, LobbyKeys.Voice) == LobbyKeys.VoiceOn;
                RunRecord.RestoreGuests(key, host, players, voice, guests);
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
