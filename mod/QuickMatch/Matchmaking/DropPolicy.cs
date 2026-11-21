using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

// The rules for a quick-match run that goes on without a player who dropped. The game side is StandIn.
//
// - A player is absent when they are in the run but not on the host's list of connected players.
// - Every wait that counts all of the run's players is filled for the absent ones by the host: their turn ends at once,
//   and the rest (the enemy turn's start, the map, the next act, the treasure, shared events) is filled once every
//   present player has acted, so the absent never act first.
// - A vote an absent player must give copies the vote of a present player picked at random. The game then picks at
//   random among all votes, so each present vote keeps the chance it would have had without the absent player.
// - From the next fight on, absent players are left out of the fight, and the enemies are scaled for the players in it.
public static class DropPolicy
{
    // The game's members StandIn reaches by reflection, by type's full name, for the test that they still exist.
    public static readonly (string Type, string Member)[] GameMembers =
    [
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSynchronizer", "EnqueueAction"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "OnReceivePlayerChoice"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "_receivedChoices"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer+ReceivedChoice", "senderId"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer+ReceivedChoice", "choiceId"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer+ReceivedChoice", "completionSource"),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "_turnState"),
        ("MegaCrit.Sts2.Core.Combat.CombatTurnState", "PlayersReadyToBeginEnemyTurn"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.ActChangeSynchronizer", "_readyPlayers"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer", "_votes"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_canonicalEvent"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_playerVotes"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_pageIndex"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "PlayerVotedForSharedOptionIndex"),
        ("MegaCrit.Sts2.Core.Entities.Creatures.Creature", "_currentHp"),
    ];

    public static List<ulong> Absent(IEnumerable<ulong> runPlayers, IReadOnlyCollection<ulong> connected)
    {
        return runPlayers.Where(id => !connected.Contains(id)).ToList();
    }

    // The local player always fights, whatever the list says, so a fight never starts without its own player.
    public static List<ulong> FightPlayers(IReadOnlyList<ulong> runPlayers, IReadOnlyCollection<ulong> absent, ulong local)
    {
        return runPlayers.Where(id => id == local || !absent.Contains(id)).ToList();
    }

    // Empty while a present player still has to act, so the absent never act first. done and present are per slot, in
    // the run's order: whether that player has acted (voted, picked, is ready) and whether they are connected.
    public static List<int> SlotsToFill(IReadOnlyList<bool> done, IReadOnlyList<bool> present)
    {
        if (done.Count != present.Count)
        {
            throw new ArgumentException($"{done.Count} slots of votes for {present.Count} players");
        }
        var fill = new List<int>();
        for (int i = 0; i < done.Count; i++)
        {
            if (present[i] && !done[i])
            {
                return [];
            }
            if (!present[i] && !done[i])
            {
                fill.Add(i);
            }
        }
        return fill;
    }

    // roll returns a number from 0 to its argument minus one. Null when no present player has voted.
    public static T? CopiedVote<T>(IReadOnlyList<T?> votes, IReadOnlyList<bool> present, Func<int, int> roll) where T : struct
    {
        var cast = new List<T>();
        for (int i = 0; i < votes.Count; i++)
        {
            if (present[i] && votes[i] is { } vote)
            {
                cast.Add(vote);
            }
        }
        return cast.Count == 0 ? null : cast[roll(cast.Count)];
    }

    // Enemy block in a fight of fighters players, as the game's MultiplayerScalingModel (v0.113.1) scales it for the
    // run's players: times the count for one or two, else the count times the act's factor.
    public static decimal BlockScale(int fighters, decimal actFactor)
    {
        return fighters <= 2 ? Math.Max(1, fighters) : fighters * actFactor;
    }

    // With at least one other present player the run goes on with no question; a host left alone is asked whether to
    // wait for the others (Regroup) or go on alone.
    public static bool AskHostAlone(int presentPlayers)
    {
        return presentPlayers <= 1;
    }
}
