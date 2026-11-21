using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
#if !GAME_0111
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;
#endif
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using QuickMatch.Patches;

namespace QuickMatch.Matchmaking;

// Each game mode starts its runs from its own lobby screen: character select for Standard, the Blitz and Dread screens
// for theirs. The three share no base type that has the lobby, so this is where a screen's mode is told apart. The
// stable branch's game has neither Blitz nor Dread (GAME_0111), so there Standard is the only mode.
internal static class StartScreens
{
    // Null for a run Quick Match does not match: a daily, a custom run, or a mode turned off by a game update.
    public static RunMode? FromGame(GameMode mode, bool isCustom) => (mode, isCustom) switch
    {
        (GameMode.Standard, false) => RunMode.Standard,
#if !GAME_0111
        (GameMode.Blitz, false) when GamePatches.IsOn(GameFeature.Blitz) => RunMode.Blitz,
        (GameMode.Dread, false) when GamePatches.IsOn(GameFeature.Dread) => RunMode.Dread,
#endif
        _ => null,
    };

    // As the game's own host menu enables its buttons; a host also turns away a joiner without the mode's unlock.
    public static List<RunMode> PlayableModes() => MatchMode.All.Where(IsPlayable).ToList();

    public static bool IsPlayable(RunMode mode) => mode switch
    {
#if GAME_0111
        RunMode.Blitz or RunMode.Dread => false,
#else
        RunMode.Blitz => GamePatches.IsOn(GameFeature.Blitz) && SaveManager.Instance?.IsEpochRevealed<BlitzEpoch>() == true,
        RunMode.Dread => GamePatches.IsOn(GameFeature.Dread) && SaveManager.Instance?.IsEpochRevealed<DreadEpoch>() == true,
#endif
        _ => true,
    };

    public static StartRunLobby? LobbyOf(Node? screen) => screen switch
    {
        NCharacterSelectScreen standard => standard.Lobby,
#if !GAME_0111
        NBlitzScreen blitz => blitz.Lobby,
        NDreadScreen dread => dread.Lobby,
#endif
        _ => null,
    };

    // As NMultiplayerHostSubmenu.StartHostAsync does for each mode; the caller pushes the screen.
    public static NSubmenu Host(NSubmenuStack stack, RunMode mode, INetGameService netService, int size)
    {
        switch (mode)
        {
#if !GAME_0111
            case RunMode.Blitz:
                NBlitzScreen blitz = stack.GetSubmenuType<NBlitzScreen>();
                blitz.InitializeMultiplayerAsHost(netService, size);
                return blitz;
            case RunMode.Dread:
                NDreadScreen dread = stack.GetSubmenuType<NDreadScreen>();
                dread.InitializeMultiplayerAsHost(netService, size);
                return dread;
#endif
            default:
                NCharacterSelectScreen standard = stack.GetSubmenuType<NCharacterSelectScreen>();
                standard.InitializeMultiplayerAsHost(netService, size);
                return standard;
        }
    }

    public static NSubmenu Join(NSubmenuStack stack, RunMode mode, INetGameService netService, ClientLobbyJoinResponseMessage response)
    {
        switch (mode)
        {
#if !GAME_0111
            case RunMode.Blitz:
                NBlitzScreen blitz = stack.GetSubmenuType<NBlitzScreen>();
                blitz.InitializeMultiplayerAsClient(netService, response);
                return blitz;
            case RunMode.Dread:
                NDreadScreen dread = stack.GetSubmenuType<NDreadScreen>();
                dread.InitializeMultiplayerAsClient(netService, response);
                return dread;
#endif
            default:
                NCharacterSelectScreen standard = stack.GetSubmenuType<NCharacterSelectScreen>();
                standard.InitializeMultiplayerAsClient(netService, response);
                return standard;
        }
    }
}
