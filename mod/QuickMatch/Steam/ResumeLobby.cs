using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickMatch.Lobby;
using QuickMatch.Matchmaking;

namespace QuickMatch.Steam;

internal static class ResumeLobby
{
    public const string Guests = "qm_saved_guests";

    public static ulong[]? ReadGuests(string? value, IEnumerable<ulong> players, ulong host)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        string[] entries = value.Split(',');
        if (entries.Length > MatchPolicy.AnySizeMax - 1)
        {
            return null;
        }
        var party = players.ToHashSet();
        if (!party.Contains(host))
        {
            return null;
        }
        var guests = new HashSet<ulong>();
        foreach (string entry in entries)
        {
            if (!ulong.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
                || id == 0 || id == host || !party.Contains(id) || !guests.Add(id))
            {
                return null;
            }
        }
        return guests.ToArray();
    }

    public static bool Publish(ILobbyBackend backend, ulong lobby, string runKey, string version, string link)
    {
        backend.SetData(lobby, LobbyKeys.Version, version);
        backend.SetData(lobby, LobbyKeys.ResumeKey, runKey);
        if (backend.Owner(lobby) is var owner and not 0)
        {
            backend.SetData(lobby, LobbyKeys.ResumeHost, owner.ToString(CultureInfo.InvariantCulture));
        }
        backend.SetData(lobby, LobbyKeys.Link, link);
        backend.DeleteData(lobby, LobbyKeys.Proto);
        backend.SetData(lobby, LobbyKeys.ResumeProto, LobbyKeys.ResumeProtoValue);
        backend.SetJoinable(lobby, true);
        return backend.SetKind(lobby, LobbyKind.Public);
    }

    public static void Unpublish(ILobbyBackend backend, ulong lobby)
    {
        backend.DeleteData(lobby, LobbyKeys.ResumeProto);
        backend.DeleteData(lobby, LobbyKeys.ResumeKey);
        backend.DeleteData(lobby, LobbyKeys.ResumeHost);
        backend.DeleteData(lobby, Guests);
    }

    public static LobbyQuery Query(string runKey, string version) => new(
        new Dictionary<string, string>
        {
            [LobbyKeys.ResumeProto] = LobbyKeys.ResumeProtoValue,
            [LobbyKeys.ResumeKey] = runKey,
            [LobbyKeys.Version] = version,
        },
        MinFreeSlots: 1,
        MaxResults: 10,
        ReadKeys: [LobbyKeys.ResumeHost, LobbyKeys.Link]);

    public static (ulong Host, bool OffersSockets)? Read(LobbyListing listing)
    {
        if (LinkPolicy.HostOffersSockets(listing.Data.GetValueOrDefault(LobbyKeys.Link)) is not bool offersSockets)
        {
            return null;
        }
        ulong.TryParse(listing.Data.GetValueOrDefault(LobbyKeys.ResumeHost), NumberStyles.None, CultureInfo.InvariantCulture, out ulong host);
        return (host, offersSockets);
    }
}
