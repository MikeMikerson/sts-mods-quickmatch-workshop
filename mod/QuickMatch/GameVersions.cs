using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuickMatch;

// The game's members that differ between the versions QuickMatch is built for (Directory.Build.props): the stable
// branch's 0.111 (GAME_0111) and the beta's 0.113. The stable branch has no Blitz or Dread, marks a custom run by its
// game mode, and has one ascension unlock for every mode.
internal static class GameVersions
{
#if GAME_0111
    // The menu card's picture, a TextureRect.
    public const string CardPicture = "Icon";

    // The host screen's cards are its own children.
    public const string HostCards = "";

    public static readonly string[] HostModeCards = [];

    public static bool IsCustom(JoinResult result) => result.gameMode == GameMode.Custom;

    public static bool IsCustom(SerializableRun run) => run.GameMode == GameMode.Custom;

    public static int JoinerMaximum(ClientLobbyJoinRequestMessage message, GameMode mode) => message.maxAscensionUnlocked;

    public static int MaxBlitzAscension(ProgressState progress) => 0;

    public static IEnumerable<NetClientData> ConnectedPeers(INetHostGameService host) =>
        host is NetHostGameService service ? service.ConnectedPeers : [];

    public static void SetCardText(NSubmenuButton card, string locKeyPrefix) => card.SetIconAndLocalization(locKeyPrefix);

    public static JoinFlow NewJoinFlow(INetClientGameService service, IClientConnectionInitializer connection) => new(service);

    // The stable branch sends no reason with a disconnect, so the joiner sees the game's own mod mismatch text.
    public static void TurnAway(NetHostGameService host, ulong peerId, NetError error, string reason) => host.DisconnectClient(peerId, error);

    public static Task<JoinResult> Begin(JoinFlow flow, IClientConnectionInitializer connection) =>
        flow.Begin(connection, (SceneTree)Engine.GetMainLoop());
#else
    // The menu card's picture, an NSubmenuPortrait.
    public const string CardPicture = "SubmenuPortrait";

    public const string HostCards = "ButtonContainer/";

    public static readonly string[] HostModeCards = [HostCards + "BlitzButton", HostCards + "DreadButton"];

    public static bool IsCustom(JoinResult result) => result.isCustom;

    public static bool IsCustom(SerializableRun run) => run.IsCustomGame;

    // Blitz has one ascension shared by every character.
    public static int JoinerMaximum(ClientLobbyJoinRequestMessage message, GameMode mode) =>
        mode == GameMode.Blitz ? message.maxBlitzAscension : message.maxStandardAscension;

    public static int MaxBlitzAscension(ProgressState progress) => progress.MaxBlitzAscension;

    public static IEnumerable<NetClientData> ConnectedPeers(INetHostGameService host) => host.ConnectedPeers;

    public static void SetCardText(NSubmenuButton card, string locKeyPrefix) => card.SetLocalization(locKeyPrefix);

    public static JoinFlow NewJoinFlow(INetClientGameService service, IClientConnectionInitializer connection) => new(service, connection);

    public static void TurnAway(NetHostGameService host, ulong peerId, NetError error, string reason) => host.DisconnectClient(peerId, error, now: false, reason);

    public static Task<JoinResult> Begin(JoinFlow flow, IClientConnectionInitializer connection) =>
        flow.Begin((SceneTree)Engine.GetMainLoop());
#endif
}
