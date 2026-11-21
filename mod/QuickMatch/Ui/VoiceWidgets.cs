using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace QuickMatch.Ui;

// In the game's fonts: Kreon (or the language's substitute, as the game's own labels get it).
internal static class VoiceWidgets
{
    private const string RegularFontPath = "res://themes/kreon_regular_shared.tres";
    private const string BoldFontPath = "res://themes/kreon_bold_shared.tres";

    private static Font? _regular;
    private static Font? _bold;

    // size is in the game's 1080-high view.
    public static Label Text(string text, int size, Color color, bool bold = false)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        SetFont(label, bold);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static void SetFont(Control control, bool bold)
    {
        Font? font = bold ? _bold ??= ResourceLoader.Load<Font>(BoldFontPath) : _regular ??= ResourceLoader.Load<Font>(RegularFontPath);
        if (font != null)
        {
            control.AddThemeFontOverride("font", font);
        }
        control.ApplyLocaleFontSubstitution(bold ? FontType.Bold : FontType.Regular, "font");
    }
}
