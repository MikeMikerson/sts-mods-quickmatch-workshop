using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuickMatch.Platforms.Android;

/// <summary>
/// Names the game method an Android-only patch class changes. Not a [HarmonyPatch]: QuickMatch's
/// PatchAll (and the iPhone app's weaver) must not pick these up, so <see cref="IdentityPatches.Apply"/>
/// patches them by hand, inside the Android launcher only.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class AndroidPatchAttribute(Type target, string method) : Attribute
{
    public Type Target { get; } = target;

    public string Method { get; } = method;
}

/// <summary>
/// Steam identity for quick-match runs in the Android launcher. The game reports a quick-match run's
/// platform as Steam and then asks the Steam platform for the local player and for names; the
/// launcher's Steamworks stub answers one made-up Steam ID for every Android player and no names.
/// These answer the signed-in account instead. The None platform (the launcher's own saves under
/// default/1 and its LAN play) is never touched. Applied only in the Android launcher.
/// </summary>
internal static class IdentityPatches
{
    /// <summary>Patches every class below with its Prefix. Throws when a target is missing (another game version).</summary>
    public static void Apply(Harmony harmony)
    {
        foreach (Type patch in typeof(IdentityPatches).GetNestedTypes(BindingFlags.NonPublic))
        {
            AndroidPatchAttribute? at = patch.GetCustomAttribute<AndroidPatchAttribute>();
            if (at == null)
            {
                continue;
            }
            MethodInfo target = AccessTools.DeclaredMethod(at.Target, at.Method)
                ?? throw new MissingMethodException(at.Target.FullName, at.Method);
            harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(patch, "Prefix")));
        }
    }

    /// <summary>Reads progress, run history and the abandon path of a finished Steam-platform run.</summary>
    [AndroidPatch(typeof(SteamPlatformUtilStrategy), nameof(SteamPlatformUtilStrategy.GetLocalPlayerId))]
    private static class LocalPlayerId
    {
        private static bool Prefix(ref ulong __result)
        {
            ulong id = AndroidPlatform.Backend?.LocalId ?? 0;
            if (id == 0)
            {
                return true;
            }
            __result = id;
            return false;
        }
    }

    [AndroidPatch(typeof(SteamPlatformUtilStrategy), nameof(SteamPlatformUtilStrategy.GetPlayerName))]
    private static class PlayerName
    {
        private static bool Prefix(ulong playerId, ref string __result)
        {
            if (AndroidPlatform.Backend is not { } backend)
            {
                return true;
            }
            // The game's own fallback when Steam knows no name.
            __result = backend.PlayerName(playerId) ?? playerId.ToString();
            return false;
        }
    }

    /// <summary>
    /// The multiplayer menu loads the run save with the primary platform's local ID, which is 1 here.
    /// A quick-match save lists Steam IDs, so the game would call the save invalid and rename it as
    /// broken. For a Steam-platform save that lists our Steam ID and not the given one, use ours.
    /// </summary>
    [AndroidPatch(typeof(RunManager), nameof(RunManager.CanonicalizeSave))]
    private static class CanonicalizeSave
    {
        private static void Prefix(SerializableRun save, ref ulong localPlayerId)
        {
            ulong steamId = AndroidPlatform.Backend?.LocalId ?? 0;
            if (steamId == 0 || steamId == localPlayerId || save.PlatformType != PlatformType.Steam || save.Players == null)
            {
                return;
            }
            ulong given = localPlayerId;
            if (save.Players.Any(p => p.NetId == given) || !save.Players.Any(p => p.NetId == steamId))
            {
                return;
            }
            MainFile.Logger.Info($"loading a Steam-platform run save as {steamId} instead of {given}");
            localPlayerId = steamId;
        }
    }
}
