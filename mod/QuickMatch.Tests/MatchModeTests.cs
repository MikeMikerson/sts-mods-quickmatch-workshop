using System.Linq;
using QuickMatch.Matchmaking;
using QuickMatch.Steam;
using Xunit;

namespace QuickMatch.Tests;

public class MatchModeTests
{
    [Fact]
    public void EachModeIsItsOwnPool()
    {
        Assert.Equal(MatchMode.All.Count, MatchMode.All.Select(MatchMode.LobbyValue).Distinct().Count());
        Assert.DoesNotContain(MatchMode.All, mode => mode != RunMode.Standard && MatchMode.LobbyValue(mode) == LobbyKeys.ModeStandard);
    }

    [Fact]
    public void ASavedModeIsKeptOnlyWhileThePlayerCanPlayIt()
    {
        Assert.Equal(RunMode.Blitz, MatchMode.FromSaved("blitz", _ => true));
        Assert.Equal(RunMode.Dread, MatchMode.FromSaved("dread", _ => true));
        Assert.Equal(RunMode.Standard, MatchMode.FromSaved("blitz", mode => mode != RunMode.Blitz));
        Assert.Equal(RunMode.Standard, MatchMode.FromSaved(null, _ => true));
        Assert.Equal(RunMode.Standard, MatchMode.FromSaved("daily", _ => true));
    }

    [Fact]
    public void EachModeMatchesOnItsOwnAscension()
    {
        Assert.Equal(7, MatchMode.MaxAscension(RunMode.Standard, standardMax: 7, blitzMax: 2));
        Assert.Equal(2, MatchMode.MaxAscension(RunMode.Blitz, standardMax: 7, blitzMax: 2));
        Assert.Equal(0, MatchMode.MaxAscension(RunMode.Dread, standardMax: 7, blitzMax: 2));
    }

    // Dread is always played at one level, so every Dread player meets every other, whatever they chose for Standard.
    [Fact]
    public void DreadPlayersAllMeetWhateverTheirSavedAscension()
    {
        int max = MatchMode.MaxAscension(RunMode.Dread, standardMax: 9, blitzMax: 5);
        var chosen = new AscensionChoice(AscensionMode.Choose, [4, 6]);
        AscensionChoice playNow = MatchPolicy.PlayNowChoice(chosen, max);

        Assert.Equal([0], MatchPolicy.AllowedAscensions(playNow, max));
        Assert.Equal(0, MatchPolicy.HostAscension(playNow, max));
        Assert.Equal([0], MatchPolicy.AllowedAscensions(AscensionChoice.Random, max));
    }

    // Blitz has five levels, so a Blitz player at 5 has nothing left to climb.
    [Fact]
    public void BlitzTopsOutAtFive()
    {
        Assert.Equal(5, MatchMode.TopAscension(RunMode.Blitz));
        Assert.Equal(MatchPolicy.MaxAscension, MatchMode.TopAscension(RunMode.Standard));
        Assert.Equal([0, 1, 2, 3, 4, 5], MatchPolicy.AllowedAscensions(AscensionChoice.Random, MatchMode.MaxAscension(RunMode.Blitz, 10, 5)));
    }
}
