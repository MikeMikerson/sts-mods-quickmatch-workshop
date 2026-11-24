using MegaCrit.Sts2.Core.Multiplayer;
using QuickMatch.Matchmaking;

namespace QuickMatch.Transport;

// A host can send the first roster in the same update as the join response, before character select opens.
internal sealed class QuickMatchClientService : NetClientGameService
{
    public QuickMatchClientService() : base(PeerVersionInfo.LocalDefault())
    {
        RegisterMessageHandler<QuickMatchRosterMessage>(OnRoster);
    }

    public QuickMatchRosterMessage? Roster { get; private set; }

    private void OnRoster(QuickMatchRosterMessage message, ulong sender)
    {
        if (IsConnected && sender == HostNetId)
        {
            Roster = message;
        }
    }
}
