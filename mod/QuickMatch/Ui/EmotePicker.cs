using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using QuickMatch.Emotes;

namespace QuickMatch.Ui;

// The emote picker under the emote button: the game's status icons in two rows, each beside the mark it is sent
// with: "I have" with a thumb up (dimmed for a status the hand or deck cannot apply) and "Who has?" with a question
// mark. Pointing at one shows the game's tooltip for the status; a click sends it. A click anywhere else closes
// the picker.
internal static class EmotePicker
{
    private const string NodeName = "QuickMatchEmotePicker";
    private const float IconSize = 52f;

    private static Control? _open;

    public static void Toggle(Control button)
    {
        if (_open != null)
        {
            Close();
            return;
        }
        if (!EmoteRoom.CanSend || button.GetTree() is not { } tree)
        {
            return;
        }
        var root = new Control { Name = NodeName, MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true })
            {
                Close();
            }
        };
        PanelContainer panel = GameLook.StripPanel();
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        panel.AddChild(rows);
        IReadOnlyList<EmoteStatus> have = EmoteRoom.Have();
        rows.AddChild(Row(panel, have, ask: false));
        rows.AddChild(Row(panel, have, ask: true));
        root.AddChild(panel);
        GameLook.AddStrip(tree, root);
        panel.GlobalPosition = button.GlobalPosition + new Vector2(0, button.Size.Y + 8);
        _open = root;
        root.TreeExited += () =>
        {
            if (_open == root)
            {
                _open = null;
            }
        };
    }

    public static void Close()
    {
        if (_open != null && GodotObject.IsInstanceValid(_open))
        {
            _open.QueueFree();
        }
        _open = null;
    }

    private static HBoxContainer Row(Control panel, IReadOnlyList<EmoteStatus> have, bool ask)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        foreach (EmoteStatus status in EmoteArt.Statuses)
        {
            bool enabled = ask || have.Contains(status);
            row.AddChild(Icon(panel, new Emote(status, ask), enabled));
        }
        return row;
    }

    private static Control Icon(Control panel, Emote emote, bool enabled)
    {
        EmotePair pair = EmoteArt.Pair(emote, IconSize);
        Control icon = pair.Row;
        icon.Name = $"QuickMatchEmote{emote.Status}{(emote.Ask ? "Ask" : "")}";
        icon.MouseFilter = Control.MouseFilterEnum.Stop;
        icon.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.3f);
        icon.Resized += () => icon.PivotOffset = icon.Size / 2;
        icon.MouseEntered += () =>
        {
            // The picker sits above the game's tooltip layer, so the tooltip goes beside the panel, not under it.
            NHoverTipSet.CreateAndShow(icon, EmoteArt.Tip(emote.Status))?.SetAlignment(panel, HoverTipAlignment.Right);
            if (enabled)
            {
                icon.Scale = Vector2.One * 1.08f;
            }
        };
        icon.MouseExited += () =>
        {
            NHoverTipSet.Remove(icon);
            icon.Scale = Vector2.One;
        };
        icon.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                icon.AcceptEvent();
                if (enabled)
                {
                    NHoverTipSet.Remove(icon);
                    EmoteRoom.Send(emote);
                    Close();
                }
            }
        };
        return icon;
    }
}
