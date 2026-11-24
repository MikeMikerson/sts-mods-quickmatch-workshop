using System.Linq;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Emotes;
using QuickMatch.Lobby;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class LobbyGuard
{
    // A host that sends us away while a vote to remove us runs, or after one passed, is carrying the vote out, whichever
    // of its message and the vote's result reaches us first (for the line we are shown).
    public static bool VoteAgainstSelf =>
        Active && Self() is var self and not 0 && (Vote.Target == self || _result is { Passed: true } result && result.Target == self);

    public static bool InRun => Active && _inRun;

    public static bool CanVoteAgainst(ulong player) =>
        Active && Vote.CanStart(Self(), player, HostId(), Members(), Clock());

    public static void StartVote(ulong target)
    {
        if (!CanVoteAgainst(target))
        {
            return;
        }
        MainFile.Logger.Info($"{Where()}: starting a vote to remove a player");
        Send(target, VoteMessage.Yes);
        Apply(Self(), target, yes: true);
    }

    public static void Answer(bool yes)
    {
        ulong self = Self();
        if (!Active || Vote.Target == 0 || !Vote.Voters.Contains(self) || Vote.HasVoted(self))
        {
            return;
        }
        ulong target = Vote.Target;
        Send(target, yes ? VoteMessage.Yes : VoteMessage.No);
        Apply(self, target, yes);
    }

    public static void DismissResult() => _result = null;

    public static VoteView? View()
    {
        if (!Active)
        {
            return null;
        }
        double now = Clock();
        ulong self = Self();
        if (Vote.Target != 0 && Vote.Target != self && Vote.Voters.Contains(self))
        {
            return new VoteView(Vote.Target, Vote.YesCount, Vote.YesNeeded, Vote.SecondsLeft(now), !Vote.HasVoted(self) && Vote.SecondsLeft(now) > 0, null, _inRun);
        }
        if (_result is { } result && _votedInLast && now - result.At < ResultSeconds)
        {
            return new VoteView(result.Target, 0, 0, 0, false, result.Passed, _inRun);
        }
        return null;
    }

    private static void OnLobbyEvent(LobbyEvent e)
    {
        // Any thread: only queue it.
        if (e.Kind == LobbyEventKind.Chat && e.Lobby == _lobby && e.Data is { Length: KickVoteWire.Length or StartShortWire.Length or EmoteWire.Length } data)
        {
            Inbox.Enqueue((e.Member, data));
        }
    }

    private static void Tick()
    {
        if (!Active || (_inRun ? RunManager.Instance.RunLobby == null : Matchmaker.Lobby == null))
        {
            return;
        }
        ulong self = Self();
        while (Inbox.TryDequeue(out (ulong Sender, byte[] Data) message))
        {
            // Our own messages come back from Steam; we counted them when we sent them.
            if (message.Sender == self || !IsQuickMatchPeer(message.Sender))
            {
                continue;
            }
            if (_inRun && EmoteWire.TryRead(message.Data, out Emote emote))
            {
                EmoteRoom.Receive(message.Sender, emote);
                continue;
            }
            if (!_inRun && StartShortWire.TryRead(message.Data, out StartShortMessage question, out int players, out byte round))
            {
                Matchmaker.OnStartShortMessage(message.Sender, question, players, round);
                continue;
            }
            if (!KickVoteWire.TryRead(message.Data, out ulong target, out VoteMessage kind))
            {
                continue;
            }
            bool present = Members().Contains(message.Sender);
            MainFile.Logger.Info($"{Where()}: vote message {kind} about {target} from {message.Sender}{(present ? "" : ", who is no longer here")}");
            if (KickVoteWire.OutcomeOf(kind) is { } result)
            {
                ApplyResult(message.Sender, target, result);
            }
            else
            {
                Apply(message.Sender, target, kind == VoteMessage.Yes);
            }
        }
        ulong running = Vote.Target;
        if (running != 0)
        {
            Vote.Self = self;
            Handle(Vote.Update(Members(), Clock()), running);
        }
        KickVotePrompt.Refresh();
    }

    private static void Apply(ulong sender, ulong target, bool yes)
    {
        ulong before = Vote.Target;
        Vote.Self = Self();
        VoteOutcome outcome = Vote.Receive(sender, target, yes, HostId(), Members(), Clock());
        if (outcome == VoteOutcome.Ignored)
        {
            return;
        }
        if (outcome == VoteOutcome.Started || before == 0)
        {
            // A new vote (with 2 players the host's own start passes at once). The voters stay known after it ends.
            _votedInLast = Vote.Voters.Contains(Self());
            _result = null;
        }
        // A message counts only toward the running vote, so a vote that ends here is the one that was running.
        bool ended = outcome is VoteOutcome.Passed or VoteOutcome.Failed or VoteOutcome.Ended;
        Handle(outcome, ended && before != 0 ? before : target);
        KickVotePrompt.Refresh();
    }

    // The decider's result of the running vote reached us; we act on it, not on our own count.
    private static void ApplyResult(ulong sender, ulong target, VoteOutcome result)
    {
        Vote.Self = Self();
        VoteOutcome outcome = Vote.ReceiveResult(sender, target, result, Members(), Clock());
        if (outcome == VoteOutcome.Ignored)
        {
            if (result == VoteOutcome.Passed && Vote.Target == target && target != 0)
            {
                MainFile.Logger.Info($"{Where()}: a vote passed by its decider; waiting for its Yes votes");
            }
            return;
        }
        Handle(outcome, target);
        KickVotePrompt.Refresh();
    }

    private static void Handle(VoteOutcome outcome, ulong target)
    {
        // The decider's own count ended the vote: every other peer acts on the result we send.
        if (Vote.Decider == Self() && KickVoteWire.ResultOf(outcome) is { } result)
        {
            Send(target, result);
        }
        switch (outcome)
        {
            case VoteOutcome.Passed:
                MainFile.Logger.Info($"{Where()}: a vote to remove a player passed");
                _result = (target, true, Clock());
                if (_inRun)
                {
                    CarryOutInRun(target);
                }
                else if (IsHost())
                {
                    NotCounted.Add(target);
                    Matchmaker.Expel(target, "voted out", LeaveReasons.Voted);
                    SeenStore.Strike(target);
                }
                else if (target == Self())
                {
                    Matchmaker.LeaveBlockedLobby("the others voted to remove us", voted: true);
                }
                else
                {
                    _ = LeaveIfStillHereAsync(target, _epoch);
                }
                break;
            case VoteOutcome.Failed:
                MainFile.Logger.Info($"{Where()}: a vote to remove a player failed");
                _result = (target, false, Clock());
                break;
            case VoteOutcome.Ended:
                _result = null;
                break;
        }
    }

    private static void Send(ulong target, VoteMessage kind)
    {
        byte[] message = KickVoteWire.Write(target, kind);
        if (!LobbyBackend.Current.SendChat(_lobby, message, message.Length, 0))
        {
            MainFile.Logger.Warn($"{Where()}: could not send a vote");
        }
    }

    public static bool SendEmote(Emote emote)
    {
        if (!Active || !_inRun)
        {
            return false;
        }
        byte[] message = EmoteWire.Write(emote);
        if (LobbyBackend.Current.SendChat(_lobby, message, message.Length, 0))
        {
            return true;
        }
        MainFile.Logger.Warn($"{Where()}: could not send an emote");
        return false;
    }
}
