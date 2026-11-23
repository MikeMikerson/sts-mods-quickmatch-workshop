using System.Collections.Generic;
using System.Linq;

namespace QuickMatch.Emotes;

// The statuses a player may say they have: those named by the tooltips of their cards, the way the game's
// cards point at the statuses they apply. Listed in the picker's order.
public static class EmoteChoices
{
    public static IReadOnlyList<EmoteStatus> Have(IEnumerable<IEnumerable<string>> cardTipIds, IReadOnlyDictionary<string, EmoteStatus> statusByPowerId)
    {
        var have = new HashSet<EmoteStatus>();
        foreach (IEnumerable<string> tips in cardTipIds)
        {
            foreach (string id in tips)
            {
                if (statusByPowerId.TryGetValue(id, out EmoteStatus status))
                {
                    have.Add(status);
                }
            }
        }
        return have.OrderBy(s => s).ToList();
    }
}
