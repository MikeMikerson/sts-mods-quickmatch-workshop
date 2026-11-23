using MegaCrit.Sts2.Core.Modding;
using QuickMatch.Logging;
using QuickMatch.Patches;

namespace QuickMatch;

// Called before ModelDb is populated: on desktop by the loader (QuickMatch.dll), which the game scans instead of this
// build; through the attribute where this assembly is the mod's own.
[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string ModId = "QuickMatch";

    public static ModLogger Logger { get; } = new(ModId);

    public static void Initialize()
    {
        if (!GamePatches.Apply())
        {
            return;
        }

        Logger.Info("QuickMatch initialized.");
    }
}
