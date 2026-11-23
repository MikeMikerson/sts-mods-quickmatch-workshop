using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class Regroup
{
    // Replaces the game's return to the menu with an error when a client loses its host mid-run; null leaves it to the game.
    public static Task? ReplaceDisconnectFlow(NetErrorInfo info)
    {
        ulong votedHost = _leavingVotedHost;
        _leavingVotedHost = 0;
        if (!InQuickMatchRun || RunRecord.Current == null || info.GetReason() == NetError.HostAbandoned || (info.SelfInitiated && votedHost == 0))
        {
            return null;
        }
        ulong oldHost = votedHost != 0 ? votedHost : RunRecord.Current.HostId;
        LostHost? lost = info.GetReason() == NetError.Kicked && votedHost == 0 ? null : MigrationTurn(RunRecord.Current, oldHost);
        string turn = lost?.HostAfter is { } after
            ? $", reopening the run ourselves after {after:0}s if no one has{(lost.Asks ? " and we say so when asked" : "")}"
            : "";
        MainFile.Logger.Info($"{(votedHost != 0 ? "left the host voted out" : $"lost the host ({info.GetReason()})")}; going to the rejoin search{turn}");
        return ReturnAndRejoinAsync(lost);
    }

    // Read before the run is torn down. Nobody is asked before reopening the run when the host was voted out.
    private static LostHost? MigrationTurn(RunRecord record, ulong oldHost)
    {
        try
        {
            ulong local = RunRecord.LocalPlayerId();
            // The record lists the run's players in the run's order. The game has already emptied its run lobby's list
            // by now, so the players present are our copy of it; we are present ourselves.
            var present = _present.Append(local);
            var order = MigrationPolicy.HostOrder(record.PlayerIds, oldHost, present, record.Banned, record.DesktopPlayers);
            bool hasCopy = HostCopy.Has(record.Key);
            bool asks = MigrationPolicy.AsksBeforeHosting(oldHost, record.Banned);
            MainFile.Logger.Info($"host order after {oldHost}: {string.Join(",", order)}; we hold {(hasCopy ? "a" : "no")} copy of the run; {(asks ? "each is asked before reopening it" : "the host was voted out, so nobody is asked")}");
            return new LostHost(oldHost, order, MigrationPolicy.HostAfterSeconds(order, local, hasCopy), asks);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not work out the host order: {e.Message}");
            return null;
        }
    }

    private static async Task ReturnAndRejoinAsync(LostHost? lost)
    {
        try
        {
            // Same as vanilla before it returns to the menu.
            NCapstoneContainer.Instance?.Close();
            NMapScreen.Instance?.Close(animateOut: false);
            await NGame.Instance!.ReturnToMainMenuAfterRun();
            NMainMenu menu = await MainMenuAsync();
            menu.OpenMultiplayerSubmenu();
            QuickMatchHub.OpenForRejoin();
            Matchmaker.StartRejoin(lost?.HostAfter, lost: lost);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"could not start the rejoin search: {e}");
        }
    }
}

// HostAfter is null when we would never reopen the run ourselves.
internal sealed record LostHost(ulong OldHost, IReadOnlyList<ulong> Order, double? HostAfter, bool Asks);
