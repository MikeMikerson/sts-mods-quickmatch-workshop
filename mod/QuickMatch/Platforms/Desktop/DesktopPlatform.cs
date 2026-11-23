using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using QuickMatch.Lobby;
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
    private static SocketVoiceControl? _control;
    private static RelayRoute? _route;
    private static ulong _lobby;
    private static ulong _host;

    public static void AfterUpdate(SteamClient __instance)
    {
        if (__instance.IsConnected && __instance.LobbyId is { } lobby && LobbyLinks.IsListed(lobby.m_SteamID))
        {
            if (_lobby != lobby.m_SteamID || _host != __instance.HostNetId)
            {
                AfterDisconnect();
                _lobby = lobby.m_SteamID;
                _host = __instance.HostNetId;
            }
            if (MatchVoice.On)
            {
                _control ??= new SocketVoiceControl(_lobby, _host, peer => peer == _host, null);
                _control.Poll(OpenRelay);
            }
            if (_control?.Switched == true)
            {
                _route?.Update((_, _) => { });
                VoiceChat.TickRelay(_route, _lobby);
            }
            else
            {
                VoiceChat.TickSteam(_lobby);
            }
        }
    }

    private static RelayRoute? OpenRelay()
    {
        _route = RelayRoute.ForClient(RelayTransport.Current, LobbyBackend.Current.LocalId, ChatClock.Now, message => MainFile.Logger.Info(message));
        _route?.SetVoiceHost(_host);
        return _route;
    }

    public static void AfterDisconnect()
    {
        VoiceChat.End();
        _control?.Dispose();
        _control = null;
        _route?.Stop();
        _route = null;
        _lobby = 0;
        _host = 0;
    }
}
