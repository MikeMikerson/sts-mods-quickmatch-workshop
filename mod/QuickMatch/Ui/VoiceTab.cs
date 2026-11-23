using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using QuickMatch.Localization;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

// The Quick Match screen's Voice tab, built from the game's pieces (GameLook). The mic button turns voice
// activation on and off, and whether a match has voice is Match Options' Voice chat. A player's Mute and
// volume are in their window (PlayerWindow). Outside a match its settings can be changed for the next one,
// and the mic waits for a match.
//
// Node names: QuickMatchMic, QuickMatchHearing, QuickMatchVolume, QuickMatchMore, QuickMatchSensitivity and
// QuickMatchStrangers.
internal sealed class VoiceTab
{
    private readonly MegaLabel _note;
    private readonly NTickbox? _mic;
    private readonly NTickbox? _hearing;
    private readonly Button _more;
    private readonly MegaLabel _moreSummary;
    private readonly VBoxContainer _moreRows;
    private readonly Control? _meter;
    private readonly VBoxContainer _players;
    private readonly List<Action> _refresh = new();
    private bool _micEnabled = true;
    private string _playersShown = "";

    public VoiceTab(VBoxContainer page)
    {
        VoiceSettings settings = VoiceStore.Settings;
        _note = GameLook.Note(page, "", GameLook.Cream, 24);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        page.AddChild(rows);
        HBoxContainer mic = GameLook.Row(rows, Loc.Get("QUICKMATCH.VOICE.mic"), "QuickMatchMicRow");
        _mic = GameLook.Tickbox("QuickMatchMic", false, on => VoiceChat.Session?.SetMic(on));
        if (_mic != null)
        {
            mic.AddChild(_mic);
        }
        _hearing = GameLook.Tickbox("QuickMatchHearing", settings.Hearing, on => Change(() => settings.Hearing = on));
        if (_hearing != null)
        {
            GameLook.Row(rows, Loc.Get("QUICKMATCH.VOICE.hearing"), "QuickMatchHearingRow").AddChild(_hearing);
        }
        if (GameLook.Slider("QuickMatchVolume", LookLayout.Percent(settings.Volume), 100, percent => Change(() => settings.Volume = (float)LookLayout.FromPercent(percent, 0, 1))) is { } volume)
        {
            GameLook.Row(rows, Loc.Get("QUICKMATCH.VOICE.volume"), "QuickMatchVolumeRow").AddChild(volume);
        }
        GameLook.Note(rows, Loc.Get("QUICKMATCH.VOICE.volume_hint"));

        // More: a line of its own under the three rows, saying what it holds until it is open.
        var moreLine = new MarginContainer { CustomMinimumSize = new Vector2(0, 72), MouseFilter = Control.MouseFilterEnum.Ignore };
        moreLine.AddThemeConstantOverride("margin_left", 12);
        moreLine.AddThemeConstantOverride("margin_right", 12);
        moreLine.AddThemeConstantOverride("margin_top", 8);
        var moreBox = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        moreBox.AddThemeConstantOverride("separation", 20);
        _more = GameLook.Button("QuickMatchMore", Loc.Get("QUICKMATCH.VOICE.more"), ToggleMore, 200);
        moreBox.AddChild(_more);
        _moreSummary = GameLook.Label(Loc.Get("QUICKMATCH.VOICE.more_hint"), 22, GameLook.Faint);
        _moreSummary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _moreSummary.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        moreBox.AddChild(_moreSummary);
        moreLine.AddChild(moreBox);
        page.AddChild(moreLine);

        _moreRows = new VBoxContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _moreRows.AddThemeConstantOverride("separation", 6);
        page.AddChild(_moreRows);
        foreach (VoiceRow row in LookLayout.MoreRows())
        {
            switch (row)
            {
                case VoiceRow.Sensitivity:
                    HBoxContainer sensitivity = GameLook.Row(_moreRows, Loc.Get("QUICKMATCH.VOICE.sensitivity"), "QuickMatchSensitivityRow");
                    _meter = Meter();
                    sensitivity.AddChild(_meter);
                    if (GameLook.Slider("QuickMatchSensitivity", LookLayout.Percent(settings.Sensitivity), 100, percent => Change(() => settings.Sensitivity = LookLayout.FromPercent(percent, 0, 1))) is { } slider)
                    {
                        sensitivity.AddChild(slider);
                    }
                    GameLook.Note(_moreRows, Loc.Get("QUICKMATCH.VOICE.sensitivity_hint"));
                    break;
                case VoiceRow.Strangers:
                    if (GameLook.Tickbox("QuickMatchStrangers", VoiceStore.Mutes.StrangersStartMuted, on => Change(() => VoiceStore.Mutes.StrangersStartMuted = on)) is { } strangers)
                    {
                        GameLook.Row(_moreRows, Loc.Get("QUICKMATCH.VOICE.strangers"), "QuickMatchStrangersRow").AddChild(strangers);
                    }
                    break;
            }
        }

        _players = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _players.AddThemeConstantOverride("separation", 6);
        page.AddChild(_players);
        Refresh();
    }

    private void ToggleMore()
    {
        _moreRows.Visible = !_moreRows.Visible;
        _moreSummary.Visible = !_moreRows.Visible;
        GameLook.SetText(_more, Loc.Get(_moreRows.Visible ? "QUICKMATCH.VOICE.less" : "QUICKMATCH.VOICE.more"));
    }

    // Runs each frame, following the voice session.
    public void Refresh()
    {
        VoiceSession? session = VoiceChat.Session;
        string? note = Note(session);
        _note.Visible = note != null;
        if (note != null && _note.Text != note)
        {
            _note.Text = note;
        }

        bool on = session?.LocalState is LocalVoiceState.Live or LocalVoiceState.Speaking or LocalVoiceState.Asking;
        bool enabled = VoiceChat.InMatch && session != null && VoiceText.WhyNoMic(session) == null;
        if (_mic != null && _mic.IsNodeReady())
        {
            if (_mic.IsTicked != on)
            {
                _mic.IsTicked = on;
            }
            if (_micEnabled != enabled)
            {
                _micEnabled = enabled;
                _mic.SetEnabled(enabled);
            }
        }
        _meter?.QueueRedraw();
        RebuildPlayers(session);
        foreach (Action refresh in _refresh.ToList())
        {
            refresh();
        }
    }

    private static string? Note(VoiceSession? session)
    {
        if (!VoiceChat.InMatch)
        {
            return Loc.Get(session != null && !MatchVoice.On ? "QUICKMATCH.VOICE.off_this_match" : "QUICKMATCH.VOICE.outside_match");
        }
        return session?.LocalState == LocalVoiceState.Off ? Loc.Get("QUICKMATCH.VOICE.off_this_match") : VoiceText.WhyNoMic(session);
    }

    private static void Change(Action change)
    {
        change();
        VoiceStore.Save();
    }

    // Gold while the voice gate would send the mic level.
    private static Control Meter()
    {
        var meter = new Control
        {
            Name = "QuickMatchMeter",
            CustomMinimumSize = new Vector2(160, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        meter.Draw += () =>
        {
            VoiceSession? session = VoiceChat.Session;
            float level = (float)Math.Clamp(session?.MicLevel ?? 0, 0, 1);
            bool heard = session?.LocalState == LocalVoiceState.Speaking;
            meter.DrawRect(new Rect2(Vector2.Zero, meter.Size), new Color(0, 0, 0, 0.4f));
            meter.DrawRect(new Rect2(Vector2.Zero, new Vector2(meter.Size.X * level, meter.Size.Y)), heard ? GameLook.Gold : GameLook.Faint);
        };
        return meter;
    }

    // Rebuilt only when who is there changes.
    private void RebuildPlayers(VoiceSession? session)
    {
        bool inMatch = VoiceChat.InMatch && session != null;
        List<(ulong Player, TeammateState State)> teammates = inMatch ? VoiceChat.Teammates.ToList() : [];
        List<ulong> remembered = VoiceStore.Mutes.Muted
            .Where(id => id != VoiceChat.LocalId && !teammates.Contains((id, TeammateState.OnVoice))).ToList();
        string shown = $"{inMatch}|{string.Join(",", teammates)}|{string.Join(",", remembered)}";
        if (shown == _playersShown)
        {
            return;
        }
        _playersShown = shown;
        foreach (Node child in _players.GetChildren())
        {
            _players.RemoveChild(child);
            child.QueueFree();
        }
        _refresh.Clear();
        if (inMatch)
        {
            GameLook.Heading(_players, Loc.Get("QUICKMATCH.VOICE.players"));
            GameLook.Note(_players, Loc.Get(teammates.Count == 0 ? "QUICKMATCH.VOICE.no_players" : "QUICKMATCH.VOICE.players_hint"), GameLook.Cream, 24);
            int row = 0;
            foreach ((ulong player, TeammateState state) in teammates)
            {
                _refresh.Add(new TeammateRow(_players, player, state, row++).Refresh);
            }
            if (teammates.Any(t => t.State == TeammateState.OnVoice))
            {
                GameLook.Row(_players, "", "QuickMatchMuteEveryoneRow")
                    .AddChild(GameLook.Button("QuickMatchMuteEveryone", Loc.Get("QUICKMATCH.VOICE.mute_everyone"), VoiceChat.MuteEveryone));
            }
        }
        if (remembered.Count > 0)
        {
            GameLook.Heading(_players, Loc.Get("QUICKMATCH.VOICE.muted_list"));
            int n = 0;
            foreach (ulong player in remembered)
            {
                _refresh.Add(new MutedRow(_players, player, n++).Refresh);
            }
            GameLook.Note(_players, Loc.Get("QUICKMATCH.VOICE.private_note"));
        }
    }

    private sealed class TeammateRow
    {
        private readonly ulong _player;
        private readonly TeammateState _state;
        private readonly MegaLabel _status;

        public TeammateRow(VBoxContainer page, ulong player, TeammateState state, int n)
        {
            _player = player;
            _state = state;
            GameLook.Row(page, VoiceChat.NameOf(player), $"QuickMatchVoicePlayer{n}", out _, out _status);
            Refresh();
        }

        public void Refresh()
        {
            string status = _state switch
            {
                TeammateState.OnVoice when VoiceChat.Session is { } session => VoiceText.StatusOf(session, _player),
                TeammateState.Left => Loc.Get("QUICKMATCH.VOICE.status_left"),
                _ => Loc.Get("QUICKMATCH.VOICE.status_playing_no_voice"),
            };
            if (_status.Text != status)
            {
                _status.Text = status;
            }
        }
    }

    private sealed class MutedRow
    {
        private readonly ulong _player;
        private readonly Button _mute;

        public MutedRow(VBoxContainer page, ulong player, int n)
        {
            _player = player;
            HBoxContainer controls = GameLook.Row(page, VoiceChat.NameOf(player), $"QuickMatchVoicePlayerMuted{n}");
            _mute = GameLook.Button($"QuickMatchMute{n}", Loc.Get("QUICKMATCH.VOICE.unmute"), () => VoiceChat.SetMuted(_player, !IsMuted()), 200);
            controls.AddChild(_mute);
        }

        public void Refresh() => GameLook.SetText(_mute, Loc.Get(IsMuted() ? "QUICKMATCH.VOICE.unmute" : "QUICKMATCH.VOICE.mute"));

        private bool IsMuted() => VoiceStore.Mutes.IsMuted(_player, VoiceChat.IsFriend(_player));
    }
}
