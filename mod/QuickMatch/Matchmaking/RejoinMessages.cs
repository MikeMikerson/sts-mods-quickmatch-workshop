using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace QuickMatch.Matchmaking;

// A returning player to the host, sent as soon as the host says the run is under way and before the game's own rejoin
// request: "I run QuickMatch and can build the run from your answer". Registered by the game with the mod's other
// messages (see QuickMatchMoveMessage).
public struct QuickMatchRejoinHelloMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }
}

// The host to a returning player: "your rejoin is held until the party's next room starts".
public struct QuickMatchRejoinHeldMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }
}

// The host to a returning player, just before the game's rejoin answer: what the answer (a save and a combat state)
// does not hold. The players connected once they are in, who is left out of the room's fight, and the run's counters
// that every peer keeps in step without sending them (the same ones the game's combat replay records), as the host has
// them at the room's start.
public struct QuickMatchRejoinInfoMessage : INetMessage, IPacketSerializable
{
    public List<ulong> connected;

    public List<bool> modded;

    public List<ulong> fightAbsent;

    public uint nextActionId;

    public uint nextHookId;

    public uint nextChecksumId;

    public int mapGenerationCount;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        List<ulong> players = connected ?? [];
        List<bool> flags = modded ?? [];
        writer.WriteInt(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            writer.WriteULong(players[i]);
            writer.WriteBool(i < flags.Count && flags[i]);
        }
        List<ulong> absent = fightAbsent ?? [];
        writer.WriteInt(absent.Count);
        foreach (ulong player in absent)
        {
            writer.WriteULong(player);
        }
        writer.WriteUInt(nextActionId);
        writer.WriteUInt(nextHookId);
        writer.WriteUInt(nextChecksumId);
        writer.WriteInt(mapGenerationCount);
    }

    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadInt();
        connected = new List<ulong>(count);
        modded = new List<bool>(count);
        for (int i = 0; i < count; i++)
        {
            connected.Add(reader.ReadULong());
            modded.Add(reader.ReadBool());
        }
        int absent = reader.ReadInt();
        fightAbsent = new List<ulong>(absent);
        for (int i = 0; i < absent; i++)
        {
            fightAbsent.Add(reader.ReadULong());
        }
        nextActionId = reader.ReadUInt();
        nextHookId = reader.ReadUInt();
        nextChecksumId = reader.ReadUInt();
        mapGenerationCount = reader.ReadInt();
    }
}
