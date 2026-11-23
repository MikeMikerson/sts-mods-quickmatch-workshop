using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using QuickMatch.Lobby;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

internal sealed class RecentTab
{
    private readonly VBoxContainer _list;
    private string? _shown;

    public RecentTab(VBoxContainer page)
    {
        GameLook.Note(page, Loc.Get("QUICKMATCH.RECENT.hint"));
        _list = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _list.AddThemeConstantOverride("separation", 6);
        page.AddChild(_list);
        GameLook.Note(page, Loc.Get("QUICKMATCH.BLOCK.private_note"));
        Refresh();
    }

    public void Refresh()
    {
        List<RecentPlayer> players = RecentStore.List.Players.Where(p => !BlockStore.IsBlocked(p.Id)).ToList();
        string shown = string.Join(",", players.Select(p => $"{p.Id}:{p.At}"));
        if (shown == _shown)
        {
            return;
        }
        _shown = shown;
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
        if (players.Count == 0)
        {
            GameLook.Note(_list, Loc.Get("QUICKMATCH.RECENT.none"), GameLook.Cream, 24);
            return;
        }
        int n = 0;
        foreach (RecentPlayer player in players)
        {
            HBoxContainer controls = GameLook.Row(_list, NameOf(player), $"QuickMatchRecentRow{n}", out _, out MegaLabel detail);
            detail.Text = Loc.With("QUICKMATCH.RECENT.played_on", "date", BlockPlayers.Date(player.At));
            controls.AddChild(GameLook.Button($"QuickMatchRecentProfile{n}", Loc.Get("QUICKMATCH.RECENT.profile"), () => OpenProfile(player.Id)));
            n++;
        }
    }

    private static string NameOf(RecentPlayer player) =>
        player.Name ?? BlockPlayers.KnownName(player.Id)
            ?? Loc.With("QUICKMATCH.BLOCK.steam_id", "id", player.Id.ToString(CultureInfo.InvariantCulture));

    private static void OpenProfile(ulong player)
    {
        bool handled = LobbyBackend.Current.ShowProfile(player);
#if !IPHONE
        if (!handled)
        {
            OS.ShellOpen($"https://steamcommunity.com/profiles/{player.ToString(CultureInfo.InvariantCulture)}");
        }
#endif
    }
}
