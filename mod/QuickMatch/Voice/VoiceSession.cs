using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace QuickMatch.Voice;

internal interface IVoiceLink
{
    bool IsUp { get; }

    void Send(VoicePacket packet);
}

// What the local player's mic button shows.
internal enum LocalVoiceState
{
    Off,
    Unavailable,
    // Steam cannot record on this device.
    ListenOnly,
    Muted,
    // Mic on, nothing being sent (silence).
    Live,
    Speaking,
}

// Voice chat for one match, on the game's thread. The local player is recorded only when they choose
// to talk, only speech is sent, and muted players are dropped before anything is decoded. The device,
// the decoder, the output and the link are handed in, so the whole class runs in the offline tests.
internal sealed partial class VoiceSession : IDisposable
{
    // 40 ms queued at the output rides out an uneven frame rate.
    public const int OutputQueueSamples = 2 * VoiceFormat.FrameSamples;
    // A player counts as speaking for this long after their last packet.
    public const long SpeakingHoldMs = 300;
    // Players send about 17 packets a second; beyond this the rest are dropped.
    public const int MaxPacketsPerSecond = 60;
    // The local status is sent when it changes and at least this often, for players who join later.
    public const long StatusEveryMs = 5000;
    // Steam sends nothing during silence: this long without voice from it and the mic is quiet.
    public const long MicQuietMs = 200;
    // A cap, so a stalled game frame never turns into a burst of work.
    private const int MaxFramesPerUpdate = 8;

    private sealed class Peer
    {
        public readonly JitterBuffer Buffer = new();
        public readonly VoiceLimiter Limiter = new();
        public long SpeakingUntil = long.MinValue;
        public VoiceStatus? Status;
        public long WindowStart;
        public int WindowPackets;
        public int Received;
        public int Dropped;
        public long Decoded;
    }

    private readonly ulong _local;
    private readonly IVoiceDevice? _device;
    private readonly IVoiceDecoder _decoder;
    private readonly IVoiceOutput _output;
    private readonly VoiceMutes _mutes;
    private readonly Func<ulong, bool> _isFriend;
    private readonly Action<string>? _log;
    private readonly uint _stream;
    private readonly HashSet<ulong> _members = new();
    private readonly Dictionary<ulong, Peer> _peers = new();
    // Kept for the summary line after a player leaves.
    private readonly Dictionary<ulong, (int Received, int Dropped, int Late, long Decoded)> _tallies = new();
    private readonly VoiceGate _gate = new();
    private readonly short[] _decoded = new short[VoiceFormat.MaxChunkSamples];
    private readonly short[] _frame = new short[VoiceFormat.FrameSamples];
    private int _frameCount;
    private byte[]? _previousChunk;
    private int _previousSamples;
    private long _heardAt;
    private readonly List<byte[]> _outgoing = new();
    private int _outgoingSamples;
    private long _outgoingAt;
    private uint _packetSeq;
    private bool _capturing;
    private bool _sending;
    private bool _startsTalk;
    private readonly short[] _pcm = new short[VoiceFormat.FrameSamples];
    private readonly int[] _mix = new int[VoiceFormat.FrameSamples];
    private readonly short[] _mixed = new short[VoiceFormat.FrameSamples];
    private long _now;
    private bool _outputActive;
    private int _packetsSent;
    private int _packetsTooMany;
    private uint _statusSeq;
    private VoiceStatus? _statusSent;
    private long _statusSentAt;

    public VoiceSession(ulong local, IVoiceDevice? device, IVoiceDecoder decoder, IVoiceOutput output, VoiceMutes mutes,
        Func<ulong, bool> isFriend, Action<string>? log, uint? stream = null)
    {
        _local = local;
        _device = device;
        _decoder = decoder;
        _output = output;
        _mutes = mutes;
        _isFriend = isFriend;
        _log = log;
        _stream = stream ?? BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
    }

    public IVoiceLink? Link { get; set; }

    // Off: nothing is recorded, sent or played, whatever the mic does.
    public bool Enabled { get; set; } = true;

    // Off drops everything the others send.
    public bool Hearing { get; set; } = true;

    // Starts muted in every match. With it on, speech is sent by voice activation.
    public bool MicOn { get; private set; }

    public float Volume { get; set; } = 1f;

    public double Sensitivity
    {
        get => _gate.Sensitivity;
        set => _gate.Sensitivity = value;
    }

    public double MicLevel => _capturing && _now - _heardAt < MicQuietMs ? _gate.Meter : 0;

    public bool CanCapture => _device?.CaptureState == VoiceCaptureState.Ready;

    public LocalVoiceState LocalState
    {
        get
        {
            if (!Enabled)
            {
                return LocalVoiceState.Off;
            }
            if (Link is not { IsUp: true })
            {
                return LocalVoiceState.Unavailable;
            }
            if (!CanCapture)
            {
                return LocalVoiceState.ListenOnly;
            }
            if (_sending)
            {
                return LocalVoiceState.Speaking;
            }
            return MicOn ? LocalVoiceState.Live : LocalVoiceState.Muted;
        }
    }

    // Packets from anyone else are dropped.
    public IReadOnlyCollection<ulong> Members => _members;

    public void SetMembers(IEnumerable<ulong> members)
    {
        _members.Clear();
        foreach (ulong member in members)
        {
            if (member != _local)
            {
                _members.Add(member);
            }
        }
        foreach (ulong gone in _peers.Keys.Where(id => !_members.Contains(id)).ToList())
        {
            KeepTally(gone, _peers[gone]);
            _peers.Remove(gone);
        }
    }

    public void SetMic(bool on)
    {
        if (!on)
        {
            MicOn = false;
        }
        else if (CanCapture)
        {
            MicOn = true;
        }
    }

    public void ToggleMic() => SetMic(!MicOn);

    public bool IsSpeaking(ulong player) => _peers.TryGetValue(player, out Peer? peer) && _now < peer.SpeakingUntil;

    public bool IsMuted(ulong player) => _mutes.IsMuted(player, _isFriend(player));

    public bool IsTooLoud(ulong player) => _peers.TryGetValue(player, out Peer? peer) && peer.Limiter.TooLoud;

    public VoiceStatus? StatusOf(ulong player) => _peers.TryGetValue(player, out Peer? peer) ? peer.Status : null;

    public VoiceStatus LocalStatus => LocalState switch
    {
        LocalVoiceState.Off => VoiceStatus.Off,
        LocalVoiceState.ListenOnly => VoiceStatus.ListenOnly,
        LocalVoiceState.Live or LocalVoiceState.Speaking => VoiceStatus.Live,
        _ => CanCapture ? VoiceStatus.Muted : VoiceStatus.ListenOnly,
    };

    public void Mute(ulong player)
    {
        _mutes.Mute(player);
        if (_peers.TryGetValue(player, out Peer? peer))
        {
            peer.Buffer.Clear();
        }
    }

    public void Unmute(ulong player) => _mutes.Unmute(player);

    public void MuteEveryone()
    {
        _mutes.MuteAll(_members);
        foreach (Peer peer in _peers.Values)
        {
            peer.Buffer.Clear();
        }
    }

    public float VolumeOf(ulong player) => _mutes.VolumeOf(player);

    // Also undoes the player's automatic turning down.
    public void SetVolume(ulong player, float volume)
    {
        _mutes.SetVolume(player, volume);
        if (_peers.TryGetValue(player, out Peer? peer))
        {
            peer.Limiter.ResetLoudness();
        }
    }

    public void Receive(ulong sender, VoicePacket packet)
    {
        if (sender == _local || !_members.Contains(sender))
        {
            return;
        }
        if (!_peers.TryGetValue(sender, out Peer? peer))
        {
            peer = new Peer { WindowStart = _now };
            _peers[sender] = peer;
        }
        if (_now - peer.WindowStart >= 1000)
        {
            peer.WindowStart = _now;
            peer.WindowPackets = 0;
        }
        if (++peer.WindowPackets > MaxPacketsPerSecond)
        {
            _packetsTooMany++;
            return;
        }
        if (packet.Status is { } status)
        {
            peer.Status = status;
            return;
        }
        peer.Received++;
        // The ring shows who is talking even when they are muted, so the listener knows whom they muted.
        peer.SpeakingUntil = packet.EndsTalk ? _now + SpeakingHoldMs / 3 : _now + SpeakingHoldMs;
        if (!Enabled || !Hearing || IsMuted(sender))
        {
            peer.Dropped++;
            return;
        }
        if (!peer.Buffer.Accept(packet))
        {
            return;
        }
        for (int i = 0; i < packet.Chunks.Count; i++)
        {
            int samples = _decoder.Decompress(packet.Chunks[i], _decoded);
            peer.Decoded += samples;
            peer.Buffer.Add(_decoded.AsSpan(0, samples), packet.EndsTalk && i == packet.Chunks.Count - 1);
        }
    }

    public void Update(long now)
    {
        _now = now;
        UpdateCapture();
        UpdatePlayback();
        UpdateStatus();
    }

    public void Dispose()
    {
        StopCapture();
        if (_outputActive)
        {
            _output.Stop();
            _outputActive = false;
        }
        foreach ((ulong id, Peer peer) in _peers)
        {
            KeepTally(id, peer);
        }
        if (_packetsSent > 0 || _tallies.Count > 0)
        {
            string heard = string.Join(", ", _tallies.Select(t => $"{t.Key}: {t.Value.Received} received, {t.Value.Dropped} muted, " +
                $"{t.Value.Late} late, {t.Value.Decoded * 1000 / VoiceFormat.SampleRate} ms decoded"));
            _log?.Invoke($"voice: {_packetsSent} packets sent; {heard}; {_packetsTooMany} over the rate limit");
        }
        _peers.Clear();
        _tallies.Clear();
    }

    // A player who leaves and comes back in the same match gets a new peer; their counts add up.
    private void KeepTally(ulong id, Peer peer)
    {
        _tallies.TryGetValue(id, out var tally);
        _tallies[id] = (tally.Received + peer.Received, tally.Dropped + peer.Dropped, tally.Late + peer.Buffer.LatePackets,
            tally.Decoded + peer.Decoded);
    }
}
