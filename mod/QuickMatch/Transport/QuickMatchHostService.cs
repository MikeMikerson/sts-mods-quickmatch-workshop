using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using QuickMatch.Lobby;
using QuickMatch.Matchmaking;

namespace QuickMatch.Transport;

// Every desktop host is a MixedHost (the game's Steam sockets, and lobby chat for phones); where Steamworks does
// not run, a ChatHost (lobby chat only). The game's entry point for such a host sets the platform to None, which would name players from a debug
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

    public ulong? LobbyId => LobbyLinks.ParseLobbyId(GetRawLobbyIdentifier());

#if IPHONE
    public static bool HasSockets => false;

    public bool PeerOffersSockets(ulong peer) => NetHost is ChatHost chat && chat.PeerOffersSockets(peer);

    public Task<NetErrorInfo?> StartQuickMatchHost(int maxClients, bool runVoice) =>
        StartTestHost(new ChatHost(this, LobbyBackend.Current), maxClients);
#else
    // True where the game's own Steam sockets work: the desktop game with Steamworks.
    public static bool HasSockets => LobbyBackend.Current is SteamworksLobbyBackend { IsAvailable: true };

    public bool PeerOffersSockets(ulong peer) => NetHost switch
    {
        MixedHost mixed => mixed.PeerOffersSockets(peer),
        ChatHost chat => chat.PeerOffersSockets(peer),
        _ => false,
    };

    public Task<NetErrorInfo?> StartQuickMatchHost(int maxClients, bool runVoice) =>
        HasSockets ? StartMixedHost(maxClients, runVoice) : StartTestHost(new ChatHost(this, LobbyBackend.Current), maxClients);

    // Kept apart, so a phone's runtime never compiles code that builds the game's Steam host.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<NetErrorInfo?> StartMixedHost(int maxClients, bool runVoice) =>
        StartTestHost(new MixedHost(this, LobbyBackend.Current, runVoice), maxClients);
#endif

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
