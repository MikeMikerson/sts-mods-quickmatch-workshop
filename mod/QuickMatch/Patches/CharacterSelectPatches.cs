using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Matchmaking;
using QuickMatch.Ui;

namespace QuickMatch.Patches;

[PatchFeature(GameFeature.Core)]
public static class CharacterSelectPatches
{
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    public static class Opened
    {
        public static void Postfix(NCharacterSelectScreen __instance) => AfterOpened(__instance);
    }

    // A prefix: the vanilla method nulls the lobby, which is needed to know whose screen closed.
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
    public static class Closing
    {
        public static void Prefix(NCharacterSelectScreen __instance) => Matchmaker.OnCharacterSelectClosing(__instance);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    public static class RunBegins
    {
        public static void Postfix(NCharacterSelectScreen __instance) => Matchmaker.OnRunStarted(__instance);
    }

    public static void AfterOpened(NSubmenu screen)
    {
        LobbyStatusPanel.Refresh(screen);
        Matchmaker.OnCharacterSelectOpened(screen);
    }
}
