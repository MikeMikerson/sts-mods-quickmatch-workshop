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
    // The run record is forgotten, so there is no rejoin. A host saves and quits (the save stays, for Load by
    // hand); a client disconnects, which the game itself turns into a return to the menu.
    public static void LeaveRunAfterBlocking()
    {
        if (!InQuickMatchRun)
        {
            return;
        }
        MainFile.Logger.Info("leaving the run after blocking a player in it");
        RunRecord.Clear("left the run after blocking a player");
        InQuickMatchRun = false;
        _ = LeaveRunAsync();
    }

    // The run record is forgotten, so there is no Rejoin. A client disconnects at once. A host saves and quits a
    // moment later (the voters wait for it, and carry on under a new host when it goes), and moves its save of
    // the run aside so its Load does not reopen it.
    public static void LeaveRunVotedOut(bool asHost)
    {
        if (!InQuickMatchRun)
        {
            return;
        }
        string? key = RunRecord.Current?.Key;
        MainFile.Logger.Info($"voted out of the run{(asHost ? " as its host" : "")}; leaving it");
        // The line stays up over the return to the menu.
        Notice.Show(Loc.Get("QUICKMATCH.NOTICE.voted_out_run"), 10);
        RunRecord.Clear("voted out of the run");
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

    // Our own disconnect then goes to the migration as a lost host's does, among the voters, the host banned.
    public static void LeaveHostVotedOut(ulong host)
    {
        RunManager run = RunManager.Instance;
        if (!InQuickMatchRun || run.NetService.Type != NetGameType.Client || !run.NetService.IsConnected)
        {
            return;
        }
        MainFile.Logger.Info("the host was voted out of the run; leaving it to carry on under a new host");
        _leavingVotedHost = host;
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
