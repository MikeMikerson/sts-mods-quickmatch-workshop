using QuickMatch.Voice;
using QuickMatch.Platforms.Desktop;
using Xunit;

namespace QuickMatch.Tests;

public class VoiceWireTests
{
    [Fact]
    public void Only_the_current_lobbys_host_can_switch_a_sockets_client_to_the_relay()
    {
        byte[] change = SocketVoiceWire.SwitchToRelay(200);
        Assert.True(SocketVoiceWire.Read(change, lobby: 200, sender: 30, host: 30, null));
        Assert.False(SocketVoiceWire.Read(change, lobby: 200, sender: 71, host: 30, null));
        Assert.False(SocketVoiceWire.Read(change, lobby: 201, sender: 30, host: 30, null));
        Assert.False(SocketVoiceWire.Read(change[..^1], lobby: 200, sender: 30, host: 30, null));
        Assert.False(SocketVoiceWire.Read([.. change, 0], lobby: 200, sender: 30, host: 30, null));
    }

    [Fact]
    public void Packet_chunks_round_trip()
    {
        var packet = new VoicePacket(42, 1000, StartsTalk: true, EndsTalk: false, new[] { new byte[] { 1, 2, 3 }, new byte[] { 4 }, new byte[] { 5, 6 } });
        VoicePacket? read = VoicePacket.ReadChunks(42, 1000, packet.WriteChunks());
        Assert.NotNull(read);
        Assert.True(read!.StartsTalk);
        Assert.False(read.EndsTalk);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, read.Chunks.SelectMany(c => c).ToArray());
    }

    [Fact]
    public void Status_packets_round_trip_and_carry_no_audio()
    {
        VoicePacket? read = VoicePacket.ReadChunks(3, 8, VoicePacket.ForStatus(3, 8, VoiceStatus.ListenOnly).WriteChunks());
        Assert.Equal(VoiceStatus.ListenOnly, read!.Status);
        Assert.Empty(read.Chunks);
        Assert.Null(VoicePacket.ReadChunks(3, 8, new byte[] { 4, 1, 1, 0, 9 }));
        Assert.Null(VoicePacket.ReadChunks(3, 8, new byte[] { 5, 1, 1, 0, 1 }));
        Assert.Null(VoicePacket.ReadChunks(3, 8, new byte[] { 12, 1, 1, 0, 1 }));
        Assert.Null(VoicePacket.ReadChunks(3, 8, new byte[] { 4, 1, 1, 0, 1, 0 }));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0 })] // no chunks
    [InlineData(new byte[] { 0, 9 })] // more than the most chunks a packet may carry
    [InlineData(new byte[] { 16, 1, 1, 0, 9 })] // unknown flag
    [InlineData(new byte[] { 0, 1, 0, 0 })] // an empty chunk
    [InlineData(new byte[] { 0, 1, 2, 0, 9 })] // a chunk longer than the bytes left
    [InlineData(new byte[] { 0, 1, 1, 0, 9, 9 })] // bytes left over
    public void Malformed_packets_are_refused(byte[] part) => Assert.Null(VoicePacket.ReadChunks(1, 1, part));

    [Fact]
    public void Oversized_chunks_are_refused()
    {
        var part = new byte[4 + VoiceFormat.MaxChunkBytes + 1];
        part[1] = 1;
        part[2] = (byte)((VoiceFormat.MaxChunkBytes + 1) & 0xff);
        part[3] = (byte)((VoiceFormat.MaxChunkBytes + 1) >> 8);
        Assert.Null(VoicePacket.ReadChunks(1, 1, part));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AVoicePacketCrossesSteamUnchanged(bool opus)
    {
        var packet = new VoicePacket(42, 1000, StartsTalk: true, EndsTalk: false, new[] { new byte[] { 1, 2, 3 }, new byte[] { 4 }, new byte[] { 5, 6 } }, IsOpus: opus);

        VoicePacket? read = SteamVoiceWire.Read(SteamVoiceWire.Write(packet));

        Assert.NotNull(read);
        Assert.Equal((42u, 1000u, true, false), (read!.Stream, read.Seq, read.StartsTalk, read.EndsTalk));
        Assert.Equal(packet.Chunks, read.Chunks);
        Assert.Equal(opus, read.IsOpus);
    }

    [Fact]
    public void RelayVoiceAuthenticatesCodecSenderAndSequence()
    {
        using var sender = new VoiceCrypto(VoiceCrypto.NewKey());
        using var otherSender = new VoiceCrypto(VoiceCrypto.NewKey());
        var packet = new VoicePacket(42, 1000, true, false, [new byte[] { 1, 2, 3 }], IsOpus: true);
        byte[] sealedPacket = sender.Seal(packet);

        VoicePacket read = Assert.IsType<VoicePacket>(sender.Open(sealedPacket));
        Assert.True(read.IsOpus);
        Assert.Equal(packet.Chunks, read.Chunks);
        Assert.Null(otherSender.Open(sealedPacket));

        byte[] changedSequence = (byte[])sealedPacket.Clone();
        changedSequence[7] ^= 1;
        Assert.Null(sender.Open(changedSequence));
        sealedPacket[VoiceCrypto.HeaderSize] ^= 8;
        Assert.Null(sender.Open(sealedPacket));
    }

    [Fact]
    public void AStatusPacketCrossesSteamUnchanged()
    {
        VoicePacket? read = SteamVoiceWire.Read(SteamVoiceWire.Write(VoicePacket.ForStatus(3, 8, VoiceStatus.Muted)));

        Assert.Equal(VoiceStatus.Muted, read?.Status);
        Assert.Equal((3u, 8u), (read!.Stream, read.Seq));
    }

    [Fact]
    public void SteamVoiceRefusesWhatIsNotAVoicePacket()
    {
        byte[] good = SteamVoiceWire.Write(new VoicePacket(1, 2, false, false, new[] { new byte[] { 9 } }));

        Assert.Null(SteamVoiceWire.Read(good.AsSpan(0, SteamVoiceWire.HeaderSize)));
        Assert.Null(SteamVoiceWire.Read(good.Append((byte)0).ToArray()));
        byte[] otherVersion = (byte[])good.Clone();
        otherVersion[2] = SteamVoiceWire.FormatVersion + 1;
        Assert.Null(SteamVoiceWire.Read(otherVersion));
        byte[] otherMagic = (byte[])good.Clone();
        otherMagic[1] = (byte)'X';
        Assert.Null(SteamVoiceWire.Read(otherMagic));
        Assert.Null(SteamVoiceWire.Read(new byte[SteamVoiceWire.MaxBytes + 1]));
    }
}
