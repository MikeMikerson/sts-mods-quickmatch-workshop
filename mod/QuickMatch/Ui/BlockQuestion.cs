using Godot;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// The block question, a window of its own in the game's look. Its answers are Block, or in a quick-match run
// with the player Block, Stay Until the End and Block and Leave Now; then Cancel. Vote to Remove is its own
// button in the player's window (PlayerWindow, VoteQuestion). It opens over whatever is showing. Works
// without voice chat: nothing here needs a voice session.
//
// Node names: QuickMatchBlockStay, QuickMatchBlockLeave, QuickMatchBlockYes and QuickMatchBlockCancel.
internal sealed class BlockQuestion
{
    internal const string NodeName = "QuickMatchBlockQuestion";

    private static BlockQuestion? _open;

    private readonly GameWindow _window;
    private readonly ulong _player;
    private readonly string _name;

    public static void Ask(ulong player, string? name = null)
    {
        if (player == 0 || player == BlockPlayers.LocalId() || Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        _open?.Close();
        _open = new BlockQuestion(tree, player, name ?? BlockPlayers.NameOf(player));
    }

    public static void CloseIfOpen() => _open?.Close();

    private BlockQuestion(SceneTree tree, ulong player, string name)
    {
        _player = player;
        _name = name;
        long now = BlockStore.Now();
        bool inRun = BlockPlayers.InRunWith(player);
        bool blocked = BlockStore.List.Contains(player);
        long? again = blocked ? null : BlockStore.List.BlockableAgainAt(player, now);
        bool blockable = !blocked && again is null;

        string title = blockable ? Loc.With("QUICKMATCH.BLOCK.ask_title", "player", name) : name;
        _window = new GameWindow(tree, NodeName, title, null, 1040, Close);
        string body = blocked ? Loc.With("QUICKMATCH.BLOCK.already", "player", name)
            : again is { } at ? Loc.With("QUICKMATCH.BLOCK.cooling", "player", name, "time", BlockPlayers.ClockTime(at))
            : Loc.Get(inRun ? "QUICKMATCH.BLOCK.ask_body_run" : "QUICKMATCH.BLOCK.ask_body");
        _window.AddBody(body);

        Button? safe = null;
        foreach (BlockAnswer answer in LookLayout.BlockAnswers(blockable, inRun))
        {
            Button button = answer switch
            {
                BlockAnswer.Stay => GameLook.Button("QuickMatchBlockStay", Loc.Get("QUICKMATCH.BLOCK.stay"), () => Block(leave: false), look: ButtonLook.Red),
                BlockAnswer.Leave => GameLook.Button("QuickMatchBlockLeave", Loc.Get("QUICKMATCH.BLOCK.leave"), () => Block(leave: true), look: ButtonLook.Red),
                BlockAnswer.Block => GameLook.Button("QuickMatchBlockYes", Loc.Get("QUICKMATCH.BLOCK.block"), () => Block(leave: false), look: ButtonLook.Red),
                BlockAnswer.Close => GameLook.Button("QuickMatchBlockCancel", Loc.Get("QUICKMATCH.VOICE.close"), Close),
                _ => GameLook.Button("QuickMatchBlockCancel", Loc.Get("QUICKMATCH.ASC.cancel"), Close),
            };
            _window.Buttons.AddChild(button);
            safe = button;
        }
        safe?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Block(bool leave)
    {
        bool inRun = BlockPlayers.InRunWith(_player);
        Close();
        if (BlockStore.Block(_player, _name) != BlockResult.Added || !inRun)
        {
            return;
        }
        if (leave)
        {
            QuickMatchMenu.CloseIfOpen();
            Regroup.LeaveRunAfterBlocking();
        }
        else
        {
            RunRecord.StayWith(_player);
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
