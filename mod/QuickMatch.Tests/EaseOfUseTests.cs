using QuickMatch.Matchmaking;
using QuickMatch.Steam;
using Xunit;

namespace QuickMatch.Tests;

public class EaseOfUseTests
{
    [Fact]
    public void TheWidestSearchHasNothingToOffer()
    {
        // Any size with any ascension, and any size for a player with no ascension unlocked.
        Assert.Null(MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Random, 5, [0, 1, 2, 3, 4, 5]));
        Assert.Null(MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Random, 0, [0]));
        Assert.Null(MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Progress, 0, [0]));
    }

    [Fact]
    public void AnExactSizeIsOfferedAnySize()
    {
        var expected = new AloneOffer("QUICKMATCH.ALONE.size", "QUICKMATCH.ALONE.any_size", 3, 0, false);
        Assert.Equal(expected, MatchPolicy.OfferWhenAlone(false, 3, AscensionChoice.Random, 0, [0]));
        // Any ascension of a player with some unlocked is not a narrowing either.
        Assert.Equal(expected, MatchPolicy.OfferWhenAlone(false, 3, AscensionChoice.Random, 4, [0, 1, 2, 3, 4]));
    }

    [Fact]
    public void AClimbIsOfferedAnyAscensionAndToldWhatItCosts()
    {
        Assert.Equal(new AloneOffer("QUICKMATCH.ALONE.asc", "QUICKMATCH.ALONE.any_asc", 0, 5, true),
            MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Progress, 5, MatchPolicy.AllowedAscensions(AscensionChoice.Progress, 5)));
        // Choosing exactly one's maximum climbs as well.
        Assert.True(MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Of(5), 5, [5])!.Value.ClimbNote);
        // Nothing is left to unlock at the cap, and a lower ascension unlocks nothing.
        Assert.False(MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Of(10), 10, [10])!.Value.ClimbNote);
        Assert.Equal(new AloneOffer("QUICKMATCH.ALONE.asc", "QUICKMATCH.ALONE.any_asc", 0, 3, false),
            MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Of(3), 5, [3]));
    }

    [Fact]
    public void SeveralChosenAscensionsAreNotNamedOneByOne()
    {
        Assert.Equal(new AloneOffer("QUICKMATCH.ALONE.asc_many", "QUICKMATCH.ALONE.any_asc", 0, 0, false),
            MatchPolicy.OfferWhenAlone(true, 2, AscensionChoice.Of(2, 4), 5, [2, 4]));
        Assert.Equal(new AloneOffer("QUICKMATCH.ALONE.both_many", "QUICKMATCH.ALONE.any_both", 4, 0, false),
            MatchPolicy.OfferWhenAlone(false, 4, AscensionChoice.Of(2, 4), 5, [2, 4]));
    }

    [Fact]
    public void AnExactSizeAndAscensionAreOfferedBoth()
    {
        Assert.Equal(new AloneOffer("QUICKMATCH.ALONE.both", "QUICKMATCH.ALONE.any_both", 3, 0, false),
            MatchPolicy.OfferWhenAlone(false, 3, AscensionChoice.Of(0), 5, [0]));
        Assert.Equal(new AloneOffer("QUICKMATCH.ALONE.both", "QUICKMATCH.ALONE.any_both", 2, 5, true),
            MatchPolicy.OfferWhenAlone(false, 2, AscensionChoice.Progress, 5, [5]));
    }

    [Fact]
    public void TheOneWaitingCountIsEveryoneThePlayerCouldJoin()
    {
        LobbyCandidate[] pool =
        [
            new(1, 1, 100, Ascension: 0, Size: 2),
            new(2, 2, 100, Ascension: 3, Size: 4),
            new(3, 1, 100, Ascension: 0, Size: 3),
            // Above the player's maximum, and full: neither counts.
            new(4, 1, 100, Ascension: 7, Size: 2),
            new(5, 2, 100, Ascension: 0, Size: 2),
        ];
        PoolCounts counts = MatchPolicy.Count(pool, maxAscension: 5);
        Assert.Equal(4, counts.Total);
        Assert.Equal(0, PoolCounts.Empty.Total);
    }

    [Fact]
    public void TheCountdownReadsAsMinutesAndSeconds()
    {
        Assert.Equal("1:00", MigrationPolicy.Countdown(60));
        Assert.Equal("0:52", MigrationPolicy.Countdown(52));
        Assert.Equal("0:52", MigrationPolicy.Countdown(51.2));
        Assert.Equal("0:01", MigrationPolicy.Countdown(0.1));
        Assert.Equal("0:00", MigrationPolicy.Countdown(0));
        Assert.Equal("0:00", MigrationPolicy.Countdown(-4));
        Assert.Equal("2:05", MigrationPolicy.Countdown(125));
    }

    [Fact]
    public void OnlyACandidateWithOneAfterItSaysTheNextPlayerIsAsked()
    {
        ulong[] order = [11, 22, 33];
        Assert.True(MigrationPolicy.HasNext(order, 11));
        Assert.True(MigrationPolicy.HasNext(order, 22));
        Assert.False(MigrationPolicy.HasNext(order, 33));
        Assert.False(MigrationPolicy.HasNext(order, 44));
        Assert.False(MigrationPolicy.HasNext([], 11));
    }

    // Play Now searches for the levels last chosen that are unlocked, else Any; a saved Climb is Any.
    [Fact]
    public void PlayNowUsesTheChosenLevelsThatAreUnlocked()
    {
        Assert.Equal(AscensionMode.Random, MatchPolicy.PlayNowChoice(AscensionChoice.Progress, 5).Mode);
        Assert.Equal(AscensionMode.Random, MatchPolicy.PlayNowChoice(AscensionChoice.Random, 5).Mode);
        Assert.Equal([2, 4], MatchPolicy.PlayNowChoice(AscensionChoice.Of(4, 2, 4), 5).Chosen);
        Assert.Equal([2], MatchPolicy.PlayNowChoice(AscensionChoice.Of(2, 7), 5).Chosen);
        Assert.Equal(AscensionMode.Random, MatchPolicy.PlayNowChoice(AscensionChoice.Of(7), 5).Mode);
        Assert.Equal(AscensionMode.Random, MatchPolicy.PlayNowChoice(AscensionChoice.Of(0, 2), 0).Mode);
    }

    [Fact]
    public void TheStatusAreaSaysWhoIsHereAndWhenTheRunStarts()
    {
        Assert.Equal(("QUICKMATCH.STATUS.looking", "QUICKMATCH.STATUS.starts_when"), MatchPolicy.StatusKeys(1, 2, ready: false));
        Assert.Equal(("QUICKMATCH.STATUS.looking", "QUICKMATCH.STATUS.ready_more"), MatchPolicy.StatusKeys(2, 3, ready: true));
        Assert.Equal(("QUICKMATCH.STATUS.all_here", "QUICKMATCH.STATUS.press_embark"), MatchPolicy.StatusKeys(3, 3, ready: false));
        Assert.Equal(("QUICKMATCH.STATUS.all_here", "QUICKMATCH.STATUS.ready_others"), MatchPolicy.StatusKeys(2, 2, ready: true));
    }

    [Fact]
    public void APlayerAloneForAMinuteIsToldHowToFindMorePlayers()
    {
        Assert.Equal("QUICKMATCH.STATUS.starts_when_any", MatchPolicy.StatusKeys(1, 4, ready: false, anySize: true, waited: 59).Start);
        Assert.Equal("QUICKMATCH.STATUS.few_players", MatchPolicy.StatusKeys(1, 4, ready: false, anySize: true, waited: 60).Start);
        Assert.Equal("QUICKMATCH.STATUS.few_players", MatchPolicy.StatusKeys(1, 2, ready: true, waited: 300).Start);
        Assert.Equal("QUICKMATCH.STATUS.starts_when", MatchPolicy.StatusKeys(2, 3, ready: false, waited: 300).Start);
    }

    private static LobbyCandidate Any(ulong id, int members, uint createdAt, int ascension = 0) =>
        new(id, members, createdAt, Ascension: ascension, Size: MatchPolicy.AnySizeMax, AnySize: true);

    private static LobbyCandidate Exact(ulong id, int members, uint createdAt, int size, int ascension = 0) =>
        new(id, members, createdAt, Ascension: ascension, Size: size);

    [Fact]
    public void AnAnyLobbySaysItStartsWithTwoOrMore()
    {
        Assert.Equal(("QUICKMATCH.STATUS.looking_any", "QUICKMATCH.STATUS.starts_when_any"), MatchPolicy.StatusKeys(1, 4, ready: false, anySize: true));
        Assert.Equal(("QUICKMATCH.STATUS.looking_any", "QUICKMATCH.STATUS.ready_any_alone"), MatchPolicy.StatusKeys(1, 4, ready: true, anySize: true));
        Assert.Equal(("QUICKMATCH.STATUS.looking_any", "QUICKMATCH.STATUS.starts_when_any"), MatchPolicy.StatusKeys(2, 4, ready: false, anySize: true));
        Assert.Equal(("QUICKMATCH.STATUS.looking_any", "QUICKMATCH.STATUS.ready_others"), MatchPolicy.StatusKeys(3, 4, ready: true, anySize: true));
        Assert.Equal(("QUICKMATCH.STATUS.all_here", "QUICKMATCH.STATUS.starts_when_any"), MatchPolicy.StatusKeys(4, 4, ready: false, anySize: true));
        // An exact size never finds an Any lobby: its search filters on a number, and an Any lobby's size is no number.
        Assert.False(int.TryParse(LobbyKeys.SizeAny, out _));
    }

    // Two Any hosts merge one way, as two exact hosts do, and an Any host also fills an exact lobby, however old.
    [Fact]
    public void AnAnyGroupMovesIntoAnotherAnyLobbyOrAnExactOneItFills()
    {
        LobbyCandidate older = Any(1, 1, 100);
        LobbyCandidate newer = Any(2, 1, 200);
        Assert.Equal(older, MatchPolicy.PickMergeTarget(newer, [older, newer], _ => false, size: 4));
        Assert.Null(MatchPolicy.PickMergeTarget(older, [older, newer], _ => false, size: 4));

        // The exact pair is newer than us: we still move, since its player's search can never find our lobby.
        LobbyCandidate pair = Exact(3, 1, 300, size: 2);
        Assert.Equal(pair, MatchPolicy.PickMergeTarget(older, [older, pair], _ => false, size: 4));
        // An exact lobby we would not fill is left alone: in it we would wait for more instead of starting with 2.
        LobbyCandidate trio = Exact(4, 1, 50, size: 3);
        Assert.Null(MatchPolicy.PickMergeTarget(older, [older, trio], _ => false, size: 4));
        LobbyCandidate ourPair = Any(5, 2, 150);
        Assert.Equal(Exact(6, 2, 400, size: 4), MatchPolicy.PickMergeTarget(ourPair, [ourPair, Exact(6, 2, 400, size: 4), trio], _ => false, size: 4));
        // Only at our own ascension, as every merge.
        Assert.Null(MatchPolicy.PickMergeTarget(older, [older, Exact(7, 1, 300, size: 2, ascension: 3)], _ => false, size: 4));
    }

    // So no two groups move into each other.
    [Fact]
    public void AnExactGroupNeverMovesIntoAnAnyLobby()
    {
        LobbyCandidate own = Exact(1, 1, 300, size: 2);
        Assert.Null(MatchPolicy.PickMergeTarget(own, [own, Any(2, 3, 100)], _ => false, size: 2));
        Assert.Equal(Exact(3, 1, 100, size: 2), MatchPolicy.PickMergeTarget(own, [own, Any(2, 3, 100), Exact(3, 1, 100, size: 2)], _ => false, size: 2));
    }

    // The countdown runs only when the lobby could start but for idle players, and more than half are ready.
    [Theory]
    [InlineData(2, 1, 2, false, true)]   // one of two ready in a full lobby of 2
    [InlineData(2, 1, 4, true, true)]    // the same in an Any lobby
    [InlineData(1, 1, 2, false, false)]  // alone
    [InlineData(2, 0, 2, false, false)]  // nobody ready
    [InlineData(2, 2, 2, false, false)]  // everyone ready: the run starts
    [InlineData(2, 1, 3, false, false)]  // an exact 3 still waiting for its third: nobody is held up
    [InlineData(3, 1, 3, false, false)]  // one of three is no majority
    [InlineData(3, 2, 3, false, true)]
    [InlineData(3, 2, 4, true, true)]
    [InlineData(4, 2, 4, false, false)]  // two of four is no majority
    [InlineData(4, 3, 4, true, true)]
    public void TheCountdownRunsOnceMostOfAGroupThatCouldStartAreReady(int here, int ready, int size, bool anySize, bool runs)
    {
        Assert.Equal(runs, MatchPolicy.CountdownRuns(here, ready, size, anySize));
    }

    // Embark or a new search counts from 0.
    [Fact]
    public void APlayerSentBackByTwoCountdownsInARowStopsSearching()
    {
        int streak = MatchPolicy.IdleStreak(0, IdleEvent.SearchStarted);
        Assert.False(MatchPolicy.IdleStops(streak));
        streak = MatchPolicy.IdleStreak(streak, IdleEvent.SentBack);
        Assert.False(MatchPolicy.IdleStops(streak));
        streak = MatchPolicy.IdleStreak(streak, IdleEvent.SentBack);
        Assert.True(MatchPolicy.IdleStops(streak));
    }

    [Theory]
    [InlineData(IdleEvent.Embarked)]
    [InlineData(IdleEvent.SearchStarted)]
    public void EmbarkOrANewSearchCountsFromZeroAgain(IdleEvent what)
    {
        int streak = MatchPolicy.IdleStreak(0, IdleEvent.SentBack);
        streak = MatchPolicy.IdleStreak(streak, what);
        Assert.Equal(0, streak);
        streak = MatchPolicy.IdleStreak(streak, IdleEvent.SentBack);
        Assert.False(MatchPolicy.IdleStops(streak));
        // Once stopped, the player's next search starts the count again.
        streak = MatchPolicy.IdleStreak(MatchPolicy.IdleStreak(streak, IdleEvent.SentBack), IdleEvent.SearchStarted);
        Assert.False(MatchPolicy.IdleStops(streak));
    }
}
