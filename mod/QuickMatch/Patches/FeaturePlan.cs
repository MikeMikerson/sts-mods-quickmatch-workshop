using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Patches;

// The order features are patched in and what each needs. A feature whose patching throws (a target missing or
// changed by a game update) is undone and turned off with a log line, and so is every feature that needs it; when
// Core fails, the mod stays off.
internal static class FeaturePlan
{
    public static readonly IReadOnlyList<(GameFeature Feature, GameFeature[] Needs)> All =
    [
        (GameFeature.Core, []),
        (GameFeature.InviteButton, []),
        (GameFeature.LinkJoin, []),
        (GameFeature.RunTracking, []),
        (GameFeature.Regroup, [GameFeature.RunTracking]),
        (GameFeature.StandIn, [GameFeature.RunTracking]),
        (GameFeature.Rejoin, [GameFeature.Regroup, GameFeature.StandIn]),
        (GameFeature.PlayerWindow, []),
        // Muting is in the player window.
        (GameFeature.Voice, [GameFeature.PlayerWindow]),
        // Hide Emotes is in the player window; the button shows only in a tracked quick-match run.
        (GameFeature.Emotes, [GameFeature.RunTracking, GameFeature.PlayerWindow]),
        (GameFeature.Blitz, []),
        (GameFeature.Dread, []),
    ];

    public sealed record Outcome(bool ModOn, IReadOnlySet<GameFeature> Off);

    public static Outcome Apply(Action<GameFeature> apply, Action<GameFeature> undo, Action<string> log)
    {
        var off = new HashSet<GameFeature>();
        foreach (var (feature, needs) in All)
        {
            GameFeature[] missing = needs.Where(off.Contains).ToArray();
            if (missing.Length > 0)
            {
                off.Add(feature);
                log($"{feature} is off: it needs {string.Join(", ", missing)}");
                continue;
            }
            try
            {
                apply(feature);
            }
            catch (Exception e)
            {
                undo(feature);
                // Harmony wraps the cause, such as "Undefined target method for patch method ...".
                string reason = e.GetBaseException().Message;
                if (feature == GameFeature.Core)
                {
                    log($"QuickMatch is off: this game version changed what it needs ({reason})");
                    return new Outcome(false, All.Select(f => f.Feature).ToHashSet());
                }
                off.Add(feature);
                log($"{feature} is off: this game version changed what it patches ({reason})");
            }
        }
        return new Outcome(true, off);
    }
}
