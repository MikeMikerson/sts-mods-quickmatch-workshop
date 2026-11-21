using System;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace QuickMatch.Ui;

// A window in the game's look over the dimmed screen: a title, rows in Page and buttons at the bottom. Esc, the
// controller's back or a click outside calls close.
internal sealed class GameWindow
{
    private const int Layer = 100;
    private const int RowInset = 12;

    private readonly CanvasLayer _layer;
    private readonly PanelContainer _panel;
    private readonly Action _close;
    private readonly MarginContainer _buttonsGap;

    public GameWindow(SceneTree tree, string name, string title, string? hint, float width, Action close)
    {
        _close = close;
        _layer = new CanvasLayer { Name = name, Layer = Layer };
        var backstop = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        backstop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backstop.GuiInput += OnBackstopInput;
        _layer.AddChild(backstop);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backstop.AddChild(centre);

        Vector2 view = tree.Root.GetVisibleRect().Size;
        _panel = GameLook.WindowPanel();
        _panel.CustomMinimumSize = new Vector2(width, 0);
        centre.AddChild(_panel);
        Page = new VBoxContainer();
        Page.AddThemeConstantOverride("separation", 6);
        var whole = new VBoxContainer();
        whole.AddThemeConstantOverride("separation", 10);
        whole.AddChild(GameLook.Title(title));
        if (hint != null)
        {
            MegaLabel line = GameLook.Label(hint, 24, GameLook.Faint, wrap: true);
            line.HorizontalAlignment = HorizontalAlignment.Center;
            whole.AddChild(line);
        }
        whole.AddChild(Page);
        Buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Buttons.AddThemeConstantOverride("separation", 16);
        // In line with the rows' controls, which sit 12 in from the window's sides (GameLook.Row).
        _buttonsGap = new MarginContainer();
        _buttonsGap.AddThemeConstantOverride("margin_top", 12);
        _buttonsGap.AddThemeConstantOverride("margin_left", RowInset);
        _buttonsGap.AddThemeConstantOverride("margin_right", RowInset);
        _buttonsGap.AddChild(Buttons);
        whole.AddChild(_buttonsGap);
        _panel.AddChild(whole);
        // Shrunk about its centre when larger than the view.
        _panel.Resized += () =>
        {
            _panel.PivotOffset = _panel.Size / 2;
            float scale = LookLayout.FitScale(_panel.Size.X, _panel.Size.Y, view.X, view.Y, 24);
            _panel.Scale = Vector2.One * scale;
        };

        tree.Root.AddChild(_layer);
        NHotkeyManager? hotkeys = NHotkeyManager.Instance;
        if (hotkeys != null)
        {
            hotkeys.AddBlockingScreen(_layer);
            hotkeys.PushHotkeyReleasedBinding(MegaInput.pauseAndBack, _close);
            hotkeys.PushHotkeyReleasedBinding(MegaInput.cancel, _close);
        }
    }

    public VBoxContainer Page { get; }

    // A question's words, in the popup's body text.
    public MegaLabel AddBody(string text)
    {
        MegaLabel body = GameLook.Label(text, 28, GameLook.Cream, wrap: true);
        body.Name = "Body";
        body.HorizontalAlignment = HorizontalAlignment.Center;
        var gap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        gap.AddThemeConstantOverride("margin_left", 12);
        gap.AddThemeConstantOverride("margin_right", 12);
        gap.AddThemeConstantOverride("margin_top", 6);
        gap.AddThemeConstantOverride("margin_bottom", 6);
        gap.AddChild(body);
        Page.AddChild(gap);
        return body;
    }

    public HBoxContainer Buttons { get; }

    // For a window whose rows are on scroll pages, which stop short of the scroll bar.
    public void InsetButtonsForScrollBar() => _buttonsGap.AddThemeConstantOverride("margin_right", RowInset + GameLook.ScrollGutter);

    public void Free()
    {
        NHotkeyManager? hotkeys = NHotkeyManager.Instance;
        if (hotkeys != null)
        {
            hotkeys.RemoveHotkeyReleasedBinding(MegaInput.cancel, _close);
            hotkeys.RemoveHotkeyReleasedBinding(MegaInput.pauseAndBack, _close);
            hotkeys.RemoveBlockingScreen(_layer);
        }
        if (GodotObject.IsInstanceValid(_layer))
        {
            // Out of the tree at once, so a window opened in its place keeps its node name.
            _layer.GetParent()?.RemoveChild(_layer);
            _layer.QueueFree();
        }
    }

    private void OnBackstopInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button && !_panel.GetGlobalRect().HasPoint(button.GlobalPosition))
        {
            _close();
        }
    }
}
