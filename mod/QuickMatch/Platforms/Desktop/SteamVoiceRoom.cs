using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Platform.Steam;
using QuickMatch.Lobby;
using QuickMatch.Voice;
using Steamworks;

namespace QuickMatch.Platforms.Desktop;

// Voice is sent straight to each other player with Steam's networking messages on QuickMatch's own channel:
// unreliable and without Nagle's delay, so a lost 20 ms frame is skipped rather than holding up the frames after it.
// Steam routes them over its relay network, encrypts them and names the sender, and needs nothing set up. The game's
// own connection uses Steam's sockets, not these messages, so nothing else in the game reads this channel or answers
// these session requests.
//
// Only members of the match's lobby are heard, and only their session requests are accepted. Game thread only
// (Steam's callbacks run there).
internal sealed class SteamVoiceRoom : IVoiceLink, IDisposable
{
    // "QM".
    public const int Channel = 0x514D;

    private const long MembersEveryMs = 1000;

    private const int ReceiveBatch = 32;

    private static readonly int SendFlags =
        Constants.k_nSteamNetworkingSend_UnreliableNoNagle | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;

    private readonly ulong _local;
    private readonly IntPtr[] _received = new IntPtr[ReceiveBatch];
    private readonly byte[] _buffer = new byte[SteamVoiceWire.MaxBytes];
    private Callback<SteamNetworkingMessagesSessionRequest_t>? _sessionRequest;
    private Callback<SteamNetworkingMessagesSessionFailed_t>? _sessionFailed;
    private ulong[] _members = [];
    private long _membersAt = long.MinValue;
    private bool _reportedSendFailure;
    private int _malformed;
    private bool _disposed;

    private SteamVoiceRoom(ulong lobby)
    {
        Lobby = lobby;
        _local = LobbyBackend.Current.LocalId;
        _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
        _sessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
    }

    public static SteamVoiceRoom? TryOpen(ulong lobby)
    {
        if (!SteamInitializer.Initialized)
        {
            return null;
        }
        MainFile.Logger.Info($"voice: over Steam in lobby {lobby}, channel {Channel}");
        return new SteamVoiceRoom(lobby);
    }

    public ulong Lobby { get; }

    public IReadOnlyCollection<ulong> Members
    {
        get
        {
            long now = Environment.TickCount64;
            // The first read is due at once: _membersAt starts at long.MinValue, where now - _membersAt overflows to a
            // negative number and the interval alone would never come due.
            if (_membersAt == long.MinValue || now - _membersAt >= MembersEveryMs)
            {
                _membersAt = now;
                try
                {
                    _members = LobbyBackend.Current.Members(Lobby).Where(m => m != _local && m != 0).Distinct().ToArray();
                }
                catch (Exception e)
                {
                    MainFile.Logger.Warn($"voice: could not read the lobby's members: {e.Message}");
                    _members = [];
                }
            }
            return _members;
        }
    }

    public bool IsUp => !_disposed && SteamInitializer.Initialized && Members.Count > 0;

    public void Send(VoicePacket packet)
    {
        if (!IsUp)
        {
            return;
        }
        byte[] message = SteamVoiceWire.Write(packet);
        GCHandle pinned = GCHandle.Alloc(message, GCHandleType.Pinned);
        try
        {
            foreach (ulong member in Members)
            {
                SteamNetworkingIdentity to = Identity(member);
                EResult result = SteamNetworkingMessages.SendMessageToUser(ref to, pinned.AddrOfPinnedObject(), (uint)message.Length, SendFlags, Channel);
                if (result != EResult.k_EResultOK && !_reportedSendFailure)
                {
                    _reportedSendFailure = true;
                    MainFile.Logger.Warn($"voice: Steam did not send to {member}: {result} (reported once)");
                }
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Receive(Action<ulong, VoicePacket> receive)
    {
        if (_disposed)
        {
            return;
        }
        while (true)
        {
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _received, _received.Length);
            if (count <= 0)
            {
                return;
            }
            for (int i = 0; i < count; i++)
            {
                IntPtr pointer = _received[i];
                try
                {
                    SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(pointer);
                    ulong sender = message.m_identityPeer.GetSteamID64();
                    if (!Members.Contains(sender) || message.m_cbSize <= 0 || message.m_cbSize > _buffer.Length)
                    {
                        continue;
                    }
                    Marshal.Copy(message.m_pData, _buffer, 0, message.m_cbSize);
                    VoicePacket? packet = SteamVoiceWire.Read(_buffer.AsSpan(0, message.m_cbSize));
                    if (packet == null)
                    {
                        if (_malformed++ == 0)
                        {
                            MainFile.Logger.Warn($"voice: a malformed voice message from {sender} over Steam (dropped; reported once)");
                        }
                        continue;
                    }
                    receive(sender, packet);
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(pointer);
                }
            }
            if (count < _received.Length)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _sessionRequest?.Dispose();
        _sessionRequest = null;
        _sessionFailed?.Dispose();
        _sessionFailed = null;
        if (SteamInitializer.Initialized)
        {
            foreach (ulong member in _members)
            {
                SteamNetworkingIdentity peer = Identity(member);
                SteamNetworkingMessages.CloseSessionWithUser(ref peer);
            }
        }
        MainFile.Logger.Info($"voice: over Steam closed in lobby {Lobby}");
    }

    private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t request)
    {
        if (_disposed)
        {
            return;
        }
        ulong from = request.m_identityRemote.GetSteamID64();
        // A fresh read, so a player who has just joined is not turned away by the cached list.
        _membersAt = long.MinValue;
        if (!Members.Contains(from))
        {
            MainFile.Logger.Info($"voice: ignored a Steam session request from {from}, not in lobby {Lobby}");
            return;
        }
        SteamNetworkingIdentity peer = request.m_identityRemote;
        SteamNetworkingMessages.AcceptSessionWithUser(ref peer);
    }

    private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t failed)
    {
        if (!_disposed)
        {
            MainFile.Logger.Info($"voice: a Steam session failed: {failed.m_info.m_eEndReason}");
        }
    }

    private static SteamNetworkingIdentity Identity(ulong player)
    {
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(player);
        return identity;
    }
}
