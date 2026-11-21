using System.Collections.Generic;
using QuickMatch.Steam;

namespace QuickMatch.Transport;

// A quick-match host takes the game's own Steam sockets, as vanilla co-op does, and nothing else, and marks its lobby
// with Filters; searches list only lobbies so marked and joiners always use the sockets.
//
// The lobbies searches listed are remembered, which tells a quick-match lobby from any other Steam game (a run with
// friends). Main thread only, like the rest of matchmaking.
internal static class LobbyLinks
{
    private const int MaxRemembered = 256;

    private static readonly HashSet<ulong> _listed = new();

    public static void Remember(ulong lobby)
    {
        if (_listed.Count >= MaxRemembered && !_listed.Contains(lobby))
        {
            _listed.Clear();
        }
        _listed.Add(lobby);
    }

    public static bool IsListed(ulong lobby) => _listed.Contains(lobby);

    public static IReadOnlyDictionary<string, string> Filters { get; } =
        new Dictionary<string, string> { [LobbyKeys.Pool] = LobbyKeys.PoolDesktop, [LobbyKeys.Link] = LobbyKeys.LinkSockets };

    internal static void Forget() => _listed.Clear();
}
