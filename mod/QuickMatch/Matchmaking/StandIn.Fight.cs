using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace QuickMatch.Matchmaking;

internal static partial class StandIn
{
    private static void OnTurnStarted(CombatState state)
    {
        if (state.CurrentSide != CombatSide.Player)
        {
            return;
        }
        EnemyTurnSent.Clear();
        Safely(EndAbsentTurns, "end their turn");
    }

    private static void EndAbsentTurns()
    {
        CombatManager combat = CombatManager.Instance;
        // The game's only public accessor for the combat in progress; it just reads it.
        if (!combat.IsInProgress || combat.DebugOnlyGetState() is not { } state || state.CurrentSide != CombatSide.Player)
        {
            return;
        }
        foreach (Player player in state.Players)
        {
            if (IsAbsent(player.NetId) && player.PlayerCombatState is { } turn && !player.Creature.IsDead
                && !combat.IsPlayerReadyToEndTurn(player) && EndTurnSent.Add((turn.TurnNumber, player.NetId)))
            {
                Enqueue(new EndPlayerTurnAction(player, turn.TurnNumber), player);
            }
        }
    }

    // Once a player is ready for the enemy turn, the absent follow the last present one.
    public static void FillEnemyTurn()
    {
        CombatManager combat = CombatManager.Instance;
        if (!IsHost || !combat.IsInProgress || combat.DebugOnlyGetState() is not { } state || state.CurrentSide != CombatSide.Player)
        {
            return;
        }
        if (Field(combat, "_turnState") is not { } turnState)
        {
            return;
        }
        var ready = (HashSet<Player>)turnState.GetType().GetProperty("PlayersReadyToBeginEnemyTurn", Members)!.GetValue(turnState)!;
        IReadOnlyList<Player> players = state.Players;
        foreach (int slot in DropPolicy.SlotsToFill(players.Select(ready.Contains).ToList(), Present(players)))
        {
            Player player = players[slot];
            if (EnemyTurnSent.Add(player.NetId))
            {
                Enqueue(new ReadyToBeginEnemyTurnAction(player), player);
            }
        }
    }

    public static void BeforeRoomSync()
    {
        if (!IsHost || _state == null || RunManager.Instance.RunLobby is not { } lobby)
        {
            return;
        }
        List<ulong> absent = DropPolicy.Absent(_state.Players.Select(p => p.NetId), lobby.PlayerIds.ToHashSet());
        _fightAbsent = absent.ToHashSet();
        EnemyTurnSent.Clear();
        if (absent.Count > 0)
        {
            MainFile.Logger.Info($"next room without absent players {string.Join(",", absent)}");
        }
        _net!.SendMessage(new QuickMatchFightAbsentMessage { players = absent });
    }

    // A player rejoining a run under way (RunRejoin) gets the room's list with the host's answer, since the host's
    // message for that room comes before this peer listens.
    public static void SetFightAbsent(IEnumerable<ulong> players)
    {
        if (_active)
        {
            _fightAbsent = players.ToHashSet();
        }
    }

    private static void OnFightAbsentMessage(QuickMatchFightAbsentMessage message, ulong senderId)
    {
        if (!_active || _net?.Type != NetGameType.Client)
        {
            return;
        }
        _fightAbsent = (message.players ?? []).ToHashSet();
    }

    // With no one absent, or a finished fight being loaded, the game adds every player itself.
    public static void BuildFight(CombatRoom room, IRunState? runState)
    {
        if (!_active || _fightAbsent.Count == 0 || runState == null || room.IsPreFinished || room.CombatState.Players.Count > 0)
        {
            return;
        }
        ulong local = LocalContext.NetId ?? 0;
        List<ulong> fighters = DropPolicy.FightPlayers(runState.Players.Select(p => p.NetId).ToList(), _fightAbsent, local);
        foreach (Player player in runState.Players)
        {
            if (fighters.Contains(player.NetId))
            {
                room.CombatState.AddPlayer(player);
                continue;
            }
            // A fresh, empty combat state (as the game gives every player in a finished room), since the player
            // panels read it, and HP held at 0 for the fight.
            player.ResetCombatState();
            if (player.Creature.CurrentHp > 0)
            {
                HeldHp[player.Creature] = player.Creature.CurrentHp;
                SetHp(player.Creature, 0);
            }
            MainFile.Logger.Info($"player {player.NetId} is left out of this fight");
        }
    }

    public static void GiveHpBack()
    {
        foreach ((Creature creature, int hp) in HeldHp)
        {
            SetHp(creature, hp);
        }
        HeldHp.Clear();
    }

    // Straight to the field, so none of the game's hooks or death handling runs for a player who is not in the fight.
    private static void SetHp(Creature creature, int hp) => typeof(Creature).GetField("_currentHp", Members)!.SetValue(creature, hp);
}
