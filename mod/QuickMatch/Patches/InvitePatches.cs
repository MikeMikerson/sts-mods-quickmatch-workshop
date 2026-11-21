using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using QuickMatch.Transport;

namespace QuickMatch.Patches;

// The lobby's Invite button opens the Steam overlay's invite, which vanilla only does for its own Steam host
// or client and throws for QuickMatch's host. The button is hidden there. A client keeps it, as in vanilla:
// its host takes Steam sockets too.
[PatchFeature(GameFeature.InviteButton)]
public static class InvitePatches
{
    [HarmonyPatch(typeof(NInvitePlayersButton), nameof(NInvitePlayersButton.Initialize))]
    public static class HideForQuickMatchHost
    {
        public static void Postfix(NInvitePlayersButton __instance, StartRunLobby lobby)
        {
            if (lobby.NetService is QuickMatchHostService)
            {
                ((CanvasItem)__instance).Visible = false;
            }
        }
    }
}
