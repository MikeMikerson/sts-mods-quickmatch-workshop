using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Matchmaking;
using QuickMatch.Platforms.Android;
using QuickMatch.Platforms.Desktop;
using QuickMatch.Platforms.Relay;

namespace QuickMatch.Patches;

// Applies the mod's patches at run time, one feature at a time (FeaturePlan), each under its own Harmony id so it can
// be undone alone. A feature that reaches private game members by name has them checked first, so a renamed one turns the
// feature off at start instead of failing in the middle of a run.
internal static class GamePatches
{
    private static readonly Dictionary<GameFeature, (string Type, string Member)[]> CalledByName = new()
    {
        [GameFeature.StandIn] = DropPolicy.GameMembers,
        [GameFeature.Rejoin] = RejoinPolicy.GameMembers,
    };

    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // False when the mod is off.
    public static bool Apply()
    {
        bool android = AndroidPlatform.InLauncher();
        ILookup<GameFeature, Type> classes = AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly())
            .Where(type => type.IsDefined(typeof(HarmonyPatch), false))
            .ToLookup(FeatureOf);

        FeaturePlan.Outcome outcome = FeaturePlan.Apply(
            feature =>
            {
                if (android && feature == GameFeature.LinkJoin)
                {
                    return;
                }
                CheckCalledByName(feature);
                var harmony = new Harmony(Id(feature));
                foreach (Type type in classes[feature])
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                if (!android && feature == GameFeature.Voice)
                {
                    DesktopPlatform.Install(harmony);
                }
            },
            feature => new Harmony(Id(feature)).UnpatchAll(Id(feature)),
            MainFile.Logger.Warn);
        Features.TurnOff(outcome.Off);
        if (android)
        {
            Features.TurnOff([GameFeature.LinkJoin]);
        }
        if (outcome.ModOn)
        {
            if (android)
            {
                AndroidPlatform.Install();
            }
            RelaySetup.Install();
        }
        return outcome.ModOn;
    }

    private static void CheckCalledByName(GameFeature feature)
    {
        foreach ((string typeName, string member) in CalledByName.GetValueOrDefault(feature, []))
        {
            if (typeof(RunManager).Assembly.GetType(typeName)?.GetMember(member, AllMembers).Length != 1)
            {
                throw new MissingMemberException(typeName, member);
            }
        }
    }

    // A patch class without a feature counts as Core (a test keeps every one tagged).
    private static GameFeature FeatureOf(Type type) =>
        (type.GetCustomAttribute<PatchFeatureAttribute>() ?? type.DeclaringType?.GetCustomAttribute<PatchFeatureAttribute>())?.Feature
        ?? GameFeature.Core;

    private static string Id(GameFeature feature) => $"{MainFile.ModId}.{feature}";
}
