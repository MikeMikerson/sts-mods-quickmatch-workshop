using System;
using System.Buffers.Binary;
using System.Text;
using QuickMatch.Transport;

namespace QuickMatch.Platforms.Desktop;

// Steam identifies the sender. The lobby ID keeps a late message from a previous match out.
internal static class SocketVoiceWire
{
    private const int Header = 9;
    public const int MaxBytes = Header + ChatFraming.MaxRegionLength + RelayCrypto.KeyBytes;
    private const byte Switch = 1, Offer = 2, Keys = 3;

    public static byte[] SwitchToRelay(ulong lobby) => Message(lobby, Switch, 0);

    public static byte[] Room(ulong lobby, string region, byte[] key)
    {
        byte[] message = Message(lobby, Offer, region.Length + key.Length);
        Encoding.ASCII.GetBytes(region, message.AsSpan(Header));
        key.CopyTo(message, Header + region.Length);
        return message;
    }

    public static byte[] Member(ulong lobby, ulong member, ulong tag, byte[] key)
    {
        byte[] message = Message(lobby, Keys, 16 + key.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(Header), member);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(Header + 8), tag);
        key.CopyTo(message, Header + 16);
        return message;
    }

    private static byte[] Message(ulong lobby, byte kind, int length)
    {
        var message = new byte[Header + length];
        BinaryPrimitives.WriteUInt64LittleEndian(message, lobby);
        message[8] = kind;
        return message;
    }

    public static bool Read(ReadOnlySpan<byte> message, ulong lobby, ulong sender, ulong host, RelayRoute? route)
    {
        if (message.Length < Header || BinaryPrimitives.ReadUInt64LittleEndian(message) != lobby)
        {
            return false;
        }
        byte kind = message[8];
        ReadOnlySpan<byte> body = message[Header..];
        if (kind == Switch)
        {
            return sender == host && body.IsEmpty;
        }
        if (kind == Offer && sender == host && body.Length - RelayCrypto.KeyBytes is > 0 and <= ChatFraming.MaxRegionLength)
        {
            route?.TakeVoiceOffer(sender, Encoding.ASCII.GetString(body[..^RelayCrypto.KeyBytes]), body[^RelayCrypto.KeyBytes..].ToArray());
        }
        if (kind == Keys && body.Length == 16 + RelayCrypto.KeyBytes)
        {
            route?.TakeVoiceKeys(sender, BinaryPrimitives.ReadUInt64LittleEndian(body),
                BinaryPrimitives.ReadUInt64LittleEndian(body[8..]), body[16..].ToArray());
        }
        return false;
    }
}
