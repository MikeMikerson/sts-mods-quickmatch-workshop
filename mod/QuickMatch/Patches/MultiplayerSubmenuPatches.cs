using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Ui;

namespace QuickMatch.Patches;

[PatchFeature(GameFeature.Core)]
[HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
public static class MultiplayerSubmenuReadyPatch
{
    public static void Postfix(NMultiplayerSubmenu __instance)
    {
        QuickMatchButton.Attach(__instance);
    }
}

// Keeps the card's visibility in step with the Host card (for example after abandoning a run).
[PatchFeature(GameFeature.Core)]
[HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
public static class MultiplayerSubmenuUpdateButtonsPatch
{
    public static void Postfix(NMultiplayerSubmenu __instance)
    {
        QuickMatchButton.Refresh(__instance);
    }
}

// The submenu outlives a visit; refresh the cards whenever it comes back into view (the run record may have
// changed).
[PatchFeature(GameFeature.Core)]
[HarmonyPatch(typeof(NMultiplayerSubmenu), "OnSubmenuShown")]
public static class MultiplayerSubmenuShownPatch
{
    public static void Postfix(NMultiplayerSubmenu __instance)
    {
        QuickMatchButton.Refresh(__instance);
    }
}
