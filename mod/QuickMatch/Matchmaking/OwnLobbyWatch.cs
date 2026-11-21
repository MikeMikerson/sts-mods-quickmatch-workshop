namespace QuickMatch.Matchmaking;

// Steam can close a lobby under its only member after a lost connection, leaving a host nobody can find.
// One search that misses the lobby can be Steam's lag, so it takes two in a row.
internal sealed class OwnLobbyWatch
{
    public const int MissesToGiveUp = 2;

    private int _misses;

    // True when a host alone should give its lobby up and search again.
    public bool Searched(bool ownLobbyVisible, int group)
    {
        if (ownLobbyVisible || group != 1)
        {
            _misses = 0;
            return false;
        }
        _misses++;
        return _misses >= MissesToGiveUp;
    }
}
