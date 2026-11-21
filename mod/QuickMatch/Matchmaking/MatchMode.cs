using System;
using System.Collections.Generic;
using QuickMatch.Steam;

namespace QuickMatch.Matchmaking;

// The game modes Quick Match offers, in Match Options' order.
internal enum RunMode
{
    Standard,
    Blitz,
    Dread,
}

// The game mode of the current quick match. Each mode is its own pool: lobbies carry it and every search asks for it,
// so players of different modes never meet. Each also has its own ascension: Standard's is the multiplayer maximum,
// Blitz's one level shared by every character, and Dread is always played at the same level, so a Dread match has no
// ascension to choose or match. Game thread only.
internal static class MatchMode
{
    public static readonly IReadOnlyList<RunMode> All = [RunMode.Standard, RunMode.Blitz, RunMode.Dread];

    public static RunMode On { get; private set; } = RunMode.Standard;

    public static void Set(RunMode mode) => On = mode;

    public static string LobbyValue(RunMode mode) => mode switch
    {
        RunMode.Blitz => LobbyKeys.ModeBlitz,
        RunMode.Dread => LobbyKeys.ModeDread,
        _ => LobbyKeys.ModeStandard,
    };

    public static string Text(RunMode mode) => $"mode={LobbyValue(mode)}";

    // A saved mode the player cannot play (not unlocked, or turned off by a game update) is Standard.
    public static RunMode FromSaved(string? saved, Func<RunMode, bool> playable)
    {
        foreach (RunMode mode in All)
        {
            if (string.Equals(saved, LobbyValue(mode), StringComparison.Ordinal) && playable(mode))
            {
                return mode;
            }
        }
        return RunMode.Standard;
    }

    // The mode's highest ascension: Blitz has 5 levels.
    public static int TopAscension(RunMode mode) => mode switch
    {
        RunMode.Blitz => 5,
        RunMode.Dread => 0,
        _ => MatchPolicy.MaxAscension,
    };

    // The highest ascension the player can match at in this mode; 0 for Dread, whose ascension is fixed.
    public static int MaxAscension(RunMode mode, int standardMax, int blitzMax) => mode switch
    {
        RunMode.Blitz => blitzMax,
        RunMode.Dread => 0,
        _ => standardMax,
    };
}
