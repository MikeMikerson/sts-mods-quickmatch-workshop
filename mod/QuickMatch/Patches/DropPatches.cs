using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
#if GAME_0111
using MegaCrit.Sts2.Core.Models.Singleton;
#endif
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// A quick-match run goes on without a player who dropped. Each patch hands over to
// QuickMatch.Matchmaking.StandIn, which does nothing outside a quick-match run, except the two scaling
// patches, which count the fight's players instead of the run's: the same number in any run where everyone is
// in every fight.
[PatchFeature(GameFeature.StandIn)]
public static class DropPatches
{
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
    public static class EnemyTurnReady
    {
        public static void Postfix() => StandIn.FillEnemyTurn();
    }

    [HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.OnPlayerReady))]
    public static class NextActReady
    {
        public static void Postfix() => StandIn.FillAct();
    }

    [HarmonyPatch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
    public static class SharedEventVote
    {
        public static void Postfix() => StandIn.FillEvent();
    }

    [HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
    public static class WaitingForChoice
    {
        public static void Postfix(Player player, uint choiceId) => StandIn.OnWaitingForChoice(player, choiceId);
    }

    // The host's "nothing chosen" read as combat cards: none.
    [HarmonyPatch(typeof(PlayerChoiceResult), nameof(PlayerChoiceResult.AsCombatCards))]
    public static class NoCombatCards
    {
        public static bool Prefix(List<int>? ____indexes, ref IEnumerable<CardModel> __result)
        {
            if (!StandIn.IsNothingChosen(____indexes))
            {
                return true;
            }
            __result = Array.Empty<CardModel>();
            return false;
        }
    }

    // The host's "nothing chosen" read as deck cards: none.
    [HarmonyPatch(typeof(PlayerChoiceResult), nameof(PlayerChoiceResult.AsDeckCards))]
    public static class NoDeckCards
    {
        public static bool Prefix(List<int>? ____indexes, ref IEnumerable<CardModel> __result)
        {
            if (!StandIn.IsNothingChosen(____indexes))
            {
                return true;
            }
            __result = Array.Empty<CardModel>();
            return false;
        }
    }

    // The host's "nothing chosen" read as a player: no one.
    [HarmonyPatch(typeof(PlayerChoiceResult), nameof(PlayerChoiceResult.AsPlayerId))]
    public static class NoPlayer
    {
        public static bool Prefix(List<int>? ____indexes, ref ulong? __result)
        {
            if (!StandIn.IsNothingChosen(____indexes))
            {
                return true;
            }
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameActionPlayerChoiceContext), nameof(GameActionPlayerChoiceContext.SignalPlayerChoiceEnded))]
    public static class ActionChoiceEnded
    {
        public static void Prefix(GameActionPlayerChoiceContext __instance)
        {
            if (StandIn.TakeResume(__instance.Action.OwnerId))
            {
                RunManager.Instance.ActionQueueSynchronizer.RequestResumeActionAfterPlayerChoice(__instance.Action);
            }
        }
    }

    [HarmonyPatch(typeof(HookPlayerChoiceContext), nameof(HookPlayerChoiceContext.SignalPlayerChoiceEnded))]
    public static class HookChoiceEnded
    {
        public static void Prefix(GenericHookGameAction? ____gameAction)
        {
            if (____gameAction != null && StandIn.TakeResume(____gameAction.OwnerId))
            {
                RunManager.Instance.ActionQueueSynchronizer.RequestResumeActionAfterPlayerChoice(____gameAction);
            }
        }
    }

    [HarmonyPatch(typeof(GenericHookGameAction), nameof(GenericHookGameAction.SetChoiceContext))]
    public static class HookChoiceBegun
    {
        public static void Postfix(GenericHookGameAction __instance) => StandIn.EnqueueHookIfAbsent(__instance);
    }

    // Each room's sync: first a returning player is let in (RunRejoin), then everyone is told who is left out
    // of the fight.
    [HarmonyPatch(typeof(CombatStateSynchronizer), nameof(CombatStateSynchronizer.StartSync))]
    public static class RoomSync
    {
        public static void Prefix()
        {
            RunRejoin.BeforeRoomSync();
            StandIn.BeforeRoomSync();
        }
    }

    [HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.EnterInternal))]
    public static class FightEntered
    {
        public static void Prefix(CombatRoom __instance, IRunState? runState) => StandIn.BuildFight(__instance, runState);
    }

    // A fight's end, before the game saves the run: absent players get their HP back.
    [HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.OnCombatEnded))]
    public static class FightEnded
    {
        public static void Prefix() => StandIn.GiveHpBack();
    }

    // Enemy block is scaled for the players in the fight, as enemy HP is, not for the run's.
    [HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyBlockMultiplicative))]
    public static class BlockForFighters
    {
        public static void Postfix(ref decimal __result, Creature target)
        {
            // The game returns 1 for block it does not scale, and the run's count (2 or more) for block it does.
            if (__result != 1m && target?.CombatState is { } combat)
            {
                __result = DropPolicy.BlockScale(combat.Players.Count,
                    MultiplayerScalingModel.GetMultiplayerScaling(combat.Encounter, combat.RunState.CurrentActIndex));
            }
        }
    }

    // An enemy's Plating wears off by the number of players in the fight, not in the run.
    [HarmonyPatch(typeof(PlatingPower), nameof(PlatingPower.AfterApplied))]
    public static class PlatingForFighters
    {
        public static void Postfix(PlatingPower __instance)
        {
            if (__instance.Owner.Side == CombatSide.Enemy && __instance.Owner.CombatState is { } combat)
            {
                __instance.DynamicVars["Decrement"].BaseValue = combat.Players.Count;
            }
        }
    }
}
