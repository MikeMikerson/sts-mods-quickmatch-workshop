using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Steam;
using QuickMatch.Transport;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    private const double MergeIntervalSeconds = 6;
    private const double MoveWaitSeconds = 5;

    private static bool Hosting(int gen, int epoch) => gen == _generation && epoch == _hostEpoch && State == MatchState.HostingWaiting;

    // A host with players in its lobby is never offered a wider search: they joined under its choice.
    private static async Task AloneOfferAsync(int gen, int epoch, CancellationToken ct)
    {
        if (_offered || MatchPolicy.OfferWhenAlone(RandomSize, Size, Choice, MaxAscension, _allowed) is not { } offer)
        {
            return;
        }
        await GodotDelay.Seconds(MatchPolicy.AloneOfferSeconds, ct);
        if (!Hosting(gen, epoch) || SettledGroupSize() != 1 || _offered)
        {
            return;
        }
        _offered = true;
        MainFile.Logger.Info($"alone for {MatchPolicy.AloneOfferSeconds}s in lobby {SteamLobbyId}: offering any size and ascension ({offer.LineKey})");
        var pressed = new TaskCompletionSource();
        int notice = Notice.ShowWithButton(AloneLine(offer), Loc.Get(offer.ButtonKey), () => pressed.TrySetResult());
        try
        {
            while (!pressed.Task.IsCompleted)
            {
                await Task.WhenAny(pressed.Task, GodotDelay.Seconds(0.5, ct));
                ct.ThrowIfCancellationRequested();
                // A player who is still connecting may yet fail to join, so only one who is in takes the line away.
                if (!Hosting(gen, epoch) || Lobby?.Players.Count != 1)
                {
                    MainFile.Logger.Info("offer withdrawn: no longer alone in the lobby");
                    return;
                }
            }
        }
        finally
        {
            Notice.Hide(notice);
        }
        if (!Hosting(gen, epoch) || SettledGroupSize() != 1)
        {
            MainFile.Logger.Info("offer accepted too late: the lobby changed");
            return;
        }
        // As for a merge: nobody may arrive while we leave.
        LobbyBackend.Current.SetJoinable(SteamLobbyId, false);
        if (SettledGroupSize() != 1)
        {
            LobbyBackend.Current.SetJoinable(SteamLobbyId, true);
            MainFile.Logger.Info("offer accepted too late: a player is joining");
            return;
        }
        MainFile.Logger.Info("offer accepted: searching again as any size and ascension");
        SearchStartedByPlayer();
        SetChoice(0, AscensionChoice.Random, MatchMode.On);
        SetState(MatchState.Searching, "switched to random");
        _merging = true;
        try
        {
            CloseCharacterSelect();
        }
        finally
        {
            _merging = false;
        }
        await SearchJoinOrHostAsync(ct);
    }

    private static string AloneLine(AloneOffer offer)
    {
        var text = new MegaCrit.Sts2.Core.Localization.LocString(LocSetup.Table, offer.LineKey);
        if (offer.Size != 0)
        {
            text.Add("size", offer.Size);
        }
        if (!offer.LineKey.EndsWith("_many", StringComparison.Ordinal) && offer.LineKey != "QUICKMATCH.ALONE.size")
        {
            text.Add("level", offer.Level);
        }
        string line = text.GetFormattedText();
        return offer.ClimbNote ? line + " " + Loc.Get("QUICKMATCH.ALONE.climb_note") : line;
    }

    // Two players who searched at the same moment both end up hosting, and two pairs form for a
    // 4-player game, so a group moves to a better lobby with room for all of it.
    private static async Task MergeLoopAsync(int gen, int epoch, CancellationToken ct)
    {
        var watch = new OwnLobbyWatch();
        while (true)
        {
            await GodotDelay.Seconds(MergeIntervalSeconds + Rng.NextDouble() * 3, ct);
            if (!Hosting(gen, epoch))
            {
                return;
            }
            int group = SettledGroupSize();
            // A group asked or agreed to start short stays together here.
            if (group < 1 || group >= StartSize || _ask != null)
            {
                continue;
            }
            List<LobbyCandidate> found = await LobbySearch.FindAsync(AnySize ? 0 : Size, ct, minFreeSlots: group);
            if (!Hosting(gen, epoch))
            {
                return;
            }
            bool visible = found.Any(c => c.Id == SteamLobbyId);
            MainFile.Logger.Info($"merge check: own lobby {(visible ? "is" : "is not")} visible in search");
            if (SettledGroupSize() != group)
            {
                continue;
            }
            if (watch.Searched(visible, group))
            {
                await LeaveLostLobbyAsync($"not visible in {OwnLobbyWatch.MissesToGiveUp} searches in a row", ct);
                return;
            }
            var own = new LobbyCandidate(SteamLobbyId, group, _createdAt, Ascension: HostedAscension, Size: Size, AnySize: AnySize,
                HostOffersSockets: QuickMatchHostService.HasSockets);
            LobbyCandidate? target = MatchPolicy.PickMergeTarget(own, found, IsRecentlyFailed, Size);
            if (target == null)
            {
                continue;
            }

            // Stop new players from arriving, then make sure nobody slipped in or left meanwhile.
            LobbyBackend.Current.SetJoinable(SteamLobbyId, false);
            if (SettledGroupSize() != group)
            {
                LobbyBackend.Current.SetJoinable(SteamLobbyId, true);
                MainFile.Logger.Info("merge aborted: the group changed");
                continue;
            }

            ulong targetId = target.Value.Id;
            MainFile.Logger.Info($"merge: moving group of {group} from lobby {SteamLobbyId} (t={_createdAt}) to {targetId} (n={target.Value.Members}/{target.Value.Size}{(target.Value.AnySize ? " any size" : "")} t={target.Value.CreatedAt})");
            SetState(MatchState.Merging, $"to {targetId}");
            if (group > 1 && Lobby?.NetService is NetHostGameService host)
            {
                LobbyGuard.NoteMovingEveryone();
                host.SendMessage(new QuickMatchMoveMessage
                {
                    targetLobbyId = targetId,
                    size = target.Value.Size,
                    anySize = target.Value.AnySize,
                    hostOffersSockets = target.Value.HostOffersSockets,
                });
                await WaitForClientsToLeaveAsync(host, ct);
            }
            await LeaveForAsync(targetId, target.Value.Size, target.Value.AnySize, ct);
            return;
        }
    }

    // So no client sees a "host left" popup.
    private static async Task WaitForClientsToLeaveAsync(NetHostGameService host, CancellationToken ct)
    {
        ulong started = Time.GetTicksMsec();
        while (host.ConnectedPeers.Count > 0 && Time.GetTicksMsec() - started < MoveWaitSeconds * 1000)
        {
            await GodotDelay.Seconds(0.25, ct);
        }
        MainFile.Logger.Info($"merge: {host.ConnectedPeers.Count} client(s) still connected after waiting");
    }

    private static async Task LeaveForAsync(ulong targetId, int size, bool anySize, CancellationToken ct)
    {
        _pendingCharacter = Lobby?.LocalPlayer.character;
        _pendingReady = Lobby?.LocalPlayer.isReady ?? false;
        _merging = true;
        try
        {
            CloseCharacterSelect();
        }
        finally
        {
            _merging = false;
        }
        if (await TryJoinAsync(targetId, size, anySize, ct))
        {
            Notice.Show(Loc.Get("QUICKMATCH.NOTICE.merged"));
            return;
        }
        // The pick, and Embark if it was pressed, are kept for whichever lobby the search ends up in.
        await SearchJoinOrHostAsync(ct);
    }

    // Any thread. Steam reports a lobby it closed under us as our own departure.
    private static void OnLobbyChanged(LobbyEvent e)
    {
        if (e.Kind == LobbyEventKind.MemberLeft && e.Member == LobbyBackend.Current.LocalId)
        {
            Callable.From(() => OnOwnDeparture(e.Lobby)).CallDeferred();
        }
    }

    private static void OnOwnDeparture(ulong lobbyId)
    {
        if (_cts == null || lobbyId == 0 || lobbyId != SteamLobbyId || State != MatchState.HostingWaiting || SettledGroupSize() != 1)
        {
            return;
        }
        CancellationToken ct = _cts.Token;
        _ = RunAsync(_generation, ct, () => LeaveLostLobbyAsync("Steam reported our own departure", ct), "leave");
    }

    private static async Task LeaveLostLobbyAsync(string reason, CancellationToken ct)
    {
        ulong lobbyId = SteamLobbyId;
        MainFile.Logger.Info($"own lobby {lobbyId} is gone ({reason}): searching again");
        MarkFailed(lobbyId);
        SetState(MatchState.Searching, "own lobby gone");
        await LeaveAndSearchAsync(ct, keepReady: true);
    }

    private static void OnMoveMessage(QuickMatchMoveMessage message, ulong senderId)
    {
        if (State != MatchState.InLobbyClient || _cts == null || Lobby == null)
        {
            return;
        }
        ulong hostId = LobbyBackend.Current.Owner(SteamLobbyId);
        if (!MatchPolicy.AcceptsMove(senderId, hostId, message.targetLobbyId, SteamLobbyId))
        {
            MainFile.Logger.Info($"merge: ignored a move to lobby {message.targetLobbyId} from {senderId}; the host is {hostId}");
            return;
        }
        int size = message.size is >= 2 and <= 4 ? message.size : Size;
        LobbyLinks.Remember(message.targetLobbyId, message.hostOffersSockets);
        MainFile.Logger.Info($"merge: host {senderId} moves the group to lobby {message.targetLobbyId} ({size}{(message.anySize ? " any size" : "")})");
        SetState(MatchState.Merging, $"to {message.targetLobbyId}");
        CancellationToken ct = _cts.Token;
        int gen = _generation;
        // Run outside the message dispatch: leaving tears down the connection that delivered this.
        Callable.From(() =>
        {
            if (gen == _generation)
            {
                _ = RunAsync(gen, ct, () => LeaveForAsync(message.targetLobbyId, size, message.anySize, ct), "move");
            }
        }).CallDeferred();
    }

    private static int SettledGroupSize()
    {
        if (Lobby?.NetService is not NetHostGameService host)
        {
            return 0;
        }
        int players = Lobby.Players.Count;
        if (host.ConnectedPeers.Count != players - 1)
        {
            return 0;
        }
        return LobbyBackend.Current.Members(SteamLobbyId).Count <= players ? players : 0;
    }

    private static async Task RestoreCharacterAsync(NSubmenu screen, CharacterModel character, bool ready, int gen)
    {
        // After the opening selection and any focus change it triggers have settled.
        try
        {
            await GodotDelay.Seconds(0.2, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (gen != _generation || !GodotObject.IsInstanceValid(screen) || Lobby == null || StartScreens.LobbyOf(screen) != Lobby)
        {
            return;
        }
        NCharacterSelectButton? button = screen.GetNodeOrNull<Control>("CharSelectButtons/ButtonContainer")?
            .GetChildren()
            .OfType<NCharacterSelectButton>()
            .FirstOrDefault(b => !b.IsRandom && !b.IsLocked && b.Character?.Id == character.Id);
        if (button == null)
        {
            MainFile.Logger.Info($"previous character {character.Id} is not available here");
            return;
        }
        button.GrabFocus();
        button.Select();
        MainFile.Logger.Info($"picked {character.Id} again after the move");
        if (!ready)
        {
            return;
        }
        // Embark as the player pressed it before the move, once the pick has reached the screen's button.
        try
        {
            await GodotDelay.Seconds(0.1, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (gen != _generation || !GodotObject.IsInstanceValid(screen) || Lobby == null || StartScreens.LobbyOf(screen) != Lobby || Lobby.LocalPlayer.isReady)
        {
            return;
        }
        if (screen.GetNodeOrNull<NClickableControl>("ConfirmButton") is { IsEnabled: true } embark)
        {
            embark.ForceClick();
            MainFile.Logger.Info("ready again after the move");
        }
        else
        {
            MainFile.Logger.Info("could not press Embark again after the move");
        }
    }
}
