using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace QuickMatch.Matchmaking;

internal static partial class RunRejoin
{
    public static string? Refusal(INetClientGameService net, ClientRejoinResponseMessage answer, RejoinClient client)
    {
        SerializableRun save = answer.serializableRun;
        ulong local = net.NetId;
        return client.Info == null ? "the host sent no QuickMatch rejoin info"
            : !RejoinPolicy.AcceptsAnswer(RunRecord.KeyOf(save), RunRecord.Current?.Key, save.Players.Select(p => p.NetId), local) ? "not our run"
            : BlockPolicy.FirstBlockedInRun(save.Players.Select(p => p.NetId), local, BlockStore.IsBlocked, RunRecord.Current?.Stayed ?? []) != 0 ? "a player in the run is blocked"
            : null;
    }

    // Call only once Refusal passed. A failure goes back to the main menu as a failed load does.
    public static async Task EnterAsync(INetClientGameService net, ClientRejoinResponseMessage answer, RejoinClient client)
    {
        client.Dispose();
        SerializableRun save = answer.serializableRun;
        ulong local = net.NetId;
        QuickMatchRejoinInfoMessage info = client.Info!.Value;
        MainFile.Logger.Info($"rejoining the run under way: {info.connected.Count} players connected, absent from the fight [{string.Join(",", info.fightAbsent)}]");

        var pump = new Pump(net);
        LoadRunLobby? lobby = null;
        try
        {
            SerializablePlayer me = save.Players.First(p => p.NetId == local);
            CharacterModel character = ModelDb.GetById<CharacterModel>(me.CharacterId ?? throw new InvalidOperationException("our player has no character"));
            SfxCmd.Play(character.CharacterTransitionSfx);
            await NGame.Instance!.Transition.FadeOut(0.8f, character.CharacterSelectTransitionPath);
            RunState state = RunState.FromSerializable(save);
            List<LoadRunLobbyPlayer> players = info.connected
                .Select((id, i) => new LoadRunLobbyPlayer { id = id, isModded = i < info.modded.Count && info.modded[i], isReady = true })
                .ToList();
            lobby = new LoadRunLobby(net, new NoListener(), new ClientLoadJoinResponseMessage { serializableRun = save, playersAlreadyConnected = players });
            // The cursor and reaction containers dropped their networking with the old connection. As the game's load
            // screen does, they listen again before any message reaches the lobby's synchronizer: a peer whose input
            // state exists before the cursors listen has no cursor, and aiming a card then reads it every frame.
            NGame.Instance.RemoteCursorContainer.Initialize(lobby.InputSynchronizer, lobby.PlayerIds);
            NGame.Instance.ReactionContainer.InitializeNetworking(net);
            await RunManager.Instance.SetUpSavedMultiplayer(state, lobby);

            // NGame.LoadRun's steps, with the counters set after the map is made (as the game's replay does).
            await PreloadManager.LoadRunAssets(state.Players.Select(p => p.Character));
            await PreloadManager.LoadActAssets(state.Act);
            pump.Stop();
            // The host's "left out of the fight" for this room comes buffered, before StandIn listens; the info has it.
            net.RegisterMessageHandler<QuickMatchFightAbsentMessage>(Ignore);
            try
            {
                RunManager.Instance.Launch();
            }
            finally
            {
                net.UnregisterMessageHandler<QuickMatchFightAbsentMessage>(Ignore);
            }
            StandIn.SetFightAbsent(info.fightAbsent);
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));
            await RunManager.Instance.GenerateMap();
            FastForward(answer, info);
            await RunManager.Instance.LoadIntoLatestMapCoord(null);
            if (RunManager.Instance.MapDrawingsToLoad != null)
            {
                NRun.Instance?.GlobalUi.MapScreen.Drawings.LoadDrawings(RunManager.Instance.MapDrawingsToLoad);
                RunManager.Instance.MapDrawingsToLoad = null;
            }
        }
        catch (Exception e)
        {
            pump.Stop();
            MainFile.Logger.Error($"could not rejoin the run under way: {e}");
            lobby?.CleanUp(disconnectSession: true, NetError.InternalError);
            await NGame.Instance!.ReturnToMainMenuWithInternalError(e);
            return;
        }
        lobby.CleanUp(disconnectSession: false);
        await NGame.Instance.Transition.FadeIn();
        MainFile.Logger.Info("rejoined the run under way");
    }

    private static void FastForward(ClientRejoinResponseMessage answer, QuickMatchRejoinInfoMessage info)
    {
        RunManager run = RunManager.Instance;
        run.ActionQueueSet.FastForwardNextActionId(info.nextActionId);
        run.ActionQueueSynchronizer.FastForwardHookId(info.nextHookId);
        run.ChecksumTracker.LoadReplayChecksums(null!, info.nextChecksumId);
        if (answer.combatState is { } combat)
        {
            run.PlayerChoiceSynchronizer.FastForwardChoiceIds(combat.nextChoiceIds);
            run.RewardsSetSynchronizer.FastForwardRewardIds(combat.nextRewardIds);
        }
        typeof(MapSelectionSynchronizer).GetProperty(nameof(MapSelectionSynchronizer.MapGenerationCount), Members)!
            .SetValue(run.MapSelectionSynchronizer, info.mapGenerationCount);
        MainFile.Logger.Info($"counters set to the host's: action {info.nextActionId}, hook {info.nextHookId}, checksum {info.nextChecksumId}, map {info.mapGenerationCount}");
    }

    private static void Ignore(QuickMatchFightAbsentMessage message, ulong sender)
    {
    }

    // Keeps the connection served while the run is built, until the run's own node takes over.
    private sealed class Pump
    {
        private readonly INetGameService _net;
        private bool _stopped;

        public Pump(INetGameService net)
        {
            _net = net;
            _ = RunAsync();
        }

        public void Stop() => _stopped = true;

        private async Task RunAsync()
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            while (!_stopped)
            {
                try
                {
                    _net.Update();
                }
                catch (Exception e)
                {
                    MainFile.Logger.Error($"net update while rejoining: {e}");
                }
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }

    // The load lobby here only carries the answer into SetUpSavedMultiplayer; it never runs a lobby.
    private sealed class NoListener : ILoadRunLobbyListener
    {
        public void PlayerConnected(LoadRunLobbyPlayer player)
        {
        }

        public void RemotePlayerDisconnected(ulong playerId)
        {
        }

        public Task<bool> ShouldAllowRunToBegin() => Task.FromResult(false);

        public void BeginRun()
        {
        }

        public void PlayerReadyChanged(ulong playerId)
        {
        }

        public void LocalPlayerDisconnected(NetErrorInfo info)
        {
        }
    }
}
