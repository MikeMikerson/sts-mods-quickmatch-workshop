using System.Reflection;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class MigrationTests
{
    [Fact]
    public void TheOrderIsTheRunsLessTheOldHostAndTheAbsent()
    {
        // Run order 10, 20, 30, 40; 10 hosted and is gone, 30 was away.
        Assert.Equal([20UL, 40UL], MigrationPolicy.HostOrder([10, 20, 30, 40], oldHost: 10, present: [10, 20, 40]));
    }

    [Fact]
    public void TheOrderDoesNotDependOnHowPresentIsListed()
    {
        // Every client must agree without talking, whatever order its own lobby keeps players in.
        Assert.Equal(MigrationPolicy.HostOrder([1, 2, 3], 2, [3, 1, 2]), MigrationPolicy.HostOrder([1, 2, 3], 2, [1, 2, 3]));
        Assert.Equal([1UL, 3UL], MigrationPolicy.HostOrder([1, 2, 3], 2, [3, 1, 2]));
    }

    [Fact]
    public void AHostThatWasNotFirstStillLeavesTheOthersInRunOrder()
    {
        // After a first migration the host may be any player of the run.
        Assert.Equal([1UL, 2UL], MigrationPolicy.HostOrder([1, 2, 3], oldHost: 3, present: [1, 2, 3]));
    }

    [Fact]
    public void TheSameEligibleDesktopsGoFirstForEveryPlayer()
    {
        ulong[] run = [10, 20, 30, 40];

        Assert.Equal([30UL, 20UL], MigrationPolicy.HostOrder(run, 10, [40, 20, 30], [40], desktopPlayers: [30, 40]));
        Assert.Equal([30UL, 20UL], MigrationPolicy.HostOrder(run, 10, [30, 40, 20], [40], desktopPlayers: [40, 30]));
    }

    [Fact]
    public void TheOthersLineNamesWhoeverIsAskedToHostNow()
    {
        List<ulong> order = [20, 40, 50];
        double first = MigrationPolicy.SearchFirstSeconds;
        Assert.Equal(40UL, MigrationPolicy.WaitingFor(order, first + MigrationPolicy.NextHostSeconds, local: 20));
        Assert.Equal(20UL, MigrationPolicy.WaitingFor(order, first, local: 50));
        Assert.Equal(50UL, MigrationPolicy.WaitingFor(order, first + 2 * MigrationPolicy.NextHostSeconds + 1, local: 20));
    }

    [Theory]
    [InlineData(0, 40UL)] // the first search, before any turn
    [InlineData(15 + 60, 40UL)] // our own turn: we are the one asked
    [InlineData(15 + 3 * 60, 40UL)] // every turn passed
    public void ThePlainWaitingLineShowsWhenNobodyElseIsAsked(double elapsed, ulong local)
    {
        Assert.Equal(0UL, MigrationPolicy.WaitingFor([20, 40, 50], elapsed, local));
    }

    [Fact]
    public void ThePlainWaitingLineShowsWithoutALostHost()
    {
        Assert.Equal(0UL, MigrationPolicy.WaitingFor(null, MigrationPolicy.SearchFirstSeconds + 1, local: 40));
    }

    [Fact]
    public void TheFirstSearchesFirstAndEachNextWaitsAMinuteMore()
    {
        List<ulong> order = [20, 40, 50];
        Assert.Equal(MigrationPolicy.SearchFirstSeconds, MigrationPolicy.HostAfterSeconds(order, 20, hasCopy: true));
        Assert.Equal(MigrationPolicy.SearchFirstSeconds + MigrationPolicy.NextHostSeconds, MigrationPolicy.HostAfterSeconds(order, 40, hasCopy: true));
        Assert.Equal(MigrationPolicy.SearchFirstSeconds + 2 * MigrationPolicy.NextHostSeconds, MigrationPolicy.HostAfterSeconds(order, 50, hasCopy: true));
    }

    [Fact]
    public void APlayerOutsideTheOrderOrWithoutACopyNeverHosts()
    {
        Assert.Null(MigrationPolicy.HostAfterSeconds([20, 40], 30, hasCopy: true));
        Assert.Null(MigrationPolicy.HostAfterSeconds([20, 40], 20, hasCopy: false));
    }

    [Fact]
    public void ASearchThatFoundNothingHostsOnlyOnceItsTurnHasCome()
    {
        Assert.False(MigrationPolicy.HostsNow(14.9, 15));
        Assert.True(MigrationPolicy.HostsNow(15, 15));
        Assert.False(MigrationPolicy.HostsNow(1000, null));
    }

    [Fact]
    public void TheSearchOutlastsTheLastTurnToHost()
    {
        Assert.Equal(180, MigrationPolicy.SearchTimeoutSeconds(180, null));
        Assert.Equal(180, MigrationPolicy.SearchTimeoutSeconds(180, 135));
        Assert.Equal(215, MigrationPolicy.SearchTimeoutSeconds(180, 200));
    }

    [Fact]
    public void OurResumeLobbyMayBeginWithOnePlayerOnceReady()
    {
        Assert.True(MigrationPolicy.LoneHostMayBegin(gameSays: false, isHost: true, players: 1, connecting: 0, allReady: true, ourRun: true));
    }

    [Theory]
    [InlineData(true, 1, 0, true, false)] // not our quick-match run: the game's rule stands
    [InlineData(false, 1, 0, true, true)] // a client never begins the run
    [InlineData(true, 2, 0, true, true)] // two players: the game said no for another reason (someone not ready)
    [InlineData(true, 1, 1, true, true)] // a player is still connecting
    [InlineData(true, 1, 0, false, true)] // the one player is not ready
    public void OtherwiseTheGamesRuleStands(bool isHost, int players, int connecting, bool allReady, bool ourRun)
    {
        Assert.False(MigrationPolicy.LoneHostMayBegin(gameSays: false, isHost, players, connecting, allReady, ourRun));
    }

    [Fact]
    public void OnlyOurOwnRunsOldSaveIsMovedAsideAndOnlyAsAClient()
    {
        Assert.True(MigrationPolicy.MovesSaveAside(isClient: true, "k", "k"));
        Assert.False(MigrationPolicy.MovesSaveAside(isClient: true, "other", "k"));
        Assert.False(MigrationPolicy.MovesSaveAside(isClient: true, null, "k"));
        Assert.False(MigrationPolicy.MovesSaveAside(isClient: false, "k", "k"));
    }

    [Fact]
    public void ReflectedGameMembersExistInInstalledGame()
    {
        string dataPath = typeof(MigrationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Sts2DataPath").Value!;
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(dataPath, "*.dll")), "System.Private.CoreLib");
        Assembly game = context.LoadFromAssemblyPath(Path.Combine(dataPath, "sts2.dll"));
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var missing = new List<string>();
        foreach ((string typeName, string member) in MigrationPolicy.GameMembers)
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
