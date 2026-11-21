using System.Collections.Generic;
using System.Linq;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class HarassmentTests
{
    private const ulong Host = 76561198000000001;
    private const ulong B = 76561198000000002;
    private const ulong C = 76561198000000003;
    private const ulong D = 76561198000000004;

    private static HashSet<ulong> Lobby(params ulong[] players) => players.ToHashSet();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void AVoteNeedsAMajorityOfTheOthers(int voters, int needed)
    {
        Assert.Equal(needed, KickVote.Needed(voters));
    }

    [Fact]
    public void WithTwoPlayersTheHostRemovesTheClientAtOnce()
    {
        var vote = new KickVote { Self = Host };
        Assert.Equal(VoteOutcome.Passed, vote.Receive(Host, B, true, Host, Lobby(Host, B), 0));
        Assert.Equal(0UL, vote.Target);
    }

    [Fact]
    public void NobodyCanVoteOutTheHost()
    {
        var vote = new KickVote { Self = Host };
        Assert.False(vote.CanStart(B, Host, Host, Lobby(Host, B), 0));
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(B, Host, true, Host, Lobby(Host, B, C), 0));
        Assert.Equal(0UL, vote.Target);
    }

    [Fact]
    public void WithThreePlayersBothOthersMustAgree()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C);
        Assert.Equal(VoteOutcome.Started, vote.Receive(B, C, true, Host, lobby, 0));
        Assert.Equal(2, vote.YesNeeded);
        Assert.Equal(VoteOutcome.Passed, vote.Receive(Host, C, true, Host, lobby, 5));
    }

    [Fact]
    public void WithThreePlayersOneNoEndsTheVote()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C);
        vote.Receive(Host, C, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Failed, vote.Receive(B, C, false, Host, lobby, 5));
        Assert.Equal(0UL, vote.Target);
    }

    [Fact]
    public void WithFourPlayersTwoOfTheThreeOthersAreEnough()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C, D);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Counted, vote.Receive(Host, D, false, Host, lobby, 2));
        Assert.Equal(VoteOutcome.Passed, vote.Receive(C, D, true, Host, lobby, 4));
    }

    [Fact]
    public void TheTargetHasNoVoteAndNobodyVotesTwice()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C, D);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(D, D, false, Host, lobby, 1));
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(B, D, true, Host, lobby, 1));
        Assert.Equal(1, vote.YesCount);
        Assert.DoesNotContain(D, vote.Voters);
    }

    [Fact]
    public void NoAnswerIsANoWhenTheTimeRunsOut()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C, D);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.Equal(VoteOutcome.Ignored, vote.Update(lobby, KickVote.VoteSeconds - 1));
        Assert.Equal(VoteOutcome.Failed, vote.Update(lobby, KickVote.VoteSeconds));
    }

    [Fact]
    public void AVoterWhoLeavesCountsAsNo()
    {
        var vote = new KickVote { Self = Host };
        vote.Receive(B, D, true, Host, Lobby(Host, B, C, D), 0);
        // B, who started it, leaves: their Yes goes, so the vote passes only if Host and C both say Yes.
        HashSet<ulong> after = Lobby(Host, C, D);
        vote.Receive(Host, D, true, Host, after, 2);
        Assert.Equal(VoteOutcome.Passed, vote.Receive(C, D, true, Host, after, 3));

        var other = new KickVote { Self = Host };
        other.Receive(B, D, true, Host, Lobby(Host, B, C, D), 0);
        Assert.Equal(VoteOutcome.Failed, other.Receive(Host, D, false, Host, after, 2));
    }

    [Fact]
    public void ANewcomerHasNoVote()
    {
        var vote = new KickVote { Self = Host };
        vote.Receive(B, C, true, Host, Lobby(Host, B, C), 0);
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(D, C, true, Host, Lobby(Host, B, C, D), 1));
        Assert.Equal(1, vote.YesCount);
    }

    [Fact]
    public void TheVoteEndsWhenItsPlayerLeaves()
    {
        var vote = new KickVote { Self = Host };
        vote.Receive(B, D, true, Host, Lobby(Host, B, C, D), 0);
        Assert.Equal(VoteOutcome.Ended, vote.Update(Lobby(Host, B, C), 1));
        // Leaving is not a failed vote: they can be voted on as soon as they are back.
        Assert.True(vote.CanStart(C, D, Host, Lobby(Host, B, C, D), 2));
    }

    [Fact]
    public void OneVoteRunsAtATime()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C, D);
        vote.Receive(B, D, true, Host, lobby, 0);
        Assert.False(vote.CanStart(Host, C, Host, lobby, 1));
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(Host, C, true, Host, lobby, 1));
        Assert.Equal(D, vote.Target);
    }

    [Fact]
    public void EachPlayerStartsOneVoteInTwoMinutes()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C, D);
        vote.Receive(B, D, true, Host, lobby, 0);
        vote.Receive(Host, D, false, Host, lobby, 1);
        vote.Receive(C, D, false, Host, lobby, 1);
        Assert.False(vote.CanStart(B, C, Host, lobby, KickVote.StartEverySeconds - 1));
        Assert.True(vote.CanStart(B, C, Host, lobby, KickVote.StartEverySeconds));
    }

    [Fact]
    public void AFailedVoteAgainstAPlayerWaitsFiveMinutes()
    {
        var vote = new KickVote { Self = Host };
        HashSet<ulong> lobby = Lobby(Host, B, C, D);
        vote.Receive(B, D, true, Host, lobby, 0);
        vote.Receive(Host, D, false, Host, lobby, 1);
        vote.Receive(C, D, false, Host, lobby, 1);
        Assert.False(vote.CanStart(C, D, Host, lobby, KickVote.RetrySameSeconds));
        Assert.True(vote.CanStart(C, D, Host, lobby, 1 + KickVote.RetrySameSeconds));
    }

    [Fact]
    public void ANoCannotStartAVote()
    {
        var vote = new KickVote { Self = Host };
        Assert.Equal(VoteOutcome.Ignored, vote.Receive(B, C, false, Host, Lobby(Host, B, C), 0));
        Assert.Equal(0UL, vote.Target);
    }

    [Fact]
    public void AVoteMessageRoundTrips()
    {
        byte[] message = KickVoteWire.Write(D, yes: true);
        Assert.Equal(KickVoteWire.Length, message.Length);
        Assert.True(KickVoteWire.TryRead(message, out ulong target, out VoteMessage yes));
        Assert.Equal((D, VoteMessage.Yes), (target, yes));
        Assert.True(KickVoteWire.TryRead(KickVoteWire.Write(D, yes: false), out _, out VoteMessage no));
        Assert.Equal(VoteMessage.No, no);
    }

    [Fact]
    public void OtherLobbyMessagesAreNotVotes()
    {
        byte[] message = KickVoteWire.Write(D, yes: true);
        message[1] = (byte)'C';
        Assert.False(KickVoteWire.TryRead(message, out _, out _));
        Assert.False(KickVoteWire.TryRead(KickVoteWire.Write(D, true).Take(11).ToArray(), out _, out _));
        Assert.False(KickVoteWire.TryRead(KickVoteWire.Write(0, true), out _, out _));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(119, true)]
    [InlineData(120, false)]
    public void AShortStayIsFiveSecondsToTwoMinutes(long stay, bool counts)
    {
        Assert.Equal(counts, JoinLeaveRecord.IsShortStay(1_000, 1_000 + stay));
    }

    [Fact]
    public void ThreeShortStaysInHalfAnHourAreAStrike()
    {
        var record = new JoinLeaveRecord();
        Assert.False(record.NoteLeave(B, 0, 30));
        Assert.False(record.NoteLeave(B, 100, 130));
        Assert.Null(record.RefusedUntil(B, 131));
        Assert.True(record.NoteLeave(B, 200, 230));
        Assert.Equal(230 + 300, record.RefusedUntil(B, 231));
        Assert.Null(record.RefusedUntil(B, 230 + 300));
    }

    [Fact]
    public void ShortStaysOlderThanHalfAnHourFallAway()
    {
        var record = new JoinLeaveRecord();
        record.NoteLeave(B, 0, 30);
        record.NoteLeave(B, 100, 130);
        Assert.False(record.NoteLeave(B, 1_800, 1_830));
        Assert.Null(record.RefusedUntil(B, 1_831));
    }

    [Fact]
    public void LongAndInstantStaysDoNotCount()
    {
        var record = new JoinLeaveRecord();
        for (int i = 0; i < 5; i++)
        {
            Assert.False(record.NoteLeave(B, i * 200, i * 200 + 2));
            Assert.False(record.NoteLeave(C, i * 200, i * 200 + 150));
        }
        Assert.Equal(0, record.Count);
    }

    [Fact]
    public void EachStrikeRefusesLongerUpToADay()
    {
        var record = new JoinLeaveRecord();
        long[] expected = [300, 1_800, 7_200, 86_400, 86_400];
        long now = 0;
        foreach (long seconds in expected)
        {
            record.Strike(B, now);
            Assert.Equal(now + seconds, record.RefusedUntil(B, now));
            now += 60;
        }
        Assert.Equal(4, record.Find(B)!.Step);
    }

    [Fact]
    public void EachDayWithoutAStrikeIsOneStepDown()
    {
        var record = new JoinLeaveRecord();
        record.Strike(B, 0);
        record.Strike(B, 10);
        record.Strike(B, 20);
        SeenPlayer entry = record.Find(B)!;
        Assert.Equal(3, JoinLeaveRecord.StepNow(entry, 20));
        Assert.Equal(2, JoinLeaveRecord.StepNow(entry, 20 + 86_400));
        Assert.Equal(0, JoinLeaveRecord.StepNow(entry, 20 + 3 * 86_400));
        // A strike two days later starts from step 1 again: the next refusal is 30 minutes, not 24 hours.
        long later = 20 + 2 * 86_400;
        record.Strike(B, later);
        Assert.Equal(later + 1_800, record.RefusedUntil(B, later));
    }

    [Fact]
    public void AVoteKickIsAStrikeAtOnce()
    {
        var record = new JoinLeaveRecord();
        record.Strike(C, 1_000);
        Assert.Equal(1_300, record.RefusedUntil(C, 1_000));
    }

    [Fact]
    public void ThePlayersBackOnStepZeroAreNotKept()
    {
        var record = new JoinLeaveRecord();
        record.Strike(B, 0);
        record.NoteLeave(C, 0, 30);
        Assert.Equal([B, C], record.ToKeep(60).Select(p => p.Id));
        Assert.Equal([B], record.ToKeep(1_830).Select(p => p.Id));
        Assert.Empty(record.ToKeep(86_400));
        record.Prune(86_400);
        Assert.Equal(0, record.Count);
    }

    [Fact]
    public void ARecordReadBackKeepsItsCooldown()
    {
        var record = new JoinLeaveRecord();
        record.Strike(B, 0);
        record.Strike(B, 100);
        var reread = new JoinLeaveRecord();
        reread.Load(record.ToKeep(200).Select(p => new SeenPlayer { Id = p.Id, Step = p.Step, StepAt = p.StepAt, CoolUntil = p.CoolUntil, ShortStays = p.ShortStays.ToList() }));
        Assert.Equal(100 + 1_800, reread.RefusedUntil(B, 200));
        reread.Strike(B, 2_000);
        Assert.Equal(2_000 + 7_200, reread.RefusedUntil(B, 2_000));
    }

    [Fact]
    public void ChangesAreReported()
    {
        var record = new JoinLeaveRecord();
        int changes = 0;
        record.Changed += () => changes++;
        record.NoteLeave(B, 0, 30);
        record.NoteLeave(B, 0, 2);
        record.Strike(C, 0);
        Assert.Equal(2, changes);
    }
}
