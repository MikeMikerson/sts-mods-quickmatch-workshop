using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using QuickMatch.Transport;
using QuickMatch.Voice;

namespace QuickMatch.Platforms.Desktop;

// Installed with the voice feature (GamePatches).
internal static class DesktopPlatform
{
    // Also checked by the tests against the installed game.
    internal const string UpdateMethod = nameof(SteamClient.Update);
    internal const string DisconnectMethod = "DisconnectFromHostInternal";

    public static void Install(Harmony harmony)
    {
        harmony.Patch(Target(UpdateMethod), postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(SteamClientVoice), nameof(SteamClientVoice.AfterUpdate))));
        harmony.Patch(Target(DisconnectMethod), postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(SteamClientVoice), nameof(SteamClientVoice.AfterDisconnect))));
    }

    private static MethodInfo Target(string method) =>
        AccessTools.DeclaredMethod(typeof(SteamClient), method) ?? throw new MissingMethodException(nameof(SteamClient), method);
}

// A player who joined a match over the game's own Steam client, which QuickMatch does not replace: its update drives
// voice, and its disconnect ends it. Other Steam games (friends' runs) are left alone.
internal static class SteamClientVoice
{
    public static void AfterUpdate(SteamClient __instance)
    {
        if (__instance.IsConnected && __instance.LobbyId is { } lobby && LobbyLinks.IsListed(lobby.m_SteamID))
        {
            VoiceChat.TickSteam(lobby.m_SteamID);
        }
    }

    public static void AfterDisconnect() => VoiceChat.End();
}
