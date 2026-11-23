using System;

namespace QuickMatch.Voice;

internal sealed partial class VoiceSession
{
    private void UpdateCapture()
    {
        bool allowed = Enabled && Link is { IsUp: true } && _device?.CaptureState == VoiceCaptureState.Ready;
        bool talk = allowed && MicOn;
        if (!talk)
        {
            StopCapture();
            return;
        }
        if (!_capturing)
        {
            if (!_device!.StartCapture())
            {
                return;
            }
            _capturing = true;
            _frameCount = 0;
            _previousChunk = null;
            _hasPreviousFrame = false;
            _heardAt = _now - MicQuietMs;
            _gate.Reset();
        }
        if (Link!.UsesOpus && _device is IPcmVoiceDevice device)
        {
            int count;
            while ((count = device.ReadCapture(_decoded)) > 0)
            {
                CaptureOpus(_decoded.AsSpan(0, count));
            }
        }
        else if (_steamDecoder != null)
        {
            while (_device!.ReadVoice() is { } chunk)
            {
                int count = _steamDecoder.Decompress(chunk, _decoded);
                if (Link.UsesOpus)
                {
                    CaptureOpus(_decoded.AsSpan(0, count));
                }
                else
                {
                    CaptureChunk(chunk, count);
                }
            }
        }
        if (_now - _heardAt >= MicQuietMs)
        {
            _previousChunk = null;
            _hasPreviousFrame = false;
            _frameCount = 0;
            if (_sending)
            {
                _sending = false;
                _gate.Reset();
                Flush(endsTalk: true);
            }
        }
        if (_outgoing.Count > 0 && _now - _outgoingAt >= VoiceFormat.PacketMs)
        {
            Flush(endsTalk: false);
        }
    }

    private void CaptureOpus(ReadOnlySpan<short> samples)
    {
        while (!samples.IsEmpty)
        {
            _heardAt = _now;
            int count = Math.Min(samples.Length, _frame.Length - _frameCount);
            samples[..count].CopyTo(_frame.AsSpan(_frameCount));
            _frameCount += count;
            samples = samples[count..];
            if (_frameCount < _frame.Length)
            {
                continue;
            }
            _frameCount = 0;
            if (_gate.Process(_frame))
            {
                _encoder ??= new VoiceEncoder();
                if (!_sending)
                {
                    _sending = true;
                    _startsTalk = true;
                    _encoder.Reset();
                    if (_hasPreviousFrame)
                    {
                        Queue(_encoder.Encode(_previousFrame!), _frame.Length);
                    }
                }
                Queue(_encoder.Encode(_frame), _frame.Length);
            }
            else if (_sending)
            {
                _sending = false;
                Flush(endsTalk: true);
            }
            _previousFrame ??= new short[_frame.Length];
            _frame.CopyTo(_previousFrame, 0);
            _hasPreviousFrame = true;
        }
    }

    // The gate judges the chunk by its own decoded audio, in whole frames; the rest of a frame waits for the next chunk.
    private void CaptureChunk(byte[] chunk, int samples)
    {
        _heardAt = _now;
        bool open = false;
        bool judged = false;
        for (int i = 0; i < samples; i++)
        {
            _frame[_frameCount++] = _decoded[i];
            if (_frameCount == _frame.Length)
            {
                open |= _gate.Process(_frame);
                judged = true;
                _frameCount = 0;
            }
        }
        if (!judged)
        {
            open = _gate.IsOpen;
        }
        if (open)
        {
            if (!_sending)
            {
                _sending = true;
                _startsTalk = true;
                // The chunk just before speech was detected carries the start of the first word.
                if (_previousChunk != null)
                {
                    Queue(_previousChunk, _previousSamples);
                }
            }
            Queue(chunk, samples);
        }
        else if (_sending)
        {
            _sending = false;
            Flush(endsTalk: true);
        }
        _previousChunk = chunk;
        _previousSamples = samples;
    }

    private void Queue(byte[] chunk, int samples)
    {
        if (_outgoing.Count == 0)
        {
            _outgoingAt = _now;
        }
        _outgoing.Add(chunk);
        _outgoingSamples += samples;
        if (_outgoingSamples >= VoiceFormat.PacketSamples || _outgoing.Count == VoiceFormat.MaxChunksPerPacket)
        {
            Flush(endsTalk: false);
        }
    }

    private void Flush(bool endsTalk)
    {
        if (_outgoing.Count == 0)
        {
            return;
        }
        var packet = new VoicePacket(_stream, _packetSeq++, _startsTalk, endsTalk, _outgoing.ToArray(), Link?.UsesOpus == true);
        _outgoing.Clear();
        _outgoingSamples = 0;
        _startsTalk = false;
        if (Enabled && MicOn && Link is { IsUp: true } link)
        {
            link.Send(packet);
            _packetsSent++;
        }
    }

    private void StopCapture()
    {
        if (!_capturing)
        {
            return;
        }
        if (_sending)
        {
            _sending = false;
            Flush(endsTalk: true);
        }
        _outgoing.Clear();
        _outgoingSamples = 0;
        _device?.StopCapture();
        _capturing = false;
        _gate.Reset();
    }

    private void UpdateStatus()
    {
        if (Link is not { IsUp: true } link || (link.UsesOpus && !Enabled))
        {
            _statusSent = null;
            return;
        }
        VoiceStatus status = LocalStatus;
        if (status == _statusSent && _now - _statusSentAt < StatusEveryMs)
        {
            return;
        }
        // Under the inverted stream number, so a status packet never shares a stream with audio.
        link.Send(VoicePacket.ForStatus(~_stream, _statusSeq++, status));
        _statusSent = status;
        _statusSentAt = _now;
    }
}
