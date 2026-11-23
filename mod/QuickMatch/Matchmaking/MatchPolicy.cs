using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace QuickMatch.Matchmaking;

// A quick-match lobby seen in a Steam lobby search. Members counts the host. CreatedAt is Steam server time in seconds,
// 0 if unknown. Ascension is qm_asc (0 to 10), -1 when missing or not a number. Size is qm_size (2 to 4; 4 for an Any
// lobby), 0 when unknown. AnySize is an Any lobby (qm_size "any"): up to 4 players, starting with 2 or more once all
// are ready.
public readonly record struct LobbyCandidate(ulong Id, int Members, uint CreatedAt, int Ascension = 0, int Size = 0, bool AnySize = false,
    bool HostOffersSockets = true)
{
    public int FreeSlots => Size > 0 ? Math.Max(0, Size - Members) : 0;
}

// Only asked of a player whose multiplayer maximum is above 0.
public enum AscensionMode
{
    // Exactly the player's maximum, so a win advances them.
    Progress,
    // Any ascension the player has unlocked (A0 to their maximum).
    Random,
    Choose,
}

// Chosen matters only for AscensionMode.Choose.
public readonly record struct AscensionChoice(AscensionMode Mode, IReadOnlyList<int> Chosen)
{
    public static AscensionChoice Progress => new(AscensionMode.Progress, []);
    public static AscensionChoice Random => new(AscensionMode.Random, []);
    public static AscensionChoice Of(params int[] chosen) => new(AscensionMode.Choose, chosen);

    // For the log.
    public string Text => Mode switch
    {
        AscensionMode.Progress => "progress",
        AscensionMode.Random => "random",
        _ => "choose:" + string.Join(",", (Chosen ?? []).Order()),
    };
}

// Players waiting now in open lobbies this player could join, as the hub and the ascension chooser show them: the
// search behind them asks for the player's own voice pool, so voice splits nothing here. Sizes is keyed by the size a
// search asks for: 2 to 4 counts the lobbies of that exact size, 0 (Any) every lobby. Ascensions is keyed by (size,
// ascension), sizes as in Sizes.
public sealed record PoolCounts(
    int Total,
    IReadOnlyDictionary<int, int> Sizes,
    IReadOnlyDictionary<(int Size, int Ascension), int> Ascensions)
{
    public static readonly PoolCounts Empty = new(0, new Dictionary<int, int>(), new Dictionary<(int, int), int>());

    public int ForSize(int size) => Sizes.GetValueOrDefault(size);

    // size 0 is Any.
    public int ForAscensions(int size, IEnumerable<int> ascensions) =>
        ascensions.Distinct().Sum(a => Ascensions.GetValueOrDefault((size, a)));
}

// The line a host nobody has joined gets, with its one button. Level is 0 when the line names no ascension; ClimbNote
// adds the note that a match at another ascension will not unlock the next one.
public readonly record struct AloneOffer(string LineKey, string ButtonKey, int Size, int Level, bool ClimbNote);

// What moves the idle-client cap's count (MatchPolicy.IdleStreak).
public enum IdleEvent
{
    // The player started a search themselves (Play Now, Climb, the alone line's button, Rejoin Run).
    SearchStarted,
    Embarked,
    // A host's countdown sent the player back to searching (LeaveReasons.Idle).
    SentBack,
}

// Pure matching rules, kept free of Godot and Steam types so they can be unit tested.
public static class MatchPolicy
{
    // Desktop hosts sort first. Within each kind, fuller groups, then older lobbies, then lower lobby ids sort first.
    // Everyone uses the same order, so searchers pile into the same lobbies (groups fill quickly) and two hosts waiting
    // alone always agree on who yields.
    public static int Compare(LobbyCandidate a, LobbyCandidate b)
    {
        int byHost = b.HostOffersSockets.CompareTo(a.HostOffersSockets);
        if (byHost != 0)
        {
            return byHost;
        }
        int byMembers = b.Members.CompareTo(a.Members);
        if (byMembers != 0)
        {
            return byMembers;
        }
        int byAge = a.CreatedAt.CompareTo(b.CreatedAt);
        return byAge != 0 ? byAge : a.Id.CompareTo(b.Id);
    }

    // Drops our own lobby, lobbies that failed recently, duplicates, and lobbies whose data has not fully arrived (no
    // creation time). Ascension is a filter, not a ranking key: a lobby with no valid ascension is never in ascensions.
    // Within each host kind, a randomSize search prefers the fewest free slots before applying the usual order.
    public static List<LobbyCandidate> Rank(IEnumerable<LobbyCandidate> found, ulong ownLobbyId, Func<ulong, bool> skip,
        IReadOnlyCollection<int>? ascensions = null, bool randomSize = false)
    {
        IEnumerable<LobbyCandidate> kept = found
            .Where(c => c.Id != ownLobbyId && c.CreatedAt != 0 && !skip(c.Id))
            .Where(c => ascensions == null || (c.Ascension >= 0 && ascensions.Contains(c.Ascension)))
            .Where(c => !randomSize || c.FreeSlots > 0)
            .GroupBy(c => c.Id)
            .Select(g => g.First());
        if (randomSize)
        {
            return kept.OrderByDescending(c => c.HostOffersSockets).ThenBy(c => c.FreeSlots)
                .ThenBy(c => c, Comparer<LobbyCandidate>.Create(Compare)).ToList();
        }
        return kept.OrderBy(c => c, Comparer<LobbyCandidate>.Create(Compare)).ToList();
    }

    // The game's cap on multiplayer ascension.
    public const int MaxAscension = 10;

    // Never above the player's own multiplayer maximum, so the game never pulls a lobby down because of them. Progress
    // is exactly the maximum; Random is A0 to the maximum; Choose is the picked ones that are unlocked (all of them if
    // none is left).
    public static IReadOnlyList<int> AllowedAscensions(AscensionChoice choice, int maxAscension)
    {
        int max = Math.Clamp(maxAscension, 0, MaxAscension);
        List<int> all = Enumerable.Range(0, max + 1).ToList();
        return choice.Mode switch
        {
            AscensionMode.Progress => [max],
            AscensionMode.Random => all,
            _ => (choice.Chosen ?? []).Where(a => a >= 0 && a <= max).Distinct().Order().ToList() is { Count: > 0 } picked ? picked : all,
        };
    }

    // The ascension a player hosts at when their search found nothing. Random hosts at A0, the lowest, so the most
    // players can join.
    public static int HostAscension(AscensionChoice choice, int maxAscension) =>
        choice.Mode == AscensionMode.Random ? 0 : AllowedAscensions(choice, maxAscension)[0];

    // The levels last chosen that are unlocked now, else Any; a player with nothing unlocked plays Any (A0).
    public static AscensionChoice PlayNowChoice(AscensionChoice last, int maxAscension)
    {
        int max = Math.Clamp(maxAscension, 0, MaxAscension);
        if (last.Mode != AscensionMode.Choose || max == 0)
        {
            return AscensionChoice.Random;
        }
        List<int> kept = (last.Chosen ?? []).Where(a => a >= 0 && a <= max).Distinct().Order().ToList();
        return kept.Count > 0 ? new AscensionChoice(AscensionMode.Choose, kept) : AscensionChoice.Random;
    }

    public const double FewPlayersSeconds = 60;

    // The keys of the status area's first two lines in character select: who is here, then when the run starts. An Any
    // lobby starts with 2 or more once all here are ready, and says so. A player alone for a minute is told instead that
    // Quick Match needs more players searching at once, and how to find them.
    public static (string Here, string Start) StatusKeys(int here, int size, bool ready, bool anySize = false, double waited = 0)
    {
        bool full = here >= size;
        string first = full ? "QUICKMATCH.STATUS.all_here" : anySize ? "QUICKMATCH.STATUS.looking_any" : "QUICKMATCH.STATUS.looking";
        if (here == 1 && !full && waited >= FewPlayersSeconds)
        {
            return (first, "QUICKMATCH.STATUS.few_players");
        }
        string second = anySize
            ? (ready, here >= 2) switch
            {
                (false, _) => "QUICKMATCH.STATUS.starts_when_any",
                (true, false) => "QUICKMATCH.STATUS.ready_any_alone",
                (true, true) => "QUICKMATCH.STATUS.ready_others",
            }
            : (ready, full) switch
            {
                (false, false) => "QUICKMATCH.STATUS.starts_when",
                (false, true) => "QUICKMATCH.STATUS.press_embark",
                (true, false) => "QUICKMATCH.STATUS.ready_more",
                (true, true) => "QUICKMATCH.STATUS.ready_others",
            };
        return (first, second);
    }

    public const double CountdownSeconds = 75;

    // The idle countdown runs only when the lobby could start but for the players who have not pressed Embark (an exact
    // size full, an Any lobby with 2 or more), and more than half of the players here have pressed it (one of two
    // counts). A lobby still waiting for players, or with nobody idle, has no countdown.
    public static bool CountdownRuns(int here, int ready, int size, bool anySize)
    {
        bool couldStart = anySize ? here >= 2 : here >= size;
        return couldStart && ready >= 1 && ready < here && (here == 2 || ready * 2 > here);
    }

    // A reminder to press Embark, or what happens to the others.
    public static string CountdownKey(bool ready) => ready ? "QUICKMATCH.STATUS.countdown_ready" : "QUICKMATCH.STATUS.countdown_press";

    // How many lobbies in a row may send a player back by their countdown before the search stops.
    public const int IdleSendBacksBeforeStop = 2;

    // The idle-client cap's count: lobbies in a row whose countdown sent this player back to searching. A search the
    // player starts and a press of Embark start it again from 0; being sent back adds one; nothing else (a merge, a
    // block, a host who did not press Embark) moves it.
    public static int IdleStreak(int streak, IdleEvent what) => what switch
    {
        IdleEvent.SentBack => streak + 1,
        IdleEvent.SearchStarted or IdleEvent.Embarked => 0,
        _ => streak,
    };

    // The player stops searching and goes back to the Quick Match screen, as an idle host does: a player who walked
    // away holds up no more lobbies for a countdown each.
    public static bool IdleStops(int streak) => streak >= IdleSendBacksBeforeStop;

    public static bool Admits(int joinerMaxAscension, int lobbyAscension) => joinerMaxAscension >= lobbyAscension;

    // An Any player who found nothing hosts an Any lobby, which starts with 2 or more once all here are ready.
    public const int AnySizeMax = 4;

    // A search looks among nearby lobbies first (Steam's default distance: the player's region and those near it), so
    // players far apart meet only when nobody nearby is looking, and asks worldwide only when none nearby is one to
    // join. search takes nearby and returns the lobbies to try.
    public static async Task<List<T>> NearbyFirstAsync<T>(Func<bool, Task<List<T>>> search)
    {
        List<T> near = await search(true);
        return near.Count > 0 ? near : await search(false);
    }

    // An exact-size lobby with 2 or more players that has stayed this long at the same count short of full asks
    // everyone here whether to start now. Asked once per count: a join or leave starts the wait again.
    public const double StartShortSeconds = 120;

    // A player who has not answered by then counts as a no.
    public const double StartShortAnswerSeconds = 30;

    public static bool AsksToStartShort(int here, int size, bool anySize, double secondsAtThisCount, bool askedAtThisCount) =>
        !anySize && here >= 2 && here < size && secondsAtThisCount >= StartShortSeconds && !askedAtThisCount;

    // Everyone who was asked must say yes; one no or no answer in time refuses, and anyone arriving or leaving ends
    // the question.
    public static StartShortOutcome StartShortTally(IReadOnlyCollection<ulong> asked, IReadOnlyCollection<ulong> present,
        IReadOnlyCollection<ulong> yes, bool anyNo, bool timedOut)
    {
        if (asked.Count != present.Count || !asked.All(present.Contains))
        {
            return StartShortOutcome.Ended;
        }
        if (anyNo)
        {
            return StartShortOutcome.Refused;
        }
        if (asked.All(yes.Contains))
        {
            return StartShortOutcome.Agreed;
        }
        return timedOut ? StartShortOutcome.Refused : StartShortOutcome.Waiting;
    }

    // How long a host is alone before the line that offers a wider search shows.
    public const double AloneOfferSeconds = 60;

    // Null when the search is as wide as it gets (any size, and any ascension or none unlocked), else a line naming
    // what was narrowed (the size, the ascension or both) and a button that widens it. A search for exactly the
    // player's maximum below the cap is a climb, so its line says what widening costs. allowed is AllowedAscensions.
    public static AloneOffer? OfferWhenAlone(bool anySize, int size, AscensionChoice choice, int maxAscension, IReadOnlyList<int> allowed)
    {
        bool narrowAscension = maxAscension > 0 && choice.Mode != AscensionMode.Random && allowed.Count > 0;
        if (anySize && !narrowAscension)
        {
            return null;
        }
        if (!narrowAscension)
        {
            return new AloneOffer("QUICKMATCH.ALONE.size", "QUICKMATCH.ALONE.any_size", size, 0, false);
        }
        bool one = allowed.Count == 1;
        bool climb = one && allowed[0] == maxAscension && maxAscension < MaxAscension;
        string line = anySize ? "QUICKMATCH.ALONE.asc" : "QUICKMATCH.ALONE.both";
        return new AloneOffer(one ? line : line + "_many", anySize ? "QUICKMATCH.ALONE.any_asc" : "QUICKMATCH.ALONE.any_both",
            anySize ? 0 : size, one ? allowed[0] : 0, climb);
    }

    // Counts for the hub and the ascension chooser from one wide search: the members of open lobbies that this player
    // could join (no lobby above their maximum, not full), in all, by the size a search asks for (an exact lobby under
    // its size and Any, an Any lobby under Any only, since an exact search never finds it), and by ascension within
    // that.
    public static PoolCounts Count(IEnumerable<LobbyCandidate> lobbies, int maxAscension)
    {
        int total = 0;
        var sizes = new Dictionary<int, int>();
        var ascensions = new Dictionary<(int, int), int>();
        foreach (LobbyCandidate lobby in lobbies.GroupBy(l => l.Id).Select(g => g.First()))
        {
            if (lobby.Members <= 0 || lobby.CreatedAt == 0 || lobby.Size < 2 || lobby.FreeSlots <= 0
                || lobby.Ascension < 0 || lobby.Ascension > maxAscension)
            {
                continue;
            }
            int n = lobby.Members;
            total += n;
            foreach (int size in lobby.AnySize ? new[] { 0 } : new[] { lobby.Size, 0 })
            {
                sizes[size] = sizes.GetValueOrDefault(size) + n;
                ascensions[(size, lobby.Ascension)] = ascensions.GetValueOrDefault((size, lobby.Ascension)) + n;
            }
        }
        return new PoolCounts(total, sizes, ascensions);
    }

    // For a host whose lobby is not yet full: the lobby its whole group should move into, or null to keep waiting. Only
    // a strictly better lobby of the same kind with room for the group qualifies, so of two partial groups at most one
    // moves, and never into each other. Only a lobby at our own ascension qualifies: everyone in the group has it
    // unlocked, so the move never pulls the lobby down, and a progression group stays at its ascension. An Any group
    // also moves into an exact-size lobby it would fill, however old: a search for that size never finds an Any lobby,
    // so this is how the two meet. An exact group never moves into an Any lobby, so no two groups ever move into each
    // other. size is 4 for an Any lobby.
    public static LobbyCandidate? PickMergeTarget(LobbyCandidate own, IEnumerable<LobbyCandidate> found, Func<ulong, bool> skip, int size)
    {
        if (own.Members < 1 || own.Members >= size)
        {
            return null;
        }
        foreach (LobbyCandidate candidate in Rank(found, own.Id, skip, ascensions: [own.Ascension]))
        {
            if (own.AnySize && !candidate.AnySize)
            {
                if (candidate.Size > 0 && candidate.Members + own.Members == candidate.Size)
                {
                    return candidate;
                }
                continue;
            }
            // Ranked best first, so no later lobby of our kind is better either; an exact one we would fill may still follow.
            if (candidate.AnySize != own.AnySize || Compare(candidate, own) >= 0)
            {
                continue;
            }
            if (candidate.Members + own.Members <= size)
            {
                return candidate;
            }
        }
        return null;
    }

    // Identifies one co-op run. The host computes it from its save and every player from the running game, so all of
    // them agree on it.
    public static string RunKey(string seed, IEnumerable<ulong> playerIds)
    {
        string ids = string.Join(",", playerIds.Distinct().OrderBy(id => id));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + ids));
        return Convert.ToHexString(digest, 0, 12).ToLowerInvariant();
    }

    // A client follows a group move only from the host of the lobby it is in: no other player can send the group
    // elsewhere.
    public static bool AcceptsMove(ulong senderId, ulong hostId, ulong targetLobbyId, ulong currentLobbyId) =>
        hostId != 0 && senderId == hostId && targetLobbyId != 0 && targetLobbyId != currentLobbyId;

    // Order-independent fingerprint of the gameplay-affecting mod list ("id-version" entries, as the multiplayer
    // handshake compares them).
    public static string ModsHash(IEnumerable<string>? mods)
    {
        List<string> sorted = (mods ?? [])
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();
        if (sorted.Count == 0)
        {
            return "none";
        }
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", sorted)));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }
}
