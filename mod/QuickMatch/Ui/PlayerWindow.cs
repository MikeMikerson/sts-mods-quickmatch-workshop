using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using QuickMatch.Emotes;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

// Everything about one other player in one place, a window in the game's look. Mute and Voice volume (0 to
// 200%) show only in a match with voice on for us, Hide Emotes only in a quick-match run with them. Block asks
// first (BlockQuestion); Vote to Remove asks Start Vote or Cancel (VoteQuestion). Opened by a click on a player's
// name in a quick-match lobby, or by Player Options in the run's expanded player panel (PlayerEntry).
//
// Node names: QuickMatchPlayerMute, QuickMatchPlayerVolume, QuickMatchPlayerEmotes, QuickMatchPlayerBlock,
// QuickMatchPlayerVote and QuickMatchPlayerClose.
internal sealed class PlayerWindow
{
    internal const string NodeName = "QuickMatchPlayerWindow";

    private static PlayerWindow? _open;

    private readonly GameWindow _window;
    private readonly ulong _player;
    private readonly string _name;
    private readonly SceneTree _tree;
    private MegaLabel? _voiceState;
    private Button? _mute;
    private Button? _hideEmotes;

    public static void Open(ulong player)
    {
        if (player == 0 || player == BlockPlayers.LocalId() || Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return;
        }
        _open?.Close();
        _open = new PlayerWindow(tree, player);
    }

    // The window offers Mute and Voice volume only in a match with voice on for us.
    internal static bool VoiceHere => VoiceChat.InMatch && VoiceChat.Session != null;

    private PlayerWindow(SceneTree tree, ulong player)
    {
        _tree = tree;
        _player = player;
        _name = BlockPlayers.NameOf(player);
        long now = BlockStore.Now();
        bool inRun = BlockPlayers.InRunWith(player);
        bool blocked = BlockStore.List.Contains(player);
        long? again = blocked ? null : BlockStore.List.BlockableAgainAt(player, now);
        bool blockable = !blocked && again is null;
        bool canVote = BlockPlayers.CanVoteAgainst(player);
        bool guest = LobbyGuard.IsGuest(player);
        IReadOnlyList<PlayerControl> controls = LookLayout.PlayerControls(VoiceHere && !guest, inRun && EmoteRoom.Here && !guest, blockable, canVote);

        var state = new List<string> { Loc.Get(inRun ? "QUICKMATCH.PLAYER.in_run" : "QUICKMATCH.PLAYER.in_lobby") };
        if (blocked)
        {
            state.Add(Loc.Get("QUICKMATCH.PLAYER.blocked"));
        }
        else if (again is { } at)
        {
            state.Add(Loc.With("QUICKMATCH.PLAYER.cooling", "time", BlockPlayers.ClockTime(at)));
        }
        _window = new GameWindow(tree, NodeName, _name, string.Join(" ", state), 960, Close);

        Button? safe = null;
        foreach (PlayerControl control in controls)
        {
            switch (control)
            {
                case PlayerControl.Mute:
                    HBoxContainer voice = GameLook.Row(_window.Page, Loc.Get("QUICKMATCH.VOICE.title"), "QuickMatchPlayerVoiceRow", out _, out MegaLabel detail);
                    _voiceState = detail;
                    _mute = GameLook.Button("QuickMatchPlayerMute", Loc.Get("QUICKMATCH.VOICE.mute"), () => ToggleMute(_player), 200);
                    voice.AddChild(_mute);
                    break;
                case PlayerControl.Volume:
                    HBoxContainer volume = GameLook.Row(_window.Page, Loc.Get("QUICKMATCH.VOICE.volume"), "QuickMatchPlayerVolumeRow");
                    if (GameLook.Slider("QuickMatchPlayerVolume", LookLayout.Percent(VoiceStore.Mutes.VolumeOf(player)), LookLayout.Percent(VoiceMutes.MaxVolume), OnVolume) is { } slider)
                    {
                        volume.AddChild(slider);
                    }
                    break;
                case PlayerControl.Emotes:
                    HBoxContainer emotes = GameLook.Row(_window.Page, Loc.Get("QUICKMATCH.EMOTES.title"), "QuickMatchPlayerEmotesRow");
                    _hideEmotes = GameLook.Button("QuickMatchPlayerEmotes", EmotesText(_player), ToggleEmotes, 200);
                    emotes.AddChild(_hideEmotes);
                    break;
                case PlayerControl.Block:
                    _window.Buttons.AddChild(GameLook.Button("QuickMatchPlayerBlock", Loc.Get("QUICKMATCH.BLOCK.block"), Block, 200, look: ButtonLook.Red));
                    break;
                case PlayerControl.Vote:
                    _window.Buttons.AddChild(GameLook.Button("QuickMatchPlayerVote", Loc.Get("QUICKMATCH.VOTE.remove"), Vote, look: ButtonLook.Red));
                    break;
                default:
                    safe = GameLook.Button("QuickMatchPlayerClose", Loc.Get("QUICKMATCH.VOICE.close"), Close, 200);
                    _window.Buttons.AddChild(safe);
                    break;
            }
        }
        if (_voiceState == null)
        {
            // No Mute or volume: say why there is no voice here, so the window does not look as if something is missing.
            string note = guest ? "QUICKMATCH.PLAYER.guest" : "QUICKMATCH.PLAYER.no_voice";
            GameLook.Note(_window.Page, Loc.Get(note)).HorizontalAlignment = HorizontalAlignment.Center;
        }
        Refresh();
        _tree.ProcessFrame += Refresh;
        safe?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // Runs each frame: the player's voice state (speaking, mic on, muted by you) and Mute's words.
    private void Refresh()
    {
        if (_voiceState == null || _mute == null || VoiceChat.Session is not { } session)
        {
            return;
        }
        string status = VoiceText.StatusOf(session, _player);
        if (_voiceState.Text != status)
        {
            _voiceState.Text = status;
        }
        _voiceState.AddThemeColorOverride("font_color", session.IsSpeaking(_player) ? GameLook.Gold : GameLook.Faint);
        GameLook.SetText(_mute, MuteText(session, _player));
    }

    public static void ToggleMute(ulong player) => VoiceChat.SetMuted(player, !(VoiceChat.Session?.IsMuted(player) ?? VoiceStore.Mutes.IsMuted(player, VoiceChat.IsFriend(player))));

    private void ToggleEmotes()
    {
        EmoteRoom.Gate.SetHidden(_player, !EmoteRoom.Gate.IsHidden(_player));
        if (_hideEmotes != null)
        {
            GameLook.SetText(_hideEmotes, EmotesText(_player));
        }
    }

    private static string EmotesText(ulong player) =>
        Loc.Get(EmoteRoom.Gate.IsHidden(player) ? "QUICKMATCH.EMOTES.show" : "QUICKMATCH.EMOTES.hide");

    public static string MuteText(VoiceSession session, ulong player) => Loc.Get(session.IsMuted(player) ? "QUICKMATCH.VOICE.unmute" : "QUICKMATCH.VOICE.mute");

    private void OnVolume(int percent)
    {
        float value = (float)LookLayout.FromPercent(percent, VoiceMutes.MinVolume, VoiceMutes.MaxVolume);
        if (VoiceChat.Session is { } session)
        {
            session.SetVolume(_player, value);
        }
        else
        {
            VoiceStore.Mutes.SetVolume(_player, value);
        }
    }

    private void Block()
    {
        Close();
        BlockQuestion.Ask(_player, _name);
    }

    private void Vote()
    {
        ulong player = _player;
        Close();
        VoteQuestion.Ask(player, () => Open(player));
    }

    private void Close()
    {
        if (_open != this)
        {
            return;
        }
        _open = null;
        _tree.ProcessFrame -= Refresh;
        _window.Free();
    }
}
