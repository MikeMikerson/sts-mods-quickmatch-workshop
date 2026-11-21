using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// A player who dropped comes back into a quick-match run under way. The host's half is here; the room-start
// release shares StandIn's patch on the room sync (DropPatches.RoomSync), since a game method takes one patch
// class.
[PatchFeature(GameFeature.Rejoin)]
public static class RejoinPatches
{
    // The host holds a returning QuickMatch player's rejoin until the party's next room, or refuses it.
    [HarmonyPatch(typeof(RunLobby), "HandleClientRejoinRequestMessage")]
    public static class RejoinRequest
    {
        public static bool Prefix(RunLobby __instance, ulong senderId) => RunRejoin.OnRejoinRequest(__instance, senderId);
    }
}
