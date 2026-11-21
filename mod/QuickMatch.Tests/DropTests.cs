using System.Reflection;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class DropTests
{
    [Fact]
    public void AbsentAreTheRunsPlayersNotConnected()
    {
        Assert.Equal([2UL, 4UL], DropPolicy.Absent([1, 2, 3, 4], new HashSet<ulong> { 1, 3 }));
        Assert.Empty(DropPolicy.Absent([1, 2], new HashSet<ulong> { 1, 2 }));
    }

    [Fact]
    public void FightKeepsTheRunsOrderWithoutTheAbsent()
    {
        Assert.Equal([1UL, 3UL], DropPolicy.FightPlayers([1, 2, 3], new HashSet<ulong> { 2 }, local: 1));
    }

    [Fact]
    public void LocalPlayerAlwaysFights()
    {
        Assert.Equal([1UL, 2UL], DropPolicy.FightPlayers([1, 2, 3], new HashSet<ulong> { 2, 3 }, local: 2));
    }

    [Fact]
    public void NothingIsFilledWhileAPresentPlayerHasNotActed()
    {
        Assert.Empty(DropPolicy.SlotsToFill([true, false, false], [true, true, false]));
    }

    [Fact]
    public void AbsentSlotsAreFilledOnceEveryPresentPlayerActed()
    {
        Assert.Equal([1, 3], DropPolicy.SlotsToFill([true, false, true, false], [true, false, true, false]));
    }

    [Fact]
    public void AnAbsentPlayerWhoActedBeforeLeavingIsNotFilledAgain()
    {
        Assert.Empty(DropPolicy.SlotsToFill([true, true], [true, false]));
    }

    [Fact]
    public void NoOneAbsentFillsNothing()
    {
        Assert.Empty(DropPolicy.SlotsToFill([true, true, true], [true, true, true]));
    }

    [Fact]
    public void SlotCountsMustMatch()
    {
        Assert.Throws<ArgumentException>(() => DropPolicy.SlotsToFill([true], [true, false]));
    }

    [Fact]
    public void CopiedVoteComesFromAPresentPlayer()
    {
        int?[] votes = [7, 9, 4];
        bool[] present = [true, false, true];
        Assert.Equal(7, DropPolicy.CopiedVote(votes, present, _ => 0));
        Assert.Equal(4, DropPolicy.CopiedVote(votes, present, _ => 1));
    }

    [Fact]
    public void CopiedVoteRollsAmongPresentVotesOnly()
    {
        int seen = 0;
        DropPolicy.CopiedVote<int>([1, 2, 3], [true, true, false], n => { seen = n; return 0; });
        Assert.Equal(2, seen);
    }

    [Fact]
    public void NoPresentVoteNoCopy()
    {
        Assert.Null(DropPolicy.CopiedVote<int>([null, 5], [true, false], _ => 0));
    }

    // The game picks one vote at random among all of them. With the absent player's vote copied from a present player
    // picked at random, each present player's vote keeps the chance it had without them.
    [Fact]
    public void CopyingKeepsEachPresentVotesChance()
    {
        int?[] votes = [10, 20, null];
        bool[] present = [true, true, false];
        var wins = new Dictionary<int, double> { [10] = 0, [20] = 0 };
        for (int copied = 0; copied < 2; copied++)
        {
            int?[] filled = [votes[0], votes[1], DropPolicy.CopiedVote(votes, present, _ => copied)];
            foreach (int? picked in filled)
            {
                wins[picked!.Value] += 1.0 / 2 / filled.Length;
            }
        }
        Assert.Equal(0.5, wins[10], 9);
        Assert.Equal(0.5, wins[20], 9);
    }

    [Theory]
    [InlineData(1, 1.1, 1.0)]
    [InlineData(2, 1.2, 2.0)]
    [InlineData(3, 1.1, 3.3)]
    [InlineData(4, 1.3, 5.2)]
    public void BlockIsScaledForTheFightsPlayersAsTheGameScalesTheRuns(int fighters, double factor, double expected)
    {
        Assert.Equal((decimal)expected, DropPolicy.BlockScale(fighters, (decimal)factor));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void OnlyAHostLeftAloneIsAsked(int present, bool asked)
    {
        Assert.Equal(asked, DropPolicy.AskHostAlone(present));
    }

    [Fact]
    public void ReflectedGameMembersExistInInstalledGame()
    {
        string dataPath = typeof(DropTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Sts2DataPath").Value!;
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(dataPath, "*.dll")), "System.Private.CoreLib");
        Assembly game = context.LoadFromAssemblyPath(Path.Combine(dataPath, "sts2.dll"));
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var missing = new List<string>();
        foreach ((string typeName, string member) in DropPolicy.GameMembers)
        {
            Type? type = game.GetType(typeName);
            if (type == null || type.GetMember(member, all).Length != 1)
            {
                missing.Add($"{typeName}.{member}");
            }
        }
        Assert.Empty(missing);
    }
}
