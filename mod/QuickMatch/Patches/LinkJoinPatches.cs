using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Platform.Steam;
using QuickMatch.Matchmaking;

namespace QuickMatch.Patches;

// Every Steam invite and steam://joinlobby link, also one that started the game, goes through JoinToHost. The game's
// join would put a Quick Match player into a quick-match lobby without Quick Match's session (no voice, votes or
// countdown), so such a lobby is joined through Quick Match instead. The game's debug scene and its own joins with an
// initializer are left alone.
[PatchFeature(GameFeature.LinkJoin)]
public static class LinkJoinPatches
{
    [HarmonyPatch(typeof(SteamJoinCallbackHandler), "JoinToHost")]
    public static class SteamJoin
    {
        [HarmonyReversePatch]
        public static Task GameJoin(ulong lobbyId, ulong? playerId, IClientConnectionInitializer? connInitializer) =>
            throw new NotImplementedException("replaced by the game's JoinToHost");

        public static bool Prefix(ulong lobbyId, ulong? playerId, IClientConnectionInitializer? connInitializer, ref Task __result)
        {
            if (connInitializer != null || !Matchmaker.IsAvailable || NGame.Instance?.RootSceneContainer.CurrentScene is NMultiplayerTest)
            {
                return true;
            }
            __result = LinkJoin.RouteAsync(lobbyId, playerId, () => GameJoin(lobbyId, playerId, null));
            return false;
        }
    }
}
