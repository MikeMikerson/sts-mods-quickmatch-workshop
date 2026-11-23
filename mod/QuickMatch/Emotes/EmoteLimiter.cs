using System.Collections.Generic;

namespace QuickMatch.Emotes;

// How often emotes from one player get through: none within GapSeconds of the last, and at most Burst in
// WindowSeconds. One that comes too soon is dropped; one past the burst also hides that player for QuietSeconds.
// Times are in seconds on the caller's clock.
public sealed class EmoteLimiter(double gapSeconds, int burst, double windowSeconds, double quietSeconds)
{
    // Every receiver's limits for each sender. Network delay can bring two emotes closer than they were sent, so
    // the gap is shorter than the sender's own.
    public static EmoteLimiter ForReceiving() => new(1.0, 6, 30, 60);

    // The player's own button: stricter than any receiver, so an honest player is never hidden.
    public static EmoteLimiter ForSending() => new(1.5, 5, 30, 0);

    private readonly Dictionary<ulong, Queue<double>> _recent = new();
    private readonly Dictionary<ulong, double> _last = new();
    private readonly Dictionary<ulong, double> _quietUntil = new();

    public bool Ready(ulong player, double now) =>
        !Quiet(player, now) && !TooSoon(player, now) && Recent(player, now).Count < burst;

    public bool Allow(ulong player, double now)
    {
        if (Quiet(player, now) || TooSoon(player, now))
        {
            return false;
        }
        Queue<double> recent = Recent(player, now);
        if (recent.Count >= burst)
        {
            if (quietSeconds > 0)
            {
                _quietUntil[player] = now + quietSeconds;
                _recent.Remove(player);
            }
            return false;
        }
        recent.Enqueue(now);
        _last[player] = now;
        return true;
    }

    public void Reset()
    {
        _recent.Clear();
        _last.Clear();
        _quietUntil.Clear();
    }

    private bool Quiet(ulong player, double now) => _quietUntil.TryGetValue(player, out double until) && now < until;

    private bool TooSoon(ulong player, double now) => _last.TryGetValue(player, out double last) && now - last < gapSeconds;

    private Queue<double> Recent(ulong player, double now)
    {
        if (!_recent.TryGetValue(player, out Queue<double>? times))
        {
            times = new Queue<double>();
            _recent[player] = times;
        }
        while (times.Count > 0 && now - times.Peek() >= windowSeconds)
        {
            times.Dequeue();
        }
        return times;
    }
}
