using System.Collections.Generic;

namespace QuickMatch.Patches;

// Which features are on. GamePatches turns off the ones whose patches could not be applied.
internal static class Features
{
    private static readonly HashSet<GameFeature> Off = [];

    public static bool IsOn(GameFeature feature) => !Off.Contains(feature);

    public static void TurnOff(IEnumerable<GameFeature> features) => Off.UnionWith(features);
}
