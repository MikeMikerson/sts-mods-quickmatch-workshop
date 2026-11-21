namespace QuickMatch.Lobby;

public static class LobbyBackend
{
    private static ILobbyBackend? _current;

    public static ILobbyBackend Current => _current ??= new SteamworksLobbyBackend();
}
