using System.Linq;
using QuickMatch.Matchmaking;
using Xunit;

namespace QuickMatch.Tests;

public class RecentPlayersTests
{
    private const ulong A = 76561198000000001;
    private const ulong B = 76561198000000002;
    private const ulong C = 76561198000000003;

    [Fact]
    public void The_latest_run_is_listed_first_in_the_run_s_order()
    {
        var recent = new RecentPlayers();
        recent.Note([(A, "Ann")], at: 100);
        recent.Note([(B, "Bo"), (C, "Cy")], at: 200);
        Assert.Equal(new[] { B, C, A }, recent.Players.Select(p => p.Id));
        Assert.Equal(new long[] { 200, 200, 100 }, recent.Players.Select(p => p.At));
    }

    [Fact]
    public void A_player_met_again_moves_to_the_top_once_with_the_new_date()
    {
        var recent = new RecentPlayers();
        recent.Note([(A, "Ann"), (B, "Bo")], at: 100);
        recent.Note([(B, "Bo")], at: 200);
        Assert.Equal(new[] { new RecentPlayer(B, "Bo", 200), new RecentPlayer(A, "Ann", 100) }, recent.Players);
    }

    [Fact]
    public void A_saved_name_is_kept_when_none_is_known_and_replaced_by_a_new_one()
    {
        var recent = new RecentPlayers();
        recent.Note([(A, "Ann")], at: 100);
        recent.Note([(A, " ")], at: 200);
        Assert.Equal("Ann", recent.Players.Single().Name);
        recent.Note([(A, " Anna ")], at: 300);
        Assert.Equal("Anna", recent.Players.Single().Name);
    }

    [Fact]
    public void Only_the_latest_players_are_kept()
    {
        var recent = new RecentPlayers();
        for (ulong i = 0; i < RecentPlayers.Max + 3; i++)
        {
            recent.Note([(A + i, null)], at: (long)i);
        }
        Assert.Equal(RecentPlayers.Max, recent.Players.Count);
        Assert.Equal(A + RecentPlayers.Max + 2, recent.Players[0].Id);
        Assert.DoesNotContain(recent.Players, p => p.Id == A + 2);
    }

    [Fact]
    public void Anything_that_is_not_a_player_is_left_out_and_saves_nothing()
    {
        var recent = new RecentPlayers();
        int changes = 0;
        recent.Changed += () => changes++;
        recent.Note([(0, "nobody"), (109775240917090305, "a lobby")], at: 100);
        Assert.Empty(recent.Players);
        Assert.Equal(0, changes);
        recent.Note([(A, "Ann")], at: 100);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_saved_list_loads_in_its_order_without_repeats_or_extras()
    {
        var saved = Enumerable.Range(0, RecentPlayers.Max + 2).Select(i => new RecentPlayer(A + (ulong)i, null, 500 - i)).ToList();
        saved.Insert(1, saved[0]);
        saved.Insert(0, new RecentPlayer(0, "nobody", 900));
        var recent = new RecentPlayers();
        recent.Load(saved);
        Assert.Equal(Enumerable.Range(0, RecentPlayers.Max).Select(i => A + (ulong)i), recent.Players.Select(p => p.Id));
    }
}
