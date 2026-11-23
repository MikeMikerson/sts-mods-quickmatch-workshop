using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Emotes;

// Which emotes one player's game shows: from players in the run only, never from a blocked player or one this
// player chose to hide, and within the limits (EmoteLimiter). Hidden players stay hidden until the game closes.
public sealed class EmoteGate
{
    private readonly EmoteLimiter _limiter = EmoteLimiter.ForReceiving();
    private readonly HashSet<ulong> _hidden = new();

    public bool IsHidden(ulong player) => _hidden.Contains(player);

    public void SetHidden(ulong player, bool hidden)
    {
        if (hidden)
        {
            _hidden.Add(player);
        }
        else
        {
            _hidden.Remove(player);
        }
    }

    public bool Shows(ulong sender, IReadOnlyCollection<ulong> runPlayers, bool blocked, double now) =>
        runPlayers.Contains(sender) && !blocked && !_hidden.Contains(sender) && _limiter.Allow(sender, now);

    public void NewRun() => _limiter.Reset();
}
