using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Platform.Steam;
using QuickMatch.Lobby;
using QuickMatch.Patches;
using QuickMatch.Platforms.Desktop;
using Steamworks;

namespace QuickMatch.Voice;

// One VoiceSession per match. The match's host and clients call TickSteam every frame and End when the match
// connection closes. Game thread only.
internal static class VoiceChat
{
    // Without a tick for this long the transport stopped updating and the match is over.
    private const long MatchGoneMs = 1500;

    private static VoiceSession? _session;
    private static SteamVoiceRoom? _steam;
    private static IVoiceDevice? _device;
    private static SteamVoiceDecoder? _decoder;
    private static GodotVoiceOutput? _godotOutput;
    // Null until the first tick: a long.MinValue start would overflow in InMatch and read as in a match.
    private static long? _lastTickAt;

    public static VoiceSession? Session => _session;

    // Whether the voice controls are shown. A player with voice off still runs a session, switched off, so the others
    // see their mark; they get no controls.
    public static bool InMatch => MatchVoice.On && _lastTickAt is { } at && System.Environment.TickCount64 - at < MatchGoneMs;

    public static IReadOnlyCollection<ulong> Members => _session?.Members ?? (IReadOnlyCollection<ulong>)Array.Empty<ulong>();

    public static ulong LocalId => LobbyBackend.Current.LocalId;

    public static string NameOf(ulong player)
    {
        string? name = LobbyBackend.Current.PlayerName(player) ?? VoiceStore.RememberedName(player);
        if (name == null && SteamInitializer.Initialized)
        {
            name = SteamFriends.GetFriendPersonaName(new CSteamID(player));
        }
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

    // Called every frame by the match's host (SocketsHost) and its clients (the game's Steam client).
    public static void TickSteam(ulong lobby)
    {
        if (!GamePatches.IsOn(GameFeature.Voice))
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
            Begin(room);
        }
        if (_session == null || _steam == null)
        {
            return;
        }
        _steam.Receive((from, packet) => _session?.Receive(from, packet));
        Apply(VoiceStore.Settings, _session);
        _session.SetMembers(_steam.Members);
        _session.Update(now);
    }

    public static void End()
    {
        _session?.Dispose();
        _session = null;
        _steam?.Dispose();
        _steam = null;
    }

    public static bool IsFriend(ulong player)
    {
        if (!SteamInitializer.Initialized)
        {
            return false;
        }
        return SteamFriends.GetFriendRelationship(new CSteamID(player)) == EFriendRelationship.k_EFriendRelationshipFriend;
    }

    private static void Begin(IVoiceLink link)
    {
        _device ??= SteamVoiceDevice.TryCreate();
        IVoiceDevice? device = _device;
        IVoiceOutput output = _godotOutput ??= new GodotVoiceOutput();
        var session = new VoiceSession(LocalId, device, _decoder ??= new SteamVoiceDecoder(), output, VoiceStore.Mutes, IsFriend,
            line => MainFile.Logger.Info(line))
        {
            Link = link,
        };
        Apply(VoiceStore.Settings, session);
        _session = session;
        MainFile.Logger.Info(MatchVoice.On
            ? $"voice: ready for this match over Steam, microphone {device?.CaptureState.ToString() ?? "none (listening only)"}"
            : "voice: off for us in this match (shown to the others over Steam); nothing recorded or played");
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
