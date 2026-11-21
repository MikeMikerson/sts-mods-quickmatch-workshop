using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace QuickMatch.Matchmaking;

// A quick-match run goes on without a player who dropped (the rules are in DropPolicy).
//
// - The host stands in for every absent player wherever the game waits for all of the run's players, through the
//   game's action queue, so every peer applies the same action at the same point: their turn ends at once, the enemy
//   turn starts, the map, the next act and the treasure go on, shared events are decided, and a choice they had to
//   make is answered with nothing chosen (a QuickMatch message, since the game only takes a choice from its player).
// - From the next room on, the host tells every peer who is absent just before the room's sync, and the fight is
//   built without them. While a fight they are left out of goes on, their character's HP is held at 0 (and given
//   back when it ends), so the game's "everyone is dead" check and its targeting treat them as out of the fight.
// - Enemy block and Plating count the players in the fight (patches in QuickMatch.Patches.DropPatches).
//
// The game's private members are reached by plain reflection; PatchTargetTests checks that each name in
// DropPolicy.GameMembers exists in the installed game.
internal static partial class StandIn
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // The answer to a choice an absent player had to make, recognised by reference.
    private static readonly List<int> NothingChosen = [];

    private static RunState? _state;
    private static INetGameService? _net;
    private static bool _active;
    private static bool _subscribedAsHost;

    // Who is left out of this room's fight, from the host's message at the room's start.
    private static HashSet<ulong> _fightAbsent = [];
    private static readonly Dictionary<Creature, int> HeldHp = [];

    // The host's record of what it has already sent, so no stand-in action is queued twice.
    private static readonly HashSet<(int Turn, ulong Player)> EndTurnSent = [];
    private static readonly HashSet<ulong> EnemyTurnSent = [];
    private static readonly HashSet<(MapLocation Source, int Generation, ulong Player)> MapSent = [];
    private static readonly HashSet<(int Act, ulong Player)> ActSent = [];
    private static readonly HashSet<ulong> RelicSent = [];
    private static object? _relicSession;
    private static bool _fillingEvent;
    private static readonly Dictionary<ulong, int> ResumeOwed = [];

    private static bool IsHost => _active && _net?.Type == NetGameType.Host;

    public static void OnRunLaunched(RunState state, bool quickMatch)
    {
        _state = state;
        _active = quickMatch;
        Reset();
        if (!quickMatch)
        {
            return;
        }
        RunManager run = RunManager.Instance;
        _net = run.NetService;
        if (!_net.Type.IsMultiplayer())
        {
            _active = false;
            return;
        }
        _net.RegisterMessageHandler<QuickMatchFightAbsentMessage>(OnFightAbsentMessage);
        _net.RegisterMessageHandler<QuickMatchStandInChoiceMessage>(OnStandInChoiceMessage);
        if (_net.Type == NetGameType.Host)
        {
            CombatManager.Instance.TurnStarted += OnTurnStarted;
            run.MapSelectionSynchronizer.PlayerVoteChanged += OnMapVoteChanged;
            run.TreasureRoomRelicSynchronizer.VotesChanged += FillRelics;
            _subscribedAsHost = true;
        }
    }

    public static void OnRunCleanUp()
    {
        GiveHpBack();
        if (_net != null && _active)
        {
            _net.UnregisterMessageHandler<QuickMatchFightAbsentMessage>(OnFightAbsentMessage);
            _net.UnregisterMessageHandler<QuickMatchStandInChoiceMessage>(OnStandInChoiceMessage);
        }
        if (_subscribedAsHost)
        {
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
            RunManager.Instance.MapSelectionSynchronizer.PlayerVoteChanged -= OnMapVoteChanged;
            RunManager.Instance.TreasureRoomRelicSynchronizer.VotesChanged -= FillRelics;
            _subscribedAsHost = false;
        }
        _active = false;
        _state = null;
        _net = null;
        Reset();
    }

    private static void Reset()
    {
        _fightAbsent = [];
        EndTurnSent.Clear();
        EnemyTurnSent.Clear();
        MapSent.Clear();
        ActSent.Clear();
        RelicSent.Clear();
        _relicSession = null;
        _fillingEvent = false;
        ResumeOwed.Clear();
    }

    private static bool IsAbsent(ulong player)
    {
        return _state != null && RunManager.Instance.RunLobby is { } lobby && !lobby.PlayerIds.Contains(player)
            && _state.Players.Any(p => p.NetId == player);
    }

    private static List<bool> Present(IReadOnlyList<Player> players) => players.Select(p => !IsAbsent(p.NetId)).ToList();

    public static void OnPlayerDropped(ulong player)
    {
        if (!IsHost)
        {
            return;
        }
        MainFile.Logger.Info($"standing in for absent player {player}");
        Safely(EndAbsentTurns, "end their turn");
        Safely(FillEnemyTurn, "start the enemy turn");
        Safely(FillMap, "vote on the map");
        Safely(FillAct, "go to the next act");
        Safely(FillRelics, "skip the treasure");
        Safely(FillEvent, "vote in the event");
        Safely(() => AnswerPendingChoices(player), "answer their choice");
    }

    private static void Safely(Action step, string what)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"could not {what} for an absent player: {e}");
        }
    }

    // As the host does for a client's request.
    private static void Enqueue(GameAction action, Player owner)
    {
        MainFile.Logger.Info($"host stands in for {owner.NetId}: {action}");
        Method(typeof(ActionQueueSynchronizer), "EnqueueAction").Invoke(RunManager.Instance.ActionQueueSynchronizer, [action, owner.NetId]);
    }

    private static MethodInfo Method(Type type, string name) =>
        type.GetMethod(name, Members) ?? throw new MissingMethodException(type.Name, name);

    private static object? Field(object instance, string name) =>
        (instance.GetType().GetField(name, Members) ?? throw new MissingFieldException(instance.GetType().Name, name)).GetValue(instance);
}
