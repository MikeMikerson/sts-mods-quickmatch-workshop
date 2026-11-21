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
            _heardAt = _now - MicQuietMs;
            _gate.Reset();
        }
        while (_device!.ReadVoice() is { } chunk)
        {
            CaptureChunk(chunk);
        }
        if (_now - _heardAt >= MicQuietMs)
        {
            _previousChunk = null;
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

    // The gate judges the chunk by its own decoded audio, in whole frames; the rest of a frame waits for the next chunk.
    private void CaptureChunk(byte[] chunk)
    {
        _heardAt = _now;
        int samples = _decoder.Decompress(chunk, _decoded);
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
        var packet = new VoicePacket(_stream, _packetSeq++, _startsTalk, endsTalk, _outgoing.ToArray());
        _outgoing.Clear();
        _outgoingSamples = 0;
        _startsTalk = false;
        if (Link is { IsUp: true } link)
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
        if (Link is not { IsUp: true } link)
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
