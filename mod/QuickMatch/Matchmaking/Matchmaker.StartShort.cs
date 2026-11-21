using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using QuickMatch.Localization;
using QuickMatch.Lobby;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

// An exact-size lobby that has waited short of full asks everyone in it whether to start with those here. The host
// times it, closes the lobby to joiners while it asks, counts the answers and sends the result over lobby chat, where
// Steam vouches for each sender.
internal static partial class Matchmaker
{
    private sealed record StartShortAsk(int Players, byte Round, HashSet<ulong> Asked, double At)
    {
        public HashSet<ulong> Yes { get; } = new();
        public bool AnyNo { get; set; }
    }

    // As host: the question being asked.
    private static StartShortAsk? _ask;
    private static byte _shortRound;
    // Everyone here agreed to start with this many; any join or leave clears it.
    private static int _shortStart;

    // The number of players the run starts with: the lobby's size, or fewer when everyone here agreed.
    public static int StartSize => _shortStart != 0 ? _shortStart : Size;

    private static double Seconds() => Time.GetTicksMsec() / 1000.0;

    private static async Task StartShortLoopAsync(int gen, int epoch, CancellationToken ct)
    {
        if (AnySize)
        {
            return;
        }
        int count = 0;
        double since = Seconds();
        bool asked = false;
        while (Hosting(gen, epoch))
        {
            await GodotDelay.Seconds(1, ct);
            if (!Hosting(gen, epoch))
            {
                return;
            }
            int here = SettledGroupSize();
            if (here != count)
            {
                count = here;
                since = Seconds();
                asked = false;
            }
            if (_ask != null)
            {
                TallyStartShort();
            }
            else if (_shortStart == 0 && MatchPolicy.AsksToStartShort(here, Size, AnySize, Seconds() - since, asked))
            {
                asked = true;
                AskStartShort(here);
            }
        }
    }

    private static void AskStartShort(int players)
    {
        // As for the alone offer: nobody may arrive while we ask.
        LobbyBackend.Current.SetJoinable(SteamLobbyId, false);
        if (SettledGroupSize() != players)
        {
            LobbyBackend.Current.SetJoinable(SteamLobbyId, true);
            return;
        }
        byte round = ++_shortRound;
        _ask = new StartShortAsk(players, round, new HashSet<ulong>(LobbyBackend.Current.Members(SteamLobbyId)), Seconds());
        MainFile.Logger.Info($"lobby {SteamLobbyId}: {players}/{Size} for {MatchPolicy.StartShortSeconds:0}s, asking to start with {players}");
        SendStartShort(StartShortMessage.Ask, players, round);
        StartShortQuestion.Ask(players, yes => AnswerAsHost(round, yes));
    }

    private static void AnswerAsHost(byte round, bool yes)
    {
        if (_ask is not { } ask || ask.Round != round || Lobby == null)
        {
            return;
        }
        Record(ask, Lobby.NetService.NetId, yes);
        TallyStartShort();
    }

    private static void Record(StartShortAsk ask, ulong player, bool yes)
    {
        if (!ask.Asked.Contains(player))
        {
            return;
        }
        if (yes)
        {
            ask.Yes.Add(player);
        }
        else
        {
            ask.AnyNo = true;
        }
    }

    private static void TallyStartShort()
    {
        if (_ask is not { } ask)
        {
            return;
        }
        StartShortOutcome outcome = MatchPolicy.StartShortTally(ask.Asked, LobbyBackend.Current.Members(SteamLobbyId), ask.Yes,
            ask.AnyNo, Seconds() - ask.At >= MatchPolicy.StartShortAnswerSeconds);
        if (outcome == StartShortOutcome.Waiting)
        {
            return;
        }
        _ask = null;
        StartShortQuestion.CloseIfOpen();
        if (outcome == StartShortOutcome.Agreed)
        {
            MainFile.Logger.Info($"lobby {SteamLobbyId}: everyone agreed to start with {ask.Players}");
            SendStartShort(StartShortMessage.Agreed, ask.Players, ask.Round);
            Agreed(ask.Players);
            return;
        }
        MainFile.Logger.Info($"lobby {SteamLobbyId}: start with {ask.Players} {(outcome == StartShortOutcome.Refused ? "refused" : "ended")}");
        SendStartShort(outcome == StartShortOutcome.Refused ? StartShortMessage.Refused : StartShortMessage.Ended, ask.Players, ask.Round);
        LobbyBackend.Current.SetJoinable(SteamLobbyId, true);
        if (outcome == StartShortOutcome.Refused)
        {
            Notice.Show(Loc.With("QUICKMATCH.SHORT.refused", "size", Size.ToString()));
        }
    }

    private static void Agreed(int players)
    {
        _shortStart = players;
        Notice.Show(Loc.With("QUICKMATCH.SHORT.agreed", "players", players.ToString()));
        RefreshStatus();
        // The game checks whether to begin only when someone's ready changes, and everyone may have pressed the
        // checkmark already.
        if (Lobby is { } lobby && lobby.NetService.Type == NetGameType.Host && lobby.Players.Count == players && lobby.Players.All(p => p.isReady))
        {
            lobby.SetReady(true);
        }
    }

    // A lobby chat message from sender; runs on the game's thread.
    public static void OnStartShortMessage(ulong sender, StartShortMessage kind, int players, byte round)
    {
        if (Lobby == null || sender == Lobby.NetService.NetId)
        {
            return;
        }
        if (Lobby.NetService.Type == NetGameType.Host)
        {
            if (_ask is { } ask && ask.Round == round && kind is StartShortMessage.Yes or StartShortMessage.No)
            {
                Record(ask, sender, kind == StartShortMessage.Yes);
                TallyStartShort();
            }
            return;
        }
        if (sender != LobbyBackend.Current.Owner(SteamLobbyId))
        {
            return;
        }
        switch (kind)
        {
            case StartShortMessage.Ask:
                MainFile.Logger.Info($"lobby {SteamLobbyId}: the host asks to start with {players}");
                StartShortQuestion.Ask(players, yes => SendStartShort(yes ? StartShortMessage.Yes : StartShortMessage.No, players, round));
                break;
            case StartShortMessage.Agreed:
                MainFile.Logger.Info($"lobby {SteamLobbyId}: everyone agreed to start with {players}");
                StartShortQuestion.CloseIfOpen();
                Agreed(players);
                break;
            case StartShortMessage.Refused:
                StartShortQuestion.CloseIfOpen();
                Notice.Show(Loc.With("QUICKMATCH.SHORT.refused", "size", Size.ToString()));
                break;
            case StartShortMessage.Ended:
                StartShortQuestion.CloseIfOpen();
                break;
        }
    }

    // A player joined or left: the question ends and an agreement no longer holds.
    private static void StartShortPlayersChanged()
    {
        if (_ask == null && _shortStart == 0)
        {
            return;
        }
        if (Lobby?.NetService.Type == NetGameType.Host)
        {
            MainFile.Logger.Info($"lobby {SteamLobbyId}: the players changed, so starting short is off");
            SendStartShort(StartShortMessage.Ended, _ask?.Players ?? _shortStart, _shortRound);
            LobbyBackend.Current.SetJoinable(SteamLobbyId, true);
        }
        ResetStartShort();
    }

    private static void ResetStartShort()
    {
        _ask = null;
        _shortStart = 0;
        StartShortQuestion.CloseIfOpen();
    }

    private static void SendStartShort(StartShortMessage kind, int players, byte round)
    {
        byte[] message = StartShortWire.Write(kind, players, round);
        if (!LobbyBackend.Current.SendChat(SteamLobbyId, message, message.Length))
        {
            MainFile.Logger.Warn($"lobby {SteamLobbyId}: could not send the start-short {kind}");
        }
    }
}
