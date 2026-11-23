using System;
using System.Collections.Generic;
using System.Linq;
using QuickMatch.Lobby;
using QuickMatch.Matchmaking;
using QuickMatch.Patches;
using QuickMatch.Transport;
#if !IPHONE
using QuickMatch.Platforms.Desktop;
using Steamworks;
#endif

namespace QuickMatch.Voice;

// One VoiceSession per match. The match's host and clients tick it every frame and end it when the connection closes.
// Game thread only.
internal static class VoiceChat
{
    // Without a tick for this long the transport stopped updating and the match is over.
    private const long MatchGoneMs = 1500;

    private static VoiceSession? _session;
#if !IPHONE
    private static SteamVoiceRoom? _steam;
    private static SteamVoiceDecoder? _decoder;
#endif
    private static RelayLink? _relay;
    private static GodotVoiceOutput? _godotOutput;
    private static readonly VoiceRoster _roster = new();
    // Null until the first tick: a long.MinValue start would overflow in InMatch and read as in a match.
    private static long? _lastTickAt;

    public static VoiceSession? Session => _session;

    // Whether the voice controls are shown. A player with voice off still runs a session, switched off, so the others
    // see their mark; they get no controls.
    public static bool InMatch => MatchVoice.On && _lastTickAt is { } at && System.Environment.TickCount64 - at < MatchGoneMs;

    public static IReadOnlyCollection<ulong> Members => _session?.Members ?? (IReadOnlyCollection<ulong>)Array.Empty<ulong>();

    public static IEnumerable<(ulong Player, TeammateState State)> Teammates => _roster.Teammates;

    // The match's voice goes over the relay, which is not connected; play goes on without it.
    public static bool RelayVoiceDown => _session?.Link is RelayLink { IsUp: false }
        && _roster.Teammates.Any(teammate => teammate.State == TeammateState.NoVoice);

    public static ulong LocalId => LobbyBackend.Current.LocalId;

    public static string NameOf(ulong player)
    {
        string? name = LobbyBackend.Current.PlayerName(player) ?? VoiceStore.RememberedName(player);
        return string.IsNullOrEmpty(name) ? player.ToString() : name;
    }

    // Keeps the player's name for the muted list.
    public static void SetMuted(ulong player, bool muted)
    {
        VoiceStore.RememberName(player, NameOf(player));
        if (_session != null)
        {
            if (muted)
            {
                _session.Mute(player);
            }
            else
            {
                _session.Unmute(player);
            }
        }
        else if (muted)
        {
            VoiceStore.Mutes.Mute(player);
        }
        else
        {
            VoiceStore.Mutes.Unmute(player);
        }
    }

    public static void MuteEveryone()
    {
        foreach (ulong player in Members)
        {
            VoiceStore.RememberName(player, NameOf(player));
        }
        _session?.MuteEveryone();
    }

#if !IPHONE
    // Called every frame by the match's host (MixedHost) and its clients (the game's Steam client).
    public static void TickSteam(ulong lobby)
    {
        if (!Features.IsOn(GameFeature.Voice))
        {
            return;
        }
        long now = System.Environment.TickCount64;
        _lastTickAt = now;
        if (_steam?.Lobby != lobby)
        {
            End();
            SteamVoiceRoom? room = null;
            try
            {
                room = SteamVoiceRoom.TryOpen(lobby);
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn($"voice: could not open voice over Steam: {e.Message}");
            }
            if (room == null)
            {
                return;
            }
            _steam = room;
            Begin(room, "Steam");
        }
        if (_session == null || _steam == null)
        {
            return;
        }
        Apply(VoiceStore.Settings, _session);
        _session.SetMembers(_steam.Members);
        _roster.Update(lobby, LocalId, LobbyBackend.Current.Members(lobby), _session.Members);
        _steam.Receive(_session.Receive);
        _session.Update(now);
    }
#endif

    public static void TickRelay(RelayRoute? route, ulong lobby)
    {
        if (!Features.IsOn(GameFeature.Voice))
        {
            return;
        }
        long now = System.Environment.TickCount64;
        _lastTickAt = now;
        if (_relay == null || _relay.Route != route)
        {
            DetachRelay();
            _relay = new RelayLink(route);
            if (_session == null)
            {
                Begin(_relay, "the relay");
            }
            else
            {
                _session.SwitchLink(_relay);
                MainFile.Logger.Info("voice: switched to Opus over the relay");
            }
            if (route != null)
            {
                route.VoiceReceived = ReceiveRelay;
            }
        }
        if (_session is { } session)
        {
            Apply(VoiceStore.Settings, session);
            session.SetMembers(route?.VoiceMembers ?? Array.Empty<ulong>());
            _roster.Update(lobby, LocalId, LobbyBackend.Current.Members(lobby), session.Members);
            session.Update(now);
        }
    }

    private static void ReceiveRelay(ulong sender, VoicePacket packet)
    {
        if (MatchVoice.On)
        {
            _session?.Receive(sender, packet);
        }
    }

    private sealed class RelayLink(RelayRoute? route) : IVoiceLink
    {
        public RelayRoute? Route => route;
        public bool UsesOpus => true;
        public bool IsUp => route?.VoiceUp == true;
        public void Send(VoicePacket packet) => route?.SendVoice(packet);
    }

    private static void DetachRelay()
    {
        if (_relay?.Route is { } route)
        {
            route.VoiceReceived = null;
        }
        _relay = null;
    }

    public static void End()
    {
        DetachRelay();
        _session?.Dispose();
        _session = null;
#if !IPHONE
        _steam?.Dispose();
        _steam = null;
#endif
    }

    public static bool IsFriend(ulong player)
    {
#if !IPHONE
        if (QuickMatchHostService.HasSockets)
        {
            return SteamFriends.GetFriendRelationship(new CSteamID(player)) == EFriendRelationship.k_EFriendRelationshipFriend;
        }
#endif
        return false;
    }

    private static void Begin(IVoiceLink link, string transport)
    {
        IVoiceDevice? device = VoiceDevice.Current;
#if IPHONE
        IVoiceDecoder? steamDecoder = null;
#else
        if (device == null && QuickMatchHostService.HasSockets)
        {
            VoiceDevice.Current = device = SteamVoiceDevice.TryCreate();
        }
        IVoiceDecoder steamDecoder = _decoder ??= new SteamVoiceDecoder();
#endif
        IVoiceOutput output = device as IVoiceOutput ?? (_godotOutput ??= new GodotVoiceOutput());
        var session = new VoiceSession(LocalId, device, steamDecoder, output, VoiceStore.Mutes, IsFriend,
            line => MainFile.Logger.Info(line), isBlocked: BlockStore.IsBlocked)
        {
            Link = link,
        };
        Apply(VoiceStore.Settings, session);
        _session = session;
        MainFile.Logger.Info(MatchVoice.On
            ? $"voice: ready for this match over {transport}, microphone {device?.CaptureState.ToString() ?? "none (listening only)"}"
            : $"voice: off for us in this match over {transport}; nothing recorded or played");
    }

    private static void Apply(VoiceSettings settings, VoiceSession session)
    {
        // No voice in this match: the session only tells the others, so they show the mark (a run gone back into with voice
        // turned off since).
        session.Enabled = MatchVoice.On;
        session.Hearing = settings.Hearing;
        session.Sensitivity = settings.Sensitivity;
        session.Volume = settings.Volume;
    }
}
