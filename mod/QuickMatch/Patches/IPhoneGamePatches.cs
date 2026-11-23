#if IPHONE
using HarmonyLib;
using QuickMatch.Platforms.IPhone;

namespace QuickMatch.Patches;

internal static class GamePatches
{
    public static bool Apply()
    {
        new Harmony(MainFile.ModId).PatchAll(typeof(MainFile).Assembly);
        Features.TurnOff([GameFeature.LinkJoin]);
        IPhonePlatform.Install();
        return true;
    }
}
#endif
