using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Matchmaking;
using QuickMatch.Ui;

namespace QuickMatch.Patches;

// Embark and its undo in a quick-match lobby: the screen shows its own "Waiting for other players" panel
// after Embark, and QuickMatch's status area says the same and more, so the game's panel is hidden again and
// ours says what changed. Embark also starts the idle-client cap's count again.
[PatchFeature(GameFeature.Core)]
public static class WaitingPanelPatches
{
    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
    public static class EmbarkPressed
    {
        public static void Postfix(NCharacterSelectScreen __instance) => AfterEmbark(__instance);
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "OnUnreadyPressed")]
    public static class UnreadyPressed
    {
        public static void Postfix(NCharacterSelectScreen __instance) => Follow(__instance);
    }

    public static void AfterEmbark(NSubmenu screen)
    {
        Matchmaker.OnEmbarkPressed(screen);
        Follow(screen);
    }

    public static void Follow(NSubmenu screen)
    {
        if (Matchmaker.Lobby != null && StartScreens.LobbyOf(screen) == Matchmaker.Lobby)
        {
            LobbyStatusPanel.Refresh(screen);
        }
    }
}
