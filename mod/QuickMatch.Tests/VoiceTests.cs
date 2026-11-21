using QuickMatch.Voice;
using Xunit;
using Xunit.Abstractions;

namespace QuickMatch.Tests;

public class VoiceTests
{
    private const ulong Alice = 76561198000000011;
    private const ulong Bob = 76561198000000012;
    private const ulong Carol = 76561198000000013;
    private const int N = VoiceFormat.FrameSamples;

    private readonly ITestOutputHelper _output;

    public VoiceTests(ITestOutputHelper output) => _output = output;

    // ---------- helpers ----------

    // Two harmonics at levelDb below full scale, like a voice.
    private static short[] Tone(int frameIndex, double levelDb, double hz = 220)
    {
        var frame = new short[N];
        double amplitude = Math.Pow(10, levelDb / 20) * 32767 * Math.Sqrt(2) / 1.25;
        for (int i = 0; i < N; i++)
        {
            double t = (frameIndex * N + i) / (double)VoiceFormat.SampleRate;
            frame[i] = (short)Math.Clamp(amplitude * (Math.Sin(2 * Math.PI * hz * t) + 0.5 * Math.Sin(2 * Math.PI * hz * 3 * t)) / 1.0, short.MinValue, short.MaxValue);
        }
        return frame;
    }

    private static double Db(ReadOnlySpan<short> samples) => VoiceGate.Level(samples);

    // Chunk i of packet seq carries frame k = 3 seq + i, every sample of it Value(k), so the order can be read back.
    private static short Value(int frame) => (short)(1000 + 10 * (frame / 3) + frame % 3);

    private static VoicePacket Packet(uint seq, int count = 3, bool starts = false, bool ends = false, uint stream = 7) =>
        new(stream, seq, starts, ends, Enumerable.Range(0, count)
            .Select(i => FakeCodec.Encode(Enumerable.Repeat(Value(3 * (int)seq + i), N).ToArray())).ToArray());

    // Stands in for Steam's codec: a chunk is the samples' bytes as they are.
    private sealed class FakeCodec : IVoiceDecoder
    {
        public int Decoded;

        public static byte[] Encode(short[] pcm)
        {
            var chunk = new byte[pcm.Length * 2];
            for (int i = 0; i < pcm.Length; i++)
            {
                chunk[2 * i] = (byte)pcm[i];
                chunk[2 * i + 1] = (byte)(pcm[i] >> 8);
            }
            return chunk;
        }

        public int Decompress(ReadOnlySpan<byte> voice, Span<short> pcm)
        {
            Decoded++;
            int count = Math.Min(voice.Length / 2, pcm.Length);
            for (int i = 0; i < count; i++)
            {
                pcm[i] = (short)(voice[2 * i] | (voice[2 * i + 1] << 8));
            }
            return count;
        }
    }

    // Records as Steam does: a chunk for each frame fed while capturing, nothing for an empty one (Steam's silence).
    private sealed class FakeDevice : IVoiceDevice
    {
        public readonly Queue<byte[]> Chunks = new();
        public int Starts;
        public bool Capturing;

        public VoiceCaptureState CaptureState { get; set; } = VoiceCaptureState.Ready;

        public bool StartCapture()
        {
            Starts++;
            Capturing = true;
            return true;
        }

        public void StopCapture() => Capturing = false;

        public byte[]? ReadVoice() => Chunks.TryDequeue(out byte[]? chunk) ? chunk : null;

        public void Feed(short[] frame)
        {
            if (Capturing && frame.Length > 0)
            {
                Chunks.Enqueue(FakeCodec.Encode(frame));
            }
        }
    }

    private sealed class FakeOutput : IVoiceOutput
    {
        public readonly List<short> Played = new();
        public int Queued;
        public int Stops;

        public int QueuedSamples => Queued;

        public void Play(ReadOnlySpan<short> samples)
        {
            Played.AddRange(samples.ToArray());
            Queued += samples.Length;
        }

        public void Stop()
        {
            Stops++;
            Queued = 0;
        }

        // 20 ms of the queue was heard.
        public void Tick() => Queued = Math.Max(0, Queued - N);
    }

    // Delivers packets as Steam carries them (SteamVoiceWire), after a delay in ticks.
    private sealed class FakeLink : IVoiceLink
    {
        private readonly ulong _from;
        private readonly List<(int Due, byte[] Payload)> _inFlight = new();
        public readonly List<VoiceSession> To = new();
        // Audio packets only; status packets are counted apart.
        public int Sent;
        public int StatusSent;
        public int DelayTicks;
        public int Tick;

        public FakeLink(ulong from) => _from = from;

        public bool IsUp { get; set; } = true;

        public void Send(VoicePacket packet)
        {
            if (packet.Status != null)
            {
                StatusSent++;
            }
            else
            {
                Sent++;
            }
            _inFlight.Add((Tick + DelayTicks, SteamVoiceWire.Write(packet)));
        }

        public void Deliver()
        {
            foreach (var item in _inFlight.Where(p => p.Due <= Tick).ToList())
            {
                _inFlight.Remove(item);
                VoicePacket? packet = SteamVoiceWire.Read(item.Payload);
                Assert.NotNull(packet);
                foreach (VoiceSession session in To)
                {
                    session.Receive(_from, packet!);
                }
            }
            Tick++;
        }
    }

    // ---------- gate ----------

    [Fact]
    public void Gate_opens_on_speech_holds_briefly_and_closes_on_silence()
    {
        var gate = new VoiceGate();
        for (int f = 0; f < 50; f++)
        {
            Assert.False(gate.Process(Tone(f, -70)));
        }
        Assert.True(gate.Process(Tone(50, -25)));
        for (int f = 0; f < VoiceGate.HangoverFrames; f++)
        {
            Assert.True(gate.Process(Tone(f, -70)));
        }
        Assert.False(gate.Process(Tone(99, -70)));
    }

    [Fact]
    public void Sensitivity_decides_whether_quiet_speech_opens_the_gate()
    {
        var sensitive = new VoiceGate { Sensitivity = 1 };
        var strict = new VoiceGate { Sensitivity = 0 };
        for (int f = 0; f < 100; f++)
        {
            sensitive.Process(Tone(f, -50));
            strict.Process(Tone(f, -50));
        }
        // 10 dB above the background: enough at sensitivity 1 (6 dB), not at 0 (18 dB).
        Assert.True(sensitive.Process(Tone(100, -40)));
        Assert.False(strict.Process(Tone(100, -40)));
    }

    [Fact]
    public void Loud_speech_opens_the_gate_even_in_a_noisy_room()
    {
        var gate = new VoiceGate { Sensitivity = 0 };
        for (int f = 0; f < 3000; f++)
        {
            gate.Process(Tone(f, -25, hz: 1000));
        }
        Assert.True(gate.Process(Tone(0, -10)));
    }

    // ---------- jitter buffer ----------

    // As the session does: accepted packets are decoded and added chunk by chunk.
    private static void Add(JitterBuffer buffer, VoicePacket packet)
    {
        if (!buffer.Accept(packet))
        {
            return;
        }
        for (int i = 0; i < packet.Chunks.Count; i++)
        {
            var pcm = new short[N];
            new FakeCodec().Decompress(packet.Chunks[i], pcm);
            buffer.Add(pcm, packet.EndsTalk && i == packet.Chunks.Count - 1);
        }
    }

    // The first sample of each frame taken; null while idle.
    private static List<short?> TakeAll(JitterBuffer buffer, int count)
    {
        var frame = new short[N];
        return Enumerable.Range(0, count).Select(_ => buffer.Take(frame) ? frame[0] : (short?)null).ToList();
    }

    [Fact]
    public void Buffer_waits_for_its_target_then_plays_in_order_and_stops_at_the_end()
    {
        var buffer = new JitterBuffer();
        Add(buffer, Packet(0, starts: true));
        Assert.Equal(new short?[] { null }, TakeAll(buffer, 1));
        Add(buffer, Packet(1));
        List<short?> taken = TakeAll(buffer, 1);
        Add(buffer, Packet(2, ends: true));
        taken.AddRange(TakeAll(buffer, 9));
        Assert.Equal(Enumerable.Range(0, 9).Select(k => (short?)Value(k)).Append(null), taken);
        Assert.False(buffer.IsPlaying);
    }

    [Fact]
    public void A_short_word_plays_without_waiting_for_the_target()
    {
        var buffer = new JitterBuffer();
        Add(buffer, Packet(0, count: 2, starts: true, ends: true));
        Assert.Equal(new short?[] { Value(0), Value(1), null }, TakeAll(buffer, 3));
    }

    [Fact]
    public void A_burst_after_a_stall_is_trimmed_so_the_delay_does_not_build_up()
    {
        var buffer = new JitterBuffer();
        for (uint seq = 0; seq < 20; seq++)
        {
            Add(buffer, Packet(seq));
        }
        // At most a packet more than the target is kept.
        Assert.Equal(Value(60 - (JitterBuffer.StartTargetFrames + 2)), TakeAll(buffer, 1)[0]);
        Assert.True(buffer.DroppedSamples > 0);
    }

    [Fact]
    public void Late_and_repeated_packets_are_refused_before_decoding_and_raise_the_target()
    {
        var buffer = new JitterBuffer();
        Assert.True(buffer.Accept(Packet(0)));
        Assert.True(buffer.Accept(Packet(2)));
        int target = buffer.TargetFrames;
        Assert.False(buffer.Accept(Packet(1)));
        Assert.False(buffer.Accept(Packet(2)));
        Assert.Equal(2, buffer.LatePackets);
        Assert.True(buffer.TargetFrames > target);
    }

    [Fact]
    public void A_lost_packet_leaves_no_hole_and_the_next_plays_straight_on()
    {
        var buffer = new JitterBuffer();
        Add(buffer, Packet(0));
        Add(buffer, Packet(1));
        List<short?> taken = TakeAll(buffer, 3);
        // Packet 2 never comes.
        Add(buffer, Packet(3, ends: true));
        taken.AddRange(TakeAll(buffer, 7));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 9, 10, 11 }.Select(k => (short?)Value(k)).Append(null), taken);
    }

    [Fact]
    public void Running_dry_keeps_time_with_silence_then_ends()
    {
        var buffer = new JitterBuffer();
        Add(buffer, Packet(0));
        Add(buffer, Packet(1));
        TakeAll(buffer, 6);
        List<short?> taken = TakeAll(buffer, JitterBuffer.UnderrunEndFrames + 1);
        Assert.Equal(Enumerable.Repeat((short?)0, JitterBuffer.UnderrunEndFrames).Append(null), taken);
        Assert.False(buffer.IsPlaying);
    }

    [Fact]
    public void A_new_stream_replaces_the_old_one_and_a_flood_is_capped()
    {
        var buffer = new JitterBuffer();
        Add(buffer, Packet(0, stream: 1));
        Add(buffer, Packet(5, stream: 2));
        Assert.Equal(3 * N, buffer.BufferedSamples);
        for (uint seq = 6; seq < 60; seq++)
        {
            Add(buffer, Packet(seq, count: 6, stream: 2));
        }
        Assert.Equal(JitterBuffer.MaxFrames * N, buffer.BufferedSamples);
    }

    // ---------- limiter ----------

    [Fact]
    public void Limiter_caps_peaks_and_turns_down_a_speaker_who_stays_too_loud()
    {
        var limiter = new VoiceLimiter();
        var square = new short[N];
        for (int frame = 0; frame < 300; frame++)
        {
            for (int i = 0; i < N; i++)
            {
                square[i] = (i / 20) % 2 == 0 ? short.MaxValue : short.MinValue;
            }
            limiter.Process(square, volume: 1.5f);
            Assert.True(square.Max(s => Math.Abs((int)s)) <= VoiceLimiter.Ceiling * 32768 + 1);
        }
        Assert.True(limiter.TooLoud);
        limiter.ResetLoudness();
        Assert.False(limiter.TooLoud);
    }

    [Fact]
    public void Limiter_leaves_normal_speech_alone()
    {
        var limiter = new VoiceLimiter();
        for (int f = 0; f < 300; f++)
        {
            short[] frame = Tone(f, -24);
            short[] copy = (short[])frame.Clone();
            limiter.Process(frame, volume: 1f);
            Assert.Equal(copy, frame);
        }
        Assert.False(limiter.TooLoud);
    }

    // ---------- mutes ----------

    [Fact]
    public void Mutes_follow_choices_then_the_strangers_setting()
    {
        var mutes = new VoiceMutes();
        int changes = 0;
        mutes.Changed += () => changes++;
        Assert.False(mutes.IsMuted(Bob, isFriend: false));
        mutes.StrangersStartMuted = true;
        Assert.True(mutes.IsMuted(Bob, isFriend: false));
        Assert.False(mutes.IsMuted(Carol, isFriend: true));
        mutes.Unmute(Bob);
        Assert.False(mutes.IsMuted(Bob, isFriend: false));
        mutes.Mute(Carol);
        Assert.True(mutes.IsMuted(Carol, isFriend: true));
        mutes.MuteAll(new[] { Bob, Carol });
        Assert.Equal(new[] { Bob, Carol }, mutes.Muted);
        Assert.Equal(3, changes);

        mutes.SetVolume(Bob, 5f);
        Assert.Equal(VoiceMutes.MaxVolume, mutes.VolumeOf(Bob));
        mutes.SetVolume(Bob, 1f);
        Assert.Empty(mutes.Volumes);

        var loaded = new VoiceMutes();
        loaded.Load(new[] { Carol }, new[] { Bob }, new[] { KeyValuePair.Create(Carol, 0.5f) });
        loaded.StrangersStartMuted = true;
        Assert.False(loaded.IsMuted(Bob, isFriend: false));
        Assert.True(loaded.IsMuted(Carol, isFriend: true));
        Assert.Equal(0.5f, loaded.VolumeOf(Carol));
    }

    // ---------- whole sessions ----------

    private sealed class Match
    {
        public readonly FakeDevice AliceDevice = new();
        public readonly FakeOutput AliceOutput = new();
        public readonly FakeOutput BobOutput = new();
        public readonly VoiceMutes BobMutes = new();
        public readonly FakeCodec BobCodec = new();
        public readonly VoiceSession AliceVoice;
        public readonly VoiceSession BobVoice;
        public readonly FakeLink AliceLink;
        public readonly FakeLink BobLink;
        public long Now;

        public Match(FakeDevice? bobDevice = null, Action<string>? bobLog = null)
        {
            AliceVoice = new VoiceSession(Alice, AliceDevice, new FakeCodec(), AliceOutput, new VoiceMutes(), _ => false, null, stream: 5);
            BobVoice = new VoiceSession(Bob, bobDevice, BobCodec, BobOutput, BobMutes, _ => false, bobLog, stream: 6);
            AliceLink = new FakeLink(Alice) { DelayTicks = 2 };
            AliceLink.To.Add(BobVoice);
            AliceVoice.Link = AliceLink;
            BobLink = new FakeLink(Bob);
            BobLink.To.Add(AliceVoice);
            BobVoice.Link = BobLink;
            AliceVoice.SetMembers(new[] { Alice, Bob });
            BobVoice.SetMembers(new[] { Alice, Bob });
        }

        public void Run(int ticks, Func<int, short[]> sound)
        {
            for (int t = 0; t < ticks; t++)
            {
                AliceDevice.Feed(sound(t));
                AliceVoice.Update(Now);
                AliceLink.Deliver();
                BobVoice.Update(Now);
                BobLink.Deliver();
                AliceOutput.Tick();
                BobOutput.Tick();
                Now += VoiceFormat.FrameMs;
            }
        }
    }

    [Fact]
    public void Two_players_talk_over_the_Steam_wire_and_only_speech_is_sent()
    {
        var match = new Match();
        Assert.Equal(LocalVoiceState.Muted, match.AliceVoice.LocalState);
        match.Run(20, f => Tone(f, -30));
        Assert.Equal(0, match.AliceLink.Sent);

        match.AliceVoice.SetMic(true);
        match.Run(50, f => Tone(f, -70));
        int afterSilence = match.AliceLink.Sent;
        Assert.Equal(0, afterSilence);

        match.Run(100, f => Tone(f, -25));
        Assert.Equal(LocalVoiceState.Speaking, match.AliceVoice.LocalState);
        Assert.True(match.BobVoice.IsSpeaking(Alice));
        Assert.InRange(match.AliceLink.Sent, 30, 36);

        match.Run(60, f => Tone(f, -70));
        Assert.Equal(LocalVoiceState.Live, match.AliceVoice.LocalState);
        Assert.False(match.BobVoice.IsSpeaking(Alice));
        double heard = Db(match.BobOutput.Played.ToArray());
        _output.WriteLine($"Bob heard {match.BobOutput.Played.Count / (double)VoiceFormat.SampleRate:F2} s at {heard:F1} dB; {match.AliceLink.Sent} packets");
        Assert.True(match.BobOutput.Played.Count >= 100 * N);
        Assert.InRange(heard, -36, -24);
    }

    [Fact]
    public void Speech_ends_when_Steam_goes_quiet()
    {
        var match = new Match();
        match.AliceVoice.SetMic(true);
        match.Run(30, f => Tone(f, -25));
        Assert.Equal(LocalVoiceState.Speaking, match.AliceVoice.LocalState);
        match.Run((int)(VoiceSession.MicQuietMs / VoiceFormat.FrameMs) + 1, _ => Array.Empty<short>());
        Assert.Equal(LocalVoiceState.Live, match.AliceVoice.LocalState);
        Assert.Equal(0.0, match.AliceVoice.MicLevel);
        match.Run(20, _ => Array.Empty<short>());
        Assert.False(match.BobVoice.IsSpeaking(Alice));
    }

    [Fact]
    public void The_match_summary_keeps_what_a_player_who_left_first_had_sent()
    {
        // A player who leaves before the match ends must not take what was heard from them
        // out of the others' summary lines.
        var lines = new List<string>();
        var match = new Match(bobLog: lines.Add);
        match.AliceVoice.SetMic(true);
        match.Run(60, f => Tone(f, -25));
        match.Run(20, f => Tone(f, -70));
        int sent = match.AliceLink.Sent;
        Assert.True(sent > 0);

        match.BobVoice.SetMembers(new[] { Bob });
        Assert.False(match.BobVoice.IsSpeaking(Alice));
        match.BobVoice.Dispose();

        string summary = Assert.Single(lines, line => line.StartsWith("voice: "));
        Assert.Contains($"{Alice}: {sent} received, 0 muted, 0 late", summary);
        Assert.DoesNotContain(" 0 ms decoded", summary);
    }

    [Fact]
    public void A_muted_player_shows_as_speaking_but_is_never_played()
    {
        var match = new Match();
        match.BobVoice.Mute(Alice);
        match.AliceVoice.SetMic(true);
        match.Run(60, f => Tone(f, -25));
        Assert.True(match.BobVoice.IsSpeaking(Alice));
        Assert.True(match.BobVoice.IsMuted(Alice));
        Assert.Empty(match.BobOutput.Played);
        Assert.Equal(0, match.BobCodec.Decoded);
        Assert.Equal(new[] { Alice }, match.BobMutes.Muted);
    }

    [Fact]
    public void Listening_off_plays_nothing()
    {
        var match = new Match();
        match.BobVoice.Hearing = false;
        match.AliceVoice.SetMic(true);
        match.Run(60, f => Tone(f, -25));
        Assert.Empty(match.BobOutput.Played);
    }

    [Fact]
    public void Without_Steam_the_mic_never_turns_on_and_the_player_only_listens()
    {
        var match = new Match();
        match.AliceDevice.CaptureState = VoiceCaptureState.Unsupported;
        match.AliceVoice.SetMic(true);
        match.Run(30, f => Tone(f, -25));
        Assert.False(match.AliceVoice.MicOn);
        Assert.Equal(LocalVoiceState.ListenOnly, match.AliceVoice.LocalState);
        Assert.Equal(0, match.AliceDevice.Starts);
        Assert.Equal(0, match.AliceLink.Sent);
    }

    [Fact]
    public void States_for_no_device_no_link_and_voice_off()
    {
        var match = new Match();
        Assert.Equal(LocalVoiceState.ListenOnly, match.BobVoice.LocalState);
        ((FakeLink)match.BobVoice.Link!).IsUp = false;
        Assert.Equal(LocalVoiceState.Unavailable, match.BobVoice.LocalState);
        match.BobVoice.Enabled = false;
        Assert.Equal(LocalVoiceState.Off, match.BobVoice.LocalState);

        match.AliceVoice.SetMic(true);
        match.AliceLink.IsUp = false;
        match.Run(30, f => Tone(f, -25));
        Assert.Equal(0, match.AliceLink.Sent);
        Assert.False(match.AliceDevice.Capturing);
    }

    [Fact]
    public void Each_player_sees_what_the_others_voice_is_set_to()
    {
        var match = new Match();
        match.Run(5, f => Tone(f, -70));
        Assert.Equal(VoiceStatus.Muted, match.BobVoice.StatusOf(Alice));
        // Bob has no microphone: listening only.
        Assert.Equal(VoiceStatus.ListenOnly, match.AliceVoice.StatusOf(Bob));

        match.AliceVoice.SetMic(true);
        match.Run(5, f => Tone(f, -70));
        Assert.Equal(VoiceStatus.Live, match.BobVoice.StatusOf(Alice));
        int statusPackets = match.AliceLink.StatusSent;

        // Repeated only every few seconds while nothing changes.
        match.Run((int)(VoiceSession.StatusEveryMs / VoiceFormat.FrameMs), f => Tone(f, -70));
        Assert.Equal(statusPackets + 1, match.AliceLink.StatusSent);

        match.AliceVoice.Enabled = false;
        match.Run(5, f => Tone(f, -70));
        Assert.Equal(VoiceStatus.Off, match.BobVoice.StatusOf(Alice));
    }

    // A player whose match has no voice is never heard: mic on or off, the mic button pressed any number of times, with
    // speech at the mic, the microphone never starts and no audio packet leaves.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_voice_off_no_audio_ever_leaves(bool micOn)
    {
        var match = new Match();
        match.AliceVoice.Enabled = false;
        match.AliceVoice.SetMic(micOn);
        match.Run(60, f => Tone(f, -20));
        for (int press = 0; press < 3; press++)
        {
            match.AliceVoice.ToggleMic();
            match.Run(20, f => Tone(f, -20));
        }
        Assert.Equal(0, match.AliceLink.Sent);
        Assert.Equal(0, match.AliceDevice.Starts);
        Assert.Empty(match.BobOutput.Played);
        Assert.Equal(VoiceStatus.Off, match.BobVoice.StatusOf(Alice));
    }

    // In a voice match the mic button is the only way to be heard.
    [Fact]
    public void With_the_mic_off_nothing_is_recorded_or_sent()
    {
        var match = new Match();
        match.Run(40, f => Tone(f, -20));
        Assert.Equal(0, match.AliceLink.Sent);
        Assert.Equal(0, match.AliceDevice.Starts);
        match.AliceVoice.SetMic(true);
        match.Run(40, f => Tone(f, -20));
        Assert.True(match.AliceLink.Sent > 0);
        match.AliceVoice.SetMic(false);
        int sent = match.AliceLink.Sent;
        match.Run(40, f => Tone(f, -20));
        Assert.Equal(sent, match.AliceLink.Sent);
        Assert.False(match.AliceDevice.Capturing);
    }

    [Fact]
    public void A_player_with_voice_off_is_marked_and_neither_hears_nor_is_heard()
    {
        // A run gone back into with voice turned off since it began is the one match that holds both.
        var bobDevice = new FakeDevice();
        var match = new Match(bobDevice);
        match.BobVoice.Enabled = false;
        match.AliceVoice.SetMic(true);
        match.BobVoice.SetMic(true);
        match.Run(30, f => Tone(f, -25));
        Assert.Equal(VoiceStatus.Off, match.AliceVoice.StatusOf(Bob));
        Assert.True(match.AliceLink.Sent > 0);
        Assert.Empty(match.BobOutput.Played);
        Assert.Equal(0, match.BobLink.Sent);
        Assert.False(bobDevice.Capturing);
    }

    [Fact]
    public void Packets_from_outside_the_match_and_floods_are_dropped()
    {
        var match = new Match();
        match.BobVoice.Update(0);
        match.BobVoice.Receive(Carol, Packet(0, starts: true));
        Assert.False(match.BobVoice.IsSpeaking(Carol));
        Assert.Null(match.BobVoice.StatusOf(Carol));
        for (uint i = 0; i < 200; i++)
        {
            match.BobVoice.Receive(Alice, Packet(i));
        }
        // Only the first 60 of the second were taken: at most 50 frames wait.
        match.BobVoice.Update(0);
        Assert.True(match.BobOutput.Played.Count <= VoiceSession.OutputQueueSamples);
    }
}
