using System;
using QuickMatch.Lobby;
using QuickMatch.Transport;
using QuickMatch.Voice;
using STS2Mobile.ModApi;

namespace QuickMatch.Platforms.IPhone;

internal static class IPhonePlatform
{
    public static void Install()
    {
        var lobbies = Multiplayer.Lobbies ?? throw new InvalidOperationException("The launcher has not bound Steam lobbies.");
        LobbyBackend.Current = new IPhoneLobbyBackend(lobbies);
        RelayTransport.Current = Multiplayer.GetRelay() is { } relay ? new IPhoneRelay(relay) : null;
        VoiceDevice.Current = Multiplayer.Voice is { } voice ? new IPhoneVoiceDevice(voice) : null;
    }
}
