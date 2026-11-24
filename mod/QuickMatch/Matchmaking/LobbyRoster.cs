using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace QuickMatch.Matchmaking;

// Only QuickMatch peers receive this list. The final copy precedes the game's start on the same reliable channel.
public struct QuickMatchRosterMessage : INetMessage, IPacketSerializable
{
    public ulong[] desktops;
    public ulong[] phones;
    public ulong[] guests;

    public bool IsQuickMatchPeer(ulong peer) => desktops.Contains(peer) || phones.Contains(peer);

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        Write(writer, desktops);
        Write(writer, phones);
        Write(writer, guests);
    }

    private static void Write(PacketWriter writer, ulong[] players)
    {
        writer.WriteInt(players.Length, 3);
        foreach (ulong player in players)
        {
            writer.WriteULong(player);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        desktops = Read(reader);
        phones = Read(reader);
        guests = Read(reader);
    }

    private static ulong[] Read(PacketReader reader)
    {
        var players = new ulong[reader.ReadInt(3)];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = reader.ReadULong();
        }
        return players;
    }
}
