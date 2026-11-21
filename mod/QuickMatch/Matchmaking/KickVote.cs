using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Matchmaking;

public enum VoteOutcome
{
    // A message the limits or the rules turn away, or no vote running.
    Ignored,
    Started,
    Counted,
    Passed,
    // Not enough Yes votes can come any more, or the time ran out.
    Failed,
    // The player the vote was about left by themselves.
    Ended,
}

// A vote to remove a client from a quick-match lobby. Every player of the lobby keeps one and feeds it the same votes
// (lobby chat, whose sender Steam stamps, so nobody can vote in another player's name); the host carries it out. A
// majority of the other players present when it starts must vote Yes: 1 of 1 with 2 players, 2 of 2 with 3, 2 of 3 with
// 4. Starting a vote is a Yes; a vote lasts 30 seconds; no answer, and a voter who leaves, count as No. Only clients
// can be voted out (the host carries the removal out). Each player can start one vote in 2 minutes, a failed vote
// against a player cannot start again for 5 minutes, and one vote runs at a time. Times are in seconds on the caller's
// own clock.
//
// One count decides: the Decider, the host for a vote against a client and the starter for one against the host, counts
// on its own clock and sends the result; every other peer acts on that result (ReceiveResult), not on its own clock. A
// Failed is always taken; a Passed only once this peer has the Yes votes itself (it waits SupportWaitSeconds for them).
// No result ResultWaitSeconds after the vote's time is a failed vote.
public sealed class KickVote
{
    public const double VoteSeconds = 30;
    public const double StartEverySeconds = 120;
    public const double RetrySameSeconds = 300;
    // How long a peer that does not decide waits after the vote's 30 seconds for the decider's result.
    public const double ResultWaitSeconds = 10;
    // How long a peer waits for the Yes votes behind a Passed result before it treats the vote as failed.
    public const double SupportWaitSeconds = 5;

    private readonly HashSet<ulong> _voters = new();
    private readonly HashSet<ulong> _yes = new();
    private readonly HashSet<ulong> _no = new();
    private readonly Dictionary<ulong, double> _lastStart = new();
    private readonly Dictionary<ulong, double> _failedAt = new();
    // When the decider's Passed reached us while we did not have its Yes votes yet.
    private double? _passClaimedAt;

    // True in a run: the host can be voted out too, and the voters then carry on under a new host (a migration). False
    // in a lobby, where only clients can be.
    public bool HostCanBeRemoved { get; set; }

    public ulong Self { get; set; }

    public ulong Decider { get; private set; }

    public bool Decides => Target != 0 && Decider == Self;

    public ulong Target { get; private set; }

    public double StartedAt { get; private set; }

    // Those present when the vote started, the target left out.
    public IReadOnlyCollection<ulong> Voters => _voters;

    public int YesCount => _yes.Count;

    public int YesNeeded => Needed(_voters.Count);

    public static int Needed(int voters) => voters / 2 + 1;

    public bool HasVoted(ulong player) => _yes.Contains(player) || _no.Contains(player);

    public double SecondsLeft(double now) => Target == 0 ? 0 : Math.Max(0, StartedAt + VoteSeconds - now);

    public bool CanStart(ulong starter, ulong target, ulong host, IReadOnlyCollection<ulong> members, double now)
    {
        if (Target != 0 || starter == 0 || target == 0 || starter == target || (target == host && !HostCanBeRemoved))
        {
            return false;
        }
        if (!members.Contains(starter) || !members.Contains(target))
        {
            return false;
        }
        if (_lastStart.TryGetValue(starter, out double started) && now - started < StartEverySeconds)
        {
            return false;
        }
        return !(_failedAt.TryGetValue(target, out double failed) && now - failed < RetrySameSeconds);
    }

    // A Yes with no vote running starts one, if CanStart allows it.
    public VoteOutcome Receive(ulong sender, ulong target, bool yes, ulong host, IReadOnlyCollection<ulong> members, double now)
    {
        // A vote whose time ran out ends first; a start in the same message then begins the next one.
        VoteOutcome expired = Update(members, now);
        if (Target == 0)
        {
            if (!yes || !CanStart(sender, target, host, members, now))
            {
                return expired;
            }
            Target = target;
            Decider = target == host ? sender : host;
            StartedAt = now;
            _passClaimedAt = null;
            _lastStart[sender] = now;
            _voters.Clear();
            _yes.Clear();
            _no.Clear();
            foreach (ulong member in members.Where(m => m != target && m != 0))
            {
                _voters.Add(member);
            }
            _yes.Add(sender);
            VoteOutcome first = Evaluate(members, now);
            return first == VoteOutcome.Ignored ? VoteOutcome.Started : first;
        }
        if (target != Target || !_voters.Contains(sender) || HasVoted(sender))
        {
            return VoteOutcome.Ignored;
        }
        (yes ? _yes : _no).Add(sender);
        VoteOutcome outcome = Evaluate(members, now);
        return outcome == VoteOutcome.Ignored ? VoteOutcome.Counted : outcome;
    }

    // For a peer that does not decide the vote. A result from anyone else, or about another player, is ignored. A
    // Passed this peer has not seen the Yes votes for waits for them.
    public VoteOutcome ReceiveResult(ulong sender, ulong target, VoteOutcome result, IReadOnlyCollection<ulong> members, double now)
    {
        if (Target == 0 || target != Target || sender == 0 || sender != Decider || Decider == Self)
        {
            return VoteOutcome.Ignored;
        }
        switch (result)
        {
            case VoteOutcome.Failed:
                _failedAt[Target] = now;
                Finish();
                return VoteOutcome.Failed;
            case VoteOutcome.Ended:
                Finish();
                return VoteOutcome.Ended;
            case VoteOutcome.Passed:
                _passClaimedAt ??= now;
                return Evaluate(members, now);
            default:
                return VoteOutcome.Ignored;
        }
    }

    // For a peer that does not decide the vote, this also ends one whose result never came or whose Passed its own
    // count does not back.
    public VoteOutcome Update(IReadOnlyCollection<ulong> members, double now) => Target == 0 ? VoteOutcome.Ignored : Evaluate(members, now);

    public void Reset()
    {
        Target = 0;
        Decider = 0;
        _passClaimedAt = null;
        _voters.Clear();
        _yes.Clear();
        _no.Clear();
        _lastStart.Clear();
        _failedAt.Clear();
    }

    private VoteOutcome Evaluate(IReadOnlyCollection<ulong> members, double now)
    {
        if (Decider != Self)
        {
            return Await(members, now);
        }
        if (!members.Contains(Target))
        {
            Finish();
            return VoteOutcome.Ended;
        }
        int yes = _yes.Count(members.Contains);
        int waiting = _voters.Count(v => members.Contains(v) && !HasVoted(v));
        int needed = YesNeeded;
        if (yes >= needed)
        {
            Finish();
            return VoteOutcome.Passed;
        }
        if (yes + waiting < needed || now - StartedAt >= VoteSeconds)
        {
            _failedAt[Target] = now;
            Finish();
            return VoteOutcome.Failed;
        }
        return VoteOutcome.Ignored;
    }

    // A peer that does not decide ends the vote only on the decider's result. It keeps waiting when the target leaves,
    // so a target who left on seeing a Passed is still banned when the result reaches us.
    private VoteOutcome Await(IReadOnlyCollection<ulong> members, double now)
    {
        if (_passClaimedAt is { } claimed)
        {
            // Every voter's Yes counts here, present or not: the decider counted only present ones.
            if (_yes.Count >= YesNeeded)
            {
                Finish();
                return VoteOutcome.Passed;
            }
            if (now - claimed >= SupportWaitSeconds)
            {
                _failedAt[Target] = now;
                Finish();
                return VoteOutcome.Failed;
            }
            return VoteOutcome.Ignored;
        }
        if (now - StartedAt >= VoteSeconds + ResultWaitSeconds)
        {
            if (!members.Contains(Target))
            {
                Finish();
                return VoteOutcome.Ended;
            }
            _failedAt[Target] = now;
            Finish();
            return VoteOutcome.Failed;
        }
        return VoteOutcome.Ignored;
    }

    private void Finish()
    {
        Target = 0;
        _passClaimedAt = null;
    }
}
