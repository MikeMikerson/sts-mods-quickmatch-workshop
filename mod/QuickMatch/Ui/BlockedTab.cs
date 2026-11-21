using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// The Quick Match screen's Blocked players tab, built from the game's pieces (GameLook). Block and Vote to
// Remove ask first (BlockQuestion, VoteQuestion). A Remove keeps its row with Undo until the screen closes; a
// removed player cannot be blocked again for ten minutes, and the row says from when. Rebuilt when what it
// shows changes, and each minute.
//
// Node names: QuickMatchBlockTyped (the field), QuickMatchBlockPaste, QuickMatchBlockTypedYes, and per row
// QuickMatchBlockHere{n}, QuickMatchBlockRemove{n} and QuickMatchBlockUndo{n}.
internal sealed class BlockedTab
{
    private readonly VBoxContainer _here;
    private readonly VBoxContainer _list;
    private readonly LineEdit _typed;
    private readonly MegaLabel _typedResult;
    // Newest first, for their Undo.
    private readonly List<BlockedPlayer> _removedHere = new();
    private string _shown = "";

    public BlockedTab(VBoxContainer page)
    {
        GameLook.Note(page, Loc.Get("QUICKMATCH.BLOCK.hint"));
        _here = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _here.AddThemeConstantOverride("separation", 6);
        page.AddChild(_here);

        // Typing an ID comes before the list, so its answer shows however long the list is.
        var typing = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        typing.AddThemeConstantOverride("separation", 6);
        page.AddChild(typing);
        GameLook.Heading(typing, Loc.Get("QUICKMATCH.BLOCK.by_id"));
        var typedRow = new MarginContainer { CustomMinimumSize = new Vector2(0, 64), MouseFilter = Control.MouseFilterEnum.Ignore };
        typedRow.AddThemeConstantOverride("margin_left", 12);
        typedRow.AddThemeConstantOverride("margin_right", 12);
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 12);
        _typed = GameLook.TextField("QuickMatchBlockTyped", Loc.Get("QUICKMATCH.BLOCK.by_id_placeholder"));
        _typed.TextSubmitted += _ => BlockTyped();
        line.AddChild(_typed);
        line.AddChild(GameLook.Button("QuickMatchBlockPaste", Loc.Get("QUICKMATCH.BLOCK.paste"), () => _typed.Text = DisplayServer.ClipboardGet().Trim(), 160));
        line.AddChild(GameLook.Button("QuickMatchBlockTypedYes", Loc.Get("QUICKMATCH.BLOCK.block"), BlockTyped, 160, look: ButtonLook.Red));
        typedRow.AddChild(line);
        typing.AddChild(typedRow);
        _typedResult = GameLook.Note(typing, "", GameLook.Cream);
        _typedResult.Visible = false;

        _list = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _list.AddThemeConstantOverride("separation", 6);
        page.AddChild(_list);
        GameLook.Note(page, Loc.Get("QUICKMATCH.BLOCK.private_note"));
        Refresh();
    }

    // Rebuilds only when what the lists show has changed (players here, the list, the removals, the minute).
    public void Refresh()
    {
        long now = BlockStore.Now();
        List<ulong> here = BlockPlayers.Here();
        IReadOnlyList<BlockedPlayer> blocked = BlockStore.List.Players;
        string shown = $"{Regroup.InQuickMatchRun}|{string.Join(",", here)}|{string.Join(",", here.Select(BlockPlayers.CanVoteAgainst))}|{string.Join(",", blocked.Select(p => p.Id))}|{string.Join(",", _removedHere.Select(p => p.Id))}|{string.Join(",", BlockStore.List.CoolingDown(now).Select(p => p.Id))}|{now / 60}";
        if (shown == _shown)
        {
            return;
        }
        _shown = shown;
        BuildHere(here, now);
        BuildList(blocked, now);
    }

    // Undo lasts until the screen closes.
    public void Closed() => _removedHere.Clear();

    private void BuildHere(List<ulong> here, long now)
    {
        Clear(_here);
        if (here.Count == 0)
        {
            return;
        }
        GameLook.Heading(_here, Loc.Get(Regroup.InQuickMatchRun ? "QUICKMATCH.BLOCK.in_run" : "QUICKMATCH.BLOCK.in_lobby"));
        int n = 0;
        foreach (ulong player in here)
        {
            ulong id = player;
            HBoxContainer controls = GameLook.Row(_here, BlockPlayers.NameOf(player), $"QuickMatchBlockHereRow{n}", out _, out MegaLabel detail);
            bool blocked = BlockStore.List.Contains(player);
            long? again = blocked ? null : BlockStore.List.BlockableAgainAt(player, now);
            if (blocked || again is not null)
            {
                detail.Text = blocked
                    ? Loc.Get("QUICKMATCH.BLOCK.blocked")
                    : Loc.With("QUICKMATCH.BLOCK.again_at", "time", BlockPlayers.ClockTime(again!.Value));
                // Blocked already, but still in our lobby or run: they can still be voted out.
                if (BlockPlayers.CanVoteAgainst(player))
                {
                    controls.AddChild(GameLook.Button($"QuickMatchBlockHere{n}", Loc.Get("QUICKMATCH.VOTE.remove"), () => VoteQuestion.Ask(id), look: ButtonLook.Red));
                }
            }
            else
            {
                detail.Visible = false;
                controls.AddChild(GameLook.Button($"QuickMatchBlockHere{n}", Loc.Get("QUICKMATCH.BLOCK.block"), () => BlockQuestion.Ask(id), 200, look: ButtonLook.Red));
            }
            n++;
        }
    }

    private void BuildList(IReadOnlyList<BlockedPlayer> blocked, long now)
    {
        Clear(_list);
        GameLook.Heading(_list, Loc.Get("QUICKMATCH.BLOCK.list"));
        // Removed here (with Undo) or earlier (cooling down, no Undo), shown among the blocked by their time.
        var removedEarlier = BlockStore.List.CoolingDown(now).Where(r => _removedHere.All(p => p.Id != r.Id)).ToList();
        if (blocked.Count == 0 && _removedHere.Count == 0 && removedEarlier.Count == 0)
        {
            GameLook.Note(_list, Loc.Get("QUICKMATCH.BLOCK.none"), GameLook.Cream, 24);
            return;
        }
        int n = 0;
        foreach (BlockedPlayer player in blocked)
        {
            ulong id = player.Id;
            HBoxContainer controls = GameLook.Row(_list, ListName(player), $"QuickMatchBlockRow{n}", out _, out MegaLabel detail);
            detail.Text = Loc.With("QUICKMATCH.BLOCK.blocked_on", "date", BlockPlayers.Date(player.At));
            controls.AddChild(GameLook.Button($"QuickMatchBlockRemove{n}", Loc.Get("QUICKMATCH.BLOCK.remove"), () => RemoveEntry(id), 200));
            n++;
        }
        foreach (BlockedPlayer player in _removedHere.Where(p => !BlockStore.List.Contains(p.Id)))
        {
            BlockedPlayer entry = player;
            HBoxContainer controls = GameLook.Row(_list, ListName(player), $"QuickMatchBlockRow{n}", out _, out MegaLabel detail);
            detail.Text = BlockStore.List.BlockableAgainAt(player.Id, now) is { } again
                ? Loc.With("QUICKMATCH.BLOCK.removed_until", "time", BlockPlayers.ClockTime(again))
                : Loc.Get("QUICKMATCH.BLOCK.removed");
            controls.AddChild(GameLook.Button($"QuickMatchBlockUndo{n}", Loc.Get("QUICKMATCH.BLOCK.undo"), () => UndoEntry(entry), 200));
            n++;
        }
        foreach (RemovedPlayer player in removedEarlier)
        {
            GameLook.Row(_list, BlockPlayers.NameOf(player.Id), $"QuickMatchBlockRow{n}", out _, out MegaLabel detail);
            detail.Text = Loc.With("QUICKMATCH.BLOCK.removed_until", "time", BlockPlayers.ClockTime(player.At + BlockList.ReblockCooldownSeconds));
            n++;
        }
    }

    private static string ListName(BlockedPlayer player) =>
        player.Name ?? Loc.With("QUICKMATCH.BLOCK.steam_id", "id", player.Id.ToString(CultureInfo.InvariantCulture));

    private void RemoveEntry(ulong player)
    {
        if (BlockStore.Remove(player) is { } removed)
        {
            _removedHere.RemoveAll(p => p.Id == player);
            _removedHere.Insert(0, removed);
        }
    }

    private void UndoEntry(BlockedPlayer entry)
    {
        if (BlockStore.Restore(entry))
        {
            _removedHere.RemoveAll(p => p.Id == entry.Id);
        }
    }

    private void BlockTyped()
    {
        if (BlockPolicy.ParseSteamId(_typed.Text) is not { } id)
        {
            ShowTyped(Loc.Get("QUICKMATCH.BLOCK.not_an_id"), GameLook.Gold);
            return;
        }
        // A player in this lobby or run gets the same question as a click on their name.
        if (BlockPlayers.Here().Contains(id) && !BlockStore.List.Contains(id))
        {
            _typed.Text = "";
            ShowTyped("", GameLook.Cream);
            BlockQuestion.Ask(id);
            return;
        }
        switch (BlockStore.Block(id, BlockPlayers.KnownName(id)))
        {
            case BlockResult.Added:
                _typed.Text = "";
                _removedHere.RemoveAll(p => p.Id == id);
                ShowTyped(Loc.Get("QUICKMATCH.BLOCK.typed_added"), GameLook.Cream);
                break;
            case BlockResult.AlreadyBlocked:
                ShowTyped(Loc.Get("QUICKMATCH.BLOCK.typed_already"), GameLook.Cream);
                break;
            case BlockResult.Self:
                ShowTyped(Loc.Get("QUICKMATCH.BLOCK.typed_self"), GameLook.Gold);
                break;
            case BlockResult.CoolingDown:
                ShowTyped(Loc.With("QUICKMATCH.BLOCK.typed_cooling", "time", BlockPlayers.ClockTime(BlockStore.List.BlockableAgainAt(id, BlockStore.Now()) ?? BlockStore.Now())), GameLook.Gold);
                break;
            default:
                ShowTyped(Loc.Get("QUICKMATCH.BLOCK.not_an_id"), GameLook.Gold);
                break;
        }
    }

    // Cream for what was done, gold for what was refused.
    private void ShowTyped(string text, Color color)
    {
        _typedResult.Text = text;
        _typedResult.AddThemeColorOverride("font_color", color);
        _typedResult.Visible = text.Length > 0;
    }

    private static void Clear(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
