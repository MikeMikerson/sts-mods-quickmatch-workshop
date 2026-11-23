using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;

namespace QuickMatch.SteamKit;

/// <summary>Where the lobby code logs: QuickMatch's godot.log writer in the game, a list in tests.</summary>
public sealed class SteamKitLog(Action<string> info, Action<string> warn)
{
    public void Info(string message) => info(message);

    public void Warn(string message) => warn(message);
}

/// <summary>
/// Steam lobbies and lobby chat over SteamKit2. Losing the connection leaves every lobby, so it is held
/// open while we are in one (it otherwise closes after 30 idle seconds). SteamKit2 keeps each lobby's
/// owner, members and their Steam names up to date.
///
/// QuickMatch reaches this class only on Android, so its public members use .NET types only: lobby
/// types are SteamKit2's ELobbyType values and join answers its EChatRoomEnterResponse values, as ints.
/// </summary>
public sealed class SteamKitLobbyService
{
    /// <summary>Several data changes in a row (QuickMatch publishes seven values and a type) go out as one request.</summary>
    private const int DataSendDelayMs = 50;

    private readonly ISteamSession _session;
    private readonly uint _appId;
    private readonly SteamKitLog _log;
    private readonly LobbyMessageHandler _messages;
    private readonly SteamMatchmaking _matchmaking;
    private readonly ConcurrentDictionary<ulong, OwnedLobby> _owned = new();
    private readonly ConcurrentDictionary<ulong, byte> _joined = new();
    private long _localId;

    /// <summary>
    /// The lobby service on a connection of the launcher's own kind, signed in with its saved sign-in;
    /// null (with the reason logged) when there is no sign-in or the launcher is another version.
    /// <paramref name="findType"/> looks types up by assembly-qualified name (Type.GetType in the game).
    /// </summary>
    public static SteamKitLobbyService? InLauncher(Func<string, Type?> findType, uint appId, SteamKitLog log)
    {
        LauncherSession? session = LauncherSession.Create(findType, log.Warn);
        return session == null ? null : new SteamKitLobbyService(session, appId, log);
    }

    internal SteamKitLobbyService(ISteamSession session, uint appId, SteamKitLog log)
    {
        _session = session;
        _appId = appId;
        _log = log;
        _matchmaking = session.Client.GetHandler<SteamMatchmaking>()
            ?? throw new InvalidOperationException("SteamKit2 has no matchmaking handler");
        _messages = new LobbyMessageHandler(appId);
        _messages.ChatReceived += (lobby, sender, data) => ChatReceived?.Invoke(lobby, sender, data);
        _messages.MemberJoined += (lobby, member) =>
        {
            if (_joined.ContainsKey(lobby))
            {
                MemberJoined?.Invoke(lobby, member);
            }
        };
        _messages.MemberLeft += (lobby, member) =>
        {
            if (_joined.ContainsKey(lobby))
            {
                MemberLeft?.Invoke(lobby, member);
            }
        };
        session.Client.AddHandler(_messages);
    }

    /// <summary>Lobby, sender, bytes. Raised on SteamKit2's receiving thread.</summary>
    public event Action<ulong, ulong, byte[]>? ChatReceived;

    /// <summary>Lobby, member. Raised on SteamKit2's receiving thread.</summary>
    public event Action<ulong, ulong>? MemberJoined;

    /// <summary>Lobby, member (ourselves too, when Steam takes us out). Raised on SteamKit2's receiving thread.</summary>
    public event Action<ulong, ulong>? MemberLeft;

    /// <summary>Our Steam ID once signed in on this connection; 0 before.</summary>
    public ulong LocalId => (ulong)Interlocked.Read(ref _localId);

    /// <summary>Our own Steam name, once Steam has sent it.</summary>
    public string? OwnName
    {
        get
        {
            string? name = _session.Client.GetHandler<SteamFriends>()?.GetPersonaName();
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }

    /// <summary>Signs in (if needed) and returns our Steam ID.</summary>
    public async Task<ulong> SignInAsync(CancellationToken ct)
    {
        await ConnectedAsync(ct).ConfigureAwait(false);
        return LocalId;
    }

    /// <summary>Creates a lobby we own and are in; null when Steam refused.</summary>
    public Task<ulong?> CreateAsync(int type, int maxMembers, CancellationToken ct) => CreateAsync((ELobbyType)type, maxMembers, ct);

    internal async Task<ulong?> CreateAsync(ELobbyType type, int maxMembers, CancellationToken ct)
    {
        SteamMatchmaking matchmaking = await ConnectedAsync(ct).ConfigureAwait(false);
        AsyncJob<SteamMatchmaking.CreateLobbyCallback>? job = matchmaking.CreateLobby(_appId, type, maxMembers);
        if (job == null)
        {
            _log.Warn("lobby creation refused: not signed in");
            return null;
        }
        SteamMatchmaking.CreateLobbyCallback created = await AnswerAsync(job.ToTask(), cb => cb.Result == EResult.OK ? cb.LobbySteamID.ConvertToUInt64() : 0, ct).ConfigureAwait(false);
        if (created.Result != EResult.OK)
        {
            _log.Warn("Steam refused to create a lobby: " + created.Result);
            return null;
        }
        ulong lobby = created.LobbySteamID.ConvertToUInt64();
        _owned[lobby] = new OwnedLobby(type, maxMembers);
        Hold(lobby);
        _log.Info($"created lobby {lobby} ({type}, {maxMembers} members)");
        return lobby;
    }

    /// <summary>Steam's answer as an EChatRoomEnterResponse value (1 is success).</summary>
    public async Task<int> JoinAsync(ulong lobby, CancellationToken ct) => (int)await JoinLobbyAsync(lobby, ct).ConfigureAwait(false);

    internal async Task<EChatRoomEnterResponse> JoinLobbyAsync(ulong lobby, CancellationToken ct)
    {
        SteamMatchmaking matchmaking = await ConnectedAsync(ct).ConfigureAwait(false);
        AsyncJob<SteamMatchmaking.JoinLobbyCallback>? job = matchmaking.JoinLobby(_appId, lobby);
        if (job == null)
        {
            _log.Warn("lobby join refused: no Steam name yet");
            return EChatRoomEnterResponse.Error;
        }
        SteamMatchmaking.JoinLobbyCallback joined = await AnswerAsync(job.ToTask(), cb => cb.ChatRoomEnterResponse == EChatRoomEnterResponse.Success ? lobby : 0, ct).ConfigureAwait(false);
        if (joined.ChatRoomEnterResponse == EChatRoomEnterResponse.Success)
        {
            Hold(lobby);
            _log.Info($"entered lobby {lobby}");
        }
        return joined.ChatRoomEnterResponse;
    }

    public void Leave(ulong lobby)
    {
        _owned.TryRemove(lobby, out _);
        if (!_joined.TryRemove(lobby, out _))
        {
            return;
        }
        if (_session.IsConnected)
        {
            _matchmaking.LeaveLobby(_appId, lobby);
        }
        _session.Release();
        _log.Info($"left lobby {lobby}");
    }

    /// <summary>
    /// Lobbies of our app whose data holds every value in <paramref name="equal"/> and that have
    /// at least <paramref name="minFreeSlots"/> free slots, each with its ID, member count and data
    /// values. Null when Steam reported an error.
    /// </summary>
    public async Task<IReadOnlyList<(ulong Id, int Members, IReadOnlyDictionary<string, string> Data)>?> SearchAsync(
        IReadOnlyDictionary<string, string> equal, int minFreeSlots, int maxResults, bool nearby, CancellationToken ct)
    {
        try
        {
            return await FindLobbiesAsync(equal, minFreeSlots, maxResults, nearby, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _log.Warn($"lobby search failed: {e.GetType().Name}");
            return null;
        }
    }

    private async Task<IReadOnlyList<(ulong Id, int Members, IReadOnlyDictionary<string, string> Data)>?> FindLobbiesAsync(
        IReadOnlyDictionary<string, string> equal, int minFreeSlots, int maxResults, bool nearby, CancellationToken ct)
    {
        SteamMatchmaking matchmaking = await ConnectedAsync(ct).ConfigureAwait(false);
        AsyncJob<SteamMatchmaking.GetLobbyListCallback>? job = matchmaking.GetLobbyList(_appId, Filters(equal, minFreeSlots, nearby), maxResults);
        if (job == null)
        {
            _log.Warn("lobby search refused: not signed in");
            return null;
        }
        SteamMatchmaking.GetLobbyListCallback list = await job.ToTask().WaitAsync(ct).ConfigureAwait(false);
        if (list.Result != EResult.OK)
        {
            _log.Warn("lobby search failed: " + list.Result);
            return null;
        }
        return list.Lobbies.Select(l => (l.SteamID.ConvertToUInt64(), l.NumMembers, (IReadOnlyDictionary<string, string>)l.Metadata)).ToList();
    }

    internal static List<SteamMatchmaking.Lobby.Filter> Filters(IReadOnlyDictionary<string, string> equal, int minFreeSlots, bool nearby)
    {
        var filters = new List<SteamMatchmaking.Lobby.Filter>();
        foreach (KeyValuePair<string, string> pair in equal)
        {
            filters.Add(new SteamMatchmaking.Lobby.StringFilter(pair.Key, ELobbyComparison.Equal, pair.Value));
        }
        if (minFreeSlots > 0)
        {
            filters.Add(new SteamMatchmaking.Lobby.SlotsAvailableFilter(minFreeSlots));
        }
        filters.Add(new SteamMatchmaking.Lobby.DistanceFilter(nearby ? ELobbyDistanceFilter.Default : ELobbyDistanceFilter.Worldwide));
        return filters;
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReadDataAsync(ulong lobby, CancellationToken ct)
    {
        try
        {
            SteamMatchmaking matchmaking = await ConnectedAsync(ct).ConfigureAwait(false);
            AsyncJob<SteamMatchmaking.LobbyDataCallback>? job = matchmaking.GetLobbyData(_appId, lobby);
            return job == null ? null : (await job.ToTask().WaitAsync(ct).ConfigureAwait(false)).Lobby.Metadata;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _log.Warn($"lobby data request failed: {e.GetType().Name}");
            return null;
        }
    }

    public void SetData(ulong lobby, string key, string value)
    {
        if (_owned.TryGetValue(lobby, out OwnedLobby? owned))
        {
            owned.Set(key, value);
            ScheduleDataSend(lobby, owned);
        }
    }

    public void DeleteData(ulong lobby, string key)
    {
        if (_owned.TryGetValue(lobby, out OwnedLobby? owned) && owned.Delete(key))
        {
            ScheduleDataSend(lobby, owned);
        }
    }

    /// <summary>A data value of a lobby we are in, or "" if unset.</summary>
    public string GetData(ulong lobby, string key)
    {
        if (_owned.TryGetValue(lobby, out OwnedLobby? owned))
        {
            return owned.Get(key);
        }
        return Cached(lobby)?.Metadata.TryGetValue(key, out string? value) == true ? value : "";
    }

    public bool SetType(ulong lobby, int type) => SetType(lobby, (ELobbyType)type);

    internal bool SetType(ulong lobby, ELobbyType type)
    {
        if (!_owned.TryGetValue(lobby, out OwnedLobby? owned))
        {
            return false;
        }
        owned.SetType(type);
        ScheduleDataSend(lobby, owned);
        return true;
    }

    public void SetJoinable(ulong lobby, bool joinable)
    {
        if (_owned.TryGetValue(lobby, out OwnedLobby? owned))
        {
            owned.SetJoinable(joinable);
            ScheduleDataSend(lobby, owned);
        }
    }

    public ulong Owner(ulong lobby) => Cached(lobby)?.OwnerSteamID?.ConvertToUInt64() ?? 0;

    /// <summary>Members of a lobby we are in, ourselves included.</summary>
    public IReadOnlyList<ulong> Members(ulong lobby) =>
        Cached(lobby)?.Members.Select(m => m.SteamID.ConvertToUInt64()).ToList() ?? new List<ulong>();

    /// <summary>True when the signed-in account has blocked the player in Steam (from the friends list Steam sent at sign-in).</summary>
    public bool BlockedOnSteam(ulong player)
    {
        EFriendRelationship relationship = _session.Client.GetHandler<SteamFriends>()?.GetFriendRelationship(new SteamID(player)) ?? EFriendRelationship.None;
        return relationship is EFriendRelationship.Ignored or EFriendRelationship.IgnoredFriend;
    }

    /// <summary>The Steam name of a member of a lobby we are in, or null.</summary>
    public string? MemberName(ulong member)
    {
        foreach (ulong lobby in _joined.Keys)
        {
            SteamMatchmaking.Lobby.Member? found = Cached(lobby)?.Members.FirstOrDefault(m => m.SteamID.ConvertToUInt64() == member);
            if (found != null && !string.IsNullOrEmpty(found.PersonaName))
            {
                return found.PersonaName;
            }
        }
        return null;
    }

    /// <summary>Sends one lobby chat message; target 0 goes to every member. False when not connected.</summary>
    public bool SendChat(ulong lobby, byte[] data, int length, ulong target)
    {
        if (!_session.IsConnected || !_joined.ContainsKey(lobby))
        {
            return false;
        }
        _messages.Send(lobby, target, data.AsSpan(0, length).ToArray());
        return true;
    }

    private SteamMatchmaking.Lobby? Cached(ulong lobby) => _matchmaking.GetLobby(_appId, lobby);

    /// <summary>
    /// Waits for Steam's answer. When the wait is cancelled the request still completes, and a lobby it
    /// put us in (<paramref name="entered"/> is not 0) is left again.
    /// </summary>
    private async Task<T> AnswerAsync<T>(Task<T> answer, Func<T, ulong> entered, CancellationToken ct)
    {
        try
        {
            return await answer.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _ = answer.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && entered(t.Result) is ulong lobby && lobby != 0)
                {
                    _matchmaking.LeaveLobby(_appId, lobby);
                }
            }, TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>Connecting blocks, so it happens on a worker thread rather than the game's.</summary>
    private Task<SteamMatchmaking> ConnectedAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            _session.Connect();
            if (_session.Client.SteamID is { } id && id.IsValid)
            {
                Interlocked.Exchange(ref _localId, (long)id.ConvertToUInt64());
            }
            return _matchmaking;
        }, ct);
    }

    private void Hold(ulong lobby)
    {
        if (_joined.TryAdd(lobby, 0))
        {
            _session.HoldOpen();
        }
    }

    private void ScheduleDataSend(ulong lobby, OwnedLobby owned)
    {
        if (Interlocked.Exchange(ref owned.SendScheduled, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(DataSendDelayMs).ConfigureAwait(false);
            Interlocked.Exchange(ref owned.SendScheduled, 0);
            await SendDataAsync(lobby, owned).ConfigureAwait(false);
        });
    }

    private async Task SendDataAsync(ulong lobby, OwnedLobby owned)
    {
        if (!_owned.ContainsKey(lobby) || !_session.IsConnected)
        {
            return;
        }
        (ELobbyType type, Dictionary<string, string> data) = owned.Snapshot();
        try
        {
            SteamMatchmaking.SetLobbyDataCallback result = await _matchmaking.SetLobbyData(_appId, lobby, type, owned.MaxMembers, 0, data).ToTask().ConfigureAwait(false);
            if (result.Result != EResult.OK)
            {
                _log.Warn($"Steam refused the data of lobby {lobby}: {result.Result}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"setting the data of lobby {lobby} failed: {ex.Message}");
        }
    }
}
