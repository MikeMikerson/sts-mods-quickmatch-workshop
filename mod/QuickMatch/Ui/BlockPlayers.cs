using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

internal static class BlockPlayers
{
    // The other players of our quick-match lobby, or of our quick-match run, or of the voice session.
    public static List<ulong> Here()
    {
        ulong self = LocalId();
        IEnumerable<ulong> players = Matchmaker.Lobby?.Players.Select(p => p.id)
            ?? (Regroup.InQuickMatchRun && RunRecord.Current is { } record ? record.PlayerIds : null)
            ?? VoiceChat.Members;
        return players.Where(p => p != self && p != 0).Distinct().ToList();
    }

    // In a quick-match run with this player, blocking them asks whether to stay or leave.
    public static bool InRunWith(ulong player) =>
        player != 0 && Regroup.InQuickMatchRun && RunRecord.Current is { } record && record.PlayerIds.Contains(player);

    // A vote can be started against another client of our quick-match lobby, or another present player of our
    // quick-match run, the host included, within the vote's limits.
    public static bool CanVoteAgainst(ulong player) =>
        player != 0 && LobbyGuard.CanVoteAgainst(player);

    // Voting out the host of our run moves the run to a new host.
    public static bool AsksAboutHost(ulong player) =>
        LobbyGuard.InRun && RunManager.Instance.NetService is NetClientGameService { IsConnected: true } client && client.HostNetId == player;

    public static ulong LocalId()
    {
        try
        {
            return RunRecord.LocalPlayerId();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // The Steam ID when Steam does not know the name.
    public static string NameOf(ulong player) =>
        KnownName(player) ?? BlockStore.List.Players.FirstOrDefault(p => p.Id == player)?.Name
            ?? Loc.With("QUICKMATCH.BLOCK.steam_id", "id", player.ToString(CultureInfo.InvariantCulture));

    public static string? KnownName(ulong player)
    {
        try
        {
            // The raw name: these labels are plain text, not BBCode. A player Steam does not know comes back as "[unknown]".
            string name = PlatformUtil.GetPlayerNameRaw(PlatformType.Steam, player).Trim();
            return name.Length == 0 || name == player.ToString(CultureInfo.InvariantCulture) || name.Equals("[unknown]", StringComparison.OrdinalIgnoreCase) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Date(long at) =>
        DateTimeOffset.FromUnixTimeSeconds(at).ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string ClockTime(long at) =>
        DateTimeOffset.FromUnixTimeSeconds(at).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
