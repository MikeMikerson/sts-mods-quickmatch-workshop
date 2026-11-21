using System;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using QuickMatch.Localization;
using QuickMatch.Ui;

namespace QuickMatch.Matchmaking;

// The returning player's side of one join attempt to a resume lobby, set up before the game's join flow starts: says
// hello when the host says the run is under way, notes when the host holds the rejoin and what it sends with its
// answer, and holds back the run's messages from the moment of the answer until the run is built.
internal sealed class RejoinClient : IDisposable
{
    private readonly INetClientGameService _net;
    private bool _disposed;

    public RejoinClient(INetClientGameService net)
    {
        _net = net;
        net.RegisterMessageHandler<InitialGameInfoMessage>(OnInitialInfo);
        net.RegisterMessageHandler<QuickMatchRejoinHeldMessage>(OnHeld);
        net.RegisterMessageHandler<QuickMatchRejoinInfoMessage>(OnInfo);
        net.RegisterMessageHandler<ClientRejoinResponseMessage>(OnAnswer);
    }

    public bool Held { get; private set; }

    public QuickMatchRejoinInfoMessage? Info { get; private set; }

    private void OnInitialInfo(InitialGameInfoMessage message, ulong sender)
    {
        if (message.sessionState == RunSessionState.Running && message.connectionFailureReason == null)
        {
            // Registered before the join flow's own handler, so this goes out before the game's rejoin request.
            _net.SendMessage(new QuickMatchRejoinHelloMessage());
        }
    }

    private void OnHeld(QuickMatchRejoinHeldMessage message, ulong sender)
    {
        Held = true;
        MainFile.Logger.Info("rejoin held by the host until the party's next room");
        QuickMatchHub.ShowStatus(Loc.Get("QUICKMATCH.rejoin_held"));
    }

    private void OnInfo(QuickMatchRejoinInfoMessage message, ulong sender) => Info = message;

    private void OnAnswer(ClientRejoinResponseMessage message, ulong sender)
    {
        if (Info != null)
        {
            // Everything from here on belongs to the run being built; the game releases it when the run launches.
            _net.SetBufferMessages(true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _net.UnregisterMessageHandler<InitialGameInfoMessage>(OnInitialInfo);
        _net.UnregisterMessageHandler<QuickMatchRejoinHeldMessage>(OnHeld);
        _net.UnregisterMessageHandler<QuickMatchRejoinInfoMessage>(OnInfo);
        _net.UnregisterMessageHandler<ClientRejoinResponseMessage>(OnAnswer);
    }
}
