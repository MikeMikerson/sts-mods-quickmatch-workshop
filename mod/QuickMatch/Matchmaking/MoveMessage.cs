using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace QuickMatch.Matchmaking;

// Host to clients in a partial quick-match lobby: "our group is moving into this lobby", with the target's party size
// and kind (an Any group may move into an exact-size lobby it fills). The game registers every INetMessage type in
// loaded mod assemblies by reflection; all quick-match players run this mod, so they agree on its message id.
public struct QuickMatchMoveMessage : INetMessage, IPacketSerializable
{
    public ulong targetLobbyId;

    // 2 to 4.
    public int size;

    public bool anySize;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(targetLobbyId);
        writer.WriteInt(size, 8);
        writer.WriteBool(anySize);
    }

    public void Deserialize(PacketReader reader)
    {
        targetLobbyId = reader.ReadULong();
        size = reader.ReadInt(8);
        anySize = reader.ReadBool();
    }
}
