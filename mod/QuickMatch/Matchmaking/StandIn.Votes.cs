using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace QuickMatch.Matchmaking;

internal static partial class StandIn
{
    private static void OnMapVoteChanged(Player player, MapVote? before, MapVote? after) => Safely(FillMap, "vote on the map");

    private static void FillMap()
    {
        if (!IsHost || _state == null)
        {
            return;
        }
        MapSelectionSynchronizer map = RunManager.Instance.MapSelectionSynchronizer;
        int generation = map.MapGenerationCount;
        IReadOnlyList<Player> players = _state.Players;
        List<MapVote?> votes = players.Select(p => map.GetVote(p) is { } v && v.mapGenerationCount == generation ? v : (MapVote?)null).ToList();
        List<bool> present = Present(players);
        MapLocation source = _state.MapLocation;
        foreach (int slot in DropPolicy.SlotsToFill(votes.Select(v => v.HasValue).ToList(), present))
        {
            Player player = players[slot];
            if (DropPolicy.CopiedVote(votes, present, Random.Shared.Next) is { } vote && MapSent.Add((source, generation, player.NetId)))
            {
                Enqueue(new VoteForMapCoordAction(player, source, vote), player);
            }
        }
    }

    public static void FillAct()
    {
        if (!IsHost || _state == null)
        {
            return;
        }
        var ready = (List<bool>)Field(RunManager.Instance.ActChangeSynchronizer, "_readyPlayers")!;
        IReadOnlyList<Player> players = _state.Players;
        if (ready.Count != players.Count)
        {
            return;
        }
        int act = _state.CurrentActIndex;
        foreach (int slot in DropPolicy.SlotsToFill(ready, Present(players)))
        {
            Player player = players[slot];
            if (ActSent.Add((act, player.NetId)))
            {
                Enqueue(new VoteToMoveToNextActAction(player, act), player);
            }
        }
    }

    private static void FillRelics()
    {
        if (!IsHost || _state == null)
        {
            return;
        }
        TreasureRoomRelicSynchronizer treasure = RunManager.Instance.TreasureRoomRelicSynchronizer;
        if (treasure.CurrentRelics is not { } relics)
        {
            return;
        }
        if (!ReferenceEquals(_relicSession, relics))
        {
            _relicSession = relics;
            RelicSent.Clear();
        }
        var votes = (IList)Field(treasure, "_votes")!;
        IReadOnlyList<Player> players = _state.Players;
        if (votes.Count != players.Count)
        {
            return;
        }
        var done = votes.Cast<TreasureRoomRelicSynchronizer.PlayerVote>().Select(v => v.voteReceived).ToList();
        foreach (int slot in DropPolicy.SlotsToFill(done, Present(players)))
        {
            Player player = players[slot];
            if (RelicSent.Add(player.NetId))
            {
                Enqueue(new PickRelicAction(player, null), player);
            }
        }
    }

    // Only the host decides a shared event, so the absent player's vote is recorded on the host alone, through the
    // game's own vote method.
    public static void FillEvent()
    {
        if (!IsHost || _state == null || _fillingEvent)
        {
            return;
        }
        EventSynchronizer events = RunManager.Instance.EventSynchronizer;
        if (Field(events, "_canonicalEvent") == null || !events.IsShared)
        {
            return;
        }
        var votes = (List<uint?>)Field(events, "_playerVotes")!;
        IReadOnlyList<Player> players = _state.Players;
        if (votes.Count < players.Count)
        {
            return;
        }
        List<uint?> ours = votes.Take(players.Count).ToList();
        List<bool> present = Present(players);
        List<int> slots = DropPolicy.SlotsToFill(ours.Select(v => v.HasValue).ToList(), present);
        if (slots.Count == 0)
        {
            return;
        }
        uint page = (uint)Field(events, "_pageIndex")!;
        MethodInfo vote = Method(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
        _fillingEvent = true;
        try
        {
            foreach (int slot in slots)
            {
                if (DropPolicy.CopiedVote(ours, present, Random.Shared.Next) is not { } option)
                {
                    return;
                }
                MainFile.Logger.Info($"host stands in for {players[slot].NetId}: shared event option {option} on page {page}");
                vote.Invoke(events, [players[slot], option, page]);
            }
        }
        finally
        {
            _fillingEvent = false;
        }
    }
}
