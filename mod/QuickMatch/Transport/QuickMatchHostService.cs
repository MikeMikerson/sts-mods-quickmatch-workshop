using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using QuickMatch.Matchmaking;

namespace QuickMatch.Transport;

// The game's entry point for a SocketsHost sets the platform to None, which would name players from a debug
// list and record runs without Steam IDs; the game reads the platform only through INetGameService, so it is
// answered here as Steam.
//
// Quick Match is not a gameplay mod, so the game's handshake lets in a player without it (one who followed an
// invite link); such a player is turned away here with the reason.
internal sealed class QuickMatchHostService : NetHostGameService, INetGameService, IHandshakeHandler
{
    public QuickMatchHostService()
        : base(PeerVersionInfo.LocalDefault())
    {
    }

    PlatformType INetGameService.Platform => PlatformType.Steam;

    public ulong? LobbyId => (NetHost as SocketsHost)?.LobbyId;

    public Task<NetErrorInfo?> StartQuickMatchHost(int maxClients) => StartTestHost(new SocketsHost(this), maxClients);

    void IHandshakeHandler.HandshakeSucceeded(ulong peerId, PeerVersionInfo versionInfo)
    {
        if (LinkPolicy.HasQuickMatch(versionInfo.otherMods, MainFile.ModId) || LinkPolicy.HasQuickMatch(versionInfo.gameplayAffectingMods, MainFile.ModId))
        {
            HandshakeSucceeded(peerId, versionInfo);
            return;
        }
        MainFile.Logger.Info($"refused {peerId}: no Quick Match in their mods");
        GameVersions.TurnAway(this, peerId, NetError.ModMismatch, LinkPolicy.NoQuickMatchReason);
    }
}
