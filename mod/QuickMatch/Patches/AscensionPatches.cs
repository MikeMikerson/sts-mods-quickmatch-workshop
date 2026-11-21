using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// Holds a quick-match host's lobby at its ascension (QuickMatch.Matchmaking.AscensionLock). Each patch does
// nothing outside a locked quick-match lobby. The host's refusal of a joiner below the lobby's ascension is
// in JoinPatches.
[PatchFeature(GameFeature.Core)]
public static class AscensionPatches
{
    // The game saves the lobby's ascension as the host's preferred one; a quick-match lobby's comes from
    // the match, not from the host's choice.
    [HarmonyPatch(typeof(StartRunLobby), "UpdatePreferredAscension")]
    public static class PreferredAscension
    {
        public static bool Prefix(StartRunLobby __instance) => AscensionLock.For(__instance) == null;
    }

    // The host's panel stays on the locked ascension (arrows, hotkeys and the shared maximum cannot move it).
    [HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetAscensionLevel))]
    public static class PanelLevel
    {
        public static void Prefix(NAscensionPanel __instance, ref int ascension)
        {
            if (AscensionLock.ForPanel(__instance) is { } locked)
            {
                ascension = locked;
            }
        }
    }
}
