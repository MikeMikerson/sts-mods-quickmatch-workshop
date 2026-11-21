using System;
using System.Threading;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// One line at the top of the screen that blocks nothing: what would otherwise happen without a word (a player
// dropped or came back, the group moved, a vote or a block sent us away), and the offer made to a host nobody
// has joined, which has one button and stays until it is taken away. A new line replaces the one shown. It is
// the settings screen's toast (GameLook.StripPanel) with the game's text and buttons, and it lives in the
// game's own screen tree just under its popups, so a game question draws over it; it stays while the screen
// under it changes. Game thread only.
internal static class Notice
{
    private const string NodeName = "QuickMatchNotice";
    public const double Seconds = 7;
    // The line's highest place, from the top of the game's 1080-high view, and its gap below what it must not
    // cover.
    private const float Top = 130;
    private const float Gap = 12;

    private static Control? _strip;
    private static int _shown;
    private static int _last;

    public static void Show(string text, double seconds = Seconds)
    {
        int id = Build(text, null, null);
        if (id != 0)
        {
            _ = HideLaterAsync(id, seconds);
        }
    }

    // The line stays until Hide(int) with the id returned (0: not shown).
    public static int ShowWithButton(string text, string button, Action pressed) => Build(text, button, pressed);

    // Only if it is still the one shown.
    public static void Hide(int id)
    {
        if (id != 0 && id == _shown)
        {
            Hide();
        }
    }

    public static void Hide()
    {
        _shown = 0;
        if (_strip != null && GodotObject.IsInstanceValid(_strip))
        {
            // Freed at the end of the frame: give the name up now, so a line that replaces this one keeps it.
            _strip.Name = NodeName + "Gone";
            _strip.QueueFree();
        }
        _strip = null;
    }

    private static async System.Threading.Tasks.Task HideLaterAsync(int id, double seconds)
    {
        try
        {
            await GodotDelay.Seconds(seconds, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Hide(id);
    }

    private static int Build(string text, string? button, Action? pressed)
    {
        Hide();
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return 0;
        }
        Vector2 view = tree.Root.GetVisibleRect().Size;
        float width = Math.Min(1000, view.X - 80);
        var strip = new Control { Name = NodeName, MouseFilter = Control.MouseFilterEnum.Ignore };
        strip.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var top = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        top.OffsetLeft = -width / 2;
        top.OffsetRight = width / 2;
        // Below the Quick Match screen's title, the vote strip and the lobby's status area, whichever is lowest.
        top.OffsetTop = LookLayout.StripTop(Top, Gap, QuickMatchHub.TitleBottom(), KickVotePrompt.BottomEdge(), LobbyStatusPanel.BottomEdge());
        strip.AddChild(top);
        PanelContainer panel = GameLook.StripPanel();
        panel.CustomMinimumSize = new Vector2(width, 0);
        // The line itself takes no clicks; only its button does.
        panel.MouseFilter = button == null ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Pass;
        top.AddChild(panel);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 20);
        panel.AddChild(row);
        MegaLabel label = GameLook.Label(text, 26, GameLook.Cream, wrap: true);
        if (button == null)
        {
            label.HorizontalAlignment = HorizontalAlignment.Center;
        }
        row.AddChild(label);
        int id = _last = _last == int.MaxValue ? 1 : _last + 1;
        _shown = id;
        if (button != null && pressed != null)
        {
            row.AddChild(GameLook.Button("QuickMatchNoticeButton", button, pressed, 260));
        }
        GameLook.AddStrip(tree, strip);
        _strip = strip;
        return id;
    }
}
