using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class OwnLobbyWatchTests
{
    [Fact]
    public void OneMissIsNotEnough()
    {
        var watch = new OwnLobbyWatch();
        Assert.False(watch.Searched(ownLobbyVisible: false, group: 1));
    }

    [Fact]
    public void TwoMissesInARowWhileAloneGiveTheLobbyUp()
    {
        var watch = new OwnLobbyWatch();
        watch.Searched(false, 1);
        Assert.True(watch.Searched(false, 1));
    }

    [Fact]
    public void ASightingInBetweenStartsTheCountAgain()
    {
        var watch = new OwnLobbyWatch();
        watch.Searched(false, 1);
        watch.Searched(true, 1);
        Assert.False(watch.Searched(false, 1));
        Assert.True(watch.Searched(false, 1));
    }

    [Fact]
    public void AHostWithPlayersKeepsItsLobby()
    {
        var watch = new OwnLobbyWatch();
        Assert.False(watch.Searched(false, 2));
        Assert.False(watch.Searched(false, 2));
        Assert.False(watch.Searched(false, 2));
    }

    [Fact]
    public void APlayerJoiningBetweenMissesStartsTheCountAgain()
    {
        var watch = new OwnLobbyWatch();
        watch.Searched(false, 1);
        watch.Searched(false, 2);
        Assert.False(watch.Searched(false, 1));
    }
}
