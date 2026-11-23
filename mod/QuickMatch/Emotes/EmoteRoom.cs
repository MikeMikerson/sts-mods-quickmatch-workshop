using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using QuickMatch.Matchmaking;
using QuickMatch.Ui;

namespace QuickMatch.Emotes;

// Emotes in a quick-match run: sent over the run's lobby chat (LobbyGuard), shown as a bubble over the sender
// (EmoteBubble). The run's player rows are where each player's Player and place on screen come from.
internal static class EmoteRoom
{
    public static readonly EmoteGate Gate = new();
    private static readonly EmoteLimiter Own = EmoteLimiter.ForSending();
    private static readonly Dictionary<ulong, NMultiplayerPlayerState> Rows = new();

    public static bool Here => Regroup.InQuickMatchRun && LobbyGuard.InRun;

    public static bool CanSend => Here && Own.Ready(BlockPlayers.LocalId(), Now());

    public static void AddRow(NMultiplayerPlayerState row) => Rows[row.Player.NetId] = row;

    public static NMultiplayerPlayerState? RowOf(ulong player) =>
        Rows.TryGetValue(player, out NMultiplayerPlayerState? row) && GodotObject.IsInstanceValid(row) && row.IsInsideTree() ? row : null;

    public static void NewRun()
    {
        Gate.NewRun();
        Own.Reset();
    }

    public static void Send(Emote emote)
    {
        ulong self = BlockPlayers.LocalId();
        if (!Here || self == 0 || !Own.Allow(self, Now()))
        {
            return;
        }
        if (LobbyGuard.SendEmote(emote))
        {
            EmoteBubble.Show(self, emote);
        }
    }

    // On the game's thread, from LobbyGuard's queue; our own messages are already left out.
    public static void Receive(ulong sender, Emote emote)
    {
        if (Here && Gate.Shows(sender, RunPlayers(), BlockStore.IsBlocked(sender), Now()))
        {
            EmoteBubble.Show(sender, emote);
        }
    }

    // The statuses the player's hand can apply in a fight, or their deck outside one.
    public static IReadOnlyList<EmoteStatus> Have()
    {
        if (RowOf(BlockPlayers.LocalId())?.Player is not { } me)
        {
            return [];
        }
        IEnumerable<CardModel> cards = CombatManager.Instance.IsInProgress && me.PlayerCombatState is { } combat
            ? combat.Hand.Cards
            : me.Deck.Cards;
        return EmoteChoices.Have(cards.Select(c => c.HoverTips.Select(t => t.Id)), EmoteArt.StatusByPowerId);
    }

    private static List<ulong> RunPlayers() =>
        Rows.Where(r => GodotObject.IsInstanceValid(r.Value) && r.Value.IsInsideTree()).Select(r => r.Key).ToList();

    private static double Now() => Time.GetTicksMsec() / 1000.0;
}
