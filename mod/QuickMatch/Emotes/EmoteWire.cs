using System;

namespace QuickMatch.Emotes;

// The statuses an emote can show, in the order the picker lists them.
public enum EmoteStatus : byte
{
    Vulnerable = 0,
    Weak = 1,
    Strength = 2,
    Poison = 3,
}

// Ask: "Who has it?"; otherwise "I have it".
public readonly record struct Emote(EmoteStatus Status, bool Ask);

// An emote as a lobby chat message: "QE", format 1, then the status in the low bits and the ask bit on top.
public static class EmoteWire
{
    public const int Length = 4;
    private const byte Magic0 = (byte)'Q';
    private const byte Magic1 = (byte)'E';
    private const byte Format = 1;
    private const byte AskBit = 0x80;

    public static byte[] Write(Emote emote) =>
        [Magic0, Magic1, Format, (byte)((byte)emote.Status | (emote.Ask ? AskBit : 0))];

    public static bool TryRead(ReadOnlySpan<byte> message, out Emote emote)
    {
        emote = default;
        if (message.Length != Length || message[0] != Magic0 || message[1] != Magic1 || message[2] != Format)
        {
            return false;
        }
        byte status = (byte)(message[3] & ~AskBit);
        if (status > (byte)EmoteStatus.Poison)
        {
            return false;
        }
        emote = new Emote((EmoteStatus)status, (message[3] & AskBit) != 0);
        return true;
    }
}
