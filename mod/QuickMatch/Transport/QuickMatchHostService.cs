using System;
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
internal sealed class QuickMatchHostService : NetHostGameService, INetGameService, IHandshakeHandler
{
    private readonly string? _release;
    private readonly Func<ulong, bool>? _mayJoinAsGuest;
    private bool _guestAdmissionClosed;

    public QuickMatchHostService(Func<ulong, bool>? mayJoinAsGuest = null)
        : base(PeerVersionInfo.LocalDefault())
    {
        _release = ReleaseOf(LocalVersion);
        _mayJoinAsGuest = mayJoinAsGuest;
    }

    public void CloseGuestAdmission() => _guestAdmissionClosed = true;

    private static string? ReleaseOf(PeerVersionInfo version) =>
        LinkPolicy.QuickMatchRelease(version.otherMods, version.gameplayAffectingMods, MainFile.ModId);

    public bool IsGuest(ulong peer) => GetVersionInfoForPeer(peer) is { } version && ReleaseOf(version) == null;

    public bool IsQuickMatchPeer(ulong peer) => peer == NetId ||
        (GetVersionInfoForPeer(peer) is { } version && ReleaseOf(version) is { } release && release == _release);

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
        bool usesSockets = false;
#if !IPHONE
        usesSockets = NetHost is MixedHost mixed && mixed.UsesSockets(peerId);
#endif
        bool acceptsGuest = usesSockets && !_guestAdmissionClosed && (_mayJoinAsGuest?.Invoke(peerId) ?? true);
        PeerAdmission admission = LinkPolicy.Admit(_release, ReleaseOf(versionInfo), acceptsGuest);
        if (admission is PeerAdmission.OtherRelease or PeerAdmission.GuestsUnavailable)
        {
            string reason = admission == PeerAdmission.OtherRelease ? "This lobby uses a different Quick Match release."
                : "This connection requires Quick Match.";
            MainFile.Logger.Info($"refused {peerId}: {reason}");
            GameVersions.TurnAway(this, peerId, NetError.ModMismatch, reason);
            return;
        }
        HandshakeSucceeded(peerId, versionInfo);
#if !IPHONE
        if (admission == PeerAdmission.QuickMatch && NetHost is MixedHost desktop && desktop.UsesSockets(peerId))
        {
            desktop.AdmitQuickMatchPeer(peerId);
        }
#endif
    }
}
