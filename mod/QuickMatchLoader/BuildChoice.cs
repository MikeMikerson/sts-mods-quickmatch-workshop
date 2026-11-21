using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickMatchLoader;

// The mod's folder holds a build of QuickMatch for each game version it supports, QuickMatch-<major>.<minor>.dll. A
// game runs the newest build that is not newer than itself: a later game, before its own release of QuickMatch,
// gets the build for the version before it, whose features then check the game themselves.
public static class BuildChoice
{
    public const string Prefix = "QuickMatch-";

    public static string? For(Version game, IEnumerable<string> files) =>
        files
            .Select(file => (File: file, Version: VersionOf(file)))
            .Where(build => build.Version != null && build.Version <= new Version(game.Major, game.Minor))
            .OrderByDescending(build => build.Version)
            .Select(build => build.File)
            .FirstOrDefault();

    private static Version? VersionOf(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        return name.StartsWith(Prefix, StringComparison.Ordinal) && Version.TryParse(name[Prefix.Length..], out Version? version) ? version : null;
    }
}
