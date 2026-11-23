using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using QuickMatch.Lobby;
using QuickMatch.SteamKit;

namespace QuickMatch.Platforms.Android;

/// <summary>
/// Quick Match inside the Android StS2 Launcher, which runs the game with no Steamworks (its
/// libsteam_api is a stub). Gives QuickMatch a lobby backend on the launcher's own SteamKit2 and its
/// saved Steam sign-in, and patches the Steam identity of quick-match runs.
///
/// The SteamKit2 code is an embedded assembly so these types can load on desktop. <see cref="Install"/>
/// loads it before any method that names its types is compiled, so each step is its own method.
/// </summary>
internal static class AndroidPlatform
{
    /// <summary>The launcher's Steam connection class; the game runs inside the launcher when it exists.</summary>
    internal const string ConnectionTypeName = "STS2Mobile.Steam.Connection.SteamConnection, STS2Mobile.Steam";

    internal const string SteamKitAssemblyName = "QuickMatch.SteamKit";

    internal const string SteamKitResource = "QuickMatch.SteamKit.dll";

    /// <summary>Slay the Spire 2.</summary>
    private const uint GameAppId = 2868840;

    private static Assembly? _steamKit;

    /// <summary>The lobby backend, when running in the Android launcher; null elsewhere.</summary>
    internal static AndroidLobbyBackend? Backend { get; private set; }

    public static bool InLauncher() => Type.GetType(ConnectionTypeName, throwOnError: false) != null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Install()
    {
        try
        {
            if (LoadSteamKit())
            {
                Start();
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Quick Match stays unavailable: the Android lobby code could not be started ({e.GetType().Name})");
        }
    }

    /// <summary>Loads the embedded QuickMatch.SteamKit next to QuickMatch, and answers for it when the runtime asks.</summary>
    private static bool LoadSteamKit()
    {
        Assembly own = typeof(AndroidPlatform).Assembly;
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(own) ?? AssemblyLoadContext.Default;
        using (Stream? stream = own.GetManifestResourceStream(SteamKitResource))
        {
            if (stream == null)
            {
                MainFile.Logger.Warn($"{SteamKitResource} is missing from {own.GetName().Name}");
                return false;
            }
            _steamKit = context.LoadFromStream(stream);
        }
        context.Resolving += (_, name) => name.Name == SteamKitAssemblyName ? _steamKit : null;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start()
    {
        var log = new SteamKitLog(MainFile.Logger.Info, MainFile.Logger.Warn);
        SteamKitLobbyService? lobbies = SteamKitLobbyService.InLauncher(name => Type.GetType(name, throwOnError: false), GameAppId, log);
        if (lobbies == null)
        {
            MainFile.Logger.Warn("Quick Match stays unavailable");
            return;
        }
        var backend = new AndroidLobbyBackend(lobbies, log);
        Backend = backend;
        LobbyBackend.Current = backend;
        try
        {
            IdentityPatches.Apply(new Harmony(MainFile.ModId + ".Android"));
        }
        catch (Exception e)
        {
            // Lobbies still work; runs would show the launcher's made-up Steam ID and no names.
            MainFile.Logger.Warn($"could not patch the Steam identity of quick-match runs: {e.GetType().Name}: {e.Message}");
        }
        MainFile.Logger.Info("platform: Android launcher; lobbies go through the launcher's Steam sign-in");
        // Early, so the Quick Match card and our Steam ID are ready by the main menu.
        backend.StartSignIn();
    }
}
