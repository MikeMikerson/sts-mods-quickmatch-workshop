using System.Linq;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace QuickMatch.Ui;

// The game's bad-connection mark on a player's row checks the player's ping every two seconds, but stops
// checking when the player disconnects. A player whose game was killed shows the mark during the timeout
// before that, so it stays lit after they rejoin unless it starts checking their new connection.
internal static class NetworkProblemMark
{
    public static void Recheck(ulong player)
    {
        NMultiplayerPlayerState? row = NRun.Instance?.GlobalUi.MultiplayerPlayerContainer.GetChildren()
            .OfType<NMultiplayerPlayerState>()
            .FirstOrDefault(r => r.Player?.NetId == player);
        if (row?.GetNodeOrNull<NMultiplayerNetworkProblemIndicator>("%NetworkProblemIndicator") is { IsShown: true } mark)
        {
            mark.Initialize(player);
        }
    }
}
