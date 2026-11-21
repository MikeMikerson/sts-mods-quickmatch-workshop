using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// Follows quick-match runs from launch to end, so a dropped group can regroup.
[PatchFeature(GameFeature.RunTracking)]
public static class RunPatches
{
    // Every run, new or loaded, goes through Launch once it is set up.
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    public static class Launched
    {
        public static void Postfix(RunState __result)
        {
            Regroup.OnRunLaunched(__result);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    public static class CleaningUp
    {
        public static void Prefix()
        {
            Regroup.OnRunCleanUp();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
    public static class Ended
    {
        public static void Postfix()
        {
            Regroup.OnRunEnded();
        }
    }

    // A client that loses the host mid-run goes to the rejoin search instead of the main menu with an error
    // popup. Not called for game over or abandon (vanilla filters those first).
    [PatchFeature(GameFeature.Regroup)]
    [HarmonyPatch(typeof(RunManager), "ReturnToMainMenuWithError")]
    public static class LostHost
    {
        public static bool Prefix(NetErrorInfo info, ref Task __result)
        {
            if (Regroup.ReplaceDisconnectFlow(info) is not { } replacement)
            {
                return true;
            }
            __result = replacement;
            return false;
        }
    }

    // The host reopens a saved run (vanilla Load, or a regroup). Our last quick-match run is reopened as a
    // quick-match lobby that run's players can find, once a search for it finds no new host running it; other
    // saves keep the vanilla host and friends-only lobby.
    [PatchFeature(GameFeature.Regroup)]
    [HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu.StartHost))]
    public static class LoadHosting
    {
        public static bool Prefix(SerializableRun run)
        {
            string? key = RunRecord.KeyOf(run);
            if (key == null || RunRecord.Current?.Key != key || StartScreens.FromGame(run.GameMode, GameVersions.IsCustom(run)) == null)
            {
                MainFile.Logger.Info($"loaded run key={key} is not our quick-match run; vanilla host");
                return true;
            }
            TaskHelper.RunSafely(Regroup.SearchThenHostAsync(run, key));
            return false;
        }
    }
}
