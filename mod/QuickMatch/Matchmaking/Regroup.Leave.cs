using System;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Localization;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class Regroup
{
    // A guest run stays with its original host, including the blocks on its save.
    public static void LeaveRunAfterBlocking()
    {
        if (!InQuickMatchRun)
        {
            return;
        }
        MainFile.Logger.Info("leaving the run after blocking a player in it");
        if (RunRecord.Current is not { IsHost: true, HasGuests: true })
        {
            RunRecord.Clear("left the run after blocking a player");
        }
        InQuickMatchRun = false;
        _ = LeaveRunAsync();
    }

    // A host that can be replaced moves the old save aside. A guest run has just one copy, on its host.
    public static void LeaveRunVotedOut(bool asHost)
    {
        if (!InQuickMatchRun)
        {
            return;
        }
        string? key = UsesQuickMatchRecovery ? RunRecord.Current?.Key : null;
        MainFile.Logger.Info($"voted out of the run{(asHost ? " as its host" : "")}; leaving it");
        // The line stays up over the return to the menu.
        Notice.Show(Loc.Get("QUICKMATCH.NOTICE.voted_out_run"), 10);
        if (asHost && !UsesQuickMatchRecovery)
        {
            RunRecord.Ban(RunRecord.LocalPlayerId());
        }
        else
        {
            RunRecord.Clear("voted out of the run");
        }
        InQuickMatchRun = false;
        if (!asHost)
        {
            _ = LeaveRunAsync();
            return;
        }
        _ = LeaveRunAsHostVotedOutAsync(key);
    }

    private static async Task LeaveRunAsHostVotedOutAsync(string? key)
    {
        try
        {
            await GodotDelay.Seconds(RunKickPolicy.HostLeavesAfterSeconds, CancellationToken.None);
            NRunMusicController.Instance?.StopMusic();
            await NGame.Instance!.ReturnToMainMenu();
            if (SaveManager.Instance.CurrentRunSaveTask is { } saving)
            {
                await saving;
            }
            if (key != null)
            {
                HostCopy.MoveOldSaveAside(key, HostCopy.RemovedFileName);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"could not leave the run: {e}");
        }
    }

    // Record the departed host before disconnecting when the other players can take over.
    public static void LeaveHostVotedOut(ulong host)
    {
        RunManager run = RunManager.Instance;
        if (!InQuickMatchRun || run.NetService.Type != NetGameType.Client || !run.NetService.IsConnected)
        {
            return;
        }
        MainFile.Logger.Info("leaving the host who was voted out of the run");
        if (UsesQuickMatchRecovery)
        {
            _leavingVotedHost = host;
        }
        run.NetService.Disconnect(NetError.Quit);
    }

    private static async Task LeaveRunAsync()
    {
        try
        {
            RunManager run = RunManager.Instance;
            if (run.NetService.Type == NetGameType.Client && run.NetService.IsConnected)
            {
                run.NetService.Disconnect(NetError.Quit);
                return;
            }
            NRunMusicController.Instance?.StopMusic();
            await NGame.Instance!.ReturnToMainMenu();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"could not leave the run: {e}");
        }
    }
}
