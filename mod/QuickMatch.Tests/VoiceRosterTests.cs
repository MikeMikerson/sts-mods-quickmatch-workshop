using QuickMatch.Voice;
using Xunit;

namespace QuickMatch.Tests;

public class VoiceRosterTests
{
    private const ulong Lobby = 109775241000000001;
    private const ulong Me = 76561198000000011;
    private const ulong Bob = 76561198000000012;
    private const ulong Carol = 76561198000000013;

    [Fact]
    public void A_teammate_still_in_the_lobby_after_the_relay_drops_is_playing_without_voice()
    {
        var roster = new VoiceRoster();
        roster.Update(Lobby, Me, [Me, Bob], [Bob]);
        Assert.Equal([(Bob, TeammateState.OnVoice)], roster.Teammates);

        roster.Update(Lobby, Me, [Me, Bob], []);

        Assert.Equal([(Bob, TeammateState.NoVoice)], roster.Teammates);
    }

    [Fact]
    public void A_teammate_who_leaves_stays_listed_as_left_while_the_others_play_on()
    {
        var roster = new VoiceRoster();
        roster.Update(Lobby, Me, [Me, Bob, Carol], [Bob, Carol]);

        roster.Update(Lobby, Me, [Me, Carol], []);

        Assert.Equal([(Bob, TeammateState.Left), (Carol, TeammateState.NoVoice)], roster.Teammates);
    }

    [Fact]
    public void A_teammate_who_comes_back_is_no_longer_left()
    {
        var roster = new VoiceRoster();
        roster.Update(Lobby, Me, [Me, Bob], []);
        roster.Update(Lobby, Me, [Me], []);

        roster.Update(Lobby, Me, [Me, Bob], [Bob]);

        Assert.Equal([(Bob, TeammateState.OnVoice)], roster.Teammates);
    }

    [Fact]
    public void A_voice_peer_the_lobby_list_has_not_caught_up_with_counts_as_present()
    {
        var roster = new VoiceRoster();

        roster.Update(Lobby, Me, [Me], [Bob]);

        Assert.Equal([(Bob, TeammateState.OnVoice)], roster.Teammates);
    }

    [Fact]
    public void A_new_match_forgets_the_last_matchs_players()
    {
        var roster = new VoiceRoster();
        roster.Update(Lobby, Me, [Me, Bob], [Bob]);

        roster.Update(Lobby + 1, Me, [Me, Carol], []);

        Assert.Equal([(Carol, TeammateState.NoVoice)], roster.Teammates);
    }
}
