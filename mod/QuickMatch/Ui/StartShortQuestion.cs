using System;
using Godot;
using QuickMatch.Localization;

namespace QuickMatch.Ui;

// Asks a player in a lobby short of full whether to start with those here, in the game's look. Closing the window
// answers no; closing it from code (the question ended) answers nothing.
//
// Node names: QuickMatchShortYes and QuickMatchShortNo.
internal sealed class StartShortQuestion
{
    internal const string NodeName = "QuickMatchStartShortQuestion";

    private static StartShortQuestion? _open;

    private readonly GameWindow _window;
    private readonly Action<bool> _answer;

    public static void Ask(int players, Action<bool> answer)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        _open?.Close();
        _open = new StartShortQuestion(tree, players, answer);
    }

    public static void CloseIfOpen() => _open?.Close();

    private StartShortQuestion(SceneTree tree, int players, Action<bool> answer)
    {
        _answer = answer;
        _window = new GameWindow(tree, NodeName, Loc.With("QUICKMATCH.SHORT.ask", "players", players.ToString()), null, 960, () => Choose(false));
        _window.AddBody(Loc.Get("QUICKMATCH.SHORT.hint"));
        Button? safe = null;
        foreach (VoteAnswer place in LookLayout.VoteAnswers())
        {
            Button button = place == VoteAnswer.Start
                ? GameLook.Button("QuickMatchShortYes", Loc.Get("QUICKMATCH.SHORT.yes"), () => Choose(true))
                : GameLook.Button("QuickMatchShortNo", Loc.Get("QUICKMATCH.SHORT.no"), () => Choose(false));
            _window.Buttons.AddChild(button);
            safe = button;
        }
        safe?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Choose(bool yes)
    {
        if (_open != this)
        {
            return;
        }
        Close();
        _answer(yes);
    }

    private void Close()
    {
        if (_open != this)
        {
            return;
        }
        _open = null;
        _window.Free();
    }
}
