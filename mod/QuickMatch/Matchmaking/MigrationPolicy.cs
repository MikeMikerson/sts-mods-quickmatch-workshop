using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

// The rules for a quick-match run whose host goes. The game side is HostCopy, Regroup and the rejoin search in
// Matchmaker.
//
// - Every client keeps a copy of the run, taken at the game's save moments as the host's save is.
// - When a client loses the host, desktops may reopen the run first. Within each platform group, candidates stay in
//   run order. Every client has the same platform list from the original host.
// - Every candidate searches for the run's lobby first and joins it when found. A client cannot tell the host leaving
//   from its own connection dropping, and in the second case the host goes on and lists the run under its key. The
//   first candidate hosts after SearchFirstSeconds of finding nothing, each next one NextHostSeconds later, in case the
//   one before did not.
// - Any hosting of our saved run by the player who holds the game's save (the original host's Load, Regroup) also
//   searches first, and joins a lobby it finds; its save is then moved aside once the joined run launches.
// - A quick-match resume lobby may begin with one player, who is asked by the game whether to go on without the missing
//   ones (the game's load lobby never begins a multiplayer run with one player).
// - A candidate is asked before it takes over: at its turn it gets Host the Run or Pass for AnswerSeconds, and passing
//   or no answer leaves it searching as a joiner while the next candidate's turn comes. A host voted out of the run is
//   replaced without asking. The others are told whose turn it is (TurnAt).
public static class MigrationPolicy
{
    // The game's members Regroup reaches by reflection, by type's full name, for the test that they still exist.
    public static readonly (string Type, string Member)[] GameMembers =
    [
        ("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby", "_connectingPlayers"),
        ("MegaCrit.Sts2.Core.Nodes.Multiplayer.NGenericPopup", "OnNoButtonPressed"),
    ];

    // A candidate's turn lasts as long as its time to answer.
    public const double AnswerSeconds = NextHostSeconds;

    // How long a candidate looks for the run's lobby before reopening the run itself.
    public const double SearchFirstSeconds = 15;

    // How much later each next candidate reopens the run, if no lobby has appeared.
    public const double NextHostSeconds = 60;

    // Leaves out the old host, the players who were absent when the host was lost, and the players voted out of the
    // run.
    public static List<ulong> HostOrder(IEnumerable<ulong> runPlayers, ulong oldHost, IEnumerable<ulong> present,
        IEnumerable<ulong>? removed = null, IEnumerable<ulong>? desktopPlayers = null)
    {
        var here = present.ToHashSet();
        var banned = removed?.ToHashSet() ?? [];
        var desktops = desktopPlayers?.ToHashSet() ?? [];
        return runPlayers.Where(p => p != oldHost && here.Contains(p) && !banned.Contains(p)).Distinct()
            .OrderByDescending(desktops.Contains).ToList();
    }

    // Seconds after the host was lost at which local reopens the run if no lobby has been found, or null if they never
    // do (not in the order, or without a copy of the run).
    public static double? HostAfterSeconds(IReadOnlyList<ulong> order, ulong local, bool hasCopy)
    {
        int index = IndexOf(order, local);
        if (index < 0 || !hasCopy)
        {
            return null;
        }
        return SearchFirstSeconds + index * NextHostSeconds;
    }

    // Nobody (0) during the first search, then each in the order for NextHostSeconds, and nobody once every turn has
    // passed.
    public static ulong TurnAt(IReadOnlyList<ulong> order, double elapsedSeconds)
    {
        if (elapsedSeconds < SearchFirstSeconds)
        {
            return 0;
        }
        double turn = (elapsedSeconds - SearchFirstSeconds) / NextHostSeconds;
        return turn < order.Count ? order[(int)turn] : 0;
    }

    // The player the others' rejoin line names as asked to host: nobody (0, the plain waiting line) without a lost
    // host's order, outside every turn, or at our own turn, when we are the one asked.
    public static ulong WaitingFor(IReadOnlyList<ulong>? order, double elapsedSeconds, ulong local)
    {
        ulong turn = order == null ? 0 : TurnAt(order, elapsedSeconds);
        return turn == local ? 0 : turn;
    }

    // So the question can say who is asked next.
    public static bool HasNext(IReadOnlyList<ulong> order, ulong local)
    {
        int index = IndexOf(order, local);
        return index >= 0 && index < order.Count - 1;
    }

    // As the question shows it, "0:52": never below 0:00, and the last second rounds up.
    public static string Countdown(double secondsLeft)
    {
        int seconds = secondsLeft <= 0 ? 0 : (int)System.Math.Ceiling(secondsLeft);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    // Always, but for a host the players voted out of the run, whom the voters chose to replace.
    public static bool AsksBeforeHosting(ulong oldHost, IReadOnlyCollection<ulong> removed) => !removed.Contains(oldHost);

    public static TakeOverStep TakeOverNow(bool? answer, double askedSeconds)
    {
        if (answer is { } hosts)
        {
            return hosts ? TakeOverStep.Host : TakeOverStep.Join;
        }
        return askedSeconds >= AnswerSeconds ? TakeOverStep.Join : TakeOverStep.Wait;
    }

    // How long the rejoin search goes on before asking whether to keep waiting, given when we would host.
    public static double SearchTimeoutSeconds(double baseTimeout, double? hostAfter) =>
        hostAfter is { } after && after + SearchFirstSeconds > baseTimeout ? after + SearchFirstSeconds : baseTimeout;

    public static bool HostsNow(double elapsedSeconds, double? hostAfter) => hostAfter is { } after && elapsedSeconds >= after;

    // True when the game's load lobby refused to begin only because it holds one player, and that lobby is our
    // quick-match run's, hosted here: the run may go on with the one player.
    public static bool LoneHostMayBegin(bool gameSays, bool isHost, int players, int connecting, bool allReady, bool ourRun)
    {
        return !gameSays && isHost && ourRun && players == 1 && connecting == 0 && allReady;
    }

    // True when the game's multiplayer save, found as a run launches here as a client, is this run's own old save (we
    // were its host before a migration) and is moved aside.
    public static bool MovesSaveAside(bool isClient, string? saveKey, string? runKey) =>
        isClient && saveKey != null && saveKey == runKey;

    private static int IndexOf(IReadOnlyList<ulong> order, ulong player)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == player)
            {
                return i;
            }
        }
        return -1;
    }
}

public enum TakeOverStep
{
    Wait,
    // Host the Run: reopen the run from our copy.
    Host,
    // Pass, or no answer in time: search on as a joiner.
    Join,
}
