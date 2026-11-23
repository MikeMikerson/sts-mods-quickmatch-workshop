using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using QuickMatch.Transport;
using Steamworks;

namespace QuickMatch.Platforms.Desktop;

// Reliable, directed relay offers and member keys for players whose game uses Steam sockets.
internal sealed class SocketVoiceControl : IDisposable
{
    private const int Channel = 0x514E;
    private readonly ulong _lobby;
    private readonly ulong _host;
    private readonly Func<ulong, bool> _connected;
    private readonly Callback<SteamNetworkingMessagesSessionRequest_t> _request;
    private readonly IntPtr[] _batch = new IntPtr[16];
    private readonly byte[] _buffer = new byte[SocketVoiceWire.MaxBytes];
    private sealed record Outgoing(byte[] Bytes, ulong Member = 0, bool Removed = false, bool RoomOffer = false);
    private readonly Dictionary<ulong, List<Outgoing>> _pending = new();
    private readonly HashSet<(ulong Peer, ulong Member)> _sentKeys = new();
    private readonly HashSet<ulong> _peers = new();
    private RelayRoute? _route;
    private long _retryAt;

    public bool Switched { get; private set; }

    public SocketVoiceControl(ulong lobby, ulong host, Func<ulong, bool> connected, RelayRoute? route)
    {
        _lobby = lobby;
        _host = host;
        _connected = connected;
        _request = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnRequest);
        Attach(route);
    }

    private void Attach(RelayRoute? route)
    {
        _route = route;
        if (route != null)
        {
            route.SendVoiceOffer = (peer, region, key) => Send(peer, new Outgoing(SocketVoiceWire.Room(_lobby, region, key), RoomOffer: true));
            route.SendVoiceKeys = (peer, member, tag, key) =>
                Send(peer, new Outgoing(SocketVoiceWire.Member(_lobby, member, tag, key), member, tag == 0));
        }
    }

    public void AddPeer(ulong peer)
    {
        if (_peers.Add(peer))
        {
            Send(peer, new Outgoing(SocketVoiceWire.SwitchToRelay(_lobby)));
            _route?.AddVoicePeer(peer);
        }
    }

    public void RemovePeer(ulong peer)
    {
        if (_peers.Remove(peer))
        {
            _route?.RemoveVoicePeer(peer);
            CloseChannel(peer);
        }
        _pending.Remove(peer);
        _sentKeys.RemoveWhere(key => key.Peer == peer);
    }

    public void Poll(Func<RelayRoute?>? onSwitch = null)
    {
        int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _batch, _batch.Length);
        for (int i = 0; i < count; i++)
        {
            try
            {
                SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(_batch[i]);
                ulong sender = message.m_identityPeer.GetSteamID64();
                if (!_connected(sender) || message.m_cbSize <= 0 || message.m_cbSize > _buffer.Length)
                {
                    continue;
                }
                Marshal.Copy(message.m_pData, _buffer, 0, message.m_cbSize);
                if (SocketVoiceWire.Read(_buffer.AsSpan(0, message.m_cbSize), _lobby, sender, _host, _route) && !Switched)
                {
                    Switched = true;
                    Attach(onSwitch?.Invoke());
                }
            }
            finally
            {
                SteamNetworkingMessage_t.Release(_batch[i]);
            }
        }
        if (_pending.Count > 0 && Environment.TickCount64 >= _retryAt)
        {
            _retryAt = Environment.TickCount64 + 500;
            foreach (ulong peer in _pending.Keys.ToArray())
            {
                var queue = _pending[peer];
                while (queue.Count > 0 && _connected(peer) && TrySend(peer, queue[0]))
                {
                    queue.RemoveAt(0);
                }
                if (queue.Count == 0 || !_connected(peer))
                {
                    _pending.Remove(peer);
                }
            }
        }
    }

    private void OnRequest(SteamNetworkingMessagesSessionRequest_t request)
    {
        if (_connected(request.m_identityRemote.GetSteamID64()))
        {
            SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);
        }
    }

    private void Send(ulong peer, Outgoing message)
    {
        if (!_pending.TryGetValue(peer, out var queue))
        {
            if (TrySend(peer, message))
            {
                return;
            }
            _pending[peer] = queue = new List<Outgoing>();
        }
        // During a disconnect retain the latest key for each member. A member who joined and
        // left without their key ever reaching this peer has nothing to revoke.
        if (message.Member != 0)
        {
            queue.RemoveAll(queued => queued.Member == message.Member);
        }
        else if (message.RoomOffer)
        {
            queue.RemoveAll(queued => queued.RoomOffer);
        }
        if (!message.Removed || _sentKeys.Contains((peer, message.Member)))
        {
            queue.Add(message);
        }
    }

    private bool TrySend(ulong peer, Outgoing message)
    {
        var to = new SteamNetworkingIdentity();
        to.SetSteamID64(peer);
        byte[] bytes = message.Bytes;
        GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (SteamNetworkingMessages.SendMessageToUser(ref to, pinned.AddrOfPinnedObject(), (uint)bytes.Length,
                Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession, Channel) != EResult.k_EResultOK)
            {
                return false;
            }
            if (message.Removed)
            {
                _sentKeys.Remove((peer, message.Member));
            }
            else if (message.Member != 0)
            {
                _sentKeys.Add((peer, message.Member));
            }
            return true;
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Dispose()
    {
        _request.Dispose();
        if (_host != 0)
        {
            CloseChannel(_host);
        }
        foreach (ulong peer in _peers)
        {
            CloseChannel(peer);
        }
        if (_route != null)
        {
            _route.SendVoiceOffer = null;
            _route.SendVoiceKeys = null;
        }
    }

    private static void CloseChannel(ulong peer)
    {
        var remote = new SteamNetworkingIdentity();
        remote.SetSteamID64(peer);
        SteamNetworkingMessages.CloseChannelWithUser(ref remote, Channel);
    }
}
