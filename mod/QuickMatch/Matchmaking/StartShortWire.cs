using System;

namespace QuickMatch.Matchmaking;

public enum StartShortOutcome
{
    Waiting,
    Agreed,
    Refused,
    Ended,
}

public enum StartShortMessage : byte
{
    // From the host: the question, and how it ended.
    Ask = 0,
    Agreed = 1,
    Refused = 2,
    Ended = 3,
    // From a player: the answer.
    Yes = 4,
    No = 5,
}

// The question whether to start short of full, as a lobby chat message: "QS", format 1, a StartShortMessage, the
// number of players and the host's round, so an answer to an earlier question is not counted toward this one.
public static class StartShortWire
{
    public const int Length = 6;
    private const byte Magic0 = (byte)'Q';
    private const byte Magic1 = (byte)'S';
    private const byte Format = 1;

    public static byte[] Write(StartShortMessage kind, int players, byte round) =>
        [Magic0, Magic1, Format, (byte)kind, (byte)players, round];

    public static bool TryRead(ReadOnlySpan<byte> message, out StartShortMessage kind, out int players, out byte round)
    {
        kind = StartShortMessage.Ask;
        players = 0;
        round = 0;
        if (message.Length != Length || message[0] != Magic0 || message[1] != Magic1 || message[2] != Format
            || message[3] > (byte)StartShortMessage.No || message[4] > 4)
        {
            return false;
        }
        kind = (StartShortMessage)message[3];
        players = message[4];
        round = message[5];
        return true;
    }
}
