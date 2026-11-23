using System;
using Concentus.Enums;
using Concentus.Structs;

namespace QuickMatch.Voice;

internal sealed class VoiceEncoder
{
    private readonly OpusEncoder _encoder;
    private readonly byte[] _buffer = new byte[VoiceFormat.MaxFrameBytes];

    public VoiceEncoder()
    {
#pragma warning disable CS0618 // The replacement factory depends on the optional native library.
        _encoder = new OpusEncoder(VoiceFormat.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
#pragma warning restore CS0618
        _encoder.Bitrate = VoiceFormat.Bitrate;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        _encoder.Complexity = 5;
    }

    public byte[] Encode(ReadOnlySpan<short> frame)
    {
        if (frame.Length != VoiceFormat.FrameSamples)
        {
            throw new ArgumentException($"Expected {VoiceFormat.FrameSamples} samples.", nameof(frame));
        }
        int length = _encoder.Encode(frame, VoiceFormat.FrameSamples, _buffer, _buffer.Length);
        return _buffer.AsSpan(0, length).ToArray();
    }

    public void Reset() => _encoder.ResetState();
}

internal sealed class VoiceDecoder : IVoiceDecoder
{
#pragma warning disable CS0618 // The replacement factory depends on the optional native library.
    private readonly OpusDecoder _decoder = new(VoiceFormat.SampleRate, 1);
#pragma warning restore CS0618

    public int Decompress(ReadOnlySpan<byte> frame, Span<short> pcm)
    {
        if (frame.IsEmpty || frame.Length > VoiceFormat.MaxFrameBytes || pcm.Length < VoiceFormat.FrameSamples)
        {
            return 0;
        }
        try
        {
            int count = _decoder.Decode(frame, pcm, VoiceFormat.FrameSamples, decode_fec: false);
            if (count == VoiceFormat.FrameSamples)
            {
                return count;
            }
        }
        catch (Exception)
        {
            // Malformed Opus can fail inside the managed decoder.
        }
        Reset();
        return 0;
    }

    public void Reset() => _decoder.ResetState();
}
