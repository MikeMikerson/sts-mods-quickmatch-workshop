namespace QuickMatch.Lobby;

// Steam's lobbies through Steamworks unless a host without a Steam client (a phone) sets its own before Initialize.
public static class LobbyBackend
{
    private static ILobbyBackend? _current;

    public static ILobbyBackend Current
    {
#if IPHONE
        get => _current ?? throw new System.InvalidOperationException("The iPhone lobby backend is not installed.");
#else
        get => _current ??= new SteamworksLobbyBackend();
#endif
        set => _current = value;
    }

    public static string NameOf(ulong player) => Current.PlayerName(player) ?? player.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
