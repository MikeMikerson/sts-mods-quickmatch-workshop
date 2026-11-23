using System.Collections.Generic;
using System.Globalization;

namespace QuickMatch.Transport;

// Remember the routes advertised by Quick Match lobbies. Steam voice also uses this to recognize a Quick Match lobby.
internal static class LobbyLinks
{
    private const int MaxRemembered = 256;

    private static readonly Dictionary<ulong, bool> _sockets = new();
    private static readonly Queue<ulong> _order = new();

    public static void Remember(ulong lobby, bool offersSockets)
    {
        if (!_sockets.ContainsKey(lobby))
        {
            if (_order.Count == MaxRemembered)
            {
                _sockets.Remove(_order.Dequeue());
            }
            _order.Enqueue(lobby);
        }
        _sockets[lobby] = offersSockets;
    }

    public static bool IsListed(ulong lobby) => _sockets.ContainsKey(lobby);

    public static bool? OffersSockets(ulong lobby) => _sockets.TryGetValue(lobby, out bool sockets) ? sockets : null;

    public static ulong? ParseLobbyId(string? raw) =>
        ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) && lobby != 0 ? lobby : null;

    internal static void Forget()
    {
        _sockets.Clear();
        _order.Clear();
    }
}
