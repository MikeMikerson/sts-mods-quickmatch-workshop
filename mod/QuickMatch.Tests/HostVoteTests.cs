using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class HostVoteTests
{
    private const ulong Host = 10, Voter = 20, Other = 30;

    [Fact]
    public void AVoterWaitsForTheHostVotedOutToLeaveFirst()
    {
        // The host takes a vote only from a player present, so the voters stay; the host leaves soon after the result, and
        // a voter leaves by itself only well after the host should have gone.
        Assert.True(RunKickPolicy.HostLeavesAfterSeconds > 0);
        Assert.True(RunKickPolicy.VoterWaitsForHostSeconds >= RunKickPolicy.HostLeavesAfterSeconds + 5);
    }

    [Fact]
    public void TheHostOfATwoPlayerRunTakesTheVoteAgainstItWhileTheVoterIsPresent()
    {
        // The voter decides at once (1 of 1) and sends its Yes, then the result. The host, the voter still present, starts
        // the same vote from the Yes and takes the Passed: it leaves as voted out.
        var voter = new KickVote { HostCanBeRemoved = true, Self = Voter };
        Assert.Equal(VoteOutcome.Passed, voter.Receive(Voter, Host, yes: true, Host, [Host, Voter], now: 0));

        var host = new KickVote { HostCanBeRemoved = true, Self = Host };
        Assert.Equal(VoteOutcome.Started, host.Receive(Voter, Host, yes: true, Host, [Host, Voter], now: 0.2));
        Assert.Equal(VoteOutcome.Passed, host.ReceiveResult(Voter, Host, VoteOutcome.Passed, [Host, Voter], now: 0.3));
        Assert.Equal(RunKickAction.ForgetRun, RunKickPolicy.OnPassed(Host, Host, Host));
    }

    [Fact]
    public void AVoteFromAPlayerWhoHasLeftIsNotTaken()
    {
        // Why the voter must stay: once its drop has reached the host, its Yes no longer counts.
        var host = new KickVote { HostCanBeRemoved = true, Self = Host };
        Assert.Equal(VoteOutcome.Ignored, host.Receive(Voter, Host, yes: true, Host, [Host], now: 0));
    }

    [Fact]
    public void APlayerVotedOutIsNotWaitedFor()
    {
        Assert.Equal([Other], RejoinPolicy.Awaited([Host, Voter, Other], [Host], removed: [Voter]));
        Assert.Empty(RejoinPolicy.Awaited([Host, Voter], [Host], removed: [Voter]));
        Assert.False(RejoinPolicy.ListsRun(RejoinPolicy.Awaited([Host, Voter], [Host], removed: [Voter]).Count));
        Assert.True(RejoinPolicy.ListsRun(RejoinPolicy.Awaited([Host, Voter], [Host], removed: []).Count));
    }

    [Fact]
    public void AResumeLobbyIsNoGroupLookingForPlayers()
    {
        Assert.True(RejoinPolicy.IsResumeListing("15"));
        Assert.False(RejoinPolicy.IsResumeListing(null));
        Assert.False(RejoinPolicy.IsResumeListing(""));
    }

    [Fact]
    public void ARunBeginsWithoutAskingWhenOnlyPlayersVotedOutAreMissing()
    {
        Assert.True(RunKickPolicy.OnlyRemovedMissing([Host, Voter], [Voter], removed: [Host]));
        Assert.False(RunKickPolicy.OnlyRemovedMissing([Host, Voter, Other], [Voter], removed: [Host]));
        Assert.False(RunKickPolicy.OnlyRemovedMissing([Host, Voter], [Host, Voter], removed: []));
        Assert.False(RunKickPolicy.OnlyRemovedMissing([Host, Voter], [Voter], removed: []));
    }

    [Fact]
    public void NobodyIsAskedToReplaceAHostVotedOut()
    {
        Assert.True(MigrationPolicy.AsksBeforeHosting(Host, removed: []));
        Assert.True(MigrationPolicy.AsksBeforeHosting(Host, removed: [Other]));
        Assert.False(MigrationPolicy.AsksBeforeHosting(Host, removed: [Host]));
    }

    [Fact]
    public void EachInTheOrderHasAMinuteAfterTheFirstSearch()
    {
        List<ulong> order = [Voter, Other];
        Assert.Equal(0UL, MigrationPolicy.TurnAt(order, 0));
        Assert.Equal(0UL, MigrationPolicy.TurnAt(order, MigrationPolicy.SearchFirstSeconds - 0.1));
        Assert.Equal(Voter, MigrationPolicy.TurnAt(order, MigrationPolicy.SearchFirstSeconds));
        Assert.Equal(Voter, MigrationPolicy.TurnAt(order, MigrationPolicy.SearchFirstSeconds + MigrationPolicy.NextHostSeconds - 0.1));
        Assert.Equal(Other, MigrationPolicy.TurnAt(order, MigrationPolicy.SearchFirstSeconds + MigrationPolicy.NextHostSeconds));
        Assert.Equal(0UL, MigrationPolicy.TurnAt(order, MigrationPolicy.SearchFirstSeconds + 2 * MigrationPolicy.NextHostSeconds));
        Assert.Equal(0UL, MigrationPolicy.TurnAt([], 100));
    }

    [Fact]
    public void ATurnIsTheTimeToAnswerAndTheNextHostsWhenItEnds()
    {
        // The next candidate's own turn begins as this one's question runs out, so the order keeps its pace.
        Assert.Equal(MigrationPolicy.NextHostSeconds, MigrationPolicy.AnswerSeconds);
        List<ulong> order = [Voter, Other];
        double voterAsked = MigrationPolicy.HostAfterSeconds(order, Voter, hasCopy: true)!.Value;
        double otherAsked = MigrationPolicy.HostAfterSeconds(order, Other, hasCopy: true)!.Value;
        Assert.Equal(voterAsked + MigrationPolicy.AnswerSeconds, otherAsked);
    }

    [Fact]
    public void TheQuestionHostsOnlyOnHostTheRun()
    {
        Assert.Equal(TakeOverStep.Wait, MigrationPolicy.TakeOverNow(null, 0));
        Assert.Equal(TakeOverStep.Wait, MigrationPolicy.TakeOverNow(null, MigrationPolicy.AnswerSeconds - 0.1));
        Assert.Equal(TakeOverStep.Join, MigrationPolicy.TakeOverNow(null, MigrationPolicy.AnswerSeconds));
        Assert.Equal(TakeOverStep.Host, MigrationPolicy.TakeOverNow(true, 5));
        Assert.Equal(TakeOverStep.Join, MigrationPolicy.TakeOverNow(false, 5));
        // An answer counts even when it came on the last moment the loop saw.
        Assert.Equal(TakeOverStep.Host, MigrationPolicy.TakeOverNow(true, MigrationPolicy.AnswerSeconds + 3));
    }
}
