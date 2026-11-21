using QuickMatch.Logging;
using QuickMatch.Patches;

namespace QuickMatch;

// The loader (QuickMatch.dll) calls Initialize when the game loads the mod, before ModelDb is populated.
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
