using System;
using System.Buffers.Binary;

namespace QuickMatch.Matchmaking;

public enum VoteMessage : byte
{
    No = 0,
    Yes = 1,
    Failed = 2,
    Passed = 3,
    Ended = 4,
}

// A vote as a lobby chat message: "QV", format 2, the target's Steam ID (little-endian) and a VoteMessage.
public static class KickVoteWire
{
    public const int Length = 12;
    private const byte Magic0 = (byte)'Q';
    private const byte Magic1 = (byte)'V';
    private const byte Format = 2;

    public static byte[] Write(ulong target, bool yes) => Write(target, yes ? VoteMessage.Yes : VoteMessage.No);

    public static byte[] Write(ulong target, VoteMessage kind)
    {
        var message = new byte[Length];
        message[0] = Magic0;
        message[1] = Magic1;
        message[2] = Format;
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(3), target);
        message[11] = (byte)kind;
        return message;
    }

    public static bool TryRead(ReadOnlySpan<byte> message, out ulong target, out VoteMessage kind)
    {
        target = 0;
        kind = VoteMessage.No;
        if (message.Length != Length || message[0] != Magic0 || message[1] != Magic1 || message[2] != Format || message[11] > (byte)VoteMessage.Ended)
        {
            return false;
        }
        target = BinaryPrimitives.ReadUInt64LittleEndian(message[3..]);
        kind = (VoteMessage)message[11];
        return target != 0;
    }

    public static VoteMessage? ResultOf(VoteOutcome outcome) => outcome switch
    {
        VoteOutcome.Passed => VoteMessage.Passed,
        VoteOutcome.Failed => VoteMessage.Failed,
        VoteOutcome.Ended => VoteMessage.Ended,
        _ => null,
    };

    public static VoteOutcome? OutcomeOf(VoteMessage kind) => kind switch
    {
        VoteMessage.Passed => VoteOutcome.Passed,
        VoteMessage.Failed => VoteOutcome.Failed,
        VoteMessage.Ended => VoteOutcome.Ended,
        _ => null,
    };
}
