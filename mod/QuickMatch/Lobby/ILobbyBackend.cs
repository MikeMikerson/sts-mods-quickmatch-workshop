using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickMatch.Lobby;

// Same values as Steam's ELobbyType.
public enum LobbyKind
{
    Private = 0,
    FriendsOnly = 1,
    Public = 2,
    Invisible = 3,
}

public enum LobbyJoinResult
{
    Ok,
    DoesNotExist,
    NotAllowed,
    Full,
    Error,
    Banned,
    Limited,
    Blocked,
    RateLimited,
}

public enum LobbyEventKind
{
    // LobbyEvent.Data holds the message's bytes.
    Chat,
    MemberJoined,
    // A member left, lost its connection, or was kicked or banned.
    MemberLeft,
}

public readonly record struct LobbyEvent(LobbyEventKind Kind, ulong Lobby, ulong Member, byte[]? Data = null);

// Members is 0 if unknown.
public readonly record struct LobbyListing(ulong Id, int Members, IReadOnlyDictionary<string, string> Data);

// A lobby search: every key in Equal must hold exactly that value, and each result carries the values of ReadKeys.
// Worldwide, or with Nearby only Steam's default distance (the player's region and those near it).
public sealed record LobbyQuery(
    IReadOnlyDictionary<string, string> Equal,
    int MinFreeSlots,
    int MaxResults,
    IReadOnlyList<string> ReadKeys,
    bool Nearby = false);

public interface ILobbyBackend
{
    bool IsAvailable { get; }

    ulong LocalId { get; }

    // Steam's clock, in seconds since 1970 (UTC).
    uint ServerTime { get; }

    // May be raised on any thread.
    event Action<LobbyEvent>? Changed;

    // Null if Steam refused.
    Task<ulong?> CreateAsync(LobbyKind kind, int maxMembers, CancellationToken ct);

    Task<LobbyJoinResult> JoinAsync(ulong lobby, CancellationToken ct);

    void Leave(ulong lobby);

    // Null when Steam reported an error. Throws only when cancelled.
    Task<IReadOnlyList<LobbyListing>?> SearchAsync(LobbyQuery query, CancellationToken ct);

    void SetData(ulong lobby, string key, string value);

    void DeleteData(ulong lobby, string key);

    // Only for a lobby we are in; "" if unset.
    string GetData(ulong lobby, string key);

    // The values of keys for a lobby we are not in (an unset key is ""); null when Steam has no such lobby or did not
    // answer. Throws only when cancelled.
    Task<IReadOnlyDictionary<string, string>?> ReadDataAsync(ulong lobby, IReadOnlyList<string> keys, CancellationToken ct);

    bool SetKind(ulong lobby, LobbyKind kind);

    void SetJoinable(ulong lobby, bool joinable);

    ulong Owner(ulong lobby);

    // Ourselves included.
    IReadOnlyList<ulong> Members(ulong lobby);

    // To every member, ourselves included.
    bool SendChat(ulong lobby, byte[] data, int length);

    string? PlayerName(ulong id);

    // Steam's "Ignored" relationship, from the friends list Steam sends at sign-in. QuickMatch treats such a player as
    // on its block list.
    bool BlockedOnSteam(ulong id);
}
