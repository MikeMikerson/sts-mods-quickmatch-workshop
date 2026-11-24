using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickMatch.Steam;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

internal enum LinkVerdict
{
    // A lobby without qm_proto: the game's own join takes it.
    NotQuickMatch,
    Join,
    OtherRelease,
    OtherGame,
    ModeLocked,
    AscensionTooHigh,
}

internal readonly record struct LinkLobby(RunMode Mode, int Size, bool AnySize, int Ascension, bool Voice, bool HostOffersSockets);

internal enum PeerAdmission
{
    QuickMatch,
    Guest,
    OtherRelease,
    GuestsUnavailable,
}

// A Steam invite to a quick-match lobby uses the same checks as a search.
internal static class LinkPolicy
{
    public const uint SteamAppId = 2868840;

    public static IReadOnlyList<string> Keys { get; } =
    [
        LobbyKeys.Proto, LobbyKeys.Mode, LobbyKeys.Voice, LobbyKeys.Size, LobbyKeys.Ascension,
        LobbyKeys.Version, LobbyKeys.Db, LobbyKeys.Mods, LobbyKeys.Link,
    ];

    public static bool? HostOffersSockets(string? link) => link switch
    {
        LobbyKeys.LinkMixed => true,
        LobbyKeys.LinkChat => false,
        _ => null,
    };

    public static bool JoinOverSockets(bool ownSockets, bool hostSockets) => ownSockets && hostSockets;

    public static string InviteLink(ulong lobby, ulong host) =>
        string.Create(CultureInfo.InvariantCulture, $"steam://joinlobby/{SteamAppId}/{lobby}/{host}");

    // The handshake lists mods as "<id>-<version>".
    public static string? QuickMatchRelease(IEnumerable<string>? otherMods, IEnumerable<string>? gameplayMods, string modId)
    {
        string prefix = modId + "-";
        return otherMods?.FirstOrDefault(m => m.StartsWith(prefix, StringComparison.Ordinal))
            ?? gameplayMods?.FirstOrDefault(m => m.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static PeerAdmission Admit(string? localRelease, string? remoteRelease, bool acceptsSocketGuest)
    {
        if (remoteRelease == null)
        {
            return acceptsSocketGuest ? PeerAdmission.Guest : PeerAdmission.GuestsUnavailable;
        }
        return remoteRelease == localRelease ? PeerAdmission.QuickMatch : PeerAdmission.OtherRelease;
    }

    public static bool IsQuickMatch(IReadOnlyDictionary<string, string> data) =>
        !string.IsNullOrEmpty(data.GetValueOrDefault(LobbyKeys.Proto));

    public static (LinkVerdict Verdict, LinkLobby Lobby) Read(
        IReadOnlyDictionary<string, string> data,
        (string Version, string Db, string Mods) local,
        Func<RunMode, bool> playable,
        Func<RunMode, int> maxAscension,
        bool voiceChoice)
    {
        string Get(string key) => data.GetValueOrDefault(key) ?? "";

        if (!IsQuickMatch(data))
        {
            return (LinkVerdict.NotQuickMatch, default);
        }
        (int size, bool anySize) = LobbyKeys.ParseSize(Get(LobbyKeys.Size));
        int ascension = LobbyKeys.ParseInRange(Get(LobbyKeys.Ascension), 0, MatchPolicy.MaxAscension, -1);
        RunMode? mode = MatchMode.All.Where(m => MatchMode.LobbyValue(m) == Get(LobbyKeys.Mode)).Select(m => (RunMode?)m).FirstOrDefault();
        if (Get(LobbyKeys.Proto) != LobbyKeys.ProtoValue
            || HostOffersSockets(Get(LobbyKeys.Link)) is not bool hostOffersSockets
            || size == 0
            || mode == null
            || ascension < 0)
        {
            return (LinkVerdict.OtherRelease, default);
        }
        if (Get(LobbyKeys.Version) != local.Version || Get(LobbyKeys.Db) != local.Db || Get(LobbyKeys.Mods) != local.Mods)
        {
            return (LinkVerdict.OtherGame, default);
        }
        var lobby = new LinkLobby(mode.Value, size, anySize, ascension, MatchVoice.ForRun(Get(LobbyKeys.Voice) == LobbyKeys.VoiceOn, voiceChoice), hostOffersSockets);
        if (!playable(mode.Value))
        {
            return (LinkVerdict.ModeLocked, lobby);
        }
        if (!MatchPolicy.Admits(maxAscension(mode.Value), ascension))
        {
            return (LinkVerdict.AscensionTooHigh, lobby);
        }
        return (LinkVerdict.Join, lobby);
    }

    public static string MessageKey(LinkVerdict verdict) => "QUICKMATCH.LINK." + verdict switch
    {
        LinkVerdict.OtherRelease => "other_release",
        LinkVerdict.OtherGame => "other_game",
        LinkVerdict.ModeLocked => "mode_locked",
        LinkVerdict.AscensionTooHigh => "ascension",
        _ => "failed",
    };
}
