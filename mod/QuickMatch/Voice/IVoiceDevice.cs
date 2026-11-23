using System;

namespace QuickMatch.Voice;

public enum VoiceCaptureState
{
    Unsupported,
    NeedsPermission,
    Asking,
    Denied,
    Ready,
}

public static class VoiceDevice
{
    public static IVoiceDevice? Current { get; set; }
}

// All calls come from the game's thread.
public interface IVoiceDevice
{
    VoiceCaptureState CaptureState { get; }

    void RequestPermission() { }

    bool StartCapture();

    void StopCapture();

    // Steam's compressed voice recorded since the last read, oldest first; null when there is none. Steam delivers
    // nothing during silence.
    byte[]? ReadVoice();
}

// A platform microphone that supplies 16 kHz mono samples directly.
public interface IPcmVoiceDevice : IVoiceDevice
{
    int ReadCapture(Span<short> destination);

    byte[]? IVoiceDevice.ReadVoice() => null;
}

// Decodes one compressed chunk to VoiceFormat.SampleRate mono.
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
