using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Matchmaking;
using QuickMatch.Ui;

namespace QuickMatch.Patches;

// The Quick Match screen (QuickMatchHub) is an instance of the vanilla host submenu: its Standard, Daily and
// Custom cards are Play Now, Climb A{n} and Match Options, and it never hosts.
[PatchFeature(GameFeature.Core)]
public static class HubPatches
{
    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "OnStandardPressed")]
    public static class StandardPressed
    {
        public static bool Prefix(NMultiplayerHostSubmenu __instance) => !Handle(__instance, QuickMatchHub.PlayNow);
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "OnDailyPressed")]
    public static class DailyPressed
    {
        public static bool Prefix(NMultiplayerHostSubmenu __instance) => !Handle(__instance, QuickMatchHub.Climb);
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "OnCustomPressed")]
    public static class CustomPressed
    {
        public static bool Prefix(NMultiplayerHostSubmenu __instance) => !Handle(__instance, MatchOptions.Open);
    }

#if !GAME_0111
    // Hidden on the hub; patched so a press that reaches it there still never hosts.
    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "OnBlitzPressed")]
    public static class BlitzPressed
    {
        public static bool Prefix(NMultiplayerHostSubmenu __instance) => !QuickMatchHub.Is(__instance);
    }
#endif

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu.OnSubmenuOpened))]
    public static class Opened
    {
        public static void Postfix(NMultiplayerHostSubmenu __instance)
        {
            if (QuickMatchHub.Is(__instance))
            {
                QuickMatchHub.OnOpened(__instance);
            }
        }
    }

    // Leaving the hub (Back, or a Steam invite popping every submenu) cancels the search and closes Match
    // Options. NMultiplayerHostSubmenu does not override OnSubmenuClosed, so the base method is patched.
    [HarmonyPatch(typeof(NSubmenu), nameof(NSubmenu.OnSubmenuClosed))]
    public static class Closed
    {
        public static void Postfix(NSubmenu __instance)
        {
            if (QuickMatchHub.Is(__instance))
            {
                MatchOptions.CloseIfOpen();
                Matchmaker.Cancel("Quick Match screen closed");
            }
        }
    }

    private static bool Handle(NMultiplayerHostSubmenu instance, System.Action action)
    {
        if (!QuickMatchHub.Is(instance))
        {
            return false;
        }
        action();
        return true;
    }
}
