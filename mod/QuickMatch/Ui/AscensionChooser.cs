using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// Match Options' Choose...: any number of the ascensions the player has unlocked, each with the players
// waiting at it now; Save keeps the levels for Play Now. Built from the game's own pieces (GameLook): the
// custom run's tickbox rows in the Join Friends panel. Cancel, Esc, the controller's back or a click outside
// cancels.
internal sealed class AscensionChooser
{
    internal const string NodeName = "QuickMatchAscensionChooser";

    private static AscensionChooser? _open;

    private readonly GameWindow _window;
    private readonly HashSet<int> _chosen;
    private readonly Button _save;
    private readonly Action<AscensionChoice> _saved;

    // size is the saved party size (0: Any), for the counts.
    public static void Open(int size, IReadOnlyList<int> levels, Action<AscensionChoice> save)
    {
        if (_open != null || Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        _open = new AscensionChooser(tree, size, levels, save);
    }

    public static void CloseIfOpen() => _open?.Close();

    private AscensionChooser(SceneTree tree, int size, IReadOnlyList<int> levels, Action<AscensionChoice> saved)
    {
        _saved = saved;
        int max = MatchChoice.MaxAscension;
        _chosen = levels.Where(level => level >= 0 && level <= max).ToHashSet();
        _window = new GameWindow(tree, NodeName, Loc.Get("QUICKMATCH.ASC.title"), Loc.Get("QUICKMATCH.ASC.hint"), 980, Close);

        var grid = new GridContainer { Columns = LookLayout.ChooserColumns(max + 1), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        grid.AddThemeConstantOverride("h_separation", 36);
        grid.AddThemeConstantOverride("v_separation", 14);
        var gridGap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        gridGap.AddThemeConstantOverride("margin_top", 14);
        gridGap.AddThemeConstantOverride("margin_bottom", 8);
        gridGap.AddChild(grid);
        _window.Page.AddChild(gridGap);
        PoolCounts? counts = PickerCounts.Current;
        for (int level = 0; level <= max; level++)
        {
            int at = level;
            var cell = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            cell.AddThemeConstantOverride("separation", 0);
            NRunModifierTickbox? row = GameLook.TickboxRow($"QuickMatchLevel{at}", Loc.WithNumber("QUICKMATCH.ASC.level", "level", at), _chosen.Contains(at), on => Toggle(at, on));
            if (row == null)
            {
                continue;
            }
            row.CustomMinimumSize = new Vector2(120, 56);
            cell.AddChild(row);
            if (counts != null)
            {
                // Only above 0; an empty line keeps the rows level.
                int waiting = counts.ForAscensions(size, [at]);
                MegaLabel count = GameLook.Label(waiting > 0 ? Loc.WithNumber("QUICKMATCH.ASC.waiting", "count", waiting) : " ", 22, GameLook.Cream);
                count.Name = $"QuickMatchWaiting{at}";
                count.HorizontalAlignment = HorizontalAlignment.Left;
                var countGap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                // Under the level's name, past the box.
                countGap.AddThemeConstantOverride("margin_left", 60);
                countGap.AddChild(count);
                cell.AddChild(countGap);
            }
            grid.AddChild(cell);
        }

        _window.Buttons.AddChild(GameLook.Button("QuickMatchAscCancel", Loc.Get("QUICKMATCH.ASC.cancel"), Close, 240));
        _save = GameLook.Button("QuickMatchAscSave", Loc.Get("QUICKMATCH.ASC.save"), Save, 240, ButtonLook.Yes);
        _window.Buttons.AddChild(_save);
        Refresh();
        _save.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Toggle(int level, bool on)
    {
        if (on)
        {
            _chosen.Add(level);
        }
        else
        {
            _chosen.Remove(level);
        }
        Refresh();
    }

    private void Refresh() => GameLook.SetEnabled(_save, _chosen.Count > 0);

    private void Save()
    {
        if (_chosen.Count == 0)
        {
            return;
        }
        AscensionChoice choice = new(AscensionMode.Choose, _chosen.Order().ToList());
        Close();
        _saved(choice);
    }

    private void Close()
    {
        if (_open != this)
        {
            return;
        }
        _open = null;
        _window.Free();
    }
}
