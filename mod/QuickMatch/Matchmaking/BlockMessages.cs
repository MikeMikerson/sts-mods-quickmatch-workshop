using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace QuickMatch.Matchmaking;

// A member to the host of a quick-match lobby: "a player here is one I have blocked; send them away". The host honours
// it for any member about any other member but itself (BlockPolicy.HostHonours), so it learns only that one block, and
// only when it matters. Registered by the game with the mod's other messages (see QuickMatchMoveMessage).
public struct QuickMatchBlockedHereMessage : INetMessage, IPacketSerializable
{
    public ulong player;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(player);
    }

    public void Deserialize(PacketReader reader)
    {
        player = reader.ReadULong();
    }
}

public static class LeaveReasons
{
    // A block or a vote: the player is told only that the group is gone (or of the vote, which they know of).
    public const byte GroupGone = 0;
    // The idle countdown ran out before the player pressed Embark.
    public const byte Idle = 1;
    // The countdown ran out before the host pressed Embark: the host leaves, and everyone searches again, keeping
    // Embark.
    public const byte HostIdle = 2;
    // In a lobby of 2 the host's own vote passes at once, and this message over the game's connection reaches the
    // player before the vote over lobby chat does, so the reason says it was a vote.
    public const byte Voted = 3;
}

// The host to one client: "leave this lobby and search again". Sent to a player someone here has blocked, so they move
// on with no error popup; the host disconnects them a moment later if they stay.
public struct QuickMatchLeaveMessage : INetMessage, IPacketSerializable
{
    public byte reason;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(reason);
    }

    public void Deserialize(PacketReader reader)
    {
        reason = reader.ReadByte();
    }
}
