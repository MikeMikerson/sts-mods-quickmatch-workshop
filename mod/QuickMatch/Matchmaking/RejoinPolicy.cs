using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

// The rules for a player who dropped coming back into a quick-match run under way. The game side is RunRejoin.
//
// - While a player of the run is absent, the host lists the run's Steam lobby under the run's key, which only the run's
//   players know; once no one is absent it goes back to private.
// - A rejoin request from a QuickMatch player of the run is held, and answered with the game's own rejoin at the start
//   of the party's next room (the sync that every peer runs as it enters a map point, right after the map vote). The
//   returning player builds the run from that answer and enters the same room through the same sync, so no fight, event
//   or reward screen is rebuilt half way.
// - A request from a client that did not say it is QuickMatch (the game's own join screen, which cannot take a run
//   under way) is refused as the game refuses a run in progress; a blocked player is refused as in a resume lobby.
public static class RejoinPolicy
{
    // The game's members RunRejoin reaches by reflection, by type's full name, for the test that they still exist.
    public static readonly (string Type, string Member)[] GameMembers =
    [
        ("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.RunLobby", "HandleClientRejoinRequestMessage"),
        ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "_syncData"),
        ("MegaCrit.Sts2.Core.Multiplayer.NetHostGameService", "SendMessageToClientInternal"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.MapSelectionSynchronizer", "MapGenerationCount"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet", "FastForwardNextActionId"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSynchronizer", "FastForwardHookId"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.ChecksumTracker", "LoadReplayChecksums"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "FastForwardChoiceIds"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer", "FastForwardRewardIds"),
    ];

    public enum Answer
    {
        // Not a player of the run: the game refuses them itself.
        Game,

        // A player of the run who did not say they run QuickMatch: refused as a run in progress.
        NotQuickMatch,

        // A player of the run whom the host refuses (blocked): refused as an invalid join.
        Refused,

        // Held until the party's next room starts.
        Hold,
    }

    public static Answer OnRequest(bool inRun, bool saidQuickMatch, bool refused)
    {
        if (!inRun)
        {
            return Answer.Game;
        }
        if (refused)
        {
            return Answer.Refused;
        }
        return saidQuickMatch ? Answer.Hold : Answer.NotQuickMatch;
    }

    // The sync that starts a room from the map has no room open (the game left them all just before). The game also
    // syncs when an event opens a fight inside its room, which is not a point to join at.
    public static bool ReleasesAt(int openRooms, int held) => openRooms == 0 && held > 0;

    // Connected players first, in the order given.
    public static List<ulong> PlayersAfter(IEnumerable<ulong> connected, IEnumerable<ulong> releasing)
    {
        var list = connected.ToList();
        foreach (ulong player in releasing)
        {
            if (!list.Contains(player))
            {
                list.Add(player);
            }
        }
        return list;
    }

    public static List<ulong> AbsentAfter(IEnumerable<ulong> runPlayers, IEnumerable<ulong> connected, IEnumerable<ulong> releasing)
    {
        return DropPolicy.Absent(runPlayers, PlayersAfter(connected, releasing));
    }

    // Players voted out of the run never come back, so the host neither lists the run nor asks Save and Wait for them.
    public static List<ulong> Awaited(IEnumerable<ulong> runPlayers, IReadOnlyCollection<ulong> connected, IReadOnlyCollection<ulong> removed)
    {
        return DropPolicy.Absent(runPlayers, connected).Where(p => !removed.Contains(p)).ToList();
    }

    // A run's resume lobby carries the resume marker; a search for players neither joins it nor counts it as waiting.
    public static bool IsResumeListing(string? resumeProto) => !string.IsNullOrEmpty(resumeProto);

    public static bool ListsRun(int absent) => absent > 0;

    // The answer must be the returning player's own run (by its key), with them in it.
    public static bool AcceptsAnswer(string? answerKey, string? recordKey, IEnumerable<ulong> answerPlayers, ulong local)
    {
        return answerKey != null && answerKey == recordKey && answerPlayers.Contains(local);
    }
}
