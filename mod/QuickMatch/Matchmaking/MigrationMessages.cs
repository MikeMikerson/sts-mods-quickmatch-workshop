using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace QuickMatch.Matchmaking;

// Sent immediately before the game's start message on the same reliable channel.
public struct QuickMatchDesktopRosterMessage : INetMessage, IPacketSerializable
{
    public ulong[] players;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(players.Length, 3);
        foreach (ulong player in players)
        {
            writer.WriteULong(player);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        players = new ulong[reader.ReadInt(3)];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = reader.ReadULong();
        }
    }
}
