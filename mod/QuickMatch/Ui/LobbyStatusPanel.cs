using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Matchmaking;
using QuickMatch.Patches;
using QuickMatch.Transport;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

// The one status area of character select in a quick-match lobby, at the top of the screen. It stays for the
// whole lobby and takes the place of the screen's own "Waiting for other players" panel after Embark, which
// is hidden in our lobby (Patches.WaitingPanelPatches). A copy of that panel (ReadyAndWaitingPanel), wider
// and taller. A timer on the panel moves the clock each second. Copy Invite Link sits to its right.
internal static class LobbyStatusPanel
{
    private const string NodeName = "QuickMatchStatus";
    private const string LinkButtonName = "QuickMatchCopyLink";
    private const float LinkButtonWidth = 300;
    private const ulong CopiedMsec = 3000;
    private const string LabelName = "WaitingForPlayers";
    private const float Width = 1100;
    private const float Height = 176;
    private const float Top = 30;
    // The Blitz and Dread screens draw their own title across the top centre: there the panel sits below it, and
    // shorter, to stay clear of the character's details.
    private const float BelowTitle = 166;
    private const float HeightBelowTitle = 134;

    private static ulong _copiedAt;

    private static float TitleClearance(Control screen) => screen is NCharacterSelectScreen ? 0 : BelowTitle;

    public static void Refresh(NSubmenu screen)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }
        StartRunLobby? lobby = Matchmaker.Lobby;
        bool show = lobby != null && StartScreens.LobbyOf(screen) == lobby;
        Control? panel = show ? GetOrCreate(screen) : screen.GetNodeOrNull<Control>(NodeName);
        if (panel == null)
        {
            return;
        }
        panel.Visible = show;
        if (screen.GetNodeOrNull<Button>(LinkButtonName) is { } link)
        {
            link.Visible = show && Features.IsOn(GameFeature.LinkJoin);
            bool copied = _copiedAt != 0 && Time.GetTicksMsec() - _copiedAt < CopiedMsec;
            GameLook.SetText(link, Loc.Get(copied ? "QUICKMATCH.LINK.copied" : "QUICKMATCH.LINK.copy"));
        }
        if (!show)
        {
            return;
        }
        // The game's own panel says the same as our second line after Embark; one area says it in our lobby.
        if (screen.GetNodeOrNull<Control>("ReadyAndWaitingPanel") is { } own)
        {
            own.Visible = false;
        }
        int here = lobby!.Players.Count;
        int size = Matchmaker.StartSize;
        bool ready = lobby.Players.Any(p => p.id == lobby.NetService.NetId && p.isReady);
        panel.GetNode<MegaRichTextLabel>(LabelName).Text = Text(here, size, Matchmaker.AnySize, ready, lobby.Ascension, Matchmaker.CountdownLeft);
    }

    private static string Text(int here, int size, bool anySize, bool ready, int ascension, double? countdown)
    {
        (string first, string second) = MatchPolicy.StatusKeys(here, size, ready, anySize, Matchmaker.SearchSeconds);
        string line1 = first is "QUICKMATCH.STATUS.looking" or "QUICKMATCH.STATUS.looking_any"
            ? Loc.With(first, "count", here.ToString(), "size", size.ToString()) + " " + Clock(Matchmaker.SearchSeconds)
            : Loc.Get(first);
        string line2 = countdown is { } left
            ? Loc.With(MatchPolicy.CountdownKey(ready), "time", Clock(left))
            : second.EndsWith("_more", System.StringComparison.Ordinal)
            ? Loc.WithNumber(second, "count", size - here)
            : Loc.WithNumber(second, "size", size);
        string voice = Loc.Get(MatchVoice.On ? "QUICKMATCH.SUMMARY.voice_on" : "QUICKMATCH.SUMMARY.voice_off");
        string line3 = (MatchMode.On != RunMode.Standard ? QuickMatchHub.ModeText(MatchMode.On) + " · " : "")
            + (MatchChoice.MaxAscension > 0 || ascension > 0
            ? Loc.WithNumber("QUICKMATCH.SUMMARY.asc_one", "level", ascension) + " · "
            : "") + voice;
        return $"{line1}\n[font_size=24][color=#e8dcc0]{line2}\n{line3}[/color][/font_size]";
    }

    private static string Clock(double seconds)
    {
        int s = (int)System.Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    public static void Hide(NSubmenu screen)
    {
        Control? panel = screen.GetNodeOrNull<Control>(NodeName);
        if (panel != null)
        {
            panel.Visible = false;
        }
        if (screen.GetNodeOrNull<Control>(LinkButtonName) is { } link)
        {
            link.Visible = false;
        }
    }

    private static void CopyLink(NSubmenu screen)
    {
        ulong lobby = Matchmaker.SteamLobbyId;
        if (lobby == 0)
        {
            return;
        }
        DisplayServer.ClipboardSet(LinkPolicy.InviteLink(lobby, LobbyBackend.Current.Owner(lobby)));
        _copiedAt = Time.GetTicksMsec();
        Refresh(screen);
    }

    // 0 while the panel is hidden, so the line strip can sit below it.
    public static float BottomEdge()
    {
        if (Matchmaker.Screen is not { } screen || !GodotObject.IsInstanceValid(screen)
            || screen.GetNodeOrNull<Control>(NodeName) is not { } panel || !panel.IsVisibleInTree())
        {
            return 0;
        }
        return panel.OffsetBottom;
    }

    private static Control GetOrCreate(NSubmenu screen)
    {
        Control? existing = screen.GetNodeOrNull<Control>(NodeName);
        if (existing != null)
        {
            return existing;
        }
        var source = screen.GetNode<Control>("ReadyAndWaitingPanel");
        var panel = (Control)source.Duplicate();
        panel.Name = NodeName;
        // The copy must not claim the screen's %WaitingForPlayers unique name.
        foreach (Node child in panel.GetChildren())
        {
            child.UniqueNameInOwner = false;
        }
        // Anchored to the top centre instead of the bottom, wider and taller for three lines.
        panel.AnchorTop = 0;
        panel.AnchorBottom = 0;
        panel.OffsetLeft = -Width / 2;
        panel.OffsetRight = Width / 2;
        float clearance = TitleClearance(screen);
        panel.OffsetTop = Top + clearance;
        panel.OffsetBottom = panel.OffsetTop + (clearance > 0 ? HeightBelowTitle : Height);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.Visible = false;
        var clock = new Timer { Name = "QuickMatchStatusClock", WaitTime = 1, Autostart = true };
        clock.Timeout += () => Refresh(screen);
        panel.AddChild(clock);
        screen.AddChild(panel);

        if (!QuickMatchHostService.HasSockets)
        {
            return panel;
        }

        Button link = GameLook.Button(LinkButtonName, Loc.Get("QUICKMATCH.LINK.copy"), () => CopyLink(screen), LinkButtonWidth);
        link.AnchorLeft = 0.5f;
        link.AnchorRight = 0.5f;
        link.OffsetLeft = Width / 2 + 20;
        link.OffsetRight = link.OffsetLeft + link.CustomMinimumSize.X;
        link.OffsetTop = panel.OffsetTop;
        link.OffsetBottom = link.OffsetTop + link.CustomMinimumSize.Y;
        link.Visible = false;
        screen.AddChild(link);
        return panel;
    }
}
