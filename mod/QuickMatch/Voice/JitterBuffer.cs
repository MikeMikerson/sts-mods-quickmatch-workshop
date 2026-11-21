using System;
using System.Collections.Generic;

namespace QuickMatch.Voice;

// Puts one speaker's decoded voice back in time. Packets can come in bursts (a slow moment on the network holds them
// back and then delivers them at once), so playing starts only once TargetFrames of audio are waiting (keeping at most
// a packet more), and while playing anything more than the target plus CatchUpSlack is dropped from the oldest end, so
// the delay never builds up. A packet that comes after a later one is refused before it is decoded; Steam's decoder
// covers the gap a lost one leaves. The target grows by a frame when a packet comes too late or the buffer runs dry,
// and shrinks again after a calm stretch. A packet marked as the end of speech lets the buffer stop as soon as it has
// played; one that never comes ends speech after UnderrunEndFrames without audio.
//
// Take is called once per 20 ms frame of playback, which is the buffer's only clock.
internal sealed class JitterBuffer
{
    public const int MinTargetFrames = 4;
    public const int StartTargetFrames = 5;
    public const int MaxTargetFrames = 10;
    public const int CatchUpSlack = 6;
    // One second: a flooding sender cannot make more wait.
    public const int MaxFrames = 50;
    // 200 ms without audio and the speech is taken as over.
    public const int UnderrunEndFrames = 10;
    // 10 s of calm before the target shrinks by one.
    public const int CalmFramesToShrink = 500;

    private const int N = VoiceFormat.FrameSamples;
    private const int PacketFrames = VoiceFormat.PacketSamples / N;

    private readonly Queue<short> _samples = new();
    private uint _stream;
    private bool _hasStream;
    private uint _seq;
    private bool _hasSeq;
    private bool _endQueued;
    private int _underrun;
    private int _calm;

    public int TargetFrames { get; private set; } = StartTargetFrames;

    public bool IsPlaying { get; private set; }

    public int BufferedSamples => _samples.Count;

    public int LatePackets { get; private set; }

    public int DroppedSamples { get; private set; }

    // Whether to decode the packet and Add it: false for one that came after a later one, or twice.
    public bool Accept(VoicePacket packet)
    {
        if (!_hasStream || packet.Stream != _stream)
        {
            // A new voice session of this speaker (their game restarted, or voice was switched off and on).
            Clear();
            _stream = packet.Stream;
            _hasStream = true;
            _hasSeq = false;
        }
        if (_hasSeq && packet.Seq <= _seq)
        {
            LatePackets++;
            Grow();
            return false;
        }
        _seq = packet.Seq;
        _hasSeq = true;
        return true;
    }

    // endsTalk on the last chunk of a packet marked as the end of speech.
    public void Add(ReadOnlySpan<short> pcm, bool endsTalk)
    {
        foreach (short sample in pcm)
        {
            _samples.Enqueue(sample);
        }
        _endQueued = endsTalk;
        Trim(MaxFrames * N);
    }

    // Fills frame with the next 20 ms (silence where nothing has arrived) and returns true while playing; returns false,
    // leaving frame as it was, while idle.
    public bool Take(Span<short> frame)
    {
        if (!IsPlaying)
        {
            if (_samples.Count == 0 || (_samples.Count < TargetFrames * N && !_endQueued))
            {
                return false;
            }
            // Audio arrives a packet at a time, so up to a packet more than the target is normal; anything beyond that
            // is a burst after a stall and is dropped.
            Trim((TargetFrames + PacketFrames - 1) * N);
            IsPlaying = true;
            _underrun = 0;
        }
        else if (_samples.Count > (TargetFrames + CatchUpSlack) * N)
        {
            Trim(TargetFrames * N);
        }

        int count = Math.Min(frame.Length, _samples.Count);
        for (int i = 0; i < count; i++)
        {
            frame[i] = _samples.Dequeue();
        }
        frame[count..].Clear();

        if (count == frame.Length)
        {
            _underrun = 0;
            if (++_calm >= CalmFramesToShrink && TargetFrames > MinTargetFrames)
            {
                TargetFrames--;
                _calm = 0;
            }
        }
        else if (!_endQueued && ++_underrun == 1)
        {
            Grow();
        }

        if (_samples.Count == 0 && _endQueued)
        {
            IsPlaying = false;
            _endQueued = false;
        }
        else if (_underrun >= UnderrunEndFrames)
        {
            IsPlaying = false;
        }
        return true;
    }

    public void Clear()
    {
        _samples.Clear();
        _endQueued = false;
        IsPlaying = false;
    }

    private void Trim(int keep)
    {
        while (_samples.Count > keep)
        {
            _samples.Dequeue();
            DroppedSamples++;
        }
    }

    private void Grow()
    {
        _calm = 0;
        if (TargetFrames < MaxTargetFrames)
        {
            TargetFrames++;
        }
    }
}
