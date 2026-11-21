using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class AscensionPolicyTests
{
    private static readonly Func<ulong, bool> SkipNone = _ => false;

    private static LobbyCandidate Lobby(ulong id, int members, uint createdAt, int ascension, int size = 4, bool anySize = false) =>
        new(id, members, createdAt, ascension, size, anySize);

    [Fact]
    public void ProgressAcceptsExactlyTheMaximum()
    {
        Assert.Equal([5], MatchPolicy.AllowedAscensions(AscensionChoice.Progress, 5));
        Assert.Equal(5, MatchPolicy.HostAscension(AscensionChoice.Progress, 5));
    }

    [Fact]
    public void RandomAcceptsEverythingUnlockedAndHostsAtZero()
    {
        Assert.Equal([0, 1, 2, 3], MatchPolicy.AllowedAscensions(AscensionChoice.Random, 3));
        Assert.Equal(0, MatchPolicy.HostAscension(AscensionChoice.Random, 3));
    }

    [Fact]
    public void ChooseKeepsOnlyUnlockedPicksAndHostsAtTheLowest()
    {
        AscensionChoice choice = AscensionChoice.Of(7, 2, 4, 2, -1);
        Assert.Equal([2, 4], MatchPolicy.AllowedAscensions(choice, 5));
        Assert.Equal(2, MatchPolicy.HostAscension(choice, 5));
    }

    [Fact]
    public void ChooseWithNothingUnlockedLeftFallsBackToEverythingUnlocked()
    {
        // Picked A6 and A7 when the maximum was higher; the save now says 3.
        Assert.Equal([0, 1, 2, 3], MatchPolicy.AllowedAscensions(AscensionChoice.Of(6, 7), 3));
    }

    [Fact]
    public void NewPlayerPlaysZeroWhateverTheChoice()
    {
        foreach (AscensionChoice choice in new[] { AscensionChoice.Progress, AscensionChoice.Random, AscensionChoice.Of(4) })
        {
            Assert.Equal([0], MatchPolicy.AllowedAscensions(choice, 0));
            Assert.Equal(0, MatchPolicy.HostAscension(choice, 0));
        }
    }

    [Fact]
    public void MaximumIsClampedToTheGamesCap()
    {
        Assert.Equal([10], MatchPolicy.AllowedAscensions(AscensionChoice.Progress, 14));
        Assert.Equal([0], MatchPolicy.AllowedAscensions(AscensionChoice.Progress, -3));
    }

    [Fact]
    public void SearchKeepsOnlyLobbiesInTheChosenSetAndSkipsLobbiesWithoutAscension()
    {
        var a0 = Lobby(1, 1, 100, 0);
        var a3 = Lobby(2, 2, 100, 3);
        var a5 = Lobby(3, 3, 100, 5);
        var missing = Lobby(4, 3, 100, -1);

        IReadOnlyList<int> allowed = MatchPolicy.AllowedAscensions(AscensionChoice.Of(0, 3), 4);
        Assert.Equal([a3, a0], MatchPolicy.Rank([a0, a3, a5, missing], 0, SkipNone, ascensions: [.. allowed]));
    }

    [Fact]
    public void AscensionFiltersButDoesNotRank()
    {
        // A fuller lobby still comes first, whatever its ascension.
        var lowFull = Lobby(1, 3, 100, 0);
        var highSolo = Lobby(2, 1, 50, 4);
        Assert.Equal([lowFull, highSolo], MatchPolicy.Rank([highSolo, lowFull], 0, SkipNone, ascensions: [0, 4]));
    }

    [Fact]
    public void NoLobbyAboveTheMaximumIsEverACandidate()
    {
        var above = Lobby(1, 1, 100, 6);
        foreach (AscensionChoice choice in new[] { AscensionChoice.Progress, AscensionChoice.Random, AscensionChoice.Of(6) })
        {
            Assert.Empty(MatchPolicy.Rank([above], 0, SkipNone, ascensions: [.. MatchPolicy.AllowedAscensions(choice, 5)]));
        }
    }

    [Fact]
    public void RandomSizePrefersTheLobbyNearestToFull()
    {
        var pairOfFour = Lobby(1, 2, 100, 0, size: 4);   // 2 free
        var soloOfTwo = Lobby(2, 1, 900, 0, size: 2);    // 1 free
        var trioOfFour = Lobby(3, 3, 500, 0, size: 4);   // 1 free, fuller
        var full = Lobby(4, 2, 100, 0, size: 2);         // 0 free
        var unknownSize = Lobby(5, 1, 100, 0, size: 0);

        List<LobbyCandidate> ranked = MatchPolicy.Rank([pairOfFour, soloOfTwo, trioOfFour, full, unknownSize], 0, SkipNone, randomSize: true);

        Assert.Equal([trioOfFour, soloOfTwo, pairOfFour], ranked);
    }

    [Fact]
    public void MergeOnlyIntoTheSameAscension()
    {
        var own = Lobby(1, 1, 500, 5);
        var betterOtherAscension = Lobby(2, 2, 100, 4);
        var sameAscension = Lobby(3, 1, 100, 5);

        Assert.Equal(sameAscension, MatchPolicy.PickMergeTarget(own, [betterOtherAscension, sameAscension], SkipNone, size: 4));
        Assert.Null(MatchPolicy.PickMergeTarget(own, [betterOtherAscension], SkipNone, size: 4));
    }

    [Fact]
    public void TwoProgressionHostsAtTheSameAscensionMeetByMerging()
    {
        var older = Lobby(1, 1, 100, 5, size: 2);
        var newer = Lobby(2, 1, 200, 5, size: 2);
        Assert.Equal(older, MatchPolicy.PickMergeTarget(newer, [older, newer], SkipNone, size: 2));
        Assert.Null(MatchPolicy.PickMergeTarget(older, [older, newer], SkipNone, size: 2));
    }

    [Fact]
    public void HostAdmitsOnlyJoinersWithTheAscensionUnlocked()
    {
        Assert.True(MatchPolicy.Admits(5, 5));
        Assert.True(MatchPolicy.Admits(7, 5));
        Assert.False(MatchPolicy.Admits(4, 5));
        Assert.True(MatchPolicy.Admits(0, 0));
    }

    [Fact]
    public void CountsAreMembersOfJoinableLobbiesBySizeAndAscension()
    {
        LobbyCandidate[] pool =
        [
            Lobby(1, 1, 100, 0, size: 2),
            Lobby(2, 2, 100, 3, size: 4),
            Lobby(3, 3, 100, 6, size: 4),                  // above our maximum of 5
            Lobby(4, 2, 100, 0, size: 2),                  // full
            Lobby(5, 1, 100, 5, size: 3),
            Lobby(6, 1, 100, 5, size: 3),
            Lobby(7, 1, 0, 0, size: 2),                    // data not arrived
            Lobby(8, 1, 100, -1, size: 2),                 // no ascension
            Lobby(2, 2, 100, 3, size: 4),                  // duplicate
            Lobby(9, 2, 100, 3, anySize: true),            // an Any lobby: only an Any search finds it
        ];

        PoolCounts counts = MatchPolicy.Count(pool, maxAscension: 5);

        Assert.Equal(7, counts.Total);
        Assert.Equal(1, counts.ForSize(2));
        Assert.Equal(2, counts.ForSize(3));
        Assert.Equal(2, counts.ForSize(4));
        Assert.Equal(7, counts.ForSize(0));
        Assert.Equal(2, counts.ForAscensions(4, [3]));
        Assert.Equal(4, counts.ForAscensions(0, [3]));
        Assert.Equal(1, counts.ForAscensions(0, [0, 1, 2]));
        Assert.Equal(7, counts.ForAscensions(0, MatchPolicy.AllowedAscensions(AscensionChoice.Random, 5)));
        Assert.Equal(2, counts.ForAscensions(3, MatchPolicy.AllowedAscensions(AscensionChoice.Progress, 5)));
    }
}
