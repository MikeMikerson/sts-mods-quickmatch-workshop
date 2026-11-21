using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace QuickMatch.Ui;

// Sets the title and description of one of the hub's cards (the game's submenu card).
internal static class PickerCards
{
    public static void Set(NSubmenuButton button, string? title = null, string? description = null)
    {
        button.RefreshLabels();
        // Godot's own property access for the card's private labels.
        if (title != null && button.Get("_title").As<GodotObject>() is MegaLabel titleLabel)
        {
            titleLabel.SetTextAutoSize(title);
        }
        if (button.Get("_description").As<GodotObject>() is not MegaRichTextLabel label)
        {
            return;
        }
        if (description != null)
        {
            label.Text = description;
        }
    }
}
