using QuickMatch.Lobby;

namespace QuickMatch.Tests;

/// <summary>
/// Steam lobbies and lobby chat as they behave in practice, on a simulated clock: every message arrives
/// <see cref="LatencyMs"/> later; a sender's messages beyond <see cref="RateLimit"/> in any second are
/// dropped without an error; Steamworks members always send to everyone, themselves included, while
/// SteamKit2 members can send to one member. Tests can also drop chosen messages.
/// </summary>
internal sealed class FakeSteam
{
    public const long LatencyMs = 420;
    public const int RateLimit = 17;

    private readonly List<(long At, Action Deliver)> _pending = new();
    private readonly List<Action> _tickers = new();
    private sealed class Lobby(ulong owner, LobbyKind kind, int capacity)
    {
        public readonly List<ulong> Members = [owner];
        public readonly Dictionary<string, string> Data = new();
        public readonly int Capacity = capacity;
        public LobbyKind Kind = kind;
        public bool Joinable = true;
    }

    private readonly Dictionary<ulong, Lobby> _lobbies = new();
    private readonly Dictionary<ulong, List<long>> _sendTimes = new();
    private ulong _nextLobby = 109775242500000000;

    public long Now { get; private set; } = 1_000_000;

    /// <summary>Decides per lobby chat message (sender, index of that sender's sends, bytes) whether it is lost.</summary>
    public Func<ulong, int, byte[], bool>? Lose { get; set; }

    /// <summary>Lobby chat messages each sender put through, with the time they were sent.</summary>
    public Dictionary<ulong, List<long>> Sent { get; } = new();

    public int RateDrops { get; private set; }

    public FakeBackend Member(ulong id, bool canTarget = false) => new(this, id, canTarget);

    /// <summary>Runs an action on the simulated clock, for other simulated networks (<see cref="FakeRelay"/>).</summary>
    public void After(long ms, Action action) => _pending.Add((Now + ms, action));

    /// <summary>Called every simulated step, like the game's frame loop calling Update.</summary>
    public void OnTick(Action tick) => _tickers.Add(tick);

    public void Advance(long ms)
    {
        long end = Now + ms;
        while (Now < end)
        {
            Now += 10;
            foreach (var due in _pending.Where(p => p.At <= Now).OrderBy(p => p.At).ToList())
            {
                _pending.Remove(due);
                due.Deliver();
            }
            foreach (Action tick in _tickers.ToList())
            {
                tick();
            }
        }
    }

    internal ulong CreateLobby(FakeBackend owner, LobbyKind kind, int capacity)
    {
        ulong id = _nextLobby++;
        _lobbies[id] = new Lobby(owner.LocalId, kind, capacity);
        return id;
    }

    internal bool Join(ulong lobby, FakeBackend member)
    {
        if (!_lobbies.TryGetValue(lobby, out Lobby? room))
        {
            return false;
        }
        if (!room.Members.Contains(member.LocalId))
        {
            room.Members.Add(member.LocalId);
            Broadcast(lobby, new LobbyEvent(LobbyEventKind.MemberJoined, lobby, member.LocalId));
        }
        return true;
    }

    internal void Leave(ulong lobby, ulong member)
    {
        if (_lobbies.TryGetValue(lobby, out Lobby? room) && room.Members.Remove(member))
        {
            Broadcast(lobby, new LobbyEvent(LobbyEventKind.MemberLeft, lobby, member));
        }
    }

    internal IReadOnlyList<ulong> Members(ulong lobby) => _lobbies.TryGetValue(lobby, out Lobby? room) ? room.Members.ToList() : [];

    internal ulong Owner(ulong lobby) => _lobbies.TryGetValue(lobby, out Lobby? room) ? room.Members.FirstOrDefault() : 0;

    internal Dictionary<string, string> Data(ulong lobby) => _lobbies[lobby].Data;

    internal void SetKind(ulong lobby, LobbyKind kind) => _lobbies[lobby].Kind = kind;

    internal void SetJoinable(ulong lobby, bool joinable) => _lobbies[lobby].Joinable = joinable;

    internal IReadOnlyList<LobbyListing> Search(LobbyQuery query) => _lobbies
        .Where(entry => entry.Value.Kind == LobbyKind.Public && entry.Value.Joinable
            && entry.Value.Capacity - entry.Value.Members.Count >= query.MinFreeSlots
            && query.Equal.All(filter => entry.Value.Data.GetValueOrDefault(filter.Key) == filter.Value))
        .Take(query.MaxResults)
        .Select(entry => new LobbyListing(entry.Key, entry.Value.Members.Count,
            query.ReadKeys.ToDictionary(key => key, key => entry.Value.Data.GetValueOrDefault(key) ?? "")))
        .ToList();

    internal bool SendChat(FakeBackend sender, ulong lobby, byte[] data, int length, ulong target)
    {
        if (!_lobbies.TryGetValue(lobby, out Lobby? room) || !room.Members.Contains(sender.LocalId))
        {
            return false;
        }
        List<long> times = _sendTimes.TryGetValue(sender.LocalId, out List<long>? t) ? t : _sendTimes[sender.LocalId] = new();
        times.RemoveAll(at => Now - at >= 1000);
        times.Add(Now);
        int index = Sent.TryGetValue(sender.LocalId, out List<long>? sent) ? sent.Count : 0;
        (Sent.TryGetValue(sender.LocalId, out sent) ? sent : Sent[sender.LocalId] = new()).Add(Now);
        if (times.Count > RateLimit)
        {
            RateDrops++;
            return true;
        }
        byte[] copy = data.AsSpan(0, length).ToArray();
        if (Lose?.Invoke(sender.LocalId, index, copy) == true)
        {
            return true;
        }
        var message = new LobbyEvent(LobbyEventKind.Chat, lobby, sender.LocalId, copy);
        if (target != 0 && sender.CanTarget)
        {
            Deliver(target, message);
        }
        else
        {
            Broadcast(lobby, message);
        }
        return true;
    }

    private readonly Dictionary<ulong, FakeBackend> _backends = new();

    internal void Register(FakeBackend backend) => _backends[backend.LocalId] = backend;

    private void Broadcast(ulong lobby, LobbyEvent e)
    {
        foreach (ulong member in _lobbies[lobby].Members)
        {
            Deliver(member, e);
        }
    }

    private void Deliver(ulong member, LobbyEvent e)
    {
        if (_backends.TryGetValue(member, out FakeBackend? backend))
        {
            _pending.Add((Now + LatencyMs, () => backend.Raise(e)));
        }
    }
}

internal sealed class FakeBackend : ILobbyBackend
{
    private readonly FakeSteam _steam;

    public FakeBackend(FakeSteam steam, ulong id, bool canTarget)
    {
        _steam = steam;
        LocalId = id;
        CanTarget = canTarget;
        steam.Register(this);
    }

    public bool CanTarget { get; }

    public bool IsAvailable => true;

    public ulong LocalId { get; }

    public uint ServerTime => (uint)(_steam.Now / 1000);

    public event Action<LobbyEvent>? Changed;

    public List<ulong> Left { get; } = new();

    public Task<ulong?> CreateAsync(LobbyKind kind, int maxMembers, CancellationToken ct) => Task.FromResult<ulong?>(_steam.CreateLobby(this, kind, maxMembers));

    public Task<LobbyJoinResult> JoinAsync(ulong lobby, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_steam.Join(lobby, this) ? LobbyJoinResult.Ok : LobbyJoinResult.DoesNotExist);
    }

    public void Leave(ulong lobby)
    {
        Left.Add(lobby);
        _steam.Leave(lobby, LocalId);
    }

    public Task<IReadOnlyList<LobbyListing>?> SearchAsync(LobbyQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<LobbyListing>?>(_steam.Search(query));

    public void SetData(ulong lobby, string key, string value) => _steam.Data(lobby)[key] = value;

    public void DeleteData(ulong lobby, string key) => _steam.Data(lobby).Remove(key);

    public string GetData(ulong lobby, string key) => _steam.Data(lobby).GetValueOrDefault(key) ?? "";

    public Task<IReadOnlyDictionary<string, string>?> ReadDataAsync(ulong lobby, IReadOnlyList<string> keys, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(keys.ToDictionary(key => key, key => GetData(lobby, key)));

    public bool SetKind(ulong lobby, LobbyKind kind)
    {
        _steam.SetKind(lobby, kind);
        return true;
    }

    public void SetJoinable(ulong lobby, bool joinable) => _steam.SetJoinable(lobby, joinable);

    public ulong Owner(ulong lobby) => _steam.Owner(lobby);

    public IReadOnlyList<ulong> Members(ulong lobby) => _steam.Members(lobby);

    public bool SendChat(ulong lobby, byte[] data, int length, ulong target) => _steam.SendChat(this, lobby, data, length, target);

    public string? PlayerName(ulong id) => null;

    /// <summary>Players this fake account has blocked in Steam.</summary>
    public HashSet<ulong> SteamBlocked { get; } = new();

    public bool BlockedOnSteam(ulong id) => SteamBlocked.Contains(id);

    public bool ShowProfile(ulong id) => false;

    internal void Raise(LobbyEvent e) => Changed?.Invoke(e);
}
