using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;
using QuickMatch.Patches;

namespace QuickMatch.Ui;

// The hub's Match Options. Each choice is saved as it is made (MatchChoice) and the hub's Play Now card shows
// it; nothing searches from here. Built from the game's own pieces (GameLook): the settings screen's rows,
// paginators, tickbox and buttons in the Join Friends panel. Done, Esc, the controller's back or a click
// outside closes it.
//
// Node names: the paginators QuickMatchMode, QuickMatchSize and QuickMatchAscension, the tickbox QuickMatchVoice, and the
// buttons QuickMatchAscChoose, QuickMatchOpenBlocked and QuickMatchOptionsDone.
internal sealed class MatchOptions
{
    internal const string NodeName = "QuickMatchOptions";

    private static MatchOptions? _open;

    private readonly GameWindow _window;
    private readonly GameLook.Choice? _mode;
    private readonly GameLook.Choice? _ascension;
    private IReadOnlyList<int> _levels;

    public static void Open()
    {
        if (_open != null || Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        _open = new MatchOptions(tree);
    }

    public static void CloseIfOpen() => _open?.Close();

    private MatchOptions(SceneTree tree)
    {
        _window = new GameWindow(tree, NodeName, Loc.Get("QUICKMATCH.OPTIONS.title"), Loc.Get("QUICKMATCH.OPTIONS.hint"), 980, Close);
        VBoxContainer page = _window.Page;
        int max = MatchChoice.MaxAscension;
        _levels = LookLayout.ChosenLevels(MatchChoice.Last, max);

        // Only the modes this player can play; with Standard alone there is nothing to choose.
        List<RunMode> modes = StartScreens.PlayableModes();
        if (modes.Count > 1)
        {
            _mode = GameLook.Paginator("QuickMatchMode", modes.Select(QuickMatchHub.ModeText).ToList(), modes.IndexOf(MatchChoice.GameMode), at => ChooseMode(modes[at]));
            if (_mode != null)
            {
                GameLook.Row(page, Loc.Get("QUICKMATCH.OPTIONS.mode"), "QuickMatchModeRow").AddChild(_mode.Node);
            }
        }

        var sizes = new List<string>();
        foreach (int each in LookLayout.Sizes)
        {
            sizes.Add(each == 0 ? Loc.Get("QUICKMATCH.OPTIONS.any") : each.ToString(CultureInfo.InvariantCulture));
        }
        GameLook.Choice? size = GameLook.Paginator("QuickMatchSize", sizes, LookLayout.SizePage(MatchChoice.Size), at => ChooseSize(LookLayout.SizeAt(at)));
        if (size != null)
        {
            GameLook.Row(page, Loc.Get("QUICKMATCH.OPTIONS.size"), "QuickMatchSizeRow").AddChild(size.Node);
        }

        if (max > 0)
        {
            HBoxContainer row = GameLook.Row(page, Loc.Get("QUICKMATCH.OPTIONS.ascension"), "QuickMatchAscensionRow");
            row.AddChild(GameLook.Button("QuickMatchAscChoose", Loc.Get("QUICKMATCH.OPTIONS.choose"), OpenChooser, 240));
            _ascension = GameLook.Paginator("QuickMatchAscension", AscensionPages(), LookLayout.AscensionPage(MatchChoice.PlayNow), ChooseAscension);
            if (_ascension != null)
            {
                row.AddChild(_ascension.Node);
            }
        }

        bool voiceWorks = GamePatches.IsOn(GameFeature.Voice);
        NTickbox? voice = GameLook.Tickbox("QuickMatchVoice", MatchChoice.Voice, ChooseVoice);
        if (voice != null)
        {
            if (!voiceWorks)
            {
                voice.Ready += () => voice.SetEnabled(false);
            }
            GameLook.Row(page, Loc.Get("QUICKMATCH.OPTIONS.voice"), "QuickMatchVoiceRow").AddChild(voice);
            GameLook.Note(page, Loc.Get(voiceWorks ? "QUICKMATCH.OPTIONS.voice_hint" : "QUICKMATCH.OPTIONS.voice_unavailable"));
        }

        GameLook.Row(page, Loc.Get("QUICKMATCH.OPTIONS.blocked"), "QuickMatchBlockedRow")
            .AddChild(GameLook.Button("QuickMatchOpenBlocked", Loc.Get("QUICKMATCH.OPTIONS.blocked_open"), OpenBlockMenu));

        Button done = GameLook.Button("QuickMatchOptionsDone", Loc.Get("QUICKMATCH.OPTIONS.done"), Close, look: ButtonLook.Yes);
        _window.Buttons.AddChild(done);
        done.CallDeferred(Control.MethodName.GrabFocus);
    }

    private List<string> AscensionPages() =>
        [Loc.Get("QUICKMATCH.OPTIONS.any"), QuickMatchHub.AscensionText(new AscensionChoice(AscensionMode.Choose, _levels))];

    // Each mode has its own ascension, so the window is built again for it.
    private void ChooseMode(RunMode mode)
    {
        MatchChoice.RememberGameMode(mode);
        PickerCounts.Refresh();
        Callable.From(() =>
        {
            Close();
            Open();
            if (_open?._mode is { } choice)
            {
                choice.Node.GrabFocus();
            }
        }).CallDeferred();
    }

    private static void ChooseSize(int size)
    {
        MatchChoice.RememberSize(size);
        QuickMatchHub.RefreshCards();
    }

    private void ChooseAscension(int page)
    {
        MatchChoice.Remember(page == LookLayout.ChosenPage ? new AscensionChoice(AscensionMode.Choose, _levels) : AscensionChoice.Random);
        QuickMatchHub.RefreshCards();
    }

    private void OpenChooser() =>
        AscensionChooser.Open(MatchChoice.Size, _levels, choice =>
        {
            _levels = LookLayout.ChosenLevels(choice, MatchChoice.MaxAscension);
            MatchChoice.Remember(choice);
            _ascension?.SetOptions(AscensionPages(), LookLayout.ChosenPage);
            QuickMatchHub.RefreshCards();
        });

    private static void ChooseVoice(bool voice)
    {
        MatchChoice.RememberVoice(voice);
        // The waiting counts are per voice pool.
        PickerCounts.Refresh();
        QuickMatchHub.RefreshCards();
    }

    private void OpenBlockMenu()
    {
        Close();
        QuickMatchMenu.Open(MenuTab.Blocked);
    }

    private void Close()
    {
        if (_open != this)
        {
            return;
        }
        // The Choose panel sits on top of this one: it goes first.
        AscensionChooser.CloseIfOpen();
        _open = null;
        _window.Free();
        QuickMatchHub.RefreshCards();
    }
}
