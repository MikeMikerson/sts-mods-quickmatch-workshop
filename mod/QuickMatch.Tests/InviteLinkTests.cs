using System.Collections.Generic;
using QuickMatch.Matchmaking;
using QuickMatch.Steam;
using Xunit;

namespace QuickMatch.Tests;

public class InviteLinkTests
{
    private static readonly (string Version, string Db, string Mods) Local = ("v0.113.1", "123", "none");

    private static Dictionary<string, string> Lobby(string voice = LobbyKeys.VoiceOn) => new()
    {
        [LobbyKeys.Proto] = LobbyKeys.ProtoValue,
        [LobbyKeys.Mode] = LobbyKeys.ModeStandard,
        [LobbyKeys.Voice] = voice,
        [LobbyKeys.Size] = "3",
        [LobbyKeys.Ascension] = "4",
        [LobbyKeys.Version] = Local.Version,
        [LobbyKeys.Db] = Local.Db,
        [LobbyKeys.Mods] = Local.Mods,
        [LobbyKeys.Pool] = LobbyKeys.PoolDesktop,
        [LobbyKeys.Link] = LobbyKeys.LinkSockets,
    };

    private static (LinkVerdict Verdict, LinkLobby Lobby) Read(
        Dictionary<string, string> data, bool voiceChoice = true, int maxAscension = 10, bool blitzUnlocked = true) =>
        LinkPolicy.Read(data, Local, mode => mode != RunMode.Blitz || blitzUnlocked, _ => maxAscension, voiceChoice);

    [Fact]
    public void TheLinkIsSteamsJoinLobbyLinkForTheGame()
    {
        Assert.Equal("steam://joinlobby/2868840/109775242500000077/76561198000000001", LinkPolicy.InviteLink(109775242500000077, 76561198000000001));
    }

    [Theory]
    [InlineData(new[] { "QuickMatch-v0.28.0" }, true)]
    [InlineData(new[] { "BetterMaps-v1.0", "QuickMatch-v0.27.4" }, true)]
    [InlineData(new[] { "QuickMatchPlus-v1.0" }, false)]
    [InlineData(new[] { "BetterMaps-v1.0" }, false)]
    [InlineData(new string[0], false)]
    public void AJoinerWithoutQuickMatchIsTurnedAway(string[] mods, bool letIn)
    {
        Assert.Equal(letIn, LinkPolicy.HasQuickMatch(mods, "QuickMatch"));
    }

    [Fact]
    public void AnUnmoddedJoinerSendsNoModListAndIsTurnedAway()
    {
        Assert.False(LinkPolicy.HasQuickMatch(null, "QuickMatch"));
    }

    // The joiner's game shows it in a BBCode popup, which drops square brackets and is cut at 100 characters.
    [Fact]
    public void TheReasonFitsTheJoinersPopup()
    {
        Assert.True(LinkPolicy.NoQuickMatchReason.Length < 100);
        Assert.All(LinkPolicy.NoQuickMatchReason, c => Assert.True(c < 128 && c != '[' && c != ']'));
    }

    [Fact]
    public void ALobbyWithoutTheProtocolIsLeftToTheGamesOwnJoin()
    {
        Dictionary<string, string> friends = Lobby();
        friends.Remove(LobbyKeys.Proto);

        Assert.Equal(LinkVerdict.NotQuickMatch, Read(friends).Verdict);
    }

    [Fact]
    public void AnOpenLobbyOfTheSameReleaseIsJoinedWithItsModeSizeAndAscension()
    {
        (LinkVerdict verdict, LinkLobby lobby) = Read(Lobby());

        Assert.Equal(LinkVerdict.Join, verdict);
        Assert.Equal(new LinkLobby(RunMode.Standard, 3, false, 4, true), lobby);
    }

    [Fact]
    public void AnAnySizeLobbyIsJoinedAsAnySize()
    {
        Dictionary<string, string> data = Lobby();
        data[LobbyKeys.Size] = LobbyKeys.SizeAny;

        LinkLobby lobby = Read(data).Lobby;

        Assert.True(lobby.AnySize);
        Assert.Equal(MatchPolicy.AnySizeMax, lobby.Size);
    }

    [Theory]
    [InlineData(LobbyKeys.Proto, "19")]
    [InlineData(LobbyKeys.Pool, "")]
    [InlineData(LobbyKeys.Link, "")]
    [InlineData(LobbyKeys.Mode, "endless")]
    [InlineData(LobbyKeys.Size, "")]
    public void ALobbyOfAnotherReleaseIsRefused(string key, string value)
    {
        Dictionary<string, string> data = Lobby();
        data[key] = value;

        Assert.Equal(LinkVerdict.OtherRelease, Read(data).Verdict);
    }

    [Theory]
    [InlineData(LobbyKeys.Version, "v0.111.1")]
    [InlineData(LobbyKeys.Db, "456")]
    [InlineData(LobbyKeys.Mods, "abc")]
    public void ALobbyOfAnotherGameVersionOrGameplayModsIsRefused(string key, string value)
    {
        Dictionary<string, string> data = Lobby();
        data[key] = value;

        Assert.Equal(LinkVerdict.OtherGame, Read(data).Verdict);
    }

    [Fact]
    public void ALobbyInAModeNotUnlockedIsRefused()
    {
        Dictionary<string, string> data = Lobby();
        data[LobbyKeys.Mode] = LobbyKeys.ModeBlitz;

        Assert.Equal(LinkVerdict.ModeLocked, Read(data, blitzUnlocked: false).Verdict);
        Assert.Equal(LinkVerdict.Join, Read(data).Verdict);
    }

    [Fact]
    public void ALobbyAboveThePlayersHighestAscensionIsRefused()
    {
        Assert.Equal(LinkVerdict.AscensionTooHigh, Read(Lobby(), maxAscension: 3).Verdict);
        Assert.Equal(LinkVerdict.Join, Read(Lobby(), maxAscension: 4).Verdict);
    }

    // A player who did not choose voice is never heard, also in a voice lobby they followed a link to.
    [Theory]
    [InlineData(LobbyKeys.VoiceOn, true, true)]
    [InlineData(LobbyKeys.VoiceOn, false, false)]
    [InlineData(LobbyKeys.VoiceOff, true, false)]
    [InlineData(LobbyKeys.VoiceOff, false, false)]
    public void ALinkJoinerHasVoiceOnlyIfTheLobbyAndThePlayerChoseIt(string lobbyVoice, bool choice, bool voice)
    {
        Assert.Equal(voice, Read(Lobby(lobbyVoice), voiceChoice: choice).Lobby.Voice);
    }
}
