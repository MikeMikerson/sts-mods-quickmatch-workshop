using System;
using System.Collections.Generic;

namespace QuickMatch.Transport;

internal sealed partial class RelayRoute
{
    // Sockets already carry these players' game messages. Only voice uses the relay.
    private readonly HashSet<ulong> _voicePeers = new();

    public Action<ulong, string, byte[]>? SendVoiceOffer { get; set; }
    public Action<ulong, ulong, ulong, byte[]>? SendVoiceKeys { get; set; }

    public void AddVoicePeer(ulong peer)
    {
        if (_stopped || !_isHost || peer == 0 || peer == _local || _peers.ContainsKey(peer) || !_voicePeers.Add(peer))
        {
            return;
        }
        if (_session is { IsJoined: true })
        {
            OfferVoiceRoom(peer);
        }
        else if (_session == null && _reconnectAt == null)
        {
            Connect();
        }
    }

    public void RemoveVoicePeer(ulong peer)
    {
        if (_isHost && _voicePeers.Remove(peer))
        {
            ForgetPeer(peer);
        }
    }

    public void SetVoiceHost(ulong host)
    {
        if (!_isHost && _host == 0 && host != 0 && host != _local)
        {
            _host = host;
            _voicePeers.Add(host);
        }
    }

    public void TakeVoiceOffer(ulong sender, string region, byte[] roomKey)
    {
        if (_voicePeers.Contains(sender))
        {
            JoinOfferedRoom(sender, region, roomKey);
        }
    }

    public void TakeVoiceKeys(ulong sender, ulong member, ulong tag, byte[] voiceKey)
    {
        if (_voicePeers.Contains(sender))
        {
            TakeMemberKeys(sender, member, tag, null, voiceKey);
        }
    }

    private void OfferVoiceRoom(ulong peer)
    {
        if (_roomKey != null && IsRegionName(_region))
        {
            SendVoiceKeys?.Invoke(peer, _local, _tag, _voiceKey);
            SendVoiceOffer?.Invoke(peer, _region, _roomKey);
        }
    }

    private void ShareVoiceKey(ulong target, ulong member, ulong tag, byte[] key)
    {
        if (_voicePeers.Contains(target))
        {
            SendVoiceKeys?.Invoke(target, member, tag, key);
        }
        else
        {
            _link?.QueueRelayKeys(target, member, tag, NoKey, key, _clock());
        }
    }
}
