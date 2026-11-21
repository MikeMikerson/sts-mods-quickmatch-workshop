using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace QuickMatch.Matchmaking;

// The host to every client at each room's start, sent just before the game's own room sync (whose RNG message every
// client waits for), so it arrives first: the players left out of this room's fight. Every peer builds the fight from
// the same list, the host's. Registered by the game with the mod's other messages (see QuickMatchMoveMessage).
public struct QuickMatchFightAbsentMessage : INetMessage, IPacketSerializable
{
    public List<ulong> players;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        List<ulong> list = players ?? [];
        writer.WriteInt(list.Count);
        foreach (ulong player in list)
        {
            writer.WriteULong(player);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadInt();
        players = new List<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            players.Add(reader.ReadULong());
        }
    }
}

// The host to every client: the choice the absent player had to make is answered with nothing chosen. Every peer that
// waits for it, or will, takes the empty answer.
public struct QuickMatchStandInChoiceMessage : INetMessage, IPacketSerializable
{
    public ulong player;

    public uint choiceId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(player);
        writer.WriteUInt(choiceId);
    }

    public void Deserialize(PacketReader reader)
    {
        player = reader.ReadULong();
        choiceId = reader.ReadUInt();
    }
}
