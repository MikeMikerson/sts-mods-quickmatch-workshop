using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// Which joiners a quick-match host turns away, before the game adds them to its lobby. Both patches do
// nothing outside a quick-match lobby, so vanilla lobbies and friends' games are left as they are.
[PatchFeature(GameFeature.Core)]
public static class JoinPatches
{
    // A lobby before the run: a player blocked by the host or sent away by a member (the block list), then
    // a joiner whose maximum for the lobby's mode is below its locked ascension. An honest Quick Match never
    // asks to join a lobby below its maximum; an old or modified one would.
    [HarmonyPatch(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage")]
    public static class StartRunJoin
    {
        public static bool Prefix(StartRunLobby __instance, ClientLobbyJoinRequestMessage message, ulong senderId)
        {
            if (__instance.NetService is not INetHostGameService host)
            {
                return true;
            }
            if (Matchmaker.RefusesJoin(__instance, senderId))
            {
                MainFile.Logger.Info($"refused {senderId}: blocked in this lobby");
                host.DisconnectClient(senderId, NetError.InvalidJoin);
                return false;
            }
            int maximum = GameVersions.JoinerMaximum(message, __instance.GameMode);
            if (AscensionLock.For(__instance) is not { } ascension || MatchPolicy.Admits(maximum, ascension))
            {
                return true;
            }
            MainFile.Logger.Info($"refused {senderId}: their {__instance.GameMode} maximum A{maximum} is below the lobby's A{ascension}");
            host.DisconnectClient(senderId, NetError.InvalidJoin);
            return false;
        }
    }

    // The lobby of a reopened quick-match run (a regroup or rejoin): the host turns away a player it has
    // blocked.
    [HarmonyPatch(typeof(LoadRunLobby), "HandleClientLoadJoinRequestMessage")]
    public static class LoadRunJoin
    {
        public static bool Prefix(LoadRunLobby __instance, ulong senderId)
        {
            if (__instance.NetService is not INetHostGameService host || !Matchmaker.RefusesRejoin(__instance.Run, senderId))
            {
                return true;
            }
            MainFile.Logger.Info($"refused {senderId}: blocked or voted out, so not let back into the saved run");
            host.DisconnectClient(senderId, NetError.InvalidJoin);
            return false;
        }
    }
}
