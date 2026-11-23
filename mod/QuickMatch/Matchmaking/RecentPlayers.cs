using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

// At is in Unix seconds.
public sealed record RecentPlayer(ulong Id, string? Name, long At);

// The last players this player finished a quick-match run with, newest first, so a good partner can be found
// again and added as a Steam friend. Kept on this device and never sent anywhere.
public sealed class RecentPlayers
{
    public const int Max = 10;

    private readonly List<RecentPlayer> _players = new();

    public event Action? Changed;

    public IReadOnlyList<RecentPlayer> Players => _players;

    // A player met again moves to the top, and keeps their saved name when none is known this time.
    public void Note(IEnumerable<(ulong Id, string? Name)> players, long at)
    {
        var met = new List<RecentPlayer>();
        foreach ((ulong id, string? name) in players)
        {
            if (!BlockPolicy.IsIndividual(id) || met.Any(p => p.Id == id))
            {
                continue;
            }
            string? known = string.IsNullOrWhiteSpace(name) ? _players.FirstOrDefault(p => p.Id == id)?.Name : name.Trim();
            met.Add(new RecentPlayer(id, known, at));
        }
        if (met.Count == 0)
        {
            return;
        }
        _players.RemoveAll(p => met.Any(m => m.Id == p.Id));
        _players.InsertRange(0, met);
        Trim();
        Changed?.Invoke();
    }

    public void Load(IEnumerable<RecentPlayer> saved)
    {
        _players.Clear();
        foreach (RecentPlayer player in saved)
        {
            if (BlockPolicy.IsIndividual(player.Id) && _players.All(p => p.Id != player.Id))
            {
                _players.Add(player);
            }
        }
        Trim();
    }

    private void Trim()
    {
        if (_players.Count > Max)
        {
            _players.RemoveRange(Max, _players.Count - Max);
        }
    }
}
