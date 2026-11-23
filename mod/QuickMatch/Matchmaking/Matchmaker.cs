using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Transport;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

internal enum MatchState
{
    Idle,
    Searching,
    Joining,
    CreatingHost,
    HostingWaiting,
    Merging,
    InLobbyClient,
    Rejoining,
}

// Everything runs on the Godot main thread (Steam callbacks and SceneTree timers fire there).
// Each session owns a CancellationTokenSource; Reset bumps the generation and cancels it, and
// every continuation re-checks the token before touching the UI.
internal static partial class Matchmaker
{
    private const ulong FailedLobbyCooldownMs = 60_000;
    // Long, since the blocked player may still be in that lobby.
    private const ulong BlockedLobbyCooldownMs = 600_000;

    private static readonly Random Rng = new();
    private static readonly Dictionary<ulong, ulong> RecentlyFailed = new();

    private static CancellationTokenSource? _cts;
    private static int _generation;
    private static JoinFlow? _flow;
    private static NSubmenu? _screen;
    private static ulong _startedMsec;

    public static NSubmenu? Screen => _screen;

    public static double SearchSeconds => (Time.GetTicksMsec() - _startedMsec) / 1000.0;
    private static bool _merging;
    private static uint _createdAt;
    private static INetGameService? _handlerService;
    private static bool _handlersAsHost;
    private static ulong[]? _desktopPlayers;

    private static readonly HashSet<ulong> RefusedHere = new();
    private static readonly HashSet<ulong> AskedHost = new();
    // Bumped each time we open a lobby, so a merge loop or offer from an earlier lobby of the session stops.
    private static int _hostEpoch;
    private static bool _offered;

    private static IReadOnlyList<int> _allowed = [0];
    private static CharacterModel? _pendingCharacter;
    private static bool _pendingReady;
    private static uint _deadline;
    private static int _idleStreak;
    private static int _idleNotice;

    public static (int Size, bool Voice, ulong HostId, ulong At, ulong[]? DesktopPlayers)? PendingRunStart { get; set; }
    public static MatchState State { get; private set; }
    public static int Size { get; private set; }
    public static bool RandomSize { get; private set; }
    public static bool AnySize { get; private set; }
    public static AscensionChoice Choice { get; private set; } = AscensionChoice.Random;
    public static int MaxAscension { get; private set; }
    public static int HostedAscension { get; private set; }
    public static StartRunLobby? Lobby { get; private set; }
    public static ulong SteamLobbyId { get; private set; }
    public static bool IsAvailable => LobbyBackend.Current.IsAvailable;

    public static void Start(int size, bool voice, AscensionChoice choice)
    {
        if (State != MatchState.Idle)
        {
            MainFile.Logger.Info($"ignoring start({size}) while {State}");
            return;
        }
        if (!IsAvailable)
        {
            MainFile.Logger.Warn("quick match needs Steam");
            return;
        }
        SetChoice(size, choice, MatchChoice.GameMode);
        _offered = false;
        _startedMsec = Time.GetTicksMsec();
        SearchStartedByPlayer();
        MatchVoice.Set(voice);
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        CancellationToken ct = _cts.Token;
        MainFile.Logger.Info($"start size={(RandomSize ? "any" : size.ToString())} {QuickMatchLobby.CompatKey.Local()} {MatchMode.Text(MatchMode.On)} {MatchVoice.Text(voice)} asc={choice.Text} max=A{MaxAscension} qm_asc in [{string.Join(",", _allowed)}] host at A{HostedAscension}");
        _ = RunAsync(gen, ct, () => SearchJoinOrHostAsync(ct), "search");
    }

    public static void StartRejoin(double? hostAfter = null, SerializableRun? hostRun = null, LostHost? lost = null)
    {
        if (State != MatchState.Idle)
        {
            MainFile.Logger.Info($"ignoring rejoin while {State}");
            return;
        }
        if (!IsAvailable || RunRecord.Current is not { } record)
        {
            MainFile.Logger.Warn("rejoin needs Steam and a run record");
            return;
        }
        Size = record.Size;
        RandomSize = false;
        AnySize = false;
        SearchStartedByPlayer();
        MatchVoice.Set(MatchVoice.ForRun(record.Voice, MatchChoice.Voice));
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        CancellationToken ct = _cts.Token;
        MainFile.Logger.Info($"rejoin key={record.Key} host={record.HostId} {MatchVoice.Text(MatchVoice.On)}{(hostAfter is { } after ? $" hosting after {after:0}s from {(hostRun != null ? "our save" : "our copy")}" : "")}");
        _ = RunAsync(gen, ct, () => RejoinAsync(record, ct, hostAfter, hostRun, lost), "rejoin");
    }

    public static void Cancel(string reason)
    {
        if (State == MatchState.Idle)
        {
            return;
        }
        MainFile.Logger.Info($"cancel: {reason}");
        Reset(reason);
    }

    public static void OnCharacterSelectClosing(NSubmenu screen)
    {
        if (Lobby == null || StartScreens.LobbyOf(screen) != Lobby)
        {
            return;
        }
        LobbyStatusPanel.Hide(screen);
        if (_merging)
        {
            // We are giving up our own lobby to join a better one; keep the session going.
            EndSession();
            return;
        }
        Cancel("character select closed");
    }

    public static void OnRunStarted(NSubmenu screen)
    {
        if (Lobby == null || StartScreens.LobbyOf(screen) != Lobby)
        {
            return;
        }
        MainFile.Logger.Info($"run started with {Lobby.Players.Count}/{Size}{(AnySize ? " (any size)" : "")} players");
        ulong hostId = Lobby.NetService.Type == NetGameType.Host
            ? Lobby.NetService.NetId
            : LobbyBackend.Current.Owner(SteamLobbyId);
        // The run's own size: an Any lobby may start below its 4.
        PendingRunStart = (Lobby.Players.Count, MatchVoice.On, hostId, Time.GetTicksMsec(), _desktopPlayers);
        LobbyStatusPanel.Hide(screen);
        Reset("run started");
    }

    // Opening character select always selects the first character, so after a move the one picked
    // in the previous lobby is selected again.
    public static void OnCharacterSelectOpened(NSubmenu screen)
    {
        if (_pendingCharacter is not { } character || Lobby == null || StartScreens.LobbyOf(screen) != Lobby)
        {
            return;
        }
        _pendingCharacter = null;
        bool ready = _pendingReady;
        _pendingReady = false;
        _ = RestoreCharacterAsync(screen, character, ready, _generation);
    }

    // Without this gate two ready players in a 3-player lobby would start a 2-player run; an Any lobby
    // keeps the game's own rule. Clients follow the host's start (the game's LobbyBeginRunMessage); the
    // gate only keeps their screen saying the same as the host's.
    public static bool BlocksStart(StartRunLobby lobby)
    {
        return Lobby != null && lobby == Lobby && !AnySize && lobby.Players.Count < StartSize;
    }

    public static void ShareDesktopRoster(StartRunLobby lobby)
    {
        if (lobby != Lobby || lobby.NetService is not QuickMatchHostService host || !lobby.IsAboutToBeginGame())
        {
            return;
        }
        _desktopPlayers = lobby.Players.Where(p => p.id == host.NetId ? QuickMatchHostService.HasSockets : host.PeerOffersSockets(p.id))
            .Select(p => p.id).ToArray();
        host.SendMessage(new QuickMatchDesktopRosterMessage { players = _desktopPlayers });
    }

    private static void OnDesktopRoster(QuickMatchDesktopRosterMessage message, ulong sender)
    {
        if (sender != 0 && sender == LobbyBackend.Current.Owner(SteamLobbyId))
        {
            _desktopPlayers = message.players;
        }
    }

    public static double? CountdownLeft
    {
        get
        {
            if (Lobby == null)
            {
                return null;
            }
            uint deadline = Lobby.NetService.Type == NetGameType.Host ? _deadline : QuickMatchLobby.Deadline(SteamLobbyId);
            if (deadline == 0)
            {
                return null;
            }
            return Math.Max(0, (double)deadline - LobbyBackend.Current.ServerTime);
        }
    }

    private static async Task RunAsync(int gen, CancellationToken ct, Func<Task> body, string what)
    {
        try
        {
            await body();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            MainFile.Logger.Info($"{what} stopped");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"{what} failed: {e}");
            if (gen == _generation)
            {
                Reset("error");
            }
        }
    }

    private static void SetChoice(int size, AscensionChoice choice, RunMode mode)
    {
        RandomSize = size == 0;
        Size = RandomSize ? MatchPolicy.AnySizeMax : size;
        MatchMode.Set(mode);
        MaxAscension = MatchChoice.MaxAscensionFor(mode);
        Choice = choice;
        _allowed = MatchPolicy.AllowedAscensions(choice, MaxAscension);
        HostedAscension = MatchPolicy.HostAscension(choice, MaxAscension);
    }

    // Popped as its Back button does, which tears the host lobby down.
    private static void CloseCharacterSelect()
    {
        NMainMenuSubmenuStack? stack = Stack();
        if (stack != null && _screen != null && stack.Peek() == _screen)
        {
            stack.Pop();
        }
    }

    private static void BeginSession(NSubmenu screen, ulong steamLobbyId, MatchState state)
    {
        _screen = screen;
        Lobby = StartScreens.LobbyOf(screen) ?? throw new InvalidOperationException("the lobby screen has no lobby");
        SteamLobbyId = steamLobbyId;
        Lobby.PlayerConnected += OnLobbyPlayersChanged;
        Lobby.PlayerDisconnected += OnLobbyPlayersChanged;
        Lobby.PlayerConnected += OnPlayerJoined;
        Lobby.PlayerDisconnected += OnPlayerLeft;
        RefusedHere.Clear();
        AskedHost.Clear();
        _handlerService = Lobby.NetService;
        _handlersAsHost = Lobby.NetService.Type == NetGameType.Host;
        if (_handlersAsHost)
        {
            _handlerService.RegisterMessageHandler<QuickMatchBlockedHereMessage>(OnBlockedHereMessage);
        }
        else
        {
            _handlerService.RegisterMessageHandler<QuickMatchMoveMessage>(OnMoveMessage);
            _handlerService.RegisterMessageHandler<QuickMatchLeaveMessage>(OnLeaveMessage);
            _handlerService.RegisterMessageHandler<QuickMatchDesktopRosterMessage>(OnDesktopRoster);
        }
        BlockStore.List.Changed += CheckBlocked;
        LobbyBackend.Current.Changed += OnLobbyChanged;
        LobbyGuard.Begin(steamLobbyId);
        SetState(state, $"lobby {steamLobbyId}");
        CheckBlocked();
    }

    private static void EndSession()
    {
        if (Lobby != null)
        {
            Lobby.PlayerConnected -= OnLobbyPlayersChanged;
            Lobby.PlayerDisconnected -= OnLobbyPlayersChanged;
            Lobby.PlayerConnected -= OnPlayerJoined;
            Lobby.PlayerDisconnected -= OnPlayerLeft;
        }
        BlockStore.List.Changed -= CheckBlocked;
        LobbyBackend.Current.Changed -= OnLobbyChanged;
        LobbyGuard.EndLobby();
        if (_handlerService != null)
        {
            try
            {
                if (_handlersAsHost)
                {
                    _handlerService.UnregisterMessageHandler<QuickMatchBlockedHereMessage>(OnBlockedHereMessage);
                }
                else
                {
                    _handlerService.UnregisterMessageHandler<QuickMatchMoveMessage>(OnMoveMessage);
                    _handlerService.UnregisterMessageHandler<QuickMatchLeaveMessage>(OnLeaveMessage);
                    _handlerService.UnregisterMessageHandler<QuickMatchDesktopRosterMessage>(OnDesktopRoster);
                }
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn($"could not unregister message handlers: {e.Message}");
            }
            _handlerService = null;
        }
        RefusedHere.Clear();
        AskedHost.Clear();
        _deadline = 0;
        _desktopPlayers = null;
        ResetStartShort();
        if (_screen != null && GodotObject.IsInstanceValid(_screen))
        {
            LobbyStatusPanel.Hide(_screen);
        }
        _screen = null;
        Lobby = null;
        SteamLobbyId = 0;
    }

    private static void OnPlayerJoined(StartRunLobbyPlayer player) => LobbyGuard.PlayerJoined(player.id);

    private static void OnPlayerLeft(StartRunLobbyPlayer player) => LobbyGuard.PlayerLeft(player.id);

    private static void OnLobbyPlayersChanged(StartRunLobbyPlayer _)
    {
        if (Lobby == null)
        {
            return;
        }
        MainFile.Logger.Info($"lobby {SteamLobbyId}: {Lobby.Players.Count}/{Size} players");
        StartShortPlayersChanged();
        if (Lobby.NetService.Type == NetGameType.Host)
        {
            QuickMatchLobby.UpdateMembers(SteamLobbyId, Lobby.Players.Count);
        }
        if (_screen != null && GodotObject.IsInstanceValid(_screen))
        {
            LobbyStatusPanel.Refresh(_screen);
        }
        CheckBlocked();
    }

    private static void Reset(string reason)
    {
        _generation++;
        CancellationTokenSource? cts = _cts;
        JoinFlow? flow = _flow;
        _cts = null;
        _flow = null;
        _pendingCharacter = null;
        _pendingReady = false;
        _deadline = 0;
        EndSession();
        SetState(MatchState.Idle, reason);
        QuickMatchHub.ShowStatus(null);
        cts?.Cancel();
        flow?.CancelToken.Cancel();
    }

    private static void SetState(MatchState state, string reason)
    {
        if (State != state)
        {
            MainFile.Logger.Info($"state {State} -> {state} ({reason})");
            State = state;
        }
    }

    private static bool IsRecentlyFailed(ulong lobbyId)
    {
        return RecentlyFailed.TryGetValue(lobbyId, out ulong until) && Time.GetTicksMsec() < until;
    }

    private static void MarkFailed(ulong lobbyId, ulong cooldownMs = FailedLobbyCooldownMs)
    {
        ulong until = Time.GetTicksMsec() + cooldownMs;
        if (!RecentlyFailed.TryGetValue(lobbyId, out ulong current) || current < until)
        {
            RecentlyFailed[lobbyId] = until;
        }
    }

    private static NMainMenuSubmenuStack? Stack() => NGame.Instance?.MainMenu?.SubmenuStack;
}
