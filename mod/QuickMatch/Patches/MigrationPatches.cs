using HarmonyLib;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// A quick-match run whose host goes carries on under one of its clients. The loss of the host is
// RunPatches.LostHost and the reopening is Regroup's hosting; here are the clients' copies of the run and the
// load lobby's start check.
[PatchFeature(GameFeature.Regroup)]
public static class MigrationPatches
{
    // Every save moment of the game (room entry, a won fight, a finished event) comes here; a client keeps a
    // copy.
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
    public static class SaveMoment
    {
        public static void Prefix(AbstractRoom? preFinishedRoom) => HostCopy.OnSave(preFinishedRoom);
    }

    // A client keeps the saved run it was given (a load lobby, or a rejoin answer) as its first copy.
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))]
    public static class SavedRunSetUp
    {
        public static void Prefix(LoadRunLobby lobby) => HostCopy.OnSavedRunSetUp(lobby);
    }

    // The game's load lobby never begins a multiplayer run with one player. A quick-match resume lobby may,
    // after the game's own question about the missing players, so the one player left of a run can go on
    // alone.
    [HarmonyPatch(typeof(LoadRunLobby), nameof(LoadRunLobby.IsAboutToBeginGame))]
    public static class AboutToBegin
    {
        public static void Postfix(LoadRunLobby __instance, ref bool __result)
        {
            if (!__result && Regroup.MayBeginAlone(__instance))
            {
                __result = true;
            }
        }
    }

    // The game asks before loading a run with players missing. A reopened quick-match run whose missing
    // players were all voted out of it begins without asking: they never come.
    [HarmonyPatch(typeof(NMultiplayerLoadGameScreen), nameof(NMultiplayerLoadGameScreen.ShouldAllowRunToBegin))]
    public static class MissingOnlyVotedOut
    {
        public static bool Prefix(LoadRunLobby? ____runLobby, ref Task<bool> __result)
        {
            if (____runLobby == null || !Regroup.MissingOnlyVotedOut(____runLobby))
            {
                return true;
            }
            __result = Task.FromResult(true);
            return false;
        }
    }
}
