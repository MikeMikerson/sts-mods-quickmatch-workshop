using System.Collections.Generic;
using System.Linq;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class RunKickTests
{
    private const ulong Host = 76561198000000001;
    private const ulong B = 76561198000000002;
    private const ulong C = 76561198000000003;
    private const ulong D = 76561198000000004;

    private static HashSet<ulong> Run(params ulong[] players) => players.ToHashSet();

    // The player keeping the vote: the host decides a vote against a client, the starter one against the host.
    private static KickVote InRun(ulong self = Host) => new() { HostCanBeRemoved = true, Self = self };

    [Fact]
    public void InARunTheHostCanBeVotedOut()
    {
        KickVote vote = InRun(self: B);
        Assert.True(vote.CanStart(B, Host, Host, Run(Host, B, C), 0));
        Assert.Equal(VoteOutcome.Started, vote.Receive(B, Host, true, Host, Run(Host, B, C), 0));
        Assert.Equal(VoteOutcome.Passed, vote.Receive(C, Host, true, Host, Run(Host, B, C), 1));
    }

    [Fact]
    public void InALobbyTheHostStillCannot()
    {
        var vote = new KickVote();
        Assert.False(vote.CanStart(B, Host, Host, Run(Host, B, C), 0));
    }

    [Fact]
    public void TheHostHasNoVoteAgainstItself()
    {
        KickVote vote = InRun();
        vote.Receive(B, Host, true, Host, Run(Host, B, C, D), 0);
        Assert.Equal([B, C, D], vote.Voters.OrderBy(v => v));
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(Host, Host, false, Host, Run(Host, B, C, D), 1));
        Assert.Equal(2, vote.YesNeeded);
    }

    [Fact]
    public void WithTwoPlayersInARunEitherCanRemoveTheOther()
    {
        Assert.Equal(VoteOutcome.Passed, InRun(self: B).Receive(B, Host, true, Host, Run(Host, B), 0));
        Assert.Equal(VoteOutcome.Passed, InRun().Receive(Host, B, true, Host, Run(Host, B), 0));
    }

    [Fact]
    public void AnAbsentPlayerCannotBeVotedOnAndHasNoVote()
    {
        KickVote vote = InRun();
        // D dropped: the run's present players are the host, B and C.
        Assert.False(vote.CanStart(B, D, Host, Run(Host, B, C), 0));
        Assert.False(vote.CanStart(D, B, Host, Run(Host, B, C), 0));
        vote.Receive(Host, B, true, Host, Run(Host, B, C), 0);
        Assert.DoesNotContain(D, vote.Voters);
    }

    [Theory]
    [InlineData(B, B, Host, RunKickAction.ForgetRun)]
    [InlineData(Host, Host, Host, RunKickAction.ForgetRun)]
    [InlineData(B, Host, Host, RunKickAction.SendAway)]
    [InlineData(Host, B, Host, RunKickAction.LeaveHost)]
    [InlineData(C, B, Host, RunKickAction.WatchHost)]
    public void EachPlayerKnowsWhatToDoOnceItPasses(ulong target, ulong self, ulong host, RunKickAction action)
    {
        Assert.Equal(action, RunKickPolicy.OnPassed(target, self, host));
    }

    [Fact]
    public void APlayerVotedOutIsRefusedWhateverTheBlockList()
    {
        static bool NotBlocked(ulong _) => false;
        Assert.True(RunKickPolicy.HostRefusesRejoin(B, NotBlocked, [], [B]));
        // Staying with a blocked player does not let in one the party voted out.
        Assert.True(RunKickPolicy.HostRefusesRejoin(B, p => p == B, [B], [B]));
        Assert.False(RunKickPolicy.HostRefusesRejoin(C, NotBlocked, [], [B]));
        Assert.False(RunKickPolicy.HostRefusesRejoin(0, NotBlocked, [], [0]));
    }

    [Fact]
    public void TheBlockListsRulesForRejoinsStay()
    {
        Assert.True(RunKickPolicy.HostRefusesRejoin(C, p => p == C, [], []));
        Assert.False(RunKickPolicy.HostRefusesRejoin(C, p => p == C, [C], []));
    }

    [Fact]
    public void TheResumeSearchSkipsALobbyHostedByAPlayerVotedOut()
    {
        Assert.True(RunKickPolicy.SkipsHost(Host, [Host]));
        Assert.False(RunKickPolicy.SkipsHost(B, [Host]));
        Assert.False(RunKickPolicy.SkipsHost(0, [0]));
    }

    [Fact]
    public void AfterTheHostIsVotedOutTheVotersHostInTheRunsOrder()
    {
        List<ulong> order = MigrationPolicy.HostOrder([Host, B, C, D], oldHost: Host, present: [Host, B, C, D], removed: [Host]);
        Assert.Equal([B, C, D], order);
        Assert.Equal(MigrationPolicy.SearchFirstSeconds, MigrationPolicy.HostAfterSeconds(order, B, hasCopy: true));
    }

    [Fact]
    public void APlayerVotedOutEarlierNeverHostsTheRun()
    {
        // B was voted out before; the host later drops. Even if B were somehow still counted present, B never hosts.
        Assert.Equal([C, D], MigrationPolicy.HostOrder([Host, B, C, D], oldHost: Host, present: [B, C, D], removed: [B]));
    }

    [Fact]
    public void WithNobodyPresentNobodyHosts()
    {
        // Why Regroup keeps its own copy of the present players: the game empties its run lobby's list before it
        // reports the lost host, and an empty list leaves no one to reopen the run.
        Assert.Empty(MigrationPolicy.HostOrder([Host, B, C], oldHost: Host, present: []));
        Assert.Equal([B, C], MigrationPolicy.HostOrder([Host, B, C], oldHost: Host, present: new ulong[] { Host, C }.Append(B)));
    }

    [Fact]
    public void TheTimesKeepTheirOrder()
    {
        // An honest client leaves before the host sends it away, and a voter waits longer than that before leaving.
        Assert.True(RunKickPolicy.SendAwayAfterSeconds < RunKickPolicy.HostRemovesSeconds);
        Assert.True(RunKickPolicy.HostLeavesAfterSeconds > 0);
    }
}
