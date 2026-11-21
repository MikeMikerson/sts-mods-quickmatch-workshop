using Godot;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

// The ways into a player's window (PlayerWindow): a click on a player's name in a quick-match lobby, and
// Player Options in the panel the game opens when a teammate's row is clicked in a run
// (NMultiplayerPlayerExpandedState). Called from the voice patches of the same methods
// (Patches.VoicePatches).
internal static class PlayerEntry
{
    public static void AttachToLobbySlot(NRemoteLobbyPlayer slot)
    {
        if (slot.GetNodeOrNull<Control>("%NameplateLabel") is not { } name)
        {
            return;
        }
        name.MouseFilter = Control.MouseFilterEnum.Stop;
        name.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        name.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }
                && GodotObject.IsInstanceValid(slot) && Matchmaker.Lobby != null && slot.PlayerId != RunRecord.LocalPlayerId())
            {
                PlayerWindow.Open(slot.PlayerId);
            }
        };
    }

    public static void AttachToPlayerPanel(Control panel, ulong player) => PlayerStrip.Attach(panel, player);
}

// The Quick Match row in a run's expanded player panel. Shown in a quick-match run and in any match with
// voice on; hidden on our own panel.
internal sealed class PlayerStrip
{
    private const string NodeName = "QuickMatchPlayerStrip";

    private readonly ulong _player;
    private readonly HBoxContainer _row;
    private readonly Label _status;
    private readonly Button _mute;
    private SceneTree? _tree;

    private PlayerStrip(ulong player)
    {
        _player = player;
        _row = new HBoxContainer { Name = NodeName, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", 18);
        _row.AddChild(VoiceWidgets.Text(Loc.Get("QUICKMATCH.MENU.title"), 26, VoiceGlyphs.Live, bold: true));
        _status = VoiceWidgets.Text("", 24, VoiceGlyphs.Ink);
        _status.CustomMinimumSize = new Vector2(240, 0);
        _row.AddChild(_status);
        // The game's plate, as the player window's own buttons have.
        _row.AddChild(GameLook.Button("QuickMatchPlayerOptions", Loc.Get("QUICKMATCH.PLAYER.options"), () => PlayerWindow.Open(_player), 200));
        _mute = GameLook.Button("QuickMatchPlayerStripMute", Loc.Get("QUICKMATCH.VOICE.mute"), () => PlayerWindow.ToggleMute(_player), 200);
        _mute.Visible = false;
        _row.AddChild(_mute);
        _row.TreeEntered += OnTreeEntered;
        _row.TreeExiting += OnTreeExiting;
    }

    public static void Attach(Control panel, ulong player)
    {
        var container = panel.GetNodeOrNull<VBoxContainer>("ScreenContents/Container");
        if (container == null || container.GetNodeOrNull(NodeName) != null)
        {
            return;
        }
        var strip = new PlayerStrip(player);
        container.AddChild(strip._row);
        container.MoveChild(strip._row, 1);
    }

    private void OnTreeEntered()
    {
        _tree = _row.GetTree();
        _tree.ProcessFrame += OnProcessFrame;
    }

    private void OnTreeExiting()
    {
        if (_tree != null)
        {
            _tree.ProcessFrame -= OnProcessFrame;
            _tree = null;
        }
    }

    private void OnProcessFrame()
    {
        bool voice = PlayerWindow.VoiceHere;
        bool show = (voice || Regroup.InQuickMatchRun) && _player != 0 && _player != BlockPlayers.LocalId();
        if (_row.Visible != show)
        {
            _row.Visible = show;
        }
        if (!show)
        {
            return;
        }
        VoiceSession? session = voice ? VoiceChat.Session : null;
        string status = session != null ? VoiceText.StatusOf(session, _player)
            : BlockStore.List.Contains(_player) ? Loc.Get("QUICKMATCH.BLOCK.blocked") : "";
        if (_status.Text != status)
        {
            _status.Text = status;
        }
        if (_mute.Visible != (session != null))
        {
            _mute.Visible = session != null;
        }
        if (session != null)
        {
            GameLook.SetText(_mute, PlayerWindow.MuteText(session, _player));
        }
    }
}
