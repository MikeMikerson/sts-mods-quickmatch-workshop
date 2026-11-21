using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

public enum RunKickAction
{
    // We were voted out: forget the run and leave it.
    ForgetRun,
    // We host and a client was voted out: send them away (after a moment, so an honest one leaves by itself).
    SendAway,
    // The host was voted out: leave it, and carry on with the other voters under a new host.
    LeaveHost,
    // A client was voted out: wait for the host to send them away, and leave the run if it does not.
    WatchHost,
}

// The vote kick in a run: the run's KickVote (over the run's Steam lobby chat, the host votable too), the run ban every
// voter writes into its run record, and the refusals every later host of the run makes. The game side is LobbyGuard,
// Regroup and the resume search in Matchmaker.
public static class RunKickPolicy
{
    // How long a host waits after a vote passes before sending the client away (an honest one has left).
    public const double SendAwayAfterSeconds = 3;

    // How long a voter waits for the host to send a client away before leaving the run itself.
    public const double HostRemovesSeconds = 8;

    // How long a host voted out stays before leaving the run. It leaves first, and the voters carry on from its leaving
    // as from a lost host, the host banned.
    public const double HostLeavesAfterSeconds = 1;

    // How long a voter stays in the run after a vote against the host passed, waiting for the host to leave: a host
    // takes a vote only from a player present, and a voter who left at once would be taken for a drop. A host that has
    // not left by then is left anyway.
    public const double VoterWaitsForHostSeconds = 10;

    public static RunKickAction OnPassed(ulong target, ulong self, ulong host)
    {
        if (target == self)
        {
            return RunKickAction.ForgetRun;
        }
        if (self == host)
        {
            return RunKickAction.SendAway;
        }
        return target == host ? RunKickAction.LeaveHost : RunKickAction.WatchHost;
    }

    // For a player of the run coming back (the resume lobby's join, or a rejoin of the run under way): a player voted
    // out of this run is refused whatever else.
    public static bool HostRefusesRejoin(ulong joiner, Func<ulong, bool> isBlocked, IReadOnlyCollection<ulong> stayedWith, IReadOnlyCollection<ulong> removed) =>
        joiner != 0 && (removed.Contains(joiner) || BlockPolicy.HostRefusesRejoin(joiner, isBlocked, stayedWith));

    public static bool SkipsHost(ulong host, IReadOnlyCollection<ulong> removed) => host != 0 && removed.Contains(host);

    // Players voted out never come, so the run begins without the game's question about missing players.
    public static bool OnlyRemovedMissing(IEnumerable<ulong> runPlayers, IReadOnlyCollection<ulong> connected, IReadOnlyCollection<ulong> removed)
    {
        var missing = runPlayers.Where(p => !connected.Contains(p)).ToList();
        return missing.Count > 0 && missing.All(removed.Contains);
    }
}
