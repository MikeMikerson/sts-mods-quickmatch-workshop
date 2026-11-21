using System.Collections.Generic;
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

        LobbyLinks.Remember(Lobby);

        Assert.True(LobbyLinks.IsListed(Lobby));
        Assert.False(LobbyLinks.IsListed(Lobby + 1));
    }

    [Fact]
    public void TheMemoryStaysSmall()
    {
        for (ulong i = 0; i < 1000; i++)
        {
            LobbyLinks.Remember(Lobby + i);
        }
        Assert.True(LobbyLinks.IsListed(Lobby + 999));
        Assert.False(LobbyLinks.IsListed(Lobby));
    }

    // Released versions search with these exact keys and values, so changing one splits players without moving the
    // protocol numbers.
    [Fact]
    public void SearchKeysMatchReleasedVersions()
    {
        Assert.Equal(
            new Dictionary<string, string> { ["qm_pool"] = "desktop", ["qm_link"] = "sockets" },
            LobbyLinks.Filters);
        Assert.Equal(("qm_vc", "on", "off"), (LobbyKeys.Voice, MatchVoice.LobbyValue(true), MatchVoice.LobbyValue(false)));
        Assert.Equal(("qm_mode", "standard", "blitz", "dread"), (LobbyKeys.Mode, LobbyKeys.ModeStandard, LobbyKeys.ModeBlitz, LobbyKeys.ModeDread));
    }
}
