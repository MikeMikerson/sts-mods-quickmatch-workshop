using System.Reflection;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class RejoinTests
{
    [Fact]
    public void AQuickMatchPlayerOfTheRunIsHeld()
    {
        Assert.Equal(RejoinPolicy.Answer.Hold, RejoinPolicy.OnRequest(inRun: true, saidQuickMatch: true, refused: false));
    }

    [Fact]
    public void TheGamesOwnJoinScreenIsRefusedAsARunInProgress()
    {
        Assert.Equal(RejoinPolicy.Answer.NotQuickMatch, RejoinPolicy.OnRequest(inRun: true, saidQuickMatch: false, refused: false));
    }

    [Fact]
    public void ABlockedPlayerIsRefusedWhateverTheySaid()
    {
        Assert.Equal(RejoinPolicy.Answer.Refused, RejoinPolicy.OnRequest(inRun: true, saidQuickMatch: true, refused: true));
        Assert.Equal(RejoinPolicy.Answer.Refused, RejoinPolicy.OnRequest(inRun: true, saidQuickMatch: false, refused: true));
    }

    [Fact]
    public void SomeoneNotInTheRunIsLeftToTheGame()
    {
        Assert.Equal(RejoinPolicy.Answer.Game, RejoinPolicy.OnRequest(inRun: false, saidQuickMatch: true, refused: false));
    }

    [Fact]
    public void ReleasedOnlyAtARoomEnteredFromTheMap()
    {
        Assert.True(RejoinPolicy.ReleasesAt(openRooms: 0, held: 1));
        // An event that opens a fight syncs with its own room still open.
        Assert.False(RejoinPolicy.ReleasesAt(openRooms: 1, held: 1));
        Assert.False(RejoinPolicy.ReleasesAt(openRooms: 0, held: 0));
    }

    [Fact]
    public void PlayersAfterAddsTheReturningOnesOnce()
    {
        Assert.Equal([1UL, 3UL, 2UL], RejoinPolicy.PlayersAfter([1, 3], [2, 3]));
    }

    [Fact]
    public void AbsentAfterKeepsOnlyThoseStillAway()
    {
        Assert.Equal([4UL], RejoinPolicy.AbsentAfter([1, 2, 3, 4], [1, 3], [2]));
        Assert.Empty(RejoinPolicy.AbsentAfter([1, 2], [1], [2]));
    }

    [Fact]
    public void AnAnswerIsTakenOnlyForOurRunWithUsInIt()
    {
        Assert.True(RejoinPolicy.AcceptsAnswer("k", "k", [1, 2], local: 2));
        Assert.False(RejoinPolicy.AcceptsAnswer("k", "other", [1, 2], local: 2));
        Assert.False(RejoinPolicy.AcceptsAnswer("k", "k", [1, 3], local: 2));
        Assert.False(RejoinPolicy.AcceptsAnswer(null, null, [2], local: 2));
    }

    [Fact]
    public void ReflectedGameMembersExistInInstalledGame()
    {
        string dataPath = typeof(RejoinTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Sts2DataPath").Value!;
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(dataPath, "*.dll")), "System.Private.CoreLib");
        Assembly game = context.LoadFromAssemblyPath(Path.Combine(dataPath, "sts2.dll"));
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var missing = new List<string>();
        foreach ((string typeName, string member) in RejoinPolicy.GameMembers)
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
