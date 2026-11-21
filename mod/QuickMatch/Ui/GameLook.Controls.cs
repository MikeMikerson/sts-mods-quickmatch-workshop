using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace QuickMatch.Ui;

// The game's controls: each returns null (and logs) if the game's scene or script is missing.
internal static partial class GameLook
{
    private const string ButtonTexture = "res://images/ui/reward_screen/reward_skip_button.png";
    // The popups' green Yes (scenes/ui/abandon_run_yes_button.tscn).
    private const string YesTexture = "res://images/atlases/ui_atlas.sprites/popup_confirm_button.tres";
    private const string YesOutlineTexture = "res://images/atlases/compressed_atlas.sprites/popup_confirm_button_outline.tres";
    private const string HsvShader = "res://shaders/hsv.gdshader";
    private const string PaginatorScene = "res://scenes/screens/paginator.tscn";
    private const string PaginatorScript = "res://src/Core/Nodes/Screens/Settings/NPaginator.cs";
    private const string TickboxScene = "res://scenes/screens/settings_tickbox.tscn";
    private const string TickboxScript = "res://src/Core/Nodes/CommonUi/NTickbox.cs";
    private const string TickboxRowScene = "res://scenes/screens/custom_run/modifier_tickbox.tscn";
    private const string SliderScene = "res://scenes/screens/settings_slider.tscn";
    private const string TabScene = "res://scenes/screens/settings_tab.tscn";
    private const string PaginatorLabel = "LabelContainer/Mask/Label";
    private const string SliderRowName = "SliderRow";

    // The settings buttons' text and outline.
    private static readonly Color ButtonText = new(0.91f, 0.86359f, 0.7462f);
    private static readonly Color ButtonOutline = new(0.29f, 0.14703f, 0.1421f);
    // The pause menu's Give Up: the same blue plate turned red through the shader's hue, with its own outline.
    private const float RedHue = 0.5f;
    private static readonly Color YesOutline = new(0.121333f, 0.28f, 0f);
    private static readonly Color RedOutline = new(0.36f, 0.1944f, 0.144f);
    // Grey, as the game's disabled arrows are.
    private static readonly Color DisabledModulate = new(0.5f, 0.5f, 0.5f, 0.8f);

    private static readonly FieldInfo? PaginatorOptions = typeof(NPaginator).GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? PaginatorIndex = typeof(NPaginator).GetField("_currentIndex", BindingFlags.NonPublic | BindingFlags.Instance);

    // The settings screen's slider row, wired as the game's own settings slider wires it (the keyboard moves it 5 at a time).
    public static Control? Slider(string name, int value, int most, Action<int> changed)
    {
        if (Load<PackedScene>(SliderScene)?.Instantiate() is not Control row || row.GetNodeOrNull<Godot.Range>("Slider") is not { } slider)
        {
            MainFile.Logger.Warn("game look: no slider; the game's scene is missing");
            return null;
        }
        row.Name = SliderRowName;
        var box = new MarginContainer { Name = name, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        // The scene draws its value to the left of its own box.
        box.AddThemeConstantOverride("margin_left", 100);
        box.AddChild(row);
        slider.MinValue = 0;
        slider.MaxValue = Math.Max(1, most);
        slider.Step = 1;
        slider.Value = Math.Clamp(value, 0, most);
        void Show(double now)
        {
            if (row.GetNodeOrNull<MegaLabel>("SliderValue") is { } label && label.IsNodeReady())
            {
                label.SetTextAutoSize(LookLayout.PercentText((int)Math.Round(now)));
            }
        }
        row.Ready += () => Show(slider.Value);
        slider.ValueChanged += now =>
        {
            Show(now);
            changed((int)Math.Round(now));
        };
        row.GuiInput += input =>
        {
            if (input.IsActionPressed(MegaInput.left))
            {
                slider.Value -= 5;
            }
            else if (input.IsActionPressed(MegaInput.right))
            {
                slider.Value += 5;
            }
        };
        return box;
    }

    public static NSettingsTab? Tab(string name, string text, Action pressed)
    {
        if (Load<PackedScene>(TabScene)?.Instantiate() is not NSettingsTab tab)
        {
            MainFile.Logger.Warn("game look: no tab; the game's scene is missing");
            return null;
        }
        tab.Name = name;
        // The hover is a shader parameter on the tab picture's material, which every copy of the scene shares: each tab gets
        // its own, before its _Ready picks it up.
        if (tab.GetNodeOrNull<CanvasItem>("TabImage") is { Material: { } material } image)
        {
            image.Material = (Material)material.Duplicate();
        }
        tab.Ready += () => tab.SetLabel(text);
        tab.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => pressed()));
        return tab;
    }

    public static LineEdit TextField(string name, string placeholder)
    {
        var field = new LineEdit
        {
            Name = name,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 56),
            ClearButtonEnabled = true,
        };
        SetFont(field, "font", RegularFont, bold: false);
        field.AddThemeFontSizeOverride("font_size", 26);
        field.AddThemeColorOverride("font_color", Cream);
        field.AddThemeColorOverride("font_placeholder_color", Faint with { A = 0.5f });
        field.AddThemeColorOverride("caret_color", Gold);
        field.AddThemeStyleboxOverride("normal", FieldBox(Divider));
        field.AddThemeStyleboxOverride("focus", FieldBox(Gold));
        return field;
    }

    private static StyleBoxFlat FieldBox(Color edge)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.4f), BorderColor = edge };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(6);
        box.ContentMarginLeft = box.ContentMarginRight = 14;
        box.ContentMarginTop = box.ContentMarginBottom = 6;
        return box;
    }

    public static Choice? Paginator(string name, IReadOnlyList<string> options, int index, Action<int> changed)
    {
        if (WithScript(PaginatorScene, PaginatorScript) is not NPaginator paginator || PaginatorOptions == null || PaginatorIndex == null)
        {
            MainFile.Logger.Warn("game look: no paginator; the game's scene or script is missing");
            return null;
        }
        paginator.Name = name;
        paginator.CustomMinimumSize = new Vector2(324, 64);
        paginator.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return new Choice(paginator, options, index, changed);
    }

    public static NTickbox? Tickbox(string name, bool on, Action<bool> toggled)
    {
        if (WithScript(TickboxScene, TickboxScript) is not NTickbox tickbox)
        {
            MainFile.Logger.Warn("game look: no tickbox; the game's scene or script is missing");
            return null;
        }
        tickbox.Name = name;
        tickbox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // The ticked and unticked pictures are found in its _Ready.
        tickbox.Ready += () => tickbox.IsTicked = on;
        tickbox.Toggled += box => toggled(box.IsTicked);
        return tickbox;
    }

    // The custom run's tickbox row, with our text beside the box.
    public static NRunModifierTickbox? TickboxRow(string name, string text, bool on, Action<bool> toggled)
    {
        if (Load<PackedScene>(TickboxRowScene)?.Instantiate() is not NRunModifierTickbox row)
        {
            MainFile.Logger.Warn("game look: no tickbox row; the game's scene is missing");
            return null;
        }
        row.Name = name;
        // Its _Ready unticks it and, with no modifier, leaves its text alone.
        row.Ready += () =>
        {
            row.IsTicked = on;
            if (row.GetNodeOrNull<RichTextLabel>("%Description") is { } description)
            {
                description.AutowrapMode = TextServer.AutowrapMode.Off;
                description.FitContent = true;
                description.Text = text;
            }
        };
        row.Toggled += box => toggled(box.IsTicked);
        return row;
    }

    // The settings screen's Reset button drawn from its picture and label over Godot's own button, so a click, the
    // keyboard and a controller all press it. Red is for the buttons that act against a player (Block, Vote to Remove);
    // the ones that close a window keeping what was chosen (Done, Save) are drawn as the popups' Yes.
    public static Button Button(string name, string text, Action pressed, float width = 240, ButtonLook look = ButtonLook.Plain)
    {
        bool yes = look == ButtonLook.Yes;
        Texture2D? plate = Load<Texture2D>(yes ? YesTexture : ButtonTexture);
        float textWidth = Load<Font>(HeaderFont)?.GetStringSize(text, HorizontalAlignment.Left, -1, 28).X ?? 0;
        float natural = plate is { } picture && picture.GetHeight() > 0 ? 64f * picture.GetWidth() / picture.GetHeight() : width;
        width = LookLayout.ButtonWidth(width, textWidth, natural);
        var button = new Button
        {
            Name = name,
            CustomMinimumSize = new Vector2(width, 64),
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            Flat = true,
        };
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }
        ShaderMaterial? hsv = Load<Shader>(HsvShader) is { } shader ? new ShaderMaterial { Shader = shader } : null;
        (float h, float s, float v) = look switch { ButtonLook.Red => (RedHue, 1f, 1f), ButtonLook.Yes => (0.75f, 1.2f, 1.1f), _ => (1f, 1f, 1f) };
        hsv?.SetShaderParameter("h", h);
        hsv?.SetShaderParameter("s", s);
        hsv?.SetShaderParameter("v", v);
        if (yes && Load<Texture2D>(YesOutlineTexture) is { } edge)
        {
            var outline = new TextureRect
            {
                Texture = edge,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = new Color(1, 1, 1, 0.5f),
                SelfModulate = Colors.Black,
            };
            outline.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            button.AddChild(outline);
        }
        var image = new TextureRect
        {
            Texture = plate,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            // Always the button's full height, so buttons side by side are the same height whatever their widths.
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = hsv,
        };
        image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(image);
        MegaLabel label = Label(text, 28, yes ? Cream : ButtonText);
        SetFont(label, "font", HeaderFont, bold: true);
        label.Name = "Label";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_outline_color", look switch { ButtonLook.Red => RedOutline, ButtonLook.Yes => YesOutline, _ => ButtonOutline });
        label.AddThemeConstantOverride("outline_size", 12);
        label.AddThemeConstantOverride("shadow_offset_x", 4);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // Inside the plate (which spans the button), past its pointed ends; a text still too long shrinks (the game's labels' auto size).
        label.OffsetLeft = 22;
        label.OffsetRight = -22;
        label.MinFontSize = 16;
        label.MaxFontSize = 28;
        label.AutoSizeEnabled = true;
        button.AddChild(label);

        Tween? tween = null;
        void Grow(bool up)
        {
            if (!GodotObject.IsInstanceValid(button) || !button.IsInsideTree())
            {
                return;
            }
            tween?.Kill();
            button.PivotOffset = button.Size / 2;
            tween = button.CreateTween().SetParallel(true);
            tween.TweenProperty(button, "scale", Vector2.One * (up && !button.Disabled ? 1.05f : 1f), 0.05);
            if (hsv != null)
            {
                tween.TweenMethod(Callable.From<float>(now => hsv.SetShaderParameter("v", now)), (float)hsv.GetShaderParameter("v"), up && !button.Disabled ? v + 0.2f : v, 0.05);
            }
        }
        button.MouseEntered += () => Grow(true);
        button.MouseExited += () => Grow(button.HasFocus());
        button.FocusEntered += () => Grow(true);
        button.FocusExited += () => Grow(false);
        button.Pressed += pressed;
        return button;
    }

    public static void SetEnabled(Button button, bool enabled)
    {
        button.Disabled = !enabled;
        button.Modulate = enabled ? Colors.White : DisabledModulate;
    }

    public static void SetText(Button button, string text)
    {
        if (button.GetNodeOrNull<MegaLabel>("Label") is { } label && label.Text != text)
        {
            label.SetTextAutoSize(text);
        }
    }

    // The game's paginator with our options, its label shown and its index followed.
    internal sealed class Choice
    {
        private readonly NPaginator _paginator;
        private readonly Action<int> _changed;
        private List<string> _options = new();
        private int _shown;
        private SceneTree? _tree;

        public Choice(NPaginator paginator, IReadOnlyList<string> options, int index, Action<int> changed)
        {
            _paginator = paginator;
            _changed = changed;
            SetOptions(options, index);
            paginator.Ready += Show;
            paginator.TreeEntered += () =>
            {
                _tree = paginator.GetTree();
                _tree.ProcessFrame += Follow;
            };
            paginator.TreeExiting += () =>
            {
                if (_tree != null)
                {
                    _tree.ProcessFrame -= Follow;
                }
                _tree = null;
            };
        }

        public Control Node => _paginator;

        public int Index => PaginatorIndex?.GetValue(_paginator) is int index ? index : 0;

        // Replaces the texts and the index shown, without calling back.
        public void SetOptions(IReadOnlyList<string> options, int index)
        {
            _options = new List<string>(options);
            if (PaginatorOptions?.GetValue(_paginator) is List<string> list)
            {
                list.Clear();
                list.AddRange(_options);
            }
            _shown = Math.Clamp(index, 0, Math.Max(0, _options.Count - 1));
            PaginatorIndex?.SetValue(_paginator, _shown);
            if (_paginator.IsNodeReady())
            {
                Show();
            }
        }

        private void Show()
        {
            if (_paginator.GetNodeOrNull<MegaLabel>(PaginatorLabel) is { } label && _shown < _options.Count)
            {
                label.SetTextAutoSize(_options[_shown]);
            }
        }

        // The game's paginator calls no one when its index moves, so its index is read once a frame.
        private void Follow()
        {
            if (!GodotObject.IsInstanceValid(_paginator))
            {
                return;
            }
            int now = Index;
            if (now == _shown || now < 0 || now >= _options.Count)
            {
                return;
            }
            _shown = now;
            Show();
            _changed(now);
        }
    }
}

internal enum ButtonLook
{
    Plain,
    Red,
    Yes,
}
