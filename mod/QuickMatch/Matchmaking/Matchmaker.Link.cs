using System.Threading;
using System.Threading.Tasks;
using Godot;
using QuickMatch.Localization;
using QuickMatch.Transport;
using QuickMatch.Ui;
using QuickMatch.Voice;

namespace QuickMatch.Matchmaking;

internal static partial class Matchmaker
{
    // A lobby from an invite link, joined as a search would join it: the player's own ascension levels and size choice
    // stay for any later search, in the lobby's mode.
    public static void StartLink(ulong lobbyId, LinkLobby lobby)
    {
        if (State != MatchState.Idle || !IsAvailable)
        {
            MainFile.Logger.Info($"ignoring invite link while {State}");
            return;
        }
        SetChoice(lobby.AnySize ? 0 : lobby.Size, MatchPolicy.PlayNowChoice(MatchChoice.Last, MatchChoice.MaxAscensionFor(lobby.Mode)), lobby.Mode);
        _offered = false;
        _startedMsec = Time.GetTicksMsec();
        SearchStartedByPlayer();
        MatchVoice.Set(lobby.Voice);
        // Its data marked it as a quick-match lobby, as a search listing would; Steam voice runs only in a listed lobby.
        LobbyLinks.Remember(lobbyId);
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        CancellationToken ct = _cts.Token;
        MainFile.Logger.Info($"invite link to lobby {lobbyId}: {MatchMode.Text(lobby.Mode)} size={(lobby.AnySize ? "any" : lobby.Size.ToString())} A{lobby.Ascension} {MatchVoice.Text(lobby.Voice)}");
        _ = RunAsync(gen, ct, () => LinkJoinAsync(lobbyId, lobby, ct), "invite link");
    }

    private static async Task LinkJoinAsync(ulong lobbyId, LinkLobby lobby, CancellationToken ct)
    {
        if (!await TryJoinAsync(lobbyId, lobby.Size, lobby.AnySize, ct))
        {
            Reset("invite link failed");
            QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.LINK.failed"));
        }
    }
}
