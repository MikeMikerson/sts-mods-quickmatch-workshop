using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickMatch.Lobby;
using QuickMatch.SteamKit;

namespace QuickMatch.Platforms.Android;

/// <summary>
/// QuickMatch's lobby interface in the Android launcher, answered over SteamKit2 because the launcher
/// has no Steamworks (its libsteam_api is a stub). The lobbies, their data and their chat are the same
/// ones desktop players reach through Steamworks and iPhone players through the iPhone port, so all of
/// them match each other. Mirrors the iPhone port's PhoneLobbyBackend. The SteamKit2 side lives in
/// the embedded QuickMatch.SteamKit assembly (see <see cref="AndroidPlatform"/>); this class only
/// holds it and passes .NET values, so it loads on desktop too, where it is never used.
/// </summary>
internal sealed class AndroidLobbyBackend : ILobbyBackend
{
    /// <summary>The launcher allows 15 s per connection attempt.</summary>
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A failed sign-in is tried again when QuickMatch next asks, but not more often than this.</summary>
    private static readonly TimeSpan SignInRetry = TimeSpan.FromSeconds(30);

    private readonly SteamKitLobbyService _lobbies;
    private readonly SteamKitLog _log;
    private int _signingIn;
    private long _lastSignInTicks;

    public AndroidLobbyBackend(SteamKitLobbyService lobbies, SteamKitLog log)
    {
        _lobbies = lobbies;
        _log = log;
        lobbies.ChatReceived += (lobby, sender, data) => Changed?.Invoke(new LobbyEvent(LobbyEventKind.Chat, lobby, sender, data));
        lobbies.MemberJoined += (lobby, member) => Changed?.Invoke(new LobbyEvent(LobbyEventKind.MemberJoined, lobby, member));
        lobbies.MemberLeft += (lobby, member) => Changed?.Invoke(new LobbyEvent(LobbyEventKind.MemberLeft, lobby, member));
    }

    /// <summary>
    /// True once this connection has signed in, which also tells us our Steam ID. Until then each
    /// question starts a sign-in in the background (see <see cref="StartSignIn"/>).
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (LocalId != 0)
            {
                return true;
            }
            StartSignIn();
            return false;
        }
    }

    public ulong LocalId => _lobbies.LocalId;

    /// <summary>The phone's clock; QuickMatch only uses it to tell older lobbies from newer ones.</summary>
    public uint ServerTime => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public event Action<LobbyEvent>? Changed;

    /// <summary>
    /// Signs in on a worker thread unless signed in, already signing in, or the last try was under
    /// <see cref="SignInRetry"/> ago. The connection closes itself when idle; lobby calls reopen it.
    /// </summary>
    public void StartSignIn()
    {
        long now = DateTime.UtcNow.Ticks;
        if (LocalId != 0 || now - Interlocked.Read(ref _lastSignInTicks) < SignInRetry.Ticks
            || Interlocked.Exchange(ref _signingIn, 1) == 1)
        {
            return;
        }
        Interlocked.Exchange(ref _lastSignInTicks, now);
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(SignInTimeout);
                ulong id = await _lobbies.SignInAsync(timeout.Token).ConfigureAwait(false);
                _log.Info($"signed in to Steam for lobbies as {id}");
            }
            catch (Exception e)
            {
                _log.Warn($"Steam sign-in for lobbies failed, Quick Match waits: {e.GetType().Name}");
            }
            finally
            {
                Interlocked.Exchange(ref _signingIn, 0);
            }
        });
    }

    public Task<ulong?> CreateAsync(LobbyKind kind, int maxMembers, CancellationToken ct) => _lobbies.CreateAsync((int)kind, maxMembers, ct);

    public async Task<LobbyJoinResult> JoinAsync(ulong lobby, CancellationToken ct) => Map(await _lobbies.JoinAsync(lobby, ct));

    /// <summary>SteamKit2's EChatRoomEnterResponse values (Steamworks' k_EChatRoomEnterResponse*).</summary>
    internal static class EnterResponse
    {
        public const int Success = 1, DoesntExist = 2, NotAllowed = 3, Full = 4, Error = 5, Banned = 6, Limited = 7,
            ClanDisabled = 8, CommunityBan = 9, MemberBlockedYou = 10, YouBlockedMember = 11,
            // k_EChatRoomEnterResponseRatelimitExceeded; SteamKit2's enum has no name for it.
            RatelimitExceeded = 15;
    }

    internal static LobbyJoinResult Map(int response) => response switch
    {
        EnterResponse.Success => LobbyJoinResult.Ok,
        EnterResponse.DoesntExist => LobbyJoinResult.DoesNotExist,
        EnterResponse.NotAllowed => LobbyJoinResult.NotAllowed,
        EnterResponse.Full => LobbyJoinResult.Full,
        EnterResponse.Banned => LobbyJoinResult.Banned,
        EnterResponse.Limited => LobbyJoinResult.Limited,
        EnterResponse.ClanDisabled
            or EnterResponse.CommunityBan
            or EnterResponse.MemberBlockedYou
            or EnterResponse.YouBlockedMember => LobbyJoinResult.Blocked,
        EnterResponse.RatelimitExceeded => LobbyJoinResult.RateLimited,
        _ => LobbyJoinResult.Error,
    };

    public void Leave(ulong lobby) => _lobbies.Leave(lobby);

    public async Task<IReadOnlyList<LobbyListing>?> SearchAsync(LobbyQuery query, CancellationToken ct)
    {
        IReadOnlyList<(ulong Id, int Members, IReadOnlyDictionary<string, string> Data)>? found =
            await _lobbies.SearchAsync(query.Equal, query.MinFreeSlots, query.MaxResults, query.Nearby, ct);
        return found == null ? null : Listings(found, query.ReadKeys);
    }

    internal static IReadOnlyList<LobbyListing> Listings(IEnumerable<(ulong Id, int Members, IReadOnlyDictionary<string, string> Data)> found, IReadOnlyList<string> readKeys) =>
        found.Select(lobby => new LobbyListing(lobby.Id, lobby.Members, ReadKeys(lobby.Data, readKeys))).ToList();

    public async Task<IReadOnlyDictionary<string, string>?> ReadDataAsync(ulong lobby, IReadOnlyList<string> keys, CancellationToken ct)
    {
        IReadOnlyDictionary<string, string>? data = await _lobbies.ReadDataAsync(lobby, ct);
        return data == null ? null : ReadKeys(data, keys);
    }

    private static Dictionary<string, string> ReadKeys(IReadOnlyDictionary<string, string> data, IReadOnlyList<string> keys) =>
        keys.ToDictionary(key => key, key => data.GetValueOrDefault(key, ""));

    public void SetData(ulong lobby, string key, string value) => _lobbies.SetData(lobby, key, value);

    public void DeleteData(ulong lobby, string key) => _lobbies.DeleteData(lobby, key);

    public string GetData(ulong lobby, string key) => _lobbies.GetData(lobby, key);

    public bool SetKind(ulong lobby, LobbyKind kind) => _lobbies.SetType(lobby, (int)kind);

    public void SetJoinable(ulong lobby, bool joinable) => _lobbies.SetJoinable(lobby, joinable);

    public ulong Owner(ulong lobby) => _lobbies.Owner(lobby);

    public IReadOnlyList<ulong> Members(ulong lobby) => _lobbies.Members(lobby);

    public bool SendChat(ulong lobby, byte[] data, int length, ulong target) => _lobbies.SendChat(lobby, data, length, target);

    /// <summary>A lobby member's Steam name, or ours; null if not known.</summary>
    public string? PlayerName(ulong id) => _lobbies.MemberName(id) ?? (id != 0 && id == LocalId ? _lobbies.OwnName : null);

    public bool BlockedOnSteam(ulong id) => _lobbies.BlockedOnSteam(id);

    public bool ShowProfile(ulong id) => false;
}
