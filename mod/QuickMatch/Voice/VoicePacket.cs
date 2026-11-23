using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace QuickMatch.Voice;

// What a player's voice is set to, as their teammates' rows show it.
internal enum VoiceStatus : byte
{
    Off = 0,
    // They hear the others but cannot record their microphone.
    ListenOnly = 1,
    Muted = 2,
    // Their mic is on; they may be silent.
    Live = 3,
}

// Stream identifies a speaker's voice session. Seq orders its packets.
//
//   chunks part = flags u8 | chunk count u8 | (length u16 | encoded audio) per chunk
//
// Flags: 1 = the first packet after silence, 2 = the last packet before silence, 4 = a status packet, whose one
// 1-byte chunk is the sender's VoiceStatus (sent when it changes and every few seconds, under its own stream number).
// Flag 8 selects Opus. With it clear, audio is in Steam's own format. Numbers are little-endian.
internal sealed record VoicePacket(uint Stream, uint Seq, bool StartsTalk, bool EndsTalk, IReadOnlyList<byte[]> Chunks,
    bool IsOpus = false)
{
    private const byte StartFlag = 1;
    private const byte EndFlag = 2;
    private const byte StatusFlag = 4;
    private const byte OpusFlag = 8;

    // Set on a status packet, which carries no audio.
    public VoiceStatus? Status { get; private init; }

    // seq must grow with each status packet sent under stream.
    public static VoicePacket ForStatus(uint stream, uint seq, VoiceStatus status) =>
        new(stream, seq, false, false, Array.Empty<byte[]>()) { Status = status };

    public const int MaxChunksPartBytes = 2 + VoiceFormat.MaxChunksPerPacket * (2 + VoiceFormat.MaxChunkBytes);

    public byte[] WriteChunks()
    {
        if (Status is { } status)
        {
            return new byte[] { StatusFlag, 1, 1, 0, (byte)status };
        }
        int length = 2;
        foreach (byte[] chunk in Chunks)
        {
            length += 2 + chunk.Length;
        }
        var part = new byte[length];
        part[0] = (byte)((StartsTalk ? StartFlag : 0) | (EndsTalk ? EndFlag : 0) | (IsOpus ? OpusFlag : 0));
        part[1] = (byte)Chunks.Count;
        int at = 2;
        foreach (byte[] chunk in Chunks)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(part.AsSpan(at), (ushort)chunk.Length);
            chunk.CopyTo(part, at + 2);
            at += 2 + chunk.Length;
        }
        return part;
    }

    // Null for anything malformed from another player: no chunks, too many, an empty or oversized chunk, or bytes left
    // over.
    public static VoicePacket? ReadChunks(uint stream, uint seq, ReadOnlySpan<byte> part)
    {
        if (part.Length < 2 || part.Length > MaxChunksPartBytes)
        {
            return null;
        }
        byte flags = part[0];
        int count = part[1];
        if (flags == StatusFlag)
        {
            return part.Length == 5 && count == 1 && part[2] == 1 && part[3] == 0 && part[4] <= (byte)VoiceStatus.Live
                ? ForStatus(stream, seq, (VoiceStatus)part[4])
                : null;
        }
        if (count is 0 or > VoiceFormat.MaxChunksPerPacket || (flags & ~(StartFlag | EndFlag | OpusFlag)) != 0)
        {
            return null;
        }
        var chunks = new byte[count][];
        int at = 2;
        for (int i = 0; i < count; i++)
        {
            if (part.Length < at + 2)
            {
                return null;
            }
            int length = BinaryPrimitives.ReadUInt16LittleEndian(part[at..]);
            at += 2;
            if (length is 0 or > VoiceFormat.MaxChunkBytes || part.Length < at + length)
            {
                return null;
            }
            chunks[i] = part.Slice(at, length).ToArray();
            at += length;
        }
        return at == part.Length
            ? new VoicePacket(stream, seq, (flags & StartFlag) != 0, (flags & EndFlag) != 0, chunks, (flags & OpusFlag) != 0)
            : null;
    }
}
