using System;
using System.Buffers.Binary;

namespace QuickMatch.Voice;

// A voice packet as it travels over Steam:
//
//   'Q' 'V' | version u8 | stream u32 | seq u32 | chunks part (VoicePacket.WriteChunks)
//
// Numbers are little-endian. Steam encrypts and authenticates these messages between the two players itself, and
// names the sender, so there is no key of our own.
internal static class SteamVoiceWire
{
    public const byte Magic0 = (byte)'Q';
    public const byte Magic1 = (byte)'V';
    public const byte FormatVersion = 3;
    public const int HeaderSize = 11;

    public const int MaxBytes = HeaderSize + VoicePacket.MaxChunksPartBytes;

    public static byte[] Write(VoicePacket packet)
    {
        byte[] chunks = packet.WriteChunks();
        var message = new byte[HeaderSize + chunks.Length];
        message[0] = Magic0;
        message[1] = Magic1;
        message[2] = FormatVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(3), packet.Stream);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(7), packet.Seq);
        chunks.CopyTo(message, HeaderSize);
        return message;
    }

    // Null for anything malformed or of another version.
    public static VoicePacket? Read(ReadOnlySpan<byte> message)
    {
        if (message.Length < HeaderSize + 2 || message.Length > MaxBytes
            || message[0] != Magic0 || message[1] != Magic1 || message[2] != FormatVersion)
        {
            return null;
        }
        uint stream = BinaryPrimitives.ReadUInt32LittleEndian(message[3..]);
        uint seq = BinaryPrimitives.ReadUInt32LittleEndian(message[7..]);
        return VoicePacket.ReadChunks(stream, seq, message[HeaderSize..]);
    }
}
