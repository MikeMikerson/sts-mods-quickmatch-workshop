using System;

namespace QuickMatch.Voice;

// Decides, frame by frame, whether the local player is speaking, so silence is never sent. It follows the room's
// background noise and opens when a frame is clearly louder than that; once open it stays open for HangoverFrames
// after the last loud frame, so the ends of words are not cut.
internal sealed class VoiceGate
{
    // 300 ms of quieter frames before the gate closes.
    public const int HangoverFrames = 15;
    // Never speech, however quiet the room is.
    public const double MinimumDb = -55;
    // Always speech, however noisy the room is.
    public const double AlwaysOpenDb = -18;
    // Digital silence (log of zero).
    public const double SilenceDb = -96;

    private double _floorDb = -60;
    private int _hangover;

    // 0: only clearly raised voices open the gate. 1: quiet speech does too.
    public double Sensitivity { get; set; } = 0.5;

    public bool IsOpen { get; private set; }

    // dB below full scale.
    public double LevelDb { get; private set; } = SilenceDb;

    public double Meter => Math.Clamp((LevelDb - MinimumDb) / -MinimumDb, 0, 1);

    public bool Process(ReadOnlySpan<short> frame)
    {
        LevelDb = Level(frame);
        double threshold = Math.Min(Math.Max(_floorDb + MarginDb(), MinimumDb), AlwaysOpenDb);
        bool loud = LevelDb >= threshold;
        if (loud)
        {
            _hangover = HangoverFrames;
        }
        IsOpen = loud || _hangover > 0;
        if (!loud && _hangover > 0)
        {
            _hangover--;
        }

        // The background drops quickly when the room goes quiet and rises slowly (about 2.5 dB a
        // second), so a long sentence is not mistaken for background noise.
        if (LevelDb < _floorDb)
        {
            _floorDb += (LevelDb - _floorDb) * 0.3;
        }
        else if (!loud)
        {
            _floorDb += Math.Min(0.05, LevelDb - _floorDb);
        }
        _floorDb = Math.Max(_floorDb, SilenceDb);
        return IsOpen;
    }

    public void Reset()
    {
        _hangover = 0;
        IsOpen = false;
    }

    // RMS, in dB below full scale.
    public static double Level(ReadOnlySpan<short> frame)
    {
        if (frame.IsEmpty)
        {
            return SilenceDb;
        }
        double sum = 0;
        foreach (short sample in frame)
        {
            sum += (double)sample * sample;
        }
        double rms = Math.Sqrt(sum / frame.Length) / 32768.0;
        return rms <= 0 ? SilenceDb : Math.Max(20 * Math.Log10(rms), SilenceDb);
    }

    private double MarginDb() => 18 - 12 * Math.Clamp(Sensitivity, 0, 1);
}
