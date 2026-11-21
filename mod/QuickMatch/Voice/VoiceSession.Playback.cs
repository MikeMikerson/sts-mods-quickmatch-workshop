using System;

namespace QuickMatch.Voice;

internal sealed partial class VoiceSession
{
    private void UpdatePlayback()
    {
        if (!Enabled || !Hearing)
        {
            foreach (Peer peer in _peers.Values)
            {
                peer.Buffer.Clear();
            }
            if (_outputActive)
            {
                _output.Stop();
                _outputActive = false;
            }
            return;
        }
        for (int frames = 0; frames < MaxFramesPerUpdate && _output.QueuedSamples < OutputQueueSamples; frames++)
        {
            Array.Clear(_mix);
            bool any = false;
            foreach ((ulong id, Peer peer) in _peers)
            {
                if (!peer.Buffer.Take(_pcm))
                {
                    continue;
                }
                any = true;
                peer.Limiter.Process(_pcm, _mutes.VolumeOf(id));
                for (int i = 0; i < _mix.Length; i++)
                {
                    _mix[i] += _pcm[i];
                }
            }
            if (!any)
            {
                break;
            }
            float volume = Math.Clamp(Volume, 0f, 1f);
            for (int i = 0; i < _mix.Length; i++)
            {
                _mixed[i] = (short)Math.Clamp((int)(_mix[i] * volume), short.MinValue, short.MaxValue);
            }
            _output.Play(_mixed);
            _outputActive = true;
        }
    }
}
