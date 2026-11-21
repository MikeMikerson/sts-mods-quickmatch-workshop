using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// The Quick Match screen: an instance of the game's own multiplayer host submenu with three of its cards
// relabelled. Back cancels the search (Patches.HubPatches). A rejoin opens it with no cards, titled Rejoin
// Run.
internal static class QuickMatchHub
{
    private const string HostIconPath = "res://images/ui/main_menu/submenu_host.png";
    private const string TitleFontPath = "res://themes/kreon_bold_glyph_space_two.tres";

    internal const string PlayNode = GameVersions.HostCards + "StandardButton";
    internal const string ClimbNode = GameVersions.HostCards + "DailyButton";
    internal const string OptionsNode = GameVersions.HostCards + "CustomRunButton";

    private const string TitleNode = "QuickMatchTitle";
    private const string WaitingNode = "QuickMatchWaitingNow";
    // In the game's 1080-high view.
    private const float WaitingFromBottom = 110;

    private static NMultiplayerHostSubmenu? _hub;
    private static bool _rejoin;

    public static bool Is(NSubmenu submenu) => _hub != null && ReferenceEquals(submenu, _hub);

    public static bool IsOnTop() =>
        _hub != null && GodotObject.IsInstanceValid(_hub) && NGame.Instance?.MainMenu?.SubmenuStack.Peek() == _hub;

    // From the top of the view while the hub is shown, else 0 (a strip starts below it).
    public static float TitleBottom() =>
        _hub != null && GodotObject.IsInstanceValid(_hub) && _hub.IsVisibleInTree()
            && _hub.GetNodeOrNull<Control>(TitleNode) is { } title && title.IsVisibleInTree()
            ? title.GetGlobalRect().End.Y
            : 0;

    public static void Open() => Open(rejoin: false);

    // The title and the search status, with no cards (the run decides the rest).
    public static void OpenForRejoin() => Open(rejoin: true);

    private static void Open(bool rejoin)
    {
        NMainMenuSubmenuStack? stack = NGame.Instance?.MainMenu?.SubmenuStack;
        if (stack == null || !Matchmaker.IsAvailable)
        {
            return;
        }
        _rejoin = rejoin;
        if (!rejoin)
        {
            // The count's one wide search, each time Quick Match opens; the line of the last visit goes until it answers.
            PickerCounts.Refresh();
        }
        NMultiplayerHostSubmenu hub = GetOrCreate(stack);
        ShowWaiting();
        stack.Push(hub);
    }

    // Called after the vanilla OnSubmenuOpened, which locks Daily and Custom by unlock progress.
    public static void OnOpened(NMultiplayerHostSubmenu hub)
    {
        foreach (string node in new[] { PlayNode, ClimbNode, OptionsNode })
        {
            hub.GetNode<NSubmenuButton>(node).SetEnabled(true);
        }
        // The vanilla method shows Dread once it is unlocked and shrinks the row to fit five cards.
        foreach (string node in GameVersions.HostModeCards)
        {
            hub.GetNode<Control>(node).Visible = false;
        }
        // The stable branch places each card on its own, with no row around them.
        if (hub.GetNodeOrNull<Control>("ButtonContainer") is { } row)
        {
            row.Scale = Vector2.One;
            row.Visible = !_rejoin;
        }
        else
        {
            foreach (string node in new[] { PlayNode, ClimbNode, OptionsNode })
            {
                hub.GetNode<Control>(node).Visible = !_rejoin;
            }
        }
        if (hub.GetNodeOrNull<Label>(TitleNode) is { } title)
        {
            title.Text = Loc.Get(_rejoin ? "QUICKMATCH.HUB.rejoin_title" : "QUICKMATCH.HUB.title");
        }
        RefreshCards();
        ShowWaiting();
        ShowStatus(null);
    }

    public static void RefreshCards()
    {
        if (_hub == null || !GodotObject.IsInstanceValid(_hub))
        {
            return;
        }
        int max = MatchChoice.MaxAscension;
        PickerCards.Set(_hub.GetNode<NSubmenuButton>(PlayNode),
            description: Loc.Get("QUICKMATCH_PLAY.description") + "\n" + Summary("\n"));
        var climb = _hub.GetNode<NSubmenuButton>(ClimbNode);
        climb.Visible = !_rejoin && max > 0 && max < MatchMode.TopAscension(MatchChoice.GameMode);
        PickerCards.Set(climb,
            title: Loc.WithNumber("QUICKMATCH.ASC.climb", "level", max),
            description: Loc.WithNumber("QUICKMATCH.ASC.progress_at", "level", max));
        // Match Options shows its Game mode row only when there is more than Standard to choose.
        PickerCards.Set(_hub.GetNode<NSubmenuButton>(OptionsNode),
            description: Loc.Get(StartScreens.PlayableModes().Count > 1 ? "QUICKMATCH_OPTIONS.description_modes" : "QUICKMATCH_OPTIONS.description"));
    }

    // The Play Now card puts each option on its own line, since the card's width breaks the line inside a
    // phrase. No game mode for Standard, and no Ascension at a maximum of 0.
    public static string Summary(string separator = " \u00b7 ")
    {
        var parts = new List<string>();
        if (MatchChoice.GameMode != RunMode.Standard)
        {
            parts.Add(ModeText(MatchChoice.GameMode));
        }
        int size = MatchChoice.Size;
        parts.Add(size == 0 ? Loc.Get("QUICKMATCH.SUMMARY.size_any") : Loc.WithNumber("QUICKMATCH.SUMMARY.size", "size", size));
        if (MatchChoice.MaxAscension > 0)
        {
            parts.Add(AscensionText(MatchChoice.PlayNow));
        }
        parts.Add(Loc.Get(MatchChoice.Voice ? "QUICKMATCH.SUMMARY.voice_on" : "QUICKMATCH.SUMMARY.voice_off"));
        return string.Join(separator, parts);
    }

    public static string ModeText(RunMode mode) => Loc.Get("QUICKMATCH.MODE." + MatchMode.LobbyValue(mode));

    public static string AscensionText(AscensionChoice choice)
    {
        if (choice.Mode != AscensionMode.Choose || choice.Chosen is not { Count: > 0 } chosen)
        {
            return Loc.Get("QUICKMATCH.SUMMARY.asc_any");
        }
        return chosen.Count == 1
            ? Loc.WithNumber("QUICKMATCH.SUMMARY.asc_one", "level", chosen[0])
            : Loc.With("QUICKMATCH.SUMMARY.asc_many", "levels", string.Join(", ", chosen.Order()));
    }

    public static void PlayNow() => Matchmaker.Start(MatchChoice.Size, MatchChoice.Voice, MatchChoice.PlayNow);

    // Exactly the player's maximum; it does not change the saved Ascension.
    public static void Climb() => Matchmaker.Start(MatchChoice.Size, MatchChoice.Voice, AscensionChoice.Progress);

    public static void ShowWaiting()
    {
        if (_hub == null || !GodotObject.IsInstanceValid(_hub) || _hub.GetNodeOrNull<Label>(WaitingNode) is not { } line)
        {
            return;
        }
        int waiting = _rejoin ? 0 : PickerCounts.Current?.Total ?? 0;
        line.Visible = waiting > 0;
        if (waiting > 0)
        {
            line.Text = waiting == 1 ? Loc.Get("QUICKMATCH.waiting_now_one") : Loc.WithNumber("QUICKMATCH.waiting_now", "count", waiting);
        }
    }

    // Null hides it.
    public static void ShowStatus(string? text)
    {
        if (_hub == null || !GodotObject.IsInstanceValid(_hub))
        {
            return;
        }
        var overlay = _hub.GetNode<Control>("%LoadingOverlay");
        overlay.Visible = text != null;
        if (text != null)
        {
            overlay.GetNode<MegaLabel>("%Label").SetTextAutoSize(text);
        }
    }

    private static NMultiplayerHostSubmenu GetOrCreate(NMainMenuSubmenuStack stack)
    {
        // The main menu (and its submenu stack) is rebuilt after every run.
        if (_hub != null && GodotObject.IsInstanceValid(_hub) && _hub.GetParent() == stack)
        {
            return _hub;
        }
        NMultiplayerHostSubmenu hub = NMultiplayerHostSubmenu.Create()
            ?? throw new InvalidOperationException("could not create the Quick Match screen");
        hub.Name = "QuickMatchHub";
        hub.Visible = false;
        stack.AddChild(hub);

        Texture2D? icon = ResourceLoader.Exists(HostIconPath) ? GD.Load<Texture2D>(HostIconPath) : null;
        foreach ((string node, string key, Texture2D? picture) in new[]
        {
            (PlayNode, "QUICKMATCH_PLAY", CardPictures.Play),
            (ClimbNode, "QUICKMATCH_CLIMB", CardPictures.Climb),
            (OptionsNode, "QUICKMATCH_OPTIONS", CardPictures.Options),
        })
        {
            var button = hub.GetNode<NSubmenuButton>(node);
            if ((picture ?? icon) is { } texture)
            {
                button.GetNode<TextureRect>(QuickMatchButton.Portrait).Texture = texture;
            }
            GameVersions.SetCardText(button, key);
        }

        hub.AddChild(Title());
        hub.AddChild(WaitingLine());
        // Keep Back above the overlay so a search can always be cancelled with the mouse.
        hub.MoveChild(hub.GetNode<Node>("BackButton"), -1);
        _hub = hub;
        return hub;
    }

    // In the look of the game's Join Friends title (screens/join_friend_submenu.tscn).
    private static Label Title()
    {
        var title = new Label
        {
            Name = TitleNode,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        title.OffsetLeft = -651;
        title.OffsetRight = 651;
        title.OffsetTop = 99;
        title.OffsetBottom = 181;
        if (ResourceLoader.Exists(TitleFontPath))
        {
            title.AddThemeFontOverride("font", GD.Load<Font>(TitleFontPath));
        }
        title.AddThemeFontSizeOverride("font_size", 48);
        title.AddThemeColorOverride("font_color", new Color(0.937255f, 0.784314f, 0.317647f));
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0));
        title.AddThemeColorOverride("font_outline_color", new Color(0.32287663f, 0.25424263f, 0.04230208f));
        title.AddThemeConstantOverride("shadow_offset_x", 4);
        title.AddThemeConstantOverride("shadow_offset_y", 3);
        title.AddThemeConstantOverride("outline_size", 12);
        title.AddThemeConstantOverride("shadow_outline_size", 12);
        return title;
    }

    private static Label WaitingLine()
    {
        Label line = VoiceWidgets.Text("", 30, VoiceGlyphs.Ink);
        line.Name = WaitingNode;
        line.HorizontalAlignment = HorizontalAlignment.Center;
        line.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        line.AddThemeConstantOverride("outline_size", 10);
        line.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        line.OffsetLeft = -600;
        line.OffsetRight = 600;
        line.OffsetTop = -WaitingFromBottom - 60;
        line.OffsetBottom = -WaitingFromBottom;
        line.Visible = false;
        return line;
    }
}
