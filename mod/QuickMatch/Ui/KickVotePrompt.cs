using System;
using System.Globalization;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// The vote to remove a player, at the top of the screen. Shown to the players who can vote, never to the
// player voted on. It does not block the screen: the lobby stays usable around it. It is the settings
// screen's toast with the game's text and buttons, like the line strip (Notice) above which it sits, and it
// lives in the game's own screen tree just under its popups, so a game question draws over it.
internal static class KickVotePrompt
{
    private const string NodeName = "QuickMatchVotePrompt";
    // The strip's highest place, from the top of the game's 1080-high view.
    private const float Top = 130;

    private static Control? _layer;
    private static VBoxContainer? _body;
    private static MarginContainer? _top;
    private static string _shown = "";

    public static void Refresh()
    {
        VoteView? view = LobbyGuard.View();
        if (view is not { } v)
        {
            Hide();
            return;
        }
        string shown = $"{v.Target}|{v.Yes}|{v.Needed}|{(int)Math.Ceiling(v.SecondsLeft)}|{v.CanAnswer}|{v.Passed}|{BlockStore.List.Contains(v.Target)}";
        if (shown == _shown && _layer != null && GodotObject.IsInstanceValid(_layer))
        {
            return;
        }
        _shown = shown;
        Build(v);
    }

    // From the top of the screen, or 0 when none is up (a line below it starts there).
    public static float BottomEdge() =>
        _layer != null && GodotObject.IsInstanceValid(_layer) && _top != null && GodotObject.IsInstanceValid(_top)
            ? _top.OffsetTop + Math.Max(_top.Size.Y, _top.GetCombinedMinimumSize().Y)
            : 0;

    public static void Hide()
    {
        _shown = "";
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
        {
            _layer.QueueFree();
        }
        _layer = null;
        _body = null;
        _top = null;
    }

    private static void Build(VoteView view)
    {
        VBoxContainer? body = Body();
        if (body == null)
        {
            return;
        }
        foreach (Node child in body.GetChildren())
        {
            body.RemoveChild(child);
            child.QueueFree();
        }
        string name = BlockPlayers.NameOf(view.Target);
        // One line for what is said beside the buttons (the count while asking, the result after), so the strip stays low.
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 16);
        if (view.Passed is { } passed)
        {
            string said = !passed ? "QUICKMATCH.VOTE.failed" : view.InRun ? "QUICKMATCH.VOTE.passed_run" : "QUICKMATCH.VOTE.passed";
            line.AddChild(GameLook.Label(Loc.With(said, "player", name), 26, passed ? GameLook.Gold : GameLook.Cream, wrap: true));
            if (passed && !BlockStore.List.Contains(view.Target))
            {
                ulong target = view.Target;
                line.AddChild(GameLook.Button("QuickMatchVoteBlock", Loc.Get("QUICKMATCH.BLOCK.block"), () => BlockQuestion.Ask(target, name), 200, look: ButtonLook.Red));
            }
            line.AddChild(GameLook.Button("QuickMatchVoteClose", Loc.Get("QUICKMATCH.VOICE.close"), Dismiss, 200));
        }
        else
        {
            body.AddChild(GameLook.Label(Loc.With(view.InRun ? "QUICKMATCH.VOTE.ask_run" : "QUICKMATCH.VOTE.ask", "player", name), 28, GameLook.Cream, wrap: true));
            string count = Loc.With("QUICKMATCH.VOTE.count", "yes", view.Yes.ToString(CultureInfo.InvariantCulture), "needed", view.Needed.ToString(CultureInfo.InvariantCulture));
            string time = Loc.With("QUICKMATCH.VOTE.time", "seconds", ((int)Math.Ceiling(view.SecondsLeft)).ToString(CultureInfo.InvariantCulture));
            MegaLabel counted = GameLook.Label($"{count}   {time}", 22, GameLook.Faint);
            counted.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(counted);
            if (view.CanAnswer)
            {
                line.AddChild(GameLook.Button("QuickMatchVoteRemove", Loc.Get("QUICKMATCH.VOTE.yes"), () => Answer(true), 200));
                line.AddChild(GameLook.Button("QuickMatchVoteKeep", Loc.Get("QUICKMATCH.VOTE.no"), () => Answer(false), 200));
            }
        }
        body.AddChild(line);
    }

    private static void Answer(bool yes) => LobbyGuard.Answer(yes);

    private static void Dismiss()
    {
        LobbyGuard.DismissResult();
        Hide();
    }

    private static VBoxContainer? Body()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer) && _body != null)
        {
            return _body;
        }
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return null;
        }
        Vector2 view = tree.Root.GetVisibleRect().Size;
        float width = Math.Min(1000, view.X - 80);
        _layer = new Control { Name = NodeName, MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var top = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        top.OffsetLeft = -width / 2;
        top.OffsetRight = width / 2;
        top.OffsetTop = LookLayout.StripTop(Top, 12, QuickMatchHub.TitleBottom());
        _layer.AddChild(top);
        _top = top;
        PanelContainer panel = GameLook.StripPanel();
        panel.CustomMinimumSize = new Vector2(width, 0);
        top.AddChild(panel);
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(_body);
        GameLook.AddStrip(tree, _layer);
        return _body;
    }
}
