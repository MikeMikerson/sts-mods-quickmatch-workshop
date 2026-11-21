using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Steam;
using QuickMatch.Transport;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

internal static partial class Regroup
{
    // The run may have gone on under a new host while we were away (our own drop starts a migration on the
    // others), so we look for its lobby first and join it when found, and host our save only if none appears.
    public static async Task SearchThenHostAsync(SerializableRun run, string key)
    {
        try
        {
            await MainMenuAsync();
            QuickMatchHub.OpenForRejoin();
            if (!QuickMatchHub.IsOnTop())
            {
                await HostSavedRunAsync(run, key);
                return;
            }
            MainFile.Logger.Info($"looking for run {key} before reopening it");
            Matchmaker.StartRejoin(MigrationPolicy.SearchFirstSeconds, run);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"could not reopen the run: {e}");
        }
    }

    private static void OnRemotePlayerDisconnected(ulong playerId)
    {
        if (InQuickMatchRun && !RunManager.Instance.IsAbandoned)
        {
            StandIn.OnPlayerDropped(playerId);
            RunRejoin.OnPlayerDropped();
        }
        if (!InQuickMatchRun || _promptOpen || _regrouping || RunManager.Instance.IsAbandoned)
        {
            return;
        }
        // A quick-match run's players are always Steam players.
        string name = PlatformUtil.GetPlayerName(PlatformType.Steam, playerId);
        if (RunRecord.Current?.Banned.Contains(playerId) == true)
        {
            // Voted out of the run: they never come back, so there is nothing to wait for.
            MainFile.Logger.Info($"player {playerId} ({name}), voted out of the run, left it");
            return;
        }
        MainFile.Logger.Info($"player {playerId} ({name}) disconnected from the run");
        int present = _trackedLobby?.Players.Count ?? 1;
        if (!DropPolicy.AskHostAlone(present))
        {
            MainFile.Logger.Info($"the run goes on with {present} players");
            return;
        }
        _ = PromptAsync(name);
    }

    private static async Task PromptAsync(string playerName)
    {
        _promptOpen = true;
        bool regroup;
        try
        {
            regroup = await RegroupPrompt.AskRegroupAsync(playerName);
        }
        finally
        {
            _promptOpen = false;
        }
        if (!regroup || !InQuickMatchRun || _regrouping)
        {
            MainFile.Logger.Info("host goes on alone");
            return;
        }
        _regrouping = true;
        try
        {
            await HostRegroupAsync();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"regroup failed: {e}");
        }
        finally
        {
            _regrouping = false;
        }
    }

    // Save and quit as the pause menu does, then reopen the save as the host's Load button does.
    private static async Task HostRegroupAsync()
    {
        MainFile.Logger.Info("regroup: saving, quitting and reopening the run");
        NRunMusicController.Instance?.StopMusic();
        await NGame.Instance!.ReturnToMainMenu();
        if (SaveManager.Instance.CurrentRunSaveTask is { } saving)
        {
            await saving;
        }
        NMainMenu menu = await MainMenuAsync();
        NMultiplayerSubmenu submenu = menu.OpenMultiplayerSubmenu();
        ReadSaveResult<SerializableRun> save = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(RunRecord.LocalPlayerId());
        if (!save.Success || save.SaveData == null)
        {
            MainFile.Logger.Warn("regroup: the multiplayer save could not be read; use Load by hand");
            return;
        }
        submenu.StartHost(save.SaveData);
    }

    // Reopens our save the way the Multiplayer menu's own host step does for a standard run (four slots; the load
    // lobby admits only the save's players), and makes it findable by the run key.
    public static async Task HostSavedRunAsync(SerializableRun run, string key)
    {
        if (RunRecord.Current is { } record && record.Key == key)
        {
            MatchVoice.Set(MatchVoice.ForRun(record.Voice, MatchChoice.Voice));
        }
        NMainMenuSubmenuStack? stack = NGame.Instance?.MainMenu?.SubmenuStack;
        if (stack == null)
        {
            MainFile.Logger.Warn("regroup: no main menu to host from");
            return;
        }
        MainFile.Logger.Info($"reopening quick-match run {key} over Steam sockets {MatchVoice.Text(MatchVoice.On)}");
        var netService = new QuickMatchHostService();
        NetErrorInfo? error = await netService.StartQuickMatchHost(4);
        if (error.HasValue)
        {
            MainFile.Logger.Warn($"could not reopen the run: {error.Value.GetReason()}");
            if (NErrorPopup.Create(error.Value) is { } popup)
            {
                NModalContainer.Instance?.Add(popup);
            }
            return;
        }
        if (netService.LobbyId is not { } lobby)
        {
            netService.Disconnect(NetError.InternalError);
            MainFile.Logger.Warn("reopened run has no lobby");
            return;
        }
        NMultiplayerLoadGameScreen screen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
        screen.InitializeAsHost(netService, run);
        stack.Push(screen);
        QuickMatchLobby.PublishResume(lobby, key);
    }

    private static readonly System.Reflection.FieldInfo? ConnectingPlayers =
        typeof(LoadRunLobby).GetField("_connectingPlayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    // The game's load lobby will not begin with one player; in our own run's resume lobby, hosted here, the one
    // player left may go on alone.
    public static bool MayBeginAlone(LoadRunLobby lobby)
    {
        try
        {
            int connecting = ConnectingPlayers?.GetValue(lobby) is System.Collections.ICollection list ? list.Count : 1;
            bool ourRun = RunRecord.Current is { } record && RunRecord.KeyOf(lobby.Run) == record.Key
                && StartScreens.FromGame(lobby.Run.GameMode, GameVersions.IsCustom(lobby.Run)) != null;
            bool allReady = true;
            foreach (LoadRunLobbyPlayer player in lobby.Players)
            {
                allReady &= player.isReady;
            }
            return MigrationPolicy.LoneHostMayBegin(false, lobby.NetService.Type == NetGameType.Host, lobby.PlayerCount, connecting, allReady, ourRun);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not check the load lobby: {e.Message}");
            return false;
        }
    }

    // A run whose only missing players were voted out begins without the game's question about missing players.
    public static bool MissingOnlyVotedOut(LoadRunLobby lobby)
    {
        try
        {
            return RunRecord.Current is { } record && RunRecord.KeyOf(lobby.Run) == record.Key
                && RunKickPolicy.OnlyRemovedMissing(lobby.Run.Players.Select(p => p.NetId), lobby.PlayerIds.ToHashSet(), record.Banned);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not check the load lobby's missing players: {e.Message}");
            return false;
        }
    }
}
