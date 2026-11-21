using System;

namespace QuickMatch.Voice;

public enum VoiceCaptureState
{
    // Steam is not running.
    Unsupported,
    Ready,
}

// All calls come from the game's thread.
public interface IVoiceDevice
{
    VoiceCaptureState CaptureState { get; }

    bool StartCapture();

    void StopCapture();

    // Steam's compressed voice recorded since the last read, oldest first; null when there is none. Steam delivers
    // nothing during silence.
    byte[]? ReadVoice();
}

// Steam's compressed voice to VoiceFormat.SampleRate mono. Never throws on what another player sent.
public interface IVoiceDecoder
{
    // Returns how many samples were written: 0 for anything that does not decode.
    int Decompress(ReadOnlySpan<byte> voice, Span<short> pcm);
}

// 16 kHz mono, called from the game's thread.
public interface IVoiceOutput
{
    // Samples handed to Play that have not been heard yet.
    int QueuedSamples { get; }

    void Play(ReadOnlySpan<short> samples);

    // Drops anything queued and releases the output until the next Play.
    void Stop();
}
