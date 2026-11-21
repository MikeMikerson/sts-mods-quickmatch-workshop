using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuickMatch.Matchmaking;

// At is in Unix seconds.
public sealed record BlockedPlayer(ulong Id, string? Name, long At);

// At is in Unix seconds.
public sealed record RemovedPlayer(ulong Id, long At);

public enum BlockResult
{
    Added,
    AlreadyBlocked,
    NotAPlayer,
    Self,
    CoolingDown,
}

// A blocked player is never in one lobby with this player: their own client refuses it whichever side the other player
// is on (BlockPolicy). Kept on this computer for the signed-in Steam account and never sent anywhere. A player removed
// from the list cannot be blocked again for ReblockCooldownSeconds, so blocking and unblocking cannot be used to throw
// a player out of lobby after lobby; the removal times are saved with the list, so the cooldown holds after a restart.
// Undo puts a removed entry back at any time, cooldown or not: it cancels the removal.
public sealed class BlockList
{
    public const long ReblockCooldownSeconds = 600;

    private readonly Dictionary<ulong, BlockedPlayer> _players = new();
    private readonly Dictionary<ulong, long> _removedAt = new();

    public event Action? Changed;

    public int Count => _players.Count;

    public bool Contains(ulong player) => _players.ContainsKey(player);

    public IReadOnlyList<BlockedPlayer> Players => _players.Values.OrderByDescending(p => p.At).ThenBy(p => p.Id).ToList();

    public bool Add(ulong player, string? name, long at, ulong self) => TryAdd(player, name, at, self) == BlockResult.Added;

    public BlockResult TryAdd(ulong player, string? name, long at, ulong self)
    {
        if (!BlockPolicy.IsIndividual(player))
        {
            return BlockResult.NotAPlayer;
        }
        if (player == self)
        {
            return BlockResult.Self;
        }
        if (_players.ContainsKey(player))
        {
            return BlockResult.AlreadyBlocked;
        }
        if (BlockableAgainAt(player, at) != null)
        {
            return BlockResult.CoolingDown;
        }
        _removedAt.Remove(player);
        _players[player] = new BlockedPlayer(player, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), at);
        Changed?.Invoke();
        return BlockResult.Added;
    }

    // In Unix seconds.
    public long? BlockableAgainAt(ulong player, long now) =>
        _removedAt.TryGetValue(player, out long removed) && now < removed + ReblockCooldownSeconds ? removed + ReblockCooldownSeconds : null;

    // Starts the player's cooldown. The removed entry is returned so an Undo can put it back as it was.
    public BlockedPlayer? Remove(ulong player, long at)
    {
        if (!_players.Remove(player, out BlockedPlayer? removed))
        {
            return null;
        }
        _removedAt[player] = at;
        Changed?.Invoke();
        return removed;
    }

    public bool Restore(BlockedPlayer player)
    {
        if (!BlockPolicy.IsIndividual(player.Id) || _players.ContainsKey(player.Id))
        {
            return false;
        }
        _removedAt.Remove(player.Id);
        _players[player.Id] = player;
        Changed?.Invoke();
        return true;
    }

    // What is saved with the list.
    public IReadOnlyList<RemovedPlayer> CoolingDown(long now) =>
        _removedAt.Where(r => now < r.Value + ReblockCooldownSeconds).OrderByDescending(r => r.Value).ThenBy(r => r.Key)
            .Select(r => new RemovedPlayer(r.Key, r.Value)).ToList();

    public void Load(IEnumerable<BlockedPlayer> players, IEnumerable<RemovedPlayer>? removed = null)
    {
        _players.Clear();
        _removedAt.Clear();
        foreach (BlockedPlayer player in players)
        {
            if (BlockPolicy.IsIndividual(player.Id))
            {
                _players[player.Id] = player;
            }
        }
        foreach (RemovedPlayer player in removed ?? [])
        {
            if (BlockPolicy.IsIndividual(player.Id) && !_players.ContainsKey(player.Id))
            {
                _removedAt[player.Id] = player.At;
            }
        }
    }
}

public enum BlockAction
{
    None,
    // We host: send the blocked player away and refuse them for the rest of this lobby.
    Expel,
    // The host is the blocked player: we leave and search again.
    Leave,
    // Another member is blocked: ask the host to send them away, and leave ourselves if it does not.
    AskHost,
}

// Each client enforces its own list: a host turns away a blocked joiner, a joiner leaves a lobby with a blocked member
// in it at once, and a member who sees a blocked player arrive has the host send them away (or leaves, if the host does
// not). Steam's own block (an "Ignored" relationship) counts as blocked too.
public static class BlockPolicy
{
    // Steam's universe, account type and instance bits of an individual's ID (SteamID64 76561197960265728 and up).
    public const ulong IndividualBase = 0x0110000100000000UL;

    public static bool IsIndividual(ulong id) => (id & 0xFFFFFFFF00000000UL) == IndividualBase && (uint)id != 0;

    // The list is checked first, as it needs no Steam call.
    public static bool IsBlocked(ulong player, BlockList list, Func<ulong, bool> blockedOnSteam) =>
        list.Contains(player) || blockedOnSteam(player);

    public static ulong FirstBlocked(IEnumerable<ulong> members, ulong self, Func<ulong, bool> isBlocked)
    {
        foreach (ulong member in members)
        {
            if (member != self && member != 0 && isBlocked(member))
            {
                return member;
            }
        }
        return 0;
    }

    // Leaves out the players this player chose to stay with when blocking them mid-run (stayedWith, kept with the run
    // record and gone with it).
    public static ulong FirstBlockedInRun(IEnumerable<ulong> players, ulong self, Func<ulong, bool> isBlocked, IReadOnlyCollection<ulong> stayedWith) =>
        FirstBlocked(players, self, player => isBlocked(player) && !stayedWith.Contains(player));

    public static bool HostRefusesRejoin(ulong joiner, Func<ulong, bool> isBlocked, IReadOnlyCollection<ulong> stayedWith) =>
        joiner != 0 && isBlocked(joiner) && !stayedWith.Contains(joiner);

    // refusedHere: the players a member of this lobby has asked the host to send away.
    public static bool HostRefuses(ulong joiner, Func<ulong, bool> isBlocked, IReadOnlySet<ulong> refusedHere) =>
        joiner != 0 && (refusedHere.Contains(joiner) || isBlocked(joiner));

    public static BlockAction InLobby(ulong blocked, ulong self, ulong host)
    {
        if (blocked == 0 || blocked == self)
        {
            return BlockAction.None;
        }
        if (self == host)
        {
            return BlockAction.Expel;
        }
        return blocked == host ? BlockAction.Leave : BlockAction.AskHost;
    }

    public static bool HostHonours(ulong sender, ulong target, ulong host, IReadOnlyCollection<ulong> members) =>
        sender != target && target != host && sender != 0 && target != 0 && members.Contains(sender) && members.Contains(target);

    // Takes a SteamID64 (7656119...), a Steam3 ID ([U:1:123]) or a profile link
    // (steamcommunity.com/profiles/7656119...). Custom profile names (steamcommunity.com/id/name) need Steam to look up
    // and are not taken.
    public static ulong? ParseSteamId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string value = text.Trim();
        int profiles = value.IndexOf("/profiles/", StringComparison.OrdinalIgnoreCase);
        if (profiles >= 0)
        {
            value = value[(profiles + "/profiles/".Length)..].Split('/', '?', '#')[0];
        }
        if (value.StartsWith("[U:1:", StringComparison.OrdinalIgnoreCase) && value.EndsWith(']'))
        {
            return uint.TryParse(value[5..^1], NumberStyles.None, CultureInfo.InvariantCulture, out uint account) && account != 0
                ? IndividualBase | account
                : null;
        }
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && IsIndividual(id) ? id : null;
    }
}
