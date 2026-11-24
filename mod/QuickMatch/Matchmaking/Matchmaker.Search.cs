using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Transport;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    private const int SearchRounds = 2;
    private const int MaxJoinAttemptsPerRound = 5;
    private const double JoinTimeoutSeconds = 15;

    private static async Task SearchJoinOrHostAsync(CancellationToken ct)
    {
        for (int round = 1; round <= SearchRounds; round++)
        {
            SetState(MatchState.Searching, $"round {round}");
            QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.searching"));
            List<LobbyCandidate> ranked = await MatchPolicy.NearbyFirstAsync(async nearby =>
            {
                List<LobbyCandidate> found = await LobbySearch.FindAsync(RandomSize ? 0 : Size, ct, nearby: nearby);
                List<LobbyCandidate> usable = MatchPolicy.Rank(found, SteamLobbyId, IsRecentlyFailed, _allowed, RandomSize);
                MainFile.Logger.Info($"search round {round}{(nearby ? " nearby" : "")}: {usable.Count} of {found.Count} with qm_asc in [{string.Join(",", _allowed)}]");
                return usable;
            });
            foreach (LobbyCandidate candidate in ranked.Take(MaxJoinAttemptsPerRound))
            {
                if (await TryJoinAsync(candidate.Id, RandomSize ? candidate.Size : Size, RandomSize && candidate.AnySize, ct))
                {
                    return;
                }
            }
            if (round < SearchRounds)
            {
                // Jitter, so two players who started at the same moment do not both give up and host.
                await GodotDelay.Seconds(1 + Rng.NextDouble() * 2, ct);
            }
        }
        await HostAsync(ct);
    }

    private static async Task<bool> TryJoinAsync(ulong lobbyId, int size, bool anySize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SetState(MatchState.Joining, $"lobby {lobbyId}");
        QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.joining"));
        if (await ConnectAsync(lobbyId, ct) is not { } connected)
        {
            MarkFailed(lobbyId);
            return false;
        }
        (JoinFlow flow, JoinResult result, _) = connected;

        try
        {
            ct.ThrowIfCancellationRequested();
            if (result.sessionState != RunSessionState.InLobby || StartScreens.FromGame(result.gameMode, GameVersions.IsCustom(result)) != MatchMode.On || !result.joinResponse.HasValue)
            {
                MainFile.Logger.Info($"join {lobbyId}: not an open {MatchMode.LobbyValue(MatchMode.On)} lobby (state={result.sessionState} mode={result.gameMode} custom={GameVersions.IsCustom(result)})");
                flow.NetService.Disconnect(result.sessionState == RunSessionState.Running ? NetError.RunInProgress : NetError.Quit);
                MarkFailed(lobbyId);
                return false;
            }
            ulong blocked = BlockPolicy.FirstBlocked(result.joinResponse.Value.playersInLobby?.Select(p => p.id) ?? [], flow.NetService.NetId, BlockStore.IsBlocked);
            if (blocked != 0)
            {
                MainFile.Logger.Info($"join {lobbyId}: a player there is blocked; leaving");
                flow.NetService.Disconnect(NetError.Quit);
                MarkFailed(lobbyId, BlockedLobbyCooldownMs);
                return false;
            }
            NMainMenuSubmenuStack stack = Stack() ?? throw new InvalidOperationException("main menu is gone");
            Size = size;
            AnySize = anySize;
            NSubmenu screen = StartScreens.Join(stack, MatchMode.On, flow.NetService, result.joinResponse.Value);
            BeginSession(screen, lobbyId, MatchState.InLobbyClient);
            QuickMatchHub.ShowStatus(null);
            stack.Push(screen);
            MainFile.Logger.Info($"joined lobby {lobbyId} ({Lobby!.Players.Count}/{Size}{(AnySize ? " any size" : "")}) at ascension {result.joinResponse.Value.ascension}");
            return true;
        }
        catch
        {
            // Cancelled (or broke) after connecting: do not leave a live connection behind.
            if (flow.NetService.IsConnected)
            {
                flow.NetService.Disconnect(NetError.Quit);
            }
            throw;
        }
    }

    // The game's join flow without its error popups. With rejoin, the join does not time out while the
    // host holds it for the party's next room.
    private static async Task<(JoinFlow Flow, JoinResult Result, RejoinClient? Rejoin)?> ConnectAsync(ulong lobbyId, CancellationToken ct, bool rejoin = false)
    {
        if (LobbyLinks.OffersSockets(lobbyId) is not bool hostOffersSockets)
        {
            return null;
        }
        IClientConnectionInitializer connection;
        if (LinkPolicy.JoinOverSockets(QuickMatchHostService.HasSockets, hostOffersSockets))
        {
            MainFile.Logger.Info($"joining lobby {lobbyId} over Steam sockets");
            connection = SocketsConnection(lobbyId);
        }
        else
        {
            connection = ChatConnectionInitializer.FromLobby(lobbyId);
        }
        var service = new QuickMatchClientService();
        RejoinClient? watcher = rejoin ? new RejoinClient(service) : null;
        JoinFlow flow = GameVersions.NewJoinFlow(service, connection);
        _flow = flow;
        bool finished = false;
        bool ok = false;
        using (ct.Register(() => flow.CancelToken.Cancel()))
        {
            _ = CancelJoinAfterTimeoutAsync(flow, () => finished || watcher?.Held == true, ct);
            try
            {
                // On failure JoinFlow disconnects by itself before throwing.
                JoinResult result = await GameVersions.Begin(flow, connection);
                ok = true;
                return (flow, result, watcher);
            }
            catch (ClientConnectionFailedException e)
            {
                MainFile.Logger.Info($"join {lobbyId} failed: {e.info.GetReason()} ({e.Message})");
                return null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                MainFile.Logger.Info($"join {lobbyId} timed out after {JoinTimeoutSeconds}s");
                return null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                MainFile.Logger.Warn($"join {lobbyId} error: {e}");
                return null;
            }
            finally
            {
                finished = true;
                if (!ok)
                {
                    watcher?.Dispose();
                }
                if (_flow == flow)
                {
                    _flow = null;
                }
            }
        }
    }

    // Keep Steam's connection initializer out of the phone's join path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IClientConnectionInitializer SocketsConnection(ulong lobbyId) => SteamClientConnectionInitializer.FromLobby(lobbyId);

    private static async Task CancelJoinAfterTimeoutAsync(JoinFlow flow, Func<bool> finished, CancellationToken ct)
    {
        try
        {
            await GodotDelay.Seconds(JoinTimeoutSeconds, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!finished())
        {
            flow.CancelToken.Cancel();
        }
    }

    private static async Task HostAsync(CancellationToken ct)
    {
        SetState(MatchState.CreatingHost, "no open lobby found");
        QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.creating"));

        if (RandomSize)
        {
            Size = MatchPolicy.AnySizeMax;
        }
        AnySize = RandomSize;
        var netService = new QuickMatchHostService();
        NetErrorInfo? error = await netService.StartQuickMatchHost(Size, MatchVoice.On);
        if (ct.IsCancellationRequested)
        {
            if (netService.IsConnected)
            {
                netService.Disconnect(NetError.Quit);
            }
            ct.ThrowIfCancellationRequested();
        }
        if (error.HasValue)
        {
            MainFile.Logger.Warn($"could not open a lobby: {error.Value.GetReason()}");
            Reset("host failed");
            NErrorPopup? popup = NErrorPopup.Create(error.Value);
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }
            return;
        }
        if (netService.LobbyId is not { } lobbyId)
        {
            netService.Disconnect(NetError.InternalError);
            throw new InvalidOperationException("host has no Steam lobby");
        }
        NMainMenuSubmenuStack? stack = Stack();
        if (stack == null)
        {
            netService.Disconnect(NetError.Quit);
            throw new InvalidOperationException("main menu is gone");
        }

        // Same steps as NMultiplayerHostSubmenu.StartHostAsync, with the chosen size instead of 4.
        NSubmenu screen = StartScreens.Host(stack, MatchMode.On, netService, Size);
        // Dread is always played at its own level, with no ascension panel to lock.
        if (MatchMode.On != RunMode.Dread)
        {
            AscensionLock.Apply(screen, HostedAscension);
        }
        _createdAt = LobbyBackend.Current.ServerTime;
        BeginSession(screen, lobbyId, MatchState.HostingWaiting);
        QuickMatchHub.ShowStatus(null);
        stack.Push(screen);
        QuickMatchLobby.Publish(lobbyId, Size, AnySize, HostedAscension, _createdAt);

        int gen = _generation;
        int epoch = ++_hostEpoch;
        _ = RunAsync(gen, ct, () => MergeLoopAsync(gen, epoch, ct), "merge loop");
        _ = RunAsync(gen, ct, () => AloneOfferAsync(gen, epoch, ct), "alone offer");
        _ = RunAsync(gen, ct, () => CountdownLoopAsync(gen, epoch, ct), "countdown");
        _ = RunAsync(gen, ct, () => StartShortLoopAsync(gen, epoch, ct), "start short");
    }
}
