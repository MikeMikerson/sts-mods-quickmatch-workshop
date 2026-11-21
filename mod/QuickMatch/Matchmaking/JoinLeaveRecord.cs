using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

// Times in Unix seconds.
public sealed class SeenPlayer
{
    public ulong Id { get; set; }

    // 1 to 4 at the last strike; it falls one step for each 24 hours since StepAt.
    public int Step { get; set; }

    public long StepAt { get; set; }

    // The host turns the player away until then.
    public long CoolUntil { get; set; }

    // Since the last strike, within the last 30 minutes.
    public List<long> ShortStays { get; set; } = new();
}

// A host's record of players its own client has seen join and leave its quick-match lobbies. A short stay is joining
// and leaving on one's own within 2 minutes, before the run starts; stays under 5 seconds are a joiner's own checks (a
// block, the version, the ascension) and do not count, nor do leaves the host caused or a member's who asked the host
// to send a blocked player away (the caller leaves those out). Three short stays within 30 minutes, or one vote kick,
// are a strike. Each strike moves the player a step up and refuses them for 5 minutes, 30 minutes, 2 hours, then 24
// hours (the last step repeats); each 24 hours without a strike moves them a step down. Only this host's own lobbies
// count: there is no server to share it. Times in Unix seconds.
public sealed class JoinLeaveRecord
{
    public const long MinStaySeconds = 5;
    public const long ShortStaySeconds = 120;
    public const long WindowSeconds = 1_800;
    public const int StaysPerStrike = 3;
    public const long StepDownSeconds = 86_400;
    public static readonly IReadOnlyList<long> StepSeconds = [300, 1_800, 7_200, 86_400];

    private readonly Dictionary<ulong, SeenPlayer> _players = new();

    public event Action? Changed;

    public int Count => _players.Count;

    public static bool IsShortStay(long joinedAt, long leftAt) =>
        leftAt - joinedAt >= MinStaySeconds && leftAt - joinedAt < ShortStaySeconds;

    public static int StepNow(SeenPlayer player, long now) =>
        player.Step <= 0 ? 0 : Math.Max(0, player.Step - (int)Math.Min(int.MaxValue, Math.Max(0, now - player.StepAt) / StepDownSeconds));

    public void Load(IEnumerable<SeenPlayer> players)
    {
        _players.Clear();
        foreach (SeenPlayer player in players.Where(p => p.Id != 0))
        {
            player.ShortStays ??= new();
            _players[player.Id] = player;
        }
    }

    public SeenPlayer? Find(ulong player) => _players.GetValueOrDefault(player);

    // True when this was their third short stay in 30 minutes, which is a strike.
    public bool NoteLeave(ulong player, long joinedAt, long leftAt)
    {
        if (player == 0 || !IsShortStay(joinedAt, leftAt))
        {
            return false;
        }
        SeenPlayer entry = Entry(player);
        entry.ShortStays.RemoveAll(at => leftAt - at >= WindowSeconds);
        entry.ShortStays.Add(leftAt);
        if (entry.ShortStays.Count >= StaysPerStrike)
        {
            entry.ShortStays.Clear();
            StrikeEntry(entry, leftAt);
            Changed?.Invoke();
            return true;
        }
        Changed?.Invoke();
        return false;
    }

    public void Strike(ulong player, long now)
    {
        if (player == 0)
        {
            return;
        }
        StrikeEntry(Entry(player), now);
        Changed?.Invoke();
    }

    public long? RefusedUntil(ulong player, long now) =>
        _players.TryGetValue(player, out SeenPlayer? entry) && entry.CoolUntil > now ? entry.CoolUntil : null;

    // What is worth saving: a player still on a step, cooling down, or with a short stay in the last 30 minutes.
    public List<SeenPlayer> ToKeep(long now)
    {
        foreach (SeenPlayer entry in _players.Values)
        {
            entry.ShortStays.RemoveAll(at => now - at >= WindowSeconds);
        }
        return _players.Values
            .Where(p => StepNow(p, now) > 0 || p.CoolUntil > now || p.ShortStays.Count > 0)
            .OrderBy(p => p.Id)
            .ToList();
    }

    public void Prune(long now)
    {
        HashSet<ulong> keep = ToKeep(now).Select(p => p.Id).ToHashSet();
        foreach (ulong id in _players.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _players.Remove(id);
        }
    }

    private SeenPlayer Entry(ulong player)
    {
        if (!_players.TryGetValue(player, out SeenPlayer? entry))
        {
            entry = new SeenPlayer { Id = player };
            _players[player] = entry;
        }
        return entry;
    }

    private static void StrikeEntry(SeenPlayer entry, long now)
    {
        int step = Math.Min(StepNow(entry, now) + 1, StepSeconds.Count);
        entry.Step = step;
        entry.StepAt = now;
        entry.CoolUntil = Math.Max(entry.CoolUntil, now + StepSeconds[step - 1]);
    }
}
