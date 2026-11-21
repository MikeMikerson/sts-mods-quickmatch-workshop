using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace QuickMatch.Matchmaking;

// A quick-match host's ascension is fixed by the player's choice in the picker: the host's character select panel shows
// it with its arrows hidden, the host turns away a joiner who has not unlocked it (so the game never pulls the run
// down), and the game's preferred multiplayer ascension is left as the player set it, so plain co-op keeps their pick
// (Patches.AscensionPatches). The lock lasts as long as the screen keeps the lobby it was set for. Game thread only.
internal static class AscensionLock
{
    private static NSubmenu? _screen;
    private static StartRunLobby? _lobby;
    private static int _ascension;

    // Call right after the screen was initialised as host.
    public static void Apply(NSubmenu screen, int ascension)
    {
        _screen = screen;
        _lobby = StartScreens.LobbyOf(screen);
        _ascension = ascension;
        NAscensionPanel panel = screen.GetNode<NAscensionPanel>("%AscensionPanel");
        // The arrows' visibility is private to the panel; Godot's own property access sets it.
        panel.Set("_arrowsVisible", false);
        // Through the panel, as the arrows do: the screen passes it on to the lobby (SyncAscensionChange).
        panel.SetAscensionLevel(ascension);
        MainFile.Logger.Info($"ascension locked at {ascension} (lobby ascension {_lobby?.Ascension}, shared maximum {_lobby?.MaxAscension})");
    }

    public static int? For(StartRunLobby lobby) => IsActive && ReferenceEquals(lobby, _lobby) ? _ascension : null;

    public static int? ForPanel(NAscensionPanel panel) =>
        IsActive && _screen!.IsAncestorOf(panel) ? _ascension : null;

    private static bool IsActive =>
        _screen != null && _lobby != null && GodotObject.IsInstanceValid(_screen) && ReferenceEquals(StartScreens.LobbyOf(_screen), _lobby);
}
