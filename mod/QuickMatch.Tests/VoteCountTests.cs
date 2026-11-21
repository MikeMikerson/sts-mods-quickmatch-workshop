using System.Collections.Generic;
using System.Linq;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

// One count decides a vote: the host's for a vote against a client, the starter's for one against the host. Every other
// peer acts on that result, takes a Failed as it is, and takes a Passed only once it has the Yes votes itself.
public class VoteCountTests
{
    private const ulong Host = 76561198000000001;
    private const ulong B = 76561198000000002;
    private const ulong C = 76561198000000003;
    private const ulong D = 76561198000000004;

    private static HashSet<ulong> Players(params ulong[] players) => players.ToHashSet();

    private static KickVote Vote(ulong self, bool inRun = false) => new() { Self = self, HostCanBeRemoved = inRun };

    [Fact]
    public void TheHostDecidesAVoteAgainstAClientAndTheStarterOneAgainstTheHost()
    {
        KickVote client = Vote(C);
        client.Receive(B, D, true, Host, Players(Host, B, C, D), 0);
        Assert.Equal(Host, client.Decider);
        Assert.False(client.Decides);

        KickVote host = Vote(C, inRun: true);
        host.Receive(B, Host, true, Host, Players(Host, B, C), 0);
        Assert.Equal(B, host.Decider);

        KickVote starter = Vote(B, inRun: true);
        starter.Receive(B, Host, true, Host, Players(Host, B, C, D), 0);
        Assert.True(starter.Decides);
    }

    [Fact]
    public void APeerThatDoesNotDecideKeepsNoClockOfItsOwn()
    {
        HashSet<ulong> lobby = Players(Host, B, C, D);
        KickVote vote = Vote(C);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Ignored, vote.Update(lobby, KickVote.VoteSeconds));
        Assert.Equal(D, vote.Target);
        // No result after the vote's time and the wait for it: the vote failed, and nothing is done.
        Assert.Equal(VoteOutcome.Ignored, vote.Update(lobby, KickVote.VoteSeconds + KickVote.ResultWaitSeconds - 1));
        Assert.Equal(VoteOutcome.Failed, vote.Update(lobby, KickVote.VoteSeconds + KickVote.ResultWaitSeconds));
        Assert.False(vote.CanStart(B, D, Host, lobby, KickVote.StartEverySeconds));
    }

    [Fact]
    public void EnoughYesVotesDoNotPassItWithoutTheDecider()
    {
        HashSet<ulong> lobby = Players(Host, B, C, D);
        KickVote vote = Vote(C);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Counted, vote.Receive(C, D, true, Host, lobby, 2));
        Assert.Equal(D, vote.Target);
        Assert.Equal(VoteOutcome.Passed, vote.ReceiveResult(Host, D, VoteOutcome.Passed, lobby, 3));
        Assert.Equal(0UL, vote.Target);
    }

    [Fact]
    public void ALastYesTooLateForTheDeciderFailsTheVoteEverywhere()
    {
        HashSet<ulong> lobby = Players(Host, B, C, D);
        // The host started counting at 0 and C at 1; C's own Yes is 30.5 s after the host's start, inside C's own 30.
        KickVote host = Vote(Host);
        KickVote c = Vote(C);
        host.Receive(B, D, true, Host, lobby, 0);
        c.Receive(B, D, true, Host, lobby, 1);
        Assert.Equal(VoteOutcome.Failed, host.Update(lobby, KickVote.VoteSeconds));
        Assert.Equal(VoteOutcome.Counted, c.Receive(C, D, true, Host, lobby, 30.5));
        Assert.Equal(VoteOutcome.Ignored, host.Receive(C, D, true, Host, lobby, 30.5));
        // C's own count would pass it here; the host's Failed decides.
        Assert.Equal(VoteOutcome.Failed, c.ReceiveResult(Host, D, VoteOutcome.Failed, lobby, 31));
    }

    [Fact]
    public void APassedWithoutItsYesVotesWaitsForThemThenFails()
    {
        HashSet<ulong> lobby = Players(Host, B, C, D);
        KickVote waits = Vote(C);
        waits.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Ignored, waits.ReceiveResult(Host, D, VoteOutcome.Passed, lobby, 5));
        Assert.Equal(VoteOutcome.Ignored, waits.Update(lobby, 5 + KickVote.SupportWaitSeconds - 1));
        Assert.Equal(VoteOutcome.Failed, waits.Update(lobby, 5 + KickVote.SupportWaitSeconds));

        // The Yes the decider counted arrives just after its result: the Passed is taken then.
        KickVote late = Vote(C);
        late.Receive(B, D, true, Host, lobby, 0);
        late.ReceiveResult(Host, D, VoteOutcome.Passed, lobby, 5);
        Assert.Equal(VoteOutcome.Passed, late.Receive(Host, D, true, Host, lobby, 6));
    }

    [Fact]
    public void AResultFromAnyoneButTheDeciderIsIgnored()
    {
        HashSet<ulong> lobby = Players(Host, B, C, D);
        KickVote vote = Vote(C);
        vote.Receive(B, D, true, Host, lobby, 0);
        vote.Receive(C, D, true, Host, lobby, 1);
        Assert.Equal(VoteOutcome.Ignored, vote.ReceiveResult(B, D, VoteOutcome.Passed, lobby, 2));
        Assert.Equal(VoteOutcome.Ignored, vote.ReceiveResult(D, D, VoteOutcome.Failed, lobby, 2));
        Assert.Equal(VoteOutcome.Ignored, vote.ReceiveResult(Host, C, VoteOutcome.Passed, lobby, 2));
        Assert.Equal(D, vote.Target);
        // The decider ignores results too: its own count decides.
        KickVote host = Vote(Host);
        host.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Ignored, host.ReceiveResult(Host, D, VoteOutcome.Passed, lobby, 1));
    }

    [Fact]
    public void ATargetWhoLeftOnSeeingThePassIsStillRemovedEverywhere()
    {
        // In a run, C sees D (voted out) leave before the host's Passed reaches it: C still writes the ban.
        KickVote vote = Vote(C, inRun: true);
        vote.Receive(B, D, true, Host, Players(Host, B, C, D), 0);
        vote.Receive(C, D, true, Host, Players(Host, B, C, D), 1);
        Assert.Equal(VoteOutcome.Ignored, vote.Update(Players(Host, B, C), 2));
        Assert.Equal(VoteOutcome.Passed, vote.ReceiveResult(Host, D, VoteOutcome.Passed, Players(Host, B, C), 2));
    }

    [Fact]
    public void AnEndedResultEndsTheVoteWithNoRetryLimit()
    {
        HashSet<ulong> lobby = Players(Host, B, C, D);
        KickVote vote = Vote(C);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Ended, vote.ReceiveResult(Host, D, VoteOutcome.Ended, lobby, 1));
        Assert.True(vote.CanStart(C, D, Host, lobby, 2));
    }

    [Fact]
    public void AVoteAgainstTheHostIsDecidedByItsStarter()
    {
        HashSet<ulong> run = Players(Host, B, C, D);
        KickVote starter = Vote(B, inRun: true);
        KickVote other = Vote(C, inRun: true);
        KickVote host = Vote(Host, inRun: true);
        foreach (KickVote vote in new[] { starter, other, host })
        {
            vote.Receive(B, Host, true, Host, run, 0);
        }
        Assert.Equal(VoteOutcome.Passed, starter.Receive(D, Host, true, Host, run, 3));
        Assert.Equal(VoteOutcome.Counted, other.Receive(D, Host, true, Host, run, 3));
        Assert.Equal(VoteOutcome.Counted, host.Receive(D, Host, true, Host, run, 3));
        Assert.Equal(VoteOutcome.Passed, other.ReceiveResult(B, Host, VoteOutcome.Passed, run, 4));
        // The host, voted out, takes the starter's result too, not its own.
        Assert.Equal(VoteOutcome.Ignored, host.ReceiveResult(Host, Host, VoteOutcome.Failed, run, 4));
        Assert.Equal(VoteOutcome.Passed, host.ReceiveResult(B, Host, VoteOutcome.Passed, run, 4));
    }

    [Fact]
    public void WithTwoPlayersTheTargetTakesTheDecidersPassAtOnce()
    {
        KickVote target = Vote(B);
        Assert.Equal(VoteOutcome.Started, target.Receive(Host, B, true, Host, Players(Host, B), 0));
        Assert.Equal(VoteOutcome.Passed, target.ReceiveResult(Host, B, VoteOutcome.Passed, Players(Host, B), 0));
    }

    [Theory]
    [InlineData(VoteMessage.No)]
    [InlineData(VoteMessage.Yes)]
    [InlineData(VoteMessage.Failed)]
    [InlineData(VoteMessage.Passed)]
    [InlineData(VoteMessage.Ended)]
    public void EveryVoteMessageRoundTrips(VoteMessage kind)
    {
        Assert.True(KickVoteWire.TryRead(KickVoteWire.Write(D, kind), out ulong target, out VoteMessage read));
        Assert.Equal((D, kind), (target, read));
        Assert.Equal(kind is VoteMessage.No or VoteMessage.Yes, KickVoteWire.OutcomeOf(kind) is null);
    }

    [Fact]
    public void AFormatOneVoteOrAnUnknownKindIsNotRead()
    {
        byte[] old = KickVoteWire.Write(D, VoteMessage.Yes);
        old[2] = 1;
        Assert.False(KickVoteWire.TryRead(old, out _, out _));
        byte[] unknown = KickVoteWire.Write(D, VoteMessage.Ended);
        unknown[11] = 5;
        Assert.False(KickVoteWire.TryRead(unknown, out _, out _));
    }

    [Theory]
    [InlineData(VoteOutcome.Passed, VoteMessage.Passed)]
    [InlineData(VoteOutcome.Failed, VoteMessage.Failed)]
    [InlineData(VoteOutcome.Ended, VoteMessage.Ended)]
    public void AnEndOfTheVoteIsSentAsItsResult(VoteOutcome outcome, VoteMessage message)
    {
        Assert.Equal(message, KickVoteWire.ResultOf(outcome));
        Assert.Equal(outcome, KickVoteWire.OutcomeOf(message));
    }
}
