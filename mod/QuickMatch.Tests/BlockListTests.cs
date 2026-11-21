using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

// Two players never share a lobby when either has blocked the other, whoever hosts, joins, merges or rejoins.
public class BlockListTests
{
    private const ulong A = 76561198000000001;
    private const ulong B = 76561198000000002;
    private const ulong C = 76561198000000003;
    private const ulong D = 76561198000000004;

    [Theory]
    [InlineData(76561197960265729UL, true)]
    [InlineData(76561198000000001UL, true)]
    [InlineData(0UL, false)]
    [InlineData(76561197960265728UL, false)] // account 0
    [InlineData(103582791429521408UL, false)] // a Steam group
    [InlineData(109775240917090305UL, false)] // a lobby
    public void OnlyIndividualAccountsCanBeBlocked(ulong id, bool individual)
    {
        Assert.Equal(individual, BlockPolicy.IsIndividual(id));
    }

    [Theory]
    [InlineData("76561198000000001", A)]
    [InlineData("  76561198000000001 ", A)]
    [InlineData("[U:1:39734273]", A)]
    [InlineData("https://steamcommunity.com/profiles/76561198000000001/", A)]
    [InlineData("steamcommunity.com/profiles/76561198000000001?l=english", A)]
    public void ParsesSteamIdsTypedByHand(string text, ulong expected)
    {
        Assert.Equal(expected, BlockPolicy.ParseSteamId(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("https://steamcommunity.com/id/someone")]
    [InlineData("12345")]
    [InlineData("-76561198000000001")]
    [InlineData("[U:1:0]")]
    [InlineData("103582791429521408")]
    public void RefusesTextThatIsNotAPlayersSteamId(string? text)
    {
        Assert.Null(BlockPolicy.ParseSteamId(text));
    }

    [Fact]
    public void AddsEachPlayerOnceAndNeverOurselves()
    {
        var list = new BlockList();
        int changes = 0;
        list.Changed += () => changes++;

        Assert.True(list.Add(B, " Bee ", 100, self: A));
        Assert.False(list.Add(B, "again", 200, self: A));
        Assert.False(list.Add(A, "me", 200, self: A));
        Assert.False(list.Add(0, null, 200, self: A));

        Assert.Equal(1, changes);
        Assert.Equal([new BlockedPlayer(B, "Bee", 100)], list.Players);
    }

    [Fact]
    public void RemoveGivesBackTheEntrySoUndoRestoresItUnchanged()
    {
        var list = new BlockList();
        list.Add(B, "Bee", 100, self: A);
        list.Add(C, null, 300, self: A);

        BlockedPlayer? removed = list.Remove(B, 400);
        Assert.Equal(new BlockedPlayer(B, "Bee", 100), removed);
        Assert.False(list.Contains(B));
        Assert.Null(list.Remove(B, 410));

        Assert.True(list.Restore(removed!));
        Assert.Equal([new BlockedPlayer(C, null, 300), new BlockedPlayer(B, "Bee", 100)], list.Players);
    }

    [Fact]
    public void TryAddSaysWhyAPlayerWasNotAdded()
    {
        var list = new BlockList();
        Assert.Equal(BlockResult.NotAPlayer, list.TryAdd(103582791429521408, null, 1, self: A));
        Assert.Equal(BlockResult.Self, list.TryAdd(A, null, 1, self: A));
        Assert.Equal(BlockResult.Added, list.TryAdd(B, null, 1, self: A));
        Assert.Equal(BlockResult.AlreadyBlocked, list.TryAdd(B, null, 2, self: A));
    }

    [Fact]
    public void ARemovedPlayerCannotBeBlockedAgainForTenMinutes()
    {
        var list = new BlockList();
        list.Add(B, "Bee", 100, self: A);
        list.Remove(B, 1000);
        int changes = 0;
        list.Changed += () => changes++;

        Assert.Equal(BlockResult.CoolingDown, list.TryAdd(B, "Bee", 1000, self: A));
        Assert.Equal(BlockResult.CoolingDown, list.TryAdd(B, "Bee", 1599, self: A));
        Assert.Equal(1600, list.BlockableAgainAt(B, 1599));
        Assert.Null(list.BlockableAgainAt(C, 1599));
        Assert.Equal(BlockResult.Added, list.TryAdd(C, null, 1599, self: A));
        Assert.Equal(1, changes);

        Assert.Null(list.BlockableAgainAt(B, 1600));
        Assert.Equal(BlockResult.Added, list.TryAdd(B, "Bee", 1600, self: A));
        Assert.Equal(new BlockedPlayer(B, "Bee", 1600), list.Players[0]);
    }

    [Fact]
    public void UndoInsideTheCooldownPutsTheEntryBackAndEndsTheCooldown()
    {
        var list = new BlockList();
        list.Add(B, "Bee", 100, self: A);
        BlockedPlayer removed = list.Remove(B, 1000)!;

        Assert.True(list.Restore(removed));
        Assert.Equal([new BlockedPlayer(B, "Bee", 100)], list.Players);
        Assert.Null(list.BlockableAgainAt(B, 1001));
        Assert.Empty(list.CoolingDown(1001));

        // A second Remove starts a new cooldown from its own time.
        list.Remove(B, 1200);
        Assert.Equal(1800, list.BlockableAgainAt(B, 1201));
    }

    [Fact]
    public void TheCooldownIsSavedWithTheListAndHoldsAfterARestart()
    {
        var list = new BlockList();
        list.Add(B, "Bee", 100, self: A);
        list.Add(C, "Sea", 200, self: A);
        list.Add(D, null, 300, self: A);
        list.Remove(B, 1000);
        list.Remove(C, 1300);

        // Saved at 1700: B's cooldown has passed and is dropped, C's is kept.
        IReadOnlyList<RemovedPlayer> saved = list.CoolingDown(1700);
        Assert.Equal([new RemovedPlayer(C, 1300)], saved);

        var restarted = new BlockList();
        restarted.Load(list.Players, saved);
        Assert.Equal([D], restarted.Players.Select(p => p.Id));
        Assert.Equal(BlockResult.CoolingDown, restarted.TryAdd(C, "Sea", 1800, self: A));
        Assert.Equal(BlockResult.Added, restarted.TryAdd(B, "Bee", 1800, self: A));
        Assert.Equal(BlockResult.Added, restarted.TryAdd(C, "Sea", 1900, self: A));
    }

    [Fact]
    public void LoadDropsARemovalForAPlayerStillOnTheListOrNotAPlayer()
    {
        var list = new BlockList();
        list.Load([new BlockedPlayer(B, "Bee", 1)], [new RemovedPlayer(B, 5), new RemovedPlayer(0, 5), new RemovedPlayer(C, 5)]);
        Assert.Null(list.BlockableAgainAt(B, 6));
        Assert.Equal([new RemovedPlayer(C, 5)], list.CoolingDown(6));
    }

    [Fact]
    public void LoadSkipsEntriesThatAreNotPlayersAndRaisesNothing()
    {
        var list = new BlockList();
        list.Changed += () => Assert.Fail("load raised Changed");
        list.Load([new BlockedPlayer(B, "Bee", 1), new BlockedPlayer(0, "none", 2), new BlockedPlayer(103582791429521408, "group", 3)]);
        Assert.Equal([B], list.Players.Select(p => p.Id));
    }

    [Fact]
    public void SteamsOwnBlockCountsAsBlocked()
    {
        var list = new BlockList();
        list.Add(B, null, 1, self: A);
        var steam = new HashSet<ulong> { C };
        Assert.True(BlockPolicy.IsBlocked(B, list, steam.Contains));
        Assert.True(BlockPolicy.IsBlocked(C, list, steam.Contains));
        Assert.False(BlockPolicy.IsBlocked(D, list, steam.Contains));
    }

    [Fact]
    public void ListIsCheckedBeforeAskingSteam()
    {
        var list = new BlockList();
        list.Add(B, null, 1, self: A);
        Assert.True(BlockPolicy.IsBlocked(B, list, _ => throw new InvalidOperationException("asked Steam")));
    }

    [Fact]
    public void FirstBlockedSkipsOurselves()
    {
        var blocked = new HashSet<ulong> { A, C };
        Assert.Equal(C, BlockPolicy.FirstBlocked([A, B, C], self: A, blocked.Contains));
        Assert.Equal(0UL, BlockPolicy.FirstBlocked([A, B], self: A, blocked.Contains));
    }

    [Fact]
    public void HostRefusesItsOwnBlocksAndPlayersAMemberSentAway()
    {
        var blocked = new HashSet<ulong> { B };
        var refusedHere = new HashSet<ulong> { C };
        Assert.True(BlockPolicy.HostRefuses(B, blocked.Contains, refusedHere));
        Assert.True(BlockPolicy.HostRefuses(C, blocked.Contains, refusedHere));
        Assert.False(BlockPolicy.HostRefuses(D, blocked.Contains, refusedHere));
    }

    [Fact]
    public void WhatAClientDoesAboutABlockedPlayerInItsLobby()
    {
        Assert.Equal(BlockAction.Expel, BlockPolicy.InLobby(B, self: A, host: A));
        Assert.Equal(BlockAction.Leave, BlockPolicy.InLobby(B, self: A, host: B));
        Assert.Equal(BlockAction.AskHost, BlockPolicy.InLobby(C, self: A, host: B));
        Assert.Equal(BlockAction.None, BlockPolicy.InLobby(0, self: A, host: B));
    }

    [Fact]
    public void HostHonoursOnlyRequestsAboutAnotherMember()
    {
        ulong[] members = [A, B, C];
        Assert.True(BlockPolicy.HostHonours(sender: B, target: C, host: A, members));
        Assert.False(BlockPolicy.HostHonours(sender: B, target: A, host: A, members)); // the host itself
        Assert.False(BlockPolicy.HostHonours(sender: B, target: B, host: A, members));
        Assert.False(BlockPolicy.HostHonours(sender: D, target: C, host: A, members)); // not a member
        Assert.False(BlockPolicy.HostHonours(sender: B, target: D, host: A, members)); // not here
    }

    // A small model of a lobby run by these rules: each player's own client applies its own list only.

    private static Func<ulong, bool> Blocks(Dictionary<ulong, ulong[]> lists, ulong player) =>
        id => lists.TryGetValue(player, out ulong[]? blocked) && blocked.Contains(id);

    // The lobby after joiner asks to join: the joiner is in it only if no client turned them away or left.
    private static List<ulong> Join(List<ulong> lobby, ulong host, ulong joiner, Dictionary<ulong, ulong[]> lists, HashSet<ulong> refusedHere)
    {
        // The host's join check, before the game adds the joiner.
        if (BlockPolicy.HostRefuses(joiner, Blocks(lists, host), refusedHere))
        {
            return lobby;
        }
        // The joiner's check of the players in the join response.
        if (BlockPolicy.FirstBlocked(lobby, joiner, Blocks(lists, joiner)) != 0)
        {
            return lobby;
        }
        var after = new List<ulong>(lobby) { joiner };
        // Each member (host included) sees the new player arrive.
        foreach (ulong member in lobby)
        {
            if (!Blocks(lists, member)(joiner))
            {
                continue;
            }
            switch (BlockPolicy.InLobby(joiner, member, host))
            {
                case BlockAction.Expel:
                    refusedHere.Add(joiner);
                    after.Remove(joiner);
                    break;
                case BlockAction.AskHost when BlockPolicy.HostHonours(member, joiner, host, after):
                    refusedHere.Add(joiner);
                    after.Remove(joiner);
                    break;
            }
        }
        return after;
    }

    private static bool Together(List<ulong> lobby, ulong x, ulong y) => lobby.Contains(x) && lobby.Contains(y);

    public static TheoryData<ulong, ulong> Blocks_ =>
        new() { { A, B }, { B, A } };

    [Theory]
    [MemberData(nameof(Blocks_))]
    public void NeverTogetherWhicheverHostsOrJoins(ulong blocker, ulong blockedPlayer)
    {
        var lists = new Dictionary<ulong, ulong[]> { [blocker] = [blockedPlayer] };
        ulong[] players = [A, B, C];
        // Every order in which A, B and C could arrive, the first one hosting.
        foreach (ulong[] order in Orders(players))
        {
            var lobby = new List<ulong> { order[0] };
            var refusedHere = new HashSet<ulong>();
            foreach (ulong joiner in order.Skip(1))
            {
                lobby = Join(lobby, order[0], joiner, lists, refusedHere);
            }
            Assert.False(Together(lobby, A, B), $"order {string.Join(",", order.Select(Name))} put A and B together");
            Assert.Contains(C, lobby);
        }
    }

    [Fact]
    public void NobodyBlockedMeansEveryoneJoins()
    {
        var lists = new Dictionary<ulong, ulong[]>();
        foreach (ulong[] order in Orders([A, B, C]))
        {
            var lobby = new List<ulong> { order[0] };
            foreach (ulong joiner in order.Skip(1))
            {
                lobby = Join(lobby, order[0], joiner, lists, new HashSet<ulong>());
            }
            Assert.Equal(3, lobby.Count);
        }
    }

    [Theory]
    [MemberData(nameof(Blocks_))]
    public void AMergingGroupNeverMovesInWithABlockedPlayer(ulong blocker, ulong blockedPlayer)
    {
        // A group (host C with B) merges into D's lobby, in which A waits. Each mover joins the target in turn.
        var lists = new Dictionary<ulong, ulong[]> { [blocker] = [blockedPlayer] };
        var target = new List<ulong> { D, A };
        var refusedHere = new HashSet<ulong>();
        foreach (ulong mover in new[] { C, B })
        {
            target = Join(target, D, mover, lists, refusedHere);
        }
        Assert.False(Together(target, A, B));
        Assert.Contains(C, target);
    }

    [Fact]
    public void ARefusedPlayerStaysRefusedInThatLobby()
    {
        // C asked the host D to send B away; B tries again and the host, who has not blocked B, still refuses.
        var lists = new Dictionary<ulong, ulong[]> { [C] = [B] };
        var refusedHere = new HashSet<ulong>();
        var lobby = Join([D], D, C, lists, refusedHere);
        lobby = Join(lobby, D, B, lists, refusedHere);
        Assert.Equal([D, C], lobby);
        Assert.Contains(B, refusedHere);
        lists.Clear();
        Assert.Equal([D, C], Join(lobby, D, B, lists, refusedHere));
    }

    [Theory]
    [MemberData(nameof(Blocks_))]
    public void ARejoinNeverReopensARunWithABlockedPlayer(ulong blocker, ulong blockedPlayer)
    {
        // The saved run has A, B and C. Whoever reopens it, the host's check and each joiner's check of the saved
        // players keep A and B apart; C, who blocked nobody and whom nobody blocked, always gets back in.
        var lists = new Dictionary<ulong, ulong[]> { [blocker] = [blockedPlayer] };
        ulong[] saved = [A, B, C];
        foreach (ulong host in saved)
        {
            var lobby = new List<ulong> { host };
            foreach (ulong joiner in saved.Where(p => p != host))
            {
                bool hostRefuses = Blocks(lists, host)(joiner);
                bool joinerLeaves = BlockPolicy.FirstBlocked(saved, joiner, Blocks(lists, joiner)) != 0;
                if (!hostRefuses && !joinerLeaves)
                {
                    lobby.Add(joiner);
                }
            }
            Assert.False(Together(lobby, A, B), $"host {Name(host)} reopened the run with A and B");
            Assert.Contains(C, lobby);
        }
    }

    [Theory]
    [MemberData(nameof(Blocks_))]
    public void StayingWithAPlayerBlockedMidRunLetsThatRunsRejoinsThrough(ulong blocker, ulong blockedPlayer)
    {
        // The blocker chose Stay: the run record exempts that player for this run, so whoever reopens it, all
        // three get back in. Nobody else is exempt.
        var lists = new Dictionary<ulong, ulong[]> { [blocker] = [blockedPlayer] };
        var stayed = new Dictionary<ulong, ulong[]> { [blocker] = [blockedPlayer] };
        ulong[] saved = [A, B, C];
        foreach (ulong host in saved)
        {
            var lobby = new List<ulong> { host };
            foreach (ulong joiner in saved.Where(p => p != host))
            {
                bool hostRefuses = BlockPolicy.HostRefusesRejoin(joiner, Blocks(lists, host), stayed.GetValueOrDefault(host, []));
                bool joinerLeaves = BlockPolicy.FirstBlockedInRun(saved, joiner, Blocks(lists, joiner), stayed.GetValueOrDefault(joiner, [])) != 0;
                if (!hostRefuses && !joinerLeaves)
                {
                    lobby.Add(joiner);
                }
            }
            Assert.Equal(saved.Order(), lobby.Order());
        }
    }

    [Fact]
    public void StayingCoversOnlyThePlayerStayedWith()
    {
        // A blocked B and C mid-run, and stayed only for B: C is still kept out of the run's rejoins.
        var blocked = new HashSet<ulong> { B, C };
        ulong[] stayedWith = [B];
        Assert.False(BlockPolicy.HostRefusesRejoin(B, blocked.Contains, stayedWith));
        Assert.True(BlockPolicy.HostRefusesRejoin(C, blocked.Contains, stayedWith));
        Assert.Equal(C, BlockPolicy.FirstBlockedInRun([A, B, C], A, blocked.Contains, stayedWith));
        Assert.Equal(0UL, BlockPolicy.FirstBlockedInRun([A, B], A, blocked.Contains, stayedWith));
        // Without the exemption (Leave, or a new run's record) the rejoin is strict again.
        Assert.True(BlockPolicy.HostRefusesRejoin(B, blocked.Contains, []));
    }

    private static string Name(ulong id) => id switch { A => "A", B => "B", C => "C", D => "D", _ => id.ToString() };

    private static IEnumerable<ulong[]> Orders(ulong[] players)
    {
        if (players.Length <= 1)
        {
            yield return players;
            yield break;
        }
        foreach (ulong first in players)
        {
            foreach (ulong[] rest in Orders(players.Where(p => p != first).ToArray()))
            {
                yield return [first, .. rest];
            }
        }
    }
}
