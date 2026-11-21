using System;
using Steamworks;

namespace QuickMatch.Voice;

internal sealed class SteamVoiceDecoder : IVoiceDecoder
{
    private readonly byte[] _compressed = new byte[VoiceFormat.MaxChunkBytes];
    private readonly byte[] _pcm = new byte[VoiceFormat.MaxChunkSamples * 2];

    public int Decompress(ReadOnlySpan<byte> voice, Span<short> pcm)
    {
        if (voice.IsEmpty || voice.Length > _compressed.Length)
        {
            return 0;
        }
        voice.CopyTo(_compressed);
        if (SteamUser.DecompressVoice(_compressed, (uint)voice.Length, _pcm, (uint)_pcm.Length, out uint bytes, VoiceFormat.SampleRate)
            != EVoiceResult.k_EVoiceResultOK)
        {
            return 0;
        }
        int samples = Math.Min((int)bytes / 2, pcm.Length);
        for (int i = 0; i < samples; i++)
        {
            pcm[i] = (short)(_pcm[2 * i] | (_pcm[2 * i + 1] << 8));
        }
        return samples;
    }
}
