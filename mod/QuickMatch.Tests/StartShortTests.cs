using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class StartShortTests
{
    private const ulong Host = 1, A = 2, B = 3;

    [Theory]
    [InlineData(2, 3, false, 120, false, true)]
    [InlineData(3, 4, false, 125, false, true)]
    [InlineData(2, 3, false, 119, false, false)]
    [InlineData(2, 3, false, 120, true, false)]
    [InlineData(1, 3, false, 600, false, false)]
    [InlineData(3, 3, false, 600, false, false)]
    [InlineData(2, 4, true, 600, false, false)]
    public void AsksOnlyAnExactSizeLobbyOfTwoOrMoreThatWaitedShortOfFull(int here, int size, bool anySize, double seconds, bool asked, bool expected)
    {
        Assert.Equal(expected, MatchPolicy.AsksToStartShort(here, size, anySize, seconds, asked));
    }

    [Fact]
    public void LobbyDecisionsWaitForPendingJoinsAndResumeAfterTheLastGuestLeaves()
    {
        ulong[] group = [Host, A];
        Assert.Equal(2, MatchPolicy.SettledQuickMatchGroup(group, Host, [A], group, []));
        Assert.Equal(0, MatchPolicy.SettledQuickMatchGroup(group, Host, [A], [Host, A, B], []));
        Assert.Equal(0, MatchPolicy.SettledQuickMatchGroup(group, Host, [B], group, []));
        Assert.Equal(0, MatchPolicy.SettledQuickMatchGroup([Host, A, B], Host, [A, B], [Host, A, B], [B]));
        Assert.Equal(2, MatchPolicy.SettledQuickMatchGroup(group, Host, [A], [Host], []));
    }

    [Fact]
    public void StartsOnlyWhenEveryoneAskedSaysYes()
    {
        ulong[] asked = [Host, A, B];

        Assert.Equal(StartShortOutcome.Waiting, MatchPolicy.StartShortTally(asked, asked, [Host, A], anyNo: false, timedOut: false));
        Assert.Equal(StartShortOutcome.Agreed, MatchPolicy.StartShortTally(asked, [B, A, Host], [Host, A, B], anyNo: false, timedOut: false));
    }

    [Fact]
    public void OneNoOrNoAnswerInTimeRefuses()
    {
        ulong[] asked = [Host, A, B];

        Assert.Equal(StartShortOutcome.Refused, MatchPolicy.StartShortTally(asked, asked, [Host, A], anyNo: true, timedOut: false));
        Assert.Equal(StartShortOutcome.Refused, MatchPolicy.StartShortTally(asked, asked, [Host, A], anyNo: false, timedOut: true));
    }

    [Fact]
    public void AJoinOrALeaveEndsTheQuestion()
    {
        ulong[] asked = [Host, A];

        Assert.Equal(StartShortOutcome.Ended, MatchPolicy.StartShortTally(asked, [Host], [Host, A], anyNo: false, timedOut: false));
        Assert.Equal(StartShortOutcome.Ended, MatchPolicy.StartShortTally(asked, [Host, A, B], [Host, A], anyNo: false, timedOut: false));
        Assert.Equal(StartShortOutcome.Ended, MatchPolicy.StartShortTally(asked, [Host, B], [Host, A], anyNo: true, timedOut: false));
    }

    [Fact]
    public void MessagesRoundTripAndOthersAreNotRead()
    {
        byte[] message = StartShortWire.Write(StartShortMessage.Yes, 2, 7);

        Assert.True(StartShortWire.TryRead(message, out StartShortMessage kind, out int players, out byte round));
        Assert.Equal((StartShortMessage.Yes, 2, (byte)7), (kind, players, round));
        Assert.False(StartShortWire.TryRead(KickVoteWire.Write(A, VoteMessage.Yes), out _, out _, out _));
        Assert.False(StartShortWire.TryRead([(byte)'Q', (byte)'S', 1, 9, 2, 7], out _, out _, out _));
        Assert.False(StartShortWire.TryRead([(byte)'Q', (byte)'S', 2, 4, 2, 7], out _, out _, out _));
    }
}
