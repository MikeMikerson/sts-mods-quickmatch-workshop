using System;
using Godot;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// Vote to Remove's confirmation, a window in the game's look. Opened from Vote to Remove in a player's window
// (PlayerWindow), where Cancel goes back to that window, or in the Blocked players tab.
//
// Node names: QuickMatchVoteStart and QuickMatchVoteCancel.
internal sealed class VoteQuestion
{
    internal const string NodeName = "QuickMatchVoteQuestion";

    private static VoteQuestion? _open;

    private readonly GameWindow _window;
    private readonly ulong _player;
    private readonly Action? _cancelled;

    // cancelled runs after Cancel.
    public static void Ask(ulong player, Action? cancelled = null)
    {
        if (player == 0 || player == BlockPlayers.LocalId() || !BlockPlayers.CanVoteAgainst(player)
            || Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        _open?.Close();
        _open = new VoteQuestion(tree, player, cancelled);
    }

    private VoteQuestion(SceneTree tree, ulong player, Action? cancelled)
    {
        _player = player;
        _cancelled = cancelled;
        string name = BlockPlayers.NameOf(player);
        bool inRun = BlockPlayers.InRunWith(player);
        _window = new GameWindow(tree, NodeName, Loc.With(inRun ? "QUICKMATCH.VOTE.ask_run" : "QUICKMATCH.VOTE.ask", "player", name), null, 960, Cancel);
        string hint = inRun ? "QUICKMATCH.VOTE.hint_run" : "QUICKMATCH.VOTE.hint";
        if (BlockPlayers.AsksAboutHost(player))
        {
            hint = RunRecord.Current?.HasGuests == true ? "QUICKMATCH.VOTE.hint_guest_run_host" : "QUICKMATCH.VOTE.hint_run_host";
        }
        _window.AddBody(Loc.With(hint, "player", name));
        Button? safe = null;
        foreach (VoteAnswer answer in LookLayout.VoteAnswers())
        {
            Button button = answer == VoteAnswer.Start
                ? GameLook.Button("QuickMatchVoteStart", Loc.Get("QUICKMATCH.VOTE.start"), Start, look: ButtonLook.Red)
                : GameLook.Button("QuickMatchVoteCancel", Loc.Get("QUICKMATCH.ASC.cancel"), Cancel);
            _window.Buttons.AddChild(button);
            safe = button;
        }
        safe?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // Also closes the Quick Match screen, so the vote shows.
    private void Start()
    {
        Close();
        LobbyGuard.StartVote(_player);
        QuickMatchMenu.CloseIfOpen();
    }

    private void Cancel()
    {
        if (_open != this)
        {
            return;
        }
        Close();
        if (_cancelled is { } cancelled)
        {
            // After this frame, so an Esc that cancelled does not also close the window it goes back to.
            Callable.From(cancelled).CallDeferred();
        }
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
