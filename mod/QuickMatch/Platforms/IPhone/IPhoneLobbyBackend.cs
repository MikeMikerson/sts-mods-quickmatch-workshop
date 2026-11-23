using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickMatch.Lobby;
using STS2Mobile.ModApi;

namespace QuickMatch.Platforms.IPhone;

internal sealed class IPhoneLobbyBackend : ILobbyBackend
{
    private readonly ISteamLobbies _lobbies;

    public IPhoneLobbyBackend(ISteamLobbies lobbies)
    {
        _lobbies = lobbies;
        _lobbies.Changed += OnLobbyUpdate;
    }

    public bool IsAvailable => LocalId != 0;
    public ulong LocalId => _lobbies.LocalId;
    public uint ServerTime => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public event Action<LobbyEvent>? Changed;

    private void OnLobbyUpdate(LobbyUpdate update)
    {
        LobbyEventKind? kind = update.Change switch
        {
            LobbyChange.Chat => LobbyEventKind.Chat,
            LobbyChange.MemberJoined => LobbyEventKind.MemberJoined,
            LobbyChange.MemberLeft => LobbyEventKind.MemberLeft,
            _ => null,
        };
        if (kind.HasValue)
        {
            Changed?.Invoke(new LobbyEvent(kind.Value, update.Lobby, update.Member, update.Data));
        }
    }

    public Task<ulong?> CreateAsync(LobbyKind kind, int maxMembers, CancellationToken ct) =>
        _lobbies.CreateAsync((int)kind, maxMembers, ct);

    public async Task<LobbyJoinResult> JoinAsync(ulong lobby, CancellationToken ct) =>
        (await _lobbies.JoinAsync(lobby, ct)) switch
        {
            LobbyEntry.Ok => LobbyJoinResult.Ok,
            LobbyEntry.DoesNotExist => LobbyJoinResult.DoesNotExist,
            LobbyEntry.NotAllowed => LobbyJoinResult.NotAllowed,
            LobbyEntry.Full => LobbyJoinResult.Full,
            LobbyEntry.Banned => LobbyJoinResult.Banned,
            LobbyEntry.Limited => LobbyJoinResult.Limited,
            LobbyEntry.Blocked => LobbyJoinResult.Blocked,
            LobbyEntry.RateLimited => LobbyJoinResult.RateLimited,
            _ => LobbyJoinResult.Error,
        };

    public void Leave(ulong lobby) => _lobbies.Leave(lobby);

    public async Task<IReadOnlyList<LobbyListing>?> SearchAsync(LobbyQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // The launcher can only search worldwide. Let matchmaking advance to its worldwide pass.
        if (query.Nearby)
        {
            return [];
        }
        var found = await _lobbies.SearchAsync(query.Equal, query.MinFreeSlots, query.MaxResults, ct);
        return found?.Select(lobby => new LobbyListing(lobby.Id, lobby.Members,
            query.ReadKeys.ToDictionary(key => key, key => lobby.Data.GetValueOrDefault(key, "")))).ToList();
    }

    // Steam invite links are handled by the desktop client's callback.
    public Task<IReadOnlyDictionary<string, string>?> ReadDataAsync(ulong lobby, IReadOnlyList<string> keys, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(null);

    public void SetData(ulong lobby, string key, string value) => _lobbies.SetData(lobby, key, value);
    public void DeleteData(ulong lobby, string key) => _lobbies.DeleteData(lobby, key);
    public string GetData(ulong lobby, string key) => _lobbies.GetData(lobby, key);
    public bool SetKind(ulong lobby, LobbyKind kind) => _lobbies.SetType(lobby, (int)kind);
    public void SetJoinable(ulong lobby, bool joinable) => _lobbies.SetJoinable(lobby, joinable);
    public ulong Owner(ulong lobby) => _lobbies.Owner(lobby);
    public IReadOnlyList<ulong> Members(ulong lobby) => _lobbies.Members(lobby);
    public bool SendChat(ulong lobby, byte[] data, int length, ulong target) => _lobbies.SendChat(lobby, data, length, target);
    public string? PlayerName(ulong id) => _lobbies.MemberName(id);
    public bool BlockedOnSteam(ulong id) => _lobbies.BlockedOnSteam(id);
    public bool ShowProfile(ulong id) => Multiplayer.RequestSteamProfile(id);
}
