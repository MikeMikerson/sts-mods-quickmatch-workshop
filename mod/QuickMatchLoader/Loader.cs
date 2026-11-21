using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace QuickMatchLoader;

// The game loads QuickMatch.dll and calls Initialize. This loads the build of QuickMatch for the game's version
// (BuildChoice) into the same load context, as ModManager loads a mod, and starts it.
[ModInitializer(nameof(Initialize))]
public static class Loader
{
    public static void Initialize()
    {
        string folder = Path.GetDirectoryName(typeof(Loader).Assembly.Location)!;
        SemanticVersion? game = ReleaseInfoManager.Instance.SemVer;
        if (game == null)
        {
            Log.Error("[QuickMatch] QuickMatch is off: the game's version is unknown");
            return;
        }
        string? build = BuildChoice.For(new Version(game.Major, game.Minor), Directory.GetFiles(folder, BuildChoice.Prefix + "*.dll"));
        if (build == null)
        {
            Log.Error($"[QuickMatch] QuickMatch is off: it has no build for game version {game}");
            return;
        }
        Log.Info($"[QuickMatch] loading {Path.GetFileName(build)} for game version {game}");
        Assembly mod = AssemblyLoadContext.GetLoadContext(typeof(Loader).Assembly)!.LoadFromAssemblyPath(build);
        // The game finds a mod's network messages and other types in the assemblies on its record, which holds only
        // QuickMatch.dll, so the build goes there too.
        if (ModManager.Mods.FirstOrDefault(m => m.manifest?.id == "QuickMatch") is not { } record)
        {
            Log.Error("[QuickMatch] QuickMatch is off: the game has no record of it");
            return;
        }
        record.assemblies.Add(mod);
        mod.GetType("QuickMatch.MainFile", throwOnError: true)!.GetMethod("Initialize")!.Invoke(null, null);
    }
}
