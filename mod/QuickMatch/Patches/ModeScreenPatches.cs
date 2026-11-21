using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// The Blitz and Dread lobby screens get what character select gets in a quick-match lobby (CharacterSelectPatches,
// WaitingPanelPatches, VoicePatches). The stable branch has neither, so its build leaves this file out.
[PatchFeature(GameFeature.Blitz)]
public static class BlitzScreenPatches
{
    [HarmonyPatch(typeof(NBlitzScreen), nameof(NBlitzScreen.OnSubmenuOpened))]
    public static class Opened
    {
        public static void Postfix(NBlitzScreen __instance) => CharacterSelectPatches.AfterOpened(__instance);
    }

    [HarmonyPatch(typeof(NBlitzScreen), nameof(NBlitzScreen.OnSubmenuClosed))]
    public static class Closing
    {
        public static void Prefix(NBlitzScreen __instance) => Matchmaker.OnCharacterSelectClosing(__instance);
    }

    [HarmonyPatch(typeof(NBlitzScreen), nameof(NBlitzScreen.BeginRun))]
    public static class RunBegins
    {
        public static void Postfix(NBlitzScreen __instance) => Matchmaker.OnRunStarted(__instance);
    }

    [HarmonyPatch(typeof(NBlitzScreen), "OnEmbarkPressed")]
    public static class EmbarkPressed
    {
        public static void Postfix(NBlitzScreen __instance) => WaitingPanelPatches.AfterEmbark(__instance);
    }

    [HarmonyPatch(typeof(NBlitzScreen), "OnUnreadyPressed")]
    public static class UnreadyPressed
    {
        public static void Postfix(NBlitzScreen __instance) => WaitingPanelPatches.Follow(__instance);
    }
}

[PatchFeature(GameFeature.Dread)]
public static class DreadScreenPatches
{
    [HarmonyPatch(typeof(NDreadScreen), nameof(NDreadScreen.OnSubmenuOpened))]
    public static class Opened
    {
        public static void Postfix(NDreadScreen __instance) => CharacterSelectPatches.AfterOpened(__instance);
    }

    [HarmonyPatch(typeof(NDreadScreen), nameof(NDreadScreen.OnSubmenuClosed))]
    public static class Closing
    {
        public static void Prefix(NDreadScreen __instance) => Matchmaker.OnCharacterSelectClosing(__instance);
    }

    [HarmonyPatch(typeof(NDreadScreen), nameof(NDreadScreen.BeginRun))]
    public static class RunBegins
    {
        public static void Postfix(NDreadScreen __instance) => Matchmaker.OnRunStarted(__instance);
    }

    [HarmonyPatch(typeof(NDreadScreen), "OnEmbarkPressed")]
    public static class EmbarkPressed
    {
        public static void Postfix(NDreadScreen __instance) => WaitingPanelPatches.AfterEmbark(__instance);
    }

    [HarmonyPatch(typeof(NDreadScreen), "OnUnreadyPressed")]
    public static class UnreadyPressed
    {
        public static void Postfix(NDreadScreen __instance) => WaitingPanelPatches.Follow(__instance);
    }
}
