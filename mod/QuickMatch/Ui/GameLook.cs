using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace QuickMatch.Ui;

// The game's own pieces for QuickMatch's panels and strips, each loaded from the game at run time so nothing here copies
// a game file. Sizes are the game's 1080-high view. Game thread only.
internal static partial class GameLook
{
    private const string RegularFont = "res://themes/kreon_regular_shared.tres";
    private const string BoldFont = "res://themes/kreon_bold_shared.tres";
    private const string HeaderFont = "res://themes/kreon_bold_glyph_space_two.tres";
    private const string PanelTexture = "res://images/ui/tiny_nine_patch.png";
    // Marks a heading, so the row under it gets no divider.
    private const string HeadingMeta = "qm_heading";

    // The game's body text (the popup's description, the paginator's value).
    public static readonly Color Cream = new(1f, 0.964706f, 0.886275f);
    // The popup's title and the settings toast's text.
    public static readonly Color Gold = new(0.937255f, 0.784314f, 0.317647f);
    public static readonly Color Faint = new(1f, 0.964706f, 0.886275f, 0.72f);
    private static readonly Color GoldOutline = new(0.33f, 0.2475f, 0f);
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.25098f);
    // The settings screen's divider between rows.
    private static readonly Color Divider = new(0.909804f, 0.862745f, 0.745098f, 0.25098f);
    // The Join Friends panel's fill, opaque, so the screen's text behind does not show through a list.
    private static readonly Color WindowFill = new(0.1131f, 0.25167164f, 0.29f, 1f);
    // The settings toast's black, nearly opaque so the screen's own text does not show through a line.
    private static readonly Color StripFill = new(0f, 0f, 0f, 0.94f);

    private static readonly Dictionary<string, Resource?> Loaded = new();

    public static MegaLabel Label(string text, int size, Color color, bool bold = false, bool wrap = false)
    {
        var label = new MegaLabel
        {
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // MegaLabel's _Ready asserts a font override, and with auto size on it shrinks its text into a box of no height.
        SetFont(label, "font", bold ? BoldFont : RegularFont, bold);
        label.AutoSizeEnabled = false;
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", Shadow);
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        label.Text = text;
        return label;
    }

    // A window's title, in the look of the game's popup header.
    public static MegaLabel Title(string text)
    {
        MegaLabel title = Label(text, 40, Gold);
        SetFont(title, "font", HeaderFont, bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.12549f));
        title.AddThemeColorOverride("font_outline_color", GoldOutline);
        title.AddThemeConstantOverride("shadow_offset_x", 5);
        title.AddThemeConstantOverride("shadow_offset_y", 4);
        title.AddThemeConstantOverride("outline_size", 12);
        return title;
    }

    public static ColorRect Rule() =>
        new() { Color = Divider, CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore };

    // A settings row: its name on the left, and the returned box on the right for its control.
    public static HBoxContainer Row(VBoxContainer page, string name, string nodeName) => AddRow(page, name, nodeName, null, out _, out _);

    // A settings row with a second, fainter line under its name.
    public static HBoxContainer Row(VBoxContainer page, string name, string nodeName, out MegaLabel title, out MegaLabel detail)
    {
        HBoxContainer controls = AddRow(page, name, nodeName, "", out title, out MegaLabel? second);
        detail = second!;
        return controls;
    }

    private static HBoxContainer AddRow(VBoxContainer page, string name, string nodeName, string? detailText, out MegaLabel title, out MegaLabel? detail)
    {
        int count = page.GetChildCount();
        if (count > 0 && !page.GetChild(count - 1).HasMeta(HeadingMeta))
        {
            page.AddChild(Rule());
        }
        var row = new MarginContainer { Name = nodeName, CustomMinimumSize = new Vector2(0, detailText == null ? 64 : 76), MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("margin_left", 12);
        row.AddThemeConstantOverride("margin_right", 12);
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 16);
        title = Label(name, 28, Cream);
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detail = null;
        if (detailText == null)
        {
            line.AddChild(title);
        }
        else
        {
            var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            names.AddThemeConstantOverride("separation", 0);
            names.AddChild(title);
            detail = Label(detailText, 22, Faint);
            detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            names.AddChild(detail);
            line.AddChild(names);
        }
        row.AddChild(line);
        page.AddChild(row);
        var controls = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore };
        controls.AddThemeConstantOverride("separation", 12);
        line.AddChild(controls);
        return controls;
    }

    public static MegaLabel Heading(VBoxContainer page, string text, string? nodeName = null)
    {
        var gap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (nodeName != null)
        {
            gap.Name = nodeName;
        }
        gap.AddThemeConstantOverride("margin_left", 12);
        gap.AddThemeConstantOverride("margin_right", 12);
        gap.AddThemeConstantOverride("margin_top", page.GetChildCount() > 0 ? 18 : 4);
        gap.AddThemeConstantOverride("margin_bottom", 2);
        MegaLabel label = Label(text, 30, Gold, bold: true);
        gap.AddChild(label);
        gap.SetMeta(HeadingMeta, true);
        page.AddChild(gap);
        return label;
    }

    // A wrapped line of text on a page, with the rows' margins.
    public static MegaLabel Note(VBoxContainer page, string text, Color? color = null, int size = 22)
    {
        MegaLabel label = Label(text, size, color ?? Faint, wrap: true);
        var gap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        gap.AddThemeConstantOverride("margin_left", 12);
        gap.AddThemeConstantOverride("margin_right", 12);
        gap.AddThemeConstantOverride("margin_top", 2);
        gap.AddThemeConstantOverride("margin_bottom", 6);
        gap.AddChild(label);
        // A note keeps its own margins out of the layout while it has nothing to say.
        label.VisibilityChanged += () => gap.Visible = label.Visible;
        page.AddChild(gap);
        return label;
    }

    public const int ScrollGutter = 18;

    public static VBoxContainer ScrollPage(Control parent, string name, float height)
    {
        var scroll = new ScrollContainer
        {
            Name = name,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        VScrollBar bar = scroll.GetVScrollBar();
        bar.CustomMinimumSize = new Vector2(10, 0);
        bar.AddThemeStyleboxOverride("scroll", new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.05f) });
        bar.AddThemeStyleboxOverride("grabber", new StyleBoxFlat { BgColor = Cream with { A = 0.35f } });
        bar.AddThemeStyleboxOverride("grabber_highlight", new StyleBoxFlat { BgColor = Cream with { A = 0.6f } });
        bar.AddThemeStyleboxOverride("grabber_pressed", new StyleBoxFlat { BgColor = Gold });
        parent.AddChild(scroll);
        var page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddThemeConstantOverride("separation", 6);
        // Room for the bar, so it never covers a row's controls.
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        gutter.AddThemeConstantOverride("margin_right", ScrollGutter);
        gutter.AddChild(page);
        scroll.AddChild(gutter);
        return page;
    }

    // The game's small nine-patch panel.
    public static PanelContainer Panel(Color fill, int marginX, int marginY)
    {
        StyleBox box;
        if (Load<Texture2D>(PanelTexture) is { } texture)
        {
            var nine = new StyleBoxTexture { Texture = texture, ModulateColor = fill };
            nine.TextureMarginLeft = nine.TextureMarginRight = 12;
            nine.TextureMarginTop = nine.TextureMarginBottom = 12;
            box = nine;
        }
        else
        {
            var flat = new StyleBoxFlat { BgColor = fill };
            flat.SetCornerRadiusAll(8);
            box = flat;
        }
        box.ContentMarginLeft = box.ContentMarginRight = marginX;
        box.ContentMarginTop = box.ContentMarginBottom = marginY;
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", box);
        return panel;
    }

    public static PanelContainer WindowPanel() => Panel(WindowFill, 40, 28);

    public static PanelContainer StripPanel() => Panel(StripFill, 32, 14);

    // A strip goes inside the game's own screen tree just under its popups, so the game's questions draw over it and
    // their backstop takes its clicks, and it stays while the screen under it changes. Without the game's tree it goes
    // on the root, above everything.
    public static void AddStrip(SceneTree tree, Control strip)
    {
        if (NGame.Instance is { } game && GodotObject.IsInstanceValid(game) && NModalContainer.Instance is { } modals
            && GodotObject.IsInstanceValid(modals) && modals.GetParent()?.GetInstanceId() == game.GetInstanceId())
        {
            game.AddChild(strip);
            game.MoveChild(strip, modals.GetIndex());
            return;
        }
        var layer = new CanvasLayer { Name = strip.Name + "Layer", Layer = 99 };
        layer.AddChild(strip);
        tree.Root.AddChild(layer);
        strip.TreeExited += () =>
        {
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        };
    }

    private static void SetFont(Control control, string slot, string path, bool bold)
    {
        if (Load<Font>(path) is { } font)
        {
            control.AddThemeFontOverride(slot, font);
        }
        control.ApplyLocaleFontSubstitution(bold ? FontType.Bold : FontType.Regular, slot);
    }

    private static T? Load<T>(string path) where T : Resource
    {
        if (!Loaded.TryGetValue(path, out Resource? resource))
        {
            resource = ResourceLoader.Exists(path) ? ResourceLoader.Load<Resource>(path) : null;
            Loaded[path] = resource;
        }
        return resource as T;
    }

    // A game scene with a game script put on its root before it enters the tree (the settings rows' roots have none).
    private static Node? WithScript(string scene, string script)
    {
        if (Load<PackedScene>(scene)?.Instantiate() is not { } root || Load<Script>(script) is not { } code)
        {
            return null;
        }
        ulong id = root.GetInstanceId();
        root.SetScript(code);
        // Setting a script makes a new object of the script's class for the same node.
        return GodotObject.InstanceFromId(id) as Node;
    }
}
