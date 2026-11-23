using System;
using MegaCrit.Sts2.Core.Platform.Steam;
using Steamworks;

namespace QuickMatch.Voice;

// Steam records with the microphone and noise handling chosen in its voice settings.
internal sealed class SteamVoiceDevice : IVoiceDevice
{
    private readonly byte[] _compressed = new byte[8 * 1024];
    private bool _recording;

    private SteamVoiceDevice()
    {
    }

    public static SteamVoiceDevice? TryCreate() => SteamInitializer.Initialized ? new SteamVoiceDevice() : null;

    public VoiceCaptureState CaptureState => SteamInitializer.Initialized ? VoiceCaptureState.Ready : VoiceCaptureState.Unsupported;

    public bool StartCapture()
    {
        if (!SteamInitializer.Initialized)
        {
            return false;
        }
        SteamUser.StartVoiceRecording();
        _recording = true;
        return true;
    }

    public void StopCapture()
    {
        if (_recording)
        {
            SteamUser.StopVoiceRecording();
            _recording = false;
        }
    }

    public byte[]? ReadVoice()
    {
        while (_recording && SteamUser.GetAvailableVoice(out uint available) == EVoiceResult.k_EVoiceResultOK && available > 0)
        {
            if (SteamUser.GetVoice(true, _compressed, (uint)_compressed.Length, out uint written) != EVoiceResult.k_EVoiceResultOK || written == 0)
            {
                return null;
            }
            // More than a chunk holds is speech held back while the game stalled: dropped, so the delay does not grow.
            if (written <= VoiceFormat.MaxChunkBytes)
            {
                return _compressed.AsSpan(0, (int)written).ToArray();
            }
        }
        return null;
    }
}
