using System;
using QuickMatch.Voice;
using Api = STS2Mobile.ModApi;

namespace QuickMatch.Platforms.IPhone;

internal sealed class IPhoneVoiceDevice(Api.IVoiceDevice device) : IPcmVoiceDevice, IVoiceOutput
{
    public VoiceCaptureState CaptureState => device.CaptureState switch
    {
        Api.MicrophoneState.NeedsPermission => VoiceCaptureState.NeedsPermission,
        Api.MicrophoneState.Asking => VoiceCaptureState.Asking,
        Api.MicrophoneState.Denied => VoiceCaptureState.Denied,
        Api.MicrophoneState.Ready => VoiceCaptureState.Ready,
        _ => VoiceCaptureState.Unsupported,
    };

    public void RequestPermission() => device.RequestPermission();
    public bool StartCapture() => device.StartCapture();
    public int ReadCapture(Span<short> destination) => device.ReadCapture(destination);
    public void StopCapture() => device.StopCapture();
    public int QueuedSamples => device.QueuedSamples;
    public void Play(ReadOnlySpan<short> samples) => device.Play(samples);
    public void Stop() => device.StopOutput();
}
