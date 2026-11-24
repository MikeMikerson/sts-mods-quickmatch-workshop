using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

// Where a Steam invite or steam://joinlobby link goes: a quick-match lobby through Quick Match's own join, any other
// lobby through the game's.
internal static class LinkJoin
{
    private static readonly string[] InviteKeys = [.. LinkPolicy.Keys, ResumeLobby.Guests, LobbyKeys.ResumeProto, LobbyKeys.ResumeKey];
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    public static async Task RouteAsync(ulong lobbyId, ulong? playerId, Func<Task> gameJoin)
    {
        IReadOnlyDictionary<string, string>? data;
        using (var timeout = new CancellationTokenSource(ReadTimeout))
        {
            try
            {
                data = await LobbyBackend.Current.ReadDataAsync(lobbyId, InviteKeys, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                data = null;
            }
        }
        bool guestSave = data != null && !string.IsNullOrEmpty(data.GetValueOrDefault(ResumeLobby.Guests));
        if (data == null || !guestSave && !LinkPolicy.IsQuickMatch(data))
        {
            MainFile.Logger.Info($"invite to lobby {lobbyId}: {(data == null ? "no lobby data from Steam" : "not a quick-match lobby")}, joining as the game does");
            await gameJoin();
            return;
        }
        QuickMatchLobby.CompatKey local = QuickMatchLobby.CompatKey.Local();
        (LinkVerdict verdict, LinkLobby lobby) = guestSave
            ? (data.GetValueOrDefault(LobbyKeys.ResumeProto) != LobbyKeys.ResumeProtoValue ? LinkVerdict.OtherRelease
                : data.GetValueOrDefault(LobbyKeys.Version) != local.Version ? LinkVerdict.OtherGame : LinkVerdict.Join, default)
            : LinkPolicy.Read(data, (local.Version, local.Db, local.Mods), StartScreens.IsPlayable, MatchChoice.MaxAscensionFor, MatchChoice.Voice);
        MainFile.Logger.Info($"invite to quick-match lobby {lobbyId}: {verdict}");
        if (verdict != LinkVerdict.Join)
        {
            ShowRefusal(Loc.Get(LinkPolicy.MessageKey(verdict)));
            return;
        }
        if (RunManager.Instance.IsInProgress && !await ConfirmLeavingRunAsync(lobbyId, playerId))
        {
            return;
        }
        if (NGame.Instance is not { } game)
        {
            return;
        }
        Matchmaker.Cancel("invite link");
        if (game.MainMenu == null)
        {
            await game.ReturnToMainMenu();
        }
        while (game.MainMenu?.SubmenuStack.Peek() != null)
        {
            game.MainMenu.SubmenuStack.Pop();
        }
        if (guestSave)
        {
            QuickMatchHub.OpenForRejoin();
            Matchmaker.StartGuestSaveLink(lobbyId, data.GetValueOrDefault(LobbyKeys.ResumeKey) ?? "", data.GetValueOrDefault(LobbyKeys.Voice) == LobbyKeys.VoiceOn);
        }
        else
        {
            QuickMatchHub.Open();
            Matchmaker.StartLink(lobbyId, lobby);
        }
    }

    // The game's own question before an invite takes a player out of a run.
    private static async Task<bool> ConfirmLeavingRunAsync(ulong lobbyId, ulong? playerId)
    {
        var body = new LocString("gameplay_ui", "QUIT_AND_JOIN_CONFIRMATION.body");
        body.Add("host", PlatformUtil.GetPlayerName(PlatformType.Steam, playerId ?? LobbyBackend.Current.Owner(lobbyId)));
        if (NModalContainer.Instance is not { } modals)
        {
            return false;
        }
        if (NGenericPopup.Create() is not { } popup)
        {
            return false;
        }
        modals.Add(popup);
        return await popup.WaitForConfirmation(
            body,
            new LocString("gameplay_ui", "QUIT_AND_JOIN_CONFIRMATION.header"),
            new LocString("gameplay_ui", "QUIT_AND_JOIN_CONFIRMATION.cancel"),
            new LocString("gameplay_ui", "QUIT_AND_JOIN_CONFIRMATION.confirm"));
    }

    private static void ShowRefusal(string text)
    {
        if (NModalContainer.Instance is { } modals && NErrorPopup.Create(Loc.Get("QUICKMATCH.LINK.title"), text, showReportBugButton: false) is { } popup)
        {
            modals.Add(popup);
        }
    }
}
