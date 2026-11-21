using System;

namespace QuickMatch.Voice;

// Protects the listener's ears from one speaker. A fast peak limiter caps a scream or a blown-out mic at once, and a
// speaker who stays loud for most of a few seconds is turned down until the listener changes that speaker's volume.
// Works after the listener's own volume for that speaker is applied.
internal sealed class VoiceLimiter
{
    // A fraction of full scale (about -6 dB).
    public const float Ceiling = 0.5f;
    // RMS, dB below full scale.
    public const double LoudDb = -12;
    // Loud for 3 s of a 5 s window makes a speaker too loud.
    public const int WindowFrames = 250;
    public const int LoudFramesForTooLoud = 150;
    // -12 dB.
    public const float TooLoudGain = 0.25f;

    // About 100 ms back to full gain after a peak.
    private const float Release = 1f / (VoiceFormat.SampleRate * 0.1f);

    private readonly bool[] _window = new bool[WindowFrames];
    private int _windowAt;
    private int _loudFrames;
    private float _reduction = 1f;

    public bool TooLoud { get; private set; }

    public void Process(Span<short> frame, float volume)
    {
        float gain = volume * (TooLoud ? TooLoudGain : 1f);
        double sentSum = 0;
        for (int i = 0; i < frame.Length; i++)
        {
            float sent = frame[i] / 32768f;
            sentSum += (double)sent * sent;
            float sample = sent * gain;
            float peak = Math.Abs(sample);
            _reduction = Math.Min(1f, _reduction + Release);
            if (peak * _reduction > Ceiling)
            {
                _reduction = Ceiling / peak;
            }
            sample *= _reduction;
            frame[i] = (short)Math.Clamp(sample * 32768f, short.MinValue, short.MaxValue);
        }
        CountLoudness(frame.Length == 0 ? VoiceGate.SilenceDb : 10 * Math.Log10(Math.Max(sentSum / frame.Length, 1e-10)));
    }

    // The listener chose a volume for this speaker, so the automatic turning down is undone.
    public void ResetLoudness()
    {
        Array.Clear(_window);
        _loudFrames = 0;
        TooLoud = false;
    }

    // Loudness as sent, before the listener's volume and the automatic turning down, so turning a speaker down does
    // not by itself clear the mark.
    private void CountLoudness(double sentDb)
    {
        bool loud = sentDb >= LoudDb;
        if (_window[_windowAt])
        {
            _loudFrames--;
        }
        _window[_windowAt] = loud;
        if (loud)
        {
            _loudFrames++;
        }
        _windowAt = (_windowAt + 1) % WindowFrames;
        if (_loudFrames >= LoudFramesForTooLoud)
        {
            TooLoud = true;
        }
    }
}
