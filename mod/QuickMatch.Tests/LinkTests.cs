using QuickMatch.Matchmaking;
using QuickMatch.Steam;
using QuickMatch.Transport;
using QuickMatch.Voice;
using Xunit;

namespace QuickMatch.Tests;

public class LinkTests
{
    private const ulong Lobby = 109775242500000077;

    public LinkTests() => LobbyLinks.Forget();

    [Fact]
    public void OnlyLobbiesASearchListedAreQuickMatchLobbies()
    {
        Assert.False(LobbyLinks.IsListed(Lobby));

        LobbyLinks.Remember(Lobby, offersSockets: false);

        Assert.True(LobbyLinks.IsListed(Lobby));
        Assert.False(LobbyLinks.OffersSockets(Lobby));
        Assert.False(LobbyLinks.IsListed(Lobby + 1));
        Assert.Null(LobbyLinks.OffersSockets(Lobby + 1));
    }

    [Fact]
    public void TheMemoryStaysSmall()
    {
        for (ulong i = 0; i < 1000; i++)
        {
            LobbyLinks.Remember(Lobby + i, offersSockets: true);
        }
        Assert.True(LobbyLinks.IsListed(Lobby + 999));
        Assert.True(LobbyLinks.OffersSockets(Lobby + 998));
        Assert.False(LobbyLinks.IsListed(Lobby));
    }

    [Theory]
    [InlineData(true, LobbyKeys.LinkMixed, true)]
    [InlineData(true, LobbyKeys.LinkChat, false)]
    [InlineData(false, LobbyKeys.LinkMixed, false)]
    [InlineData(false, LobbyKeys.LinkChat, false)]
    public void OnlyTwoDesktopsUseSockets(bool ownSockets, string link, bool expected)
    {
        bool hostSockets = Assert.IsType<bool>(LinkPolicy.HostOffersSockets(link));
        Assert.Equal(expected, LinkPolicy.JoinOverSockets(ownSockets, hostSockets));
    }

    // Released versions search with these exact keys and values, so changing one splits players without moving the
    // protocol numbers.
    [Fact]
    public void SearchKeysMatchReleasedVersions()
    {
        Assert.Equal(("qm_link", "mixed", "chat"), (LobbyKeys.Link, LobbyKeys.LinkMixed, LobbyKeys.LinkChat));
        Assert.Equal(("qm_vc", "on", "off"), (LobbyKeys.Voice, MatchVoice.LobbyValue(true), MatchVoice.LobbyValue(false)));
        Assert.Equal(("qm_mode", "standard", "blitz", "dread"), (LobbyKeys.Mode, LobbyKeys.ModeStandard, LobbyKeys.ModeBlitz, LobbyKeys.ModeDread));
    }
}
