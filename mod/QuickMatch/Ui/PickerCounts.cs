using System;
using System.Threading;
using System.Threading.Tasks;
using QuickMatch.Matchmaking;
using QuickMatch.Steam;

namespace QuickMatch.Ui;

// The players waiting now in open lobbies: one wide search each time the Quick Match screen opens (to keep
// within Steam's limits on lobby list requests), counted for the lobbies this player could join
// (MatchPolicy.Count). The hub shows the total in one line, only above 0, and takes it away when it is opened
// again, so a count is never older than the visit. Unknown (null) until the search answers. Game thread only.
internal static class PickerCounts
{
    private static int _generation;

    public static PoolCounts? Current { get; private set; }

    public static void Refresh()
    {
        Current = null;
        int gen = ++_generation;
        _ = SearchAsync(gen);
    }

    private static async Task SearchAsync(int gen)
    {
        try
        {
            var found = await LobbySearch.FindPoolAsync(MatchChoice.GameMode, MatchChoice.Voice, CancellationToken.None);
            if (gen != _generation)
            {
                return;
            }
            int max = MatchChoice.MaxAscension;
            PoolCounts counts = MatchPolicy.Count(found, max);
            Current = counts;
            MainFile.Logger.Info($"waiting counts (max A{max}): {counts.Total} in all; sizes 2={counts.ForSize(2)} 3={counts.ForSize(3)} 4={counts.ForSize(4)} any={counts.ForSize(0)}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"waiting counts failed: {e.Message}");
            return;
        }
        QuickMatchHub.ShowWaiting();
    }
}
