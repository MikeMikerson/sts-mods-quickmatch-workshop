using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace QuickMatch.Matchmaking;

internal static partial class LobbyGuard
{
    // A vote in the run passed: the player is banned from the run in our record, then we leave if it was us, send them
    // away if we host, leave a host voted out and carry on under a new one, or watch that the host sends a client away.
    private static void CarryOutInRun(ulong target)
    {
        ulong self = Self();
        ulong host = HostId();
        RunKickAction action = RunKickPolicy.OnPassed(target, self, host);
        if (action != RunKickAction.ForgetRun)
        {
            RunRecord.Ban(target);
        }
        switch (action)
        {
            case RunKickAction.ForgetRun:
                Regroup.LeaveRunVotedOut(asHost: self == host);
                break;
            case RunKickAction.SendAway:
                _ = SendAwayFromRunAsync(target, _epoch);
                break;
            case RunKickAction.LeaveHost:
                _ = LeaveHostVotedOutAsync(target, _epoch);
                break;
            case RunKickAction.WatchHost:
                _ = LeaveRunIfStillHereAsync(target, _epoch);
                break;
        }
    }

    // As host: disconnected only once an honest client has had time to leave by itself.
    private static async Task SendAwayFromRunAsync(ulong target, int epoch)
    {
        try
        {
            await GodotDelay.Seconds(RunKickPolicy.SendAwayAfterSeconds, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (epoch == _epoch && Members().Contains(target) && RunManager.Instance.NetService is INetHostGameService host)
        {
            MainFile.Logger.Info($"{Where()}: the player voted out is still here; disconnecting them");
            host.DisconnectClient(target, NetError.Kicked);
        }
    }

    // The host was voted out of the run: we stay, so the vote and its result, which we may have just sent, reach the
    // host while we are still one of its players. The host then leaves, and its leaving takes us to the migration with
    // the host banned; a host that has not left within RunKickPolicy.VoterWaitsForHostSeconds is left by us.
    private static async Task LeaveHostVotedOutAsync(ulong host, int epoch)
    {
        double until = Clock() + RunKickPolicy.VoterWaitsForHostSeconds;
        while (epoch == _epoch && Clock() < until)
        {
            try
            {
                await GodotDelay.Seconds(0.25, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (epoch == _epoch)
        {
            MainFile.Logger.Info($"{Where()}: the host voted out has not left; leaving it");
            Regroup.LeaveHostVotedOut(host);
        }
    }

    private static async Task LeaveRunIfStillHereAsync(ulong target, int epoch)
    {
        try
        {
            await GodotDelay.Seconds(RunKickPolicy.HostRemovesSeconds, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (epoch == _epoch && Members().Contains(target))
        {
            MainFile.Logger.Info($"{Where()}: the host did not remove the player voted out; leaving the run");
            Regroup.LeaveRunAfterBlocking();
        }
    }

    private static async Task LeaveIfStillHereAsync(ulong target, int epoch)
    {
        try
        {
            await GodotDelay.Seconds(HostRemovesSeconds, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (epoch == _epoch && Members().Contains(target))
        {
            Matchmaker.LeaveBlockedLobby("the host did not remove a player voted out");
        }
    }
}
