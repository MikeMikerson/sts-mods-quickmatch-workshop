using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using QuickMatch.Localization;

namespace QuickMatch.Ui;

// The Quick Match screen: one window in the game's look (GameWindow) with two of the settings screen's tabs,
// Voice (VoiceTab) and Blocked players (BlockedTab). Each tab scrolls in a page of one height, so switching
// tabs does not move the window. Done, Esc, the controller's back or a click outside closes it.
//
// Node names: the window QuickMatchMenu, the tabs QuickMatchVoiceTab and QuickMatchBlockedTab, the pages
// QuickMatchVoicePage and QuickMatchBlockedPage, and QuickMatchMenuDone.
internal sealed class QuickMatchMenu
{
    internal const string NodeName = "QuickMatchMenu";
    private const float PageHeight = 560;

    private static QuickMatchMenu? _open;

    private readonly GameWindow _window;
    private readonly NSettingsTab? _voiceTab;
    private readonly NSettingsTab? _blockedTab;
    private readonly Control _voicePage;
    private readonly Control _blockedPage;
    private readonly VoiceTab _voice;
    private readonly BlockedTab _blocked;
    private SceneTree? _tree;
    private MenuTab _shown;

    // Turns an open screen to tab instead of opening another.
    public static void Open(MenuTab tab)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        if (_open == null)
        {
            _open = new QuickMatchMenu(tree, tab);
        }
        else
        {
            _open.Show(tab);
        }
    }

    public static void CloseIfOpen() => _open?.Close();

    private QuickMatchMenu(SceneTree tree, MenuTab tab)
    {
        _tree = tree;
        _window = new GameWindow(tree, NodeName, Loc.Get("QUICKMATCH.MENU.title"), null, 1120, Close);
        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        tabs.AddThemeConstantOverride("separation", 24);
        _window.Page.AddChild(tabs);
        _voiceTab = GameLook.Tab("QuickMatchVoiceTab", Loc.Get("QUICKMATCH.MENU.voice"), () => Show(MenuTab.Voice));
        _blockedTab = GameLook.Tab("QuickMatchBlockedTab", Loc.Get("QUICKMATCH.MENU.blocked"), () => Show(MenuTab.Blocked));
        foreach (NSettingsTab? each in new[] { _voiceTab, _blockedTab })
        {
            if (each != null)
            {
                tabs.AddChild(each);
            }
        }
        _window.Page.AddChild(GameLook.Rule());

        VBoxContainer voicePage = GameLook.ScrollPage(_window.Page, "QuickMatchVoicePage", PageHeight);
        _voicePage = (Control)voicePage.GetParent().GetParent();
        _voice = new VoiceTab(voicePage);
        VBoxContainer blockedPage = GameLook.ScrollPage(_window.Page, "QuickMatchBlockedPage", PageHeight);
        _blockedPage = (Control)blockedPage.GetParent().GetParent();
        _blocked = new BlockedTab(blockedPage);

        Button done = GameLook.Button("QuickMatchMenuDone", Loc.Get("QUICKMATCH.OPTIONS.done"), Close, look: ButtonLook.Yes);
        _window.Buttons.AddChild(done);
        _window.InsetButtonsForScrollBar();
        Show(tab);
        tree.ProcessFrame += OnProcessFrame;
        done.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Show(MenuTab tab)
    {
        _shown = tab;
        _voicePage.Visible = tab == MenuTab.Voice;
        _blockedPage.Visible = tab == MenuTab.Blocked;
        // A tab's picture and outline are set up in its _Ready: before then it is selected once it is ready.
        Select(_voiceTab, tab == MenuTab.Voice);
        Select(_blockedTab, tab == MenuTab.Blocked);
    }

    private static void Select(NSettingsTab? tab, bool selected)
    {
        if (tab == null)
        {
            return;
        }
        if (!tab.IsNodeReady())
        {
            tab.Ready += () => Select(tab, selected);
            return;
        }
        if (selected)
        {
            tab.Select();
        }
        else
        {
            tab.Deselect();
        }
    }

    private void OnProcessFrame()
    {
        if (_shown == MenuTab.Voice)
        {
            _voice.Refresh();
        }
        else
        {
            _blocked.Refresh();
        }
    }

    private void Close()
    {
        if (_open != this)
        {
            return;
        }
        _open = null;
        BlockQuestion.CloseIfOpen();
        _blocked.Closed();
        if (_tree != null)
        {
            _tree.ProcessFrame -= OnProcessFrame;
            _tree = null;
        }
        _window.Free();
    }
}
