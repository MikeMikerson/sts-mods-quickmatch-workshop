using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.TestSupport;
using QuickMatch.Steam;
using QuickMatch.Voice;

namespace QuickMatch.Transport;

// The game's own Steam sockets host, exactly as in vanilla co-op, and nothing else, so every player in its
// match is on Steam sockets. Its lobby is marked so (LobbyLinks.Filters), and voice goes over Steam
// (VoiceChat.TickSteam). It is started through NetHostGameService.StartTestHost and keeps QuickMatch's lobby
// rules (QuickMatchLobby.SetClosed and OnHostStopping in place of SteamHost's own).
internal sealed class SocketsHost : AbstractTestNetHost, INetHostHandler
{
    private readonly SteamHost _sockets;
    private bool _stopping;
    private bool _reportedStop;

    public SocketsHost(INetHostHandler handler)
        : base(handler)
    {
        _sockets = new SteamHost(this);
    }

    public ulong? LobbyId { get; private set; }

    public override bool IsConnected => _sockets.IsConnected;

    public override ulong NetId => _sockets.NetId;

    public override IEnumerable<ulong> ConnectedPeerIds => _sockets.ConnectedPeerIds;

    public override async Task<NetErrorInfo?> StartHost(int maxClients)
    {
        MainFile.Logger.Info($"starting host for {maxClients} over Steam sockets only, our id {NetId}");
        NetErrorInfo? error = await _sockets.StartHost(maxClients);
        if (error.HasValue)
        {
            return error;
        }
        if (_sockets.LobbyId is not { } lobby)
        {
            return new NetErrorInfo(NetError.FailedToHost, selfInitiated: true);
        }
        LobbyId = lobby.m_SteamID;
        return null;
    }

    public override void Update()
    {
        if (_sockets.IsConnected)
        {
            _sockets.Update();
        }
        if (!_stopping && LobbyId is { } lobby)
        {
            VoiceChat.TickSteam(lobby);
        }
    }

    public override void SetHostIsClosed(bool isClosed)
    {
        // Not SteamHost's own: it would reopen the lobby as friends only.
        if (LobbyId is { } lobby)
        {
            QuickMatchLobby.SetClosed(lobby, isClosed);
        }
    }

    public override void SendMessageToClient(ulong peerId, byte[] bytes, int length, NetTransferMode mode, int channel = 0) =>
        _sockets.SendMessageToClient(peerId, bytes, length, mode, channel);

    public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0) =>
        _sockets.SendMessageToAll(bytes, length, mode, channel);

#if GAME_0111
    public override void DisconnectClient(ulong peerId, NetError reason, bool now = false) =>
        _sockets.DisconnectClient(peerId, reason, now);
#else
    public override void DisconnectClient(ulong peerId, NetError reason, bool now = false, string? debugReason = null) =>
        _sockets.DisconnectClient(peerId, reason, now, debugReason);
#endif

    public override void StopHost(NetError reason, bool now = false)
    {
        if (LobbyId is { } lobby)
        {
            QuickMatchLobby.OnHostStopping(lobby, reason);
        }
        _stopping = true;
        VoiceChat.End();
        if (_sockets.IsConnected)
        {
            _sockets.StopHost(reason, now);
        }
        LobbyId = null;
        ReportStopped(new NetErrorInfo(reason, selfInitiated: true));
    }

    public override string? GetRawLobbyIdentifier() => LobbyId?.ToString(CultureInfo.InvariantCulture);

    private void ReportStopped(NetErrorInfo info)
    {
        if (!_reportedStop)
        {
            _reportedStop = true;
            _handler.OnDisconnected(info);
        }
    }

    void INetHostHandler.OnPeerConnected(ulong peerId)
    {
        MainFile.Logger.Info($"sockets host: client {peerId} connected");
        _handler.OnPeerConnected(peerId);
    }

    void INetHostHandler.OnPeerDisconnected(ulong peerId, NetErrorInfo info)
    {
        MainFile.Logger.Info($"sockets host: client {peerId} disconnected ({info.GetReason()})");
        _handler.OnPeerDisconnected(peerId, info);
    }

    void INetHostHandler.OnDisconnected(NetErrorInfo info)
    {
        if (!_stopping)
        {
            MainFile.Logger.Warn($"sockets host stopped: {info.GetReason()}");
            VoiceChat.End();
        }
        ReportStopped(info);
    }

    void INetHandler.OnPacketReceived(ulong senderId, byte[] packetBytes, NetTransferMode mode, int channel) =>
        _handler.OnPacketReceived(senderId, packetBytes, mode, channel);
}
