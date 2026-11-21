using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Voice;

// Choices are kept per Steam ID across matches and are never told to anyone. A player without a choice is heard,
// unless StrangersStartMuted is on and they are not a Steam friend.
internal sealed class VoiceMutes
{
    public const float MinVolume = 0f;
    public const float MaxVolume = 2f;

    // True: muted by choice. False: heard by choice (a stranger the player unmuted).
    private readonly Dictionary<ulong, bool> _choices = new();
    private readonly Dictionary<ulong, float> _volumes = new();

    public bool StrangersStartMuted { get; set; }

    public event Action? Changed;

    public bool IsMuted(ulong player, bool isFriend) =>
        _choices.TryGetValue(player, out bool muted) ? muted : StrangersStartMuted && !isFriend;

    public void Mute(ulong player) => Choose(player, muted: true);

    public void Unmute(ulong player) => Choose(player, muted: false);

    public void MuteAll(IEnumerable<ulong> players)
    {
        bool changed = false;
        foreach (ulong player in players)
        {
            changed |= !_choices.TryGetValue(player, out bool muted) || !muted;
            _choices[player] = true;
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<ulong> Muted => _choices.Where(c => c.Value).Select(c => c.Key).OrderBy(id => id).ToList();

    // Heard by choice despite StrangersStartMuted.
    public IReadOnlyList<ulong> Heard => _choices.Where(c => !c.Value).Select(c => c.Key).OrderBy(id => id).ToList();

    public float VolumeOf(ulong player) => _volumes.TryGetValue(player, out float volume) ? volume : 1f;

    public void SetVolume(ulong player, float volume)
    {
        volume = Math.Clamp(volume, MinVolume, MaxVolume);
        if (Math.Abs(volume - 1f) < 0.005f)
        {
            if (_volumes.Remove(player))
            {
                Changed?.Invoke();
            }
            return;
        }
        if (!_volumes.TryGetValue(player, out float old) || old != volume)
        {
            _volumes[player] = volume;
            Changed?.Invoke();
        }
    }

    public IReadOnlyDictionary<ulong, float> Volumes => _volumes;

    // Does not raise Changed.
    public void Load(IEnumerable<ulong> muted, IEnumerable<ulong> heard, IEnumerable<KeyValuePair<ulong, float>> volumes)
    {
        _choices.Clear();
        _volumes.Clear();
        foreach (ulong player in heard)
        {
            _choices[player] = false;
        }
        foreach (ulong player in muted)
        {
            _choices[player] = true;
        }
        foreach ((ulong player, float volume) in volumes)
        {
            float clamped = Math.Clamp(volume, MinVolume, MaxVolume);
            if (Math.Abs(clamped - 1f) >= 0.005f)
            {
                _volumes[player] = clamped;
            }
        }
    }

    private void Choose(ulong player, bool muted)
    {
        if (!_choices.TryGetValue(player, out bool old) || old != muted)
        {
            _choices[player] = muted;
            Changed?.Invoke();
        }
    }
}
