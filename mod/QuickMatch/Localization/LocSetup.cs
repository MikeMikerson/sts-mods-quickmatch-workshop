using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using QuickMatch.Patches;

namespace QuickMatch.Localization;

// Merges the mod's strings into the game's main_menu_ui table, where NSubmenuButton.SetLocalization looks up
// KEY.title and KEY.description.
//
// Patches the private LocManager.SetLanguageInternal rather than Initialize: every language load (first start
// and later language changes) goes through it with a fresh set of tables, and merging before it runs means
// the strings exist before the game fires its locale-change notification. LocManager.Instance is still null
// during the first call, so the tables are taken from the method argument.
[PatchFeature(GameFeature.Core)]
[HarmonyPatch(typeof(LocManager), "SetLanguageInternal")]
internal static class LocSetup
{
    public const string Table = "main_menu_ui";

    private static readonly Dictionary<string, string> Entries = new()
    {
        { "QUICKMATCH.title", "Quick Match" },
        { "QUICKMATCH.description", "Team up with random players who are also looking for a group." },
        { "QUICKMATCH.LOCKED.description", "Quick Match needs Steam." },
        { "QUICKMATCH_SAVED.title", "Quick Match" },
        { "QUICKMATCH_SAVED.description", "Team up with random players who are also looking for a group." },
        { "QUICKMATCH_SAVED.LOCKED.description", "Finish or abandon your saved co-op run first." },
        { "QUICKMATCH.HUB.title", "Quick Match" },
        { "QUICKMATCH.HUB.rejoin_title", "Rejoin Run" },
        { "QUICKMATCH_PLAY.title", "Play Now" },
        { "QUICKMATCH_PLAY.description", "Join the first group that fits." },
        { "QUICKMATCH_CLIMB.title", "Climb" },
        { "QUICKMATCH_CLIMB.description", "Play at your highest Ascension, so a win unlocks the next." },
        { "QUICKMATCH_OPTIONS.title", "Match Options" },
        { "QUICKMATCH_OPTIONS.description", "Party size, Ascension, voice and blocked players." },
        { "QUICKMATCH_OPTIONS.description_modes", "Game mode, party size, Ascension, voice and blocked players." },
        { "QUICKMATCH.SUMMARY.size_any", "Any party size" },
        { "QUICKMATCH.SUMMARY.size", "{size} players" },
        { "QUICKMATCH.SUMMARY.asc_any", "Any Ascension" },
        { "QUICKMATCH.SUMMARY.asc_one", "Ascension {level}" },
        { "QUICKMATCH.SUMMARY.asc_many", "Ascensions {levels}" },
        { "QUICKMATCH.MODE.standard", "Standard" },
        { "QUICKMATCH.MODE.blitz", "Blitz" },
        { "QUICKMATCH.MODE.dread", "Dread" },
        { "QUICKMATCH.SUMMARY.voice_on", "Voice on" },
        { "QUICKMATCH.SUMMARY.voice_off", "Voice off" },
        { "QUICKMATCH.OPTIONS.title", "Match Options" },
        { "QUICKMATCH.OPTIONS.hint", "Saved for every Quick Match. Play Now and Climb use them." },
        { "QUICKMATCH.OPTIONS.mode", "Game mode" },
        { "QUICKMATCH.OPTIONS.size", "Party size" },
        { "QUICKMATCH.OPTIONS.ascension", "Ascension" },
        { "QUICKMATCH.OPTIONS.voice", "Voice chat" },
        { "QUICKMATCH.OPTIONS.voice_hint", "You match only with players who chose the same. With it off, nobody hears you and you hear nobody." },
        { "QUICKMATCH.OPTIONS.voice_unavailable", "Voice chat is unavailable: this version of the game changed a part of it that Quick Match needs. You match only with players without voice until Quick Match is updated." },
        { "QUICKMATCH.OPTIONS.blocked", "Blocked players" },
        { "QUICKMATCH.OPTIONS.blocked_open", "See List" },
        { "QUICKMATCH.OPTIONS.any", "Any" },
        { "QUICKMATCH.OPTIONS.choose", "Choose..." },
        { "QUICKMATCH.OPTIONS.done", "Done" },
        { "QUICKMATCH.ASC.save", "Save" },
        { "QUICKMATCH.ASC.climb", "Climb A{level}" },
        { "QUICKMATCH.ASC.progress_at", "Play at Ascension {level}, your highest, so a win unlocks the next." },
        { "QUICKMATCH.waiting_now", "{count} players waiting now" },
        { "QUICKMATCH.waiting_now_one", "1 player waiting now" },
        { "QUICKMATCH.ASC.waiting", "{count} waiting" },
        { "QUICKMATCH.ASC.title", "Choose Ascensions" },
        { "QUICKMATCH.ASC.hint", "You join a group at any of these. If none is open, you open a lobby at the lowest." },
        { "QUICKMATCH.ASC.level", "A{level}" },
        { "QUICKMATCH.ASC.cancel", "Cancel" },
        { "QUICKMATCH.ALONE.size", "Nobody looking for {size} players yet." },
        { "QUICKMATCH.SHORT.ask", "Start with {players} now?" },
        { "QUICKMATCH.SHORT.hint", "Nobody new has joined for 2 minutes. The run starts short only if everyone here says yes; no one can join while you decide." },
        { "QUICKMATCH.SHORT.yes", "Start Now" },
        { "QUICKMATCH.SHORT.no", "Keep Waiting" },
        { "QUICKMATCH.SHORT.agreed", "Everyone agreed: the run starts with {players} when all press the checkmark." },
        { "QUICKMATCH.SHORT.refused", "Not everyone agreed. Waiting for {size} players." },
        { "QUICKMATCH.ALONE.asc", "Nobody at Ascension {level} yet." },
        { "QUICKMATCH.ALONE.asc_many", "Nobody at the Ascensions you chose yet." },
        { "QUICKMATCH.ALONE.both", "Nobody looking for {size} players at Ascension {level} yet." },
        { "QUICKMATCH.ALONE.both_many", "Nobody looking for {size} players at the Ascensions you chose yet." },
        { "QUICKMATCH.ALONE.climb_note", "A match at another Ascension will not unlock the next one." },
        { "QUICKMATCH.ALONE.any_size", "Play Any Size" },
        { "QUICKMATCH.ALONE.any_asc", "Play Any Ascension" },
        { "QUICKMATCH.ALONE.any_both", "Play Any Size and Ascension" },
        { "QUICKMATCH.NOTICE.merged", "Moved to a fuller group." },
        { "QUICKMATCH.NOTICE.dropped", "{player} disconnected. The run goes on; they can rejoin." },
        { "QUICKMATCH.NOTICE.back", "{player} is back." },
        { "QUICKMATCH.NOTICE.voted_out", "You were removed from this group by a vote. Looking for another..." },
        { "QUICKMATCH.NOTICE.voted_out_run", "You were removed from this run by a vote." },
        { "QUICKMATCH.NOTICE.group_gone", "That group is no longer available. Looking for another..." },
        { "QUICKMATCH.NOTICE.idle_out", "You did not press the checkmark in time. Looking for another group..." },
        { "QUICKMATCH.NOTICE.host_idle", "The host did not press the checkmark in time. Looking for another group..." },
        { "QUICKMATCH.NOTICE.idle_host_self", "You did not press the checkmark in time, so your group went on without you." },
        { "QUICKMATCH.NOTICE.idle_stopped", "You did not press the checkmark in two groups in a row, so Quick Match stopped looking. Press Play Now when you are back." },
        { "QUICKMATCH.NOTICE.ok", "OK" },
        { "QUICKMATCH.searching", "Looking for players..." },
        { "QUICKMATCH.joining", "Joining a group..." },
        { "QUICKMATCH.LINK.copy", "Copy Invite Link" },
        { "QUICKMATCH.LINK.copied", "Link Copied" },
        { "QUICKMATCH.LINK.title", "Quick Match" },
        { "QUICKMATCH.LINK.failed", "Could not join that group. It may be full or already playing." },
        { "QUICKMATCH.LINK.other_release", "That group uses a different Quick Match version. Update Quick Match to play together." },
        { "QUICKMATCH.LINK.other_game", "That group has a different game version or different gameplay mods." },
        { "QUICKMATCH.LINK.mode_locked", "That group plays a game mode you have not unlocked yet." },
        { "QUICKMATCH.LINK.ascension", "That group plays an Ascension above the highest you have reached." },
        { "QUICKMATCH.creating", "Opening a lobby..." },
        { "QUICKMATCH.STATUS.looking", "{count} of {size} players here. Looking for more..." },
        { "QUICKMATCH.STATUS.looking_any", "{count} of up to {size} players here. Looking for more..." },
        { "QUICKMATCH.STATUS.starts_when_any", "The run starts when everyone here presses the checkmark (2 to 4 players)." },
        { "QUICKMATCH.STATUS.ready_any_alone", "You are ready. Waiting for another player to join." },
        { "QUICKMATCH.STATUS.countdown_press", "Press the checkmark within {time} to stay in this group." },
        { "QUICKMATCH.STATUS.countdown_ready", "Anyone who has not pressed the checkmark in {time} goes to find another group." },
        { "QUICKMATCH.STATUS.all_here", "Everyone is here." },
        { "QUICKMATCH.STATUS.few_players", "Quick Match works best when more people have it. Try busier hours or a Discord group." },
        { "QUICKMATCH.STATUS.starts_when", "The run starts when {size} players are here and all press the checkmark." },
        { "QUICKMATCH.STATUS.press_embark", "The run starts when everyone presses the checkmark." },
        { "QUICKMATCH.STATUS.ready_more", "You are ready. Waiting for {count} more to join." },
        { "QUICKMATCH.STATUS.guest_recovery", "Playing with a guest. If someone drops, the host can reload and invite everyone back." },
        { "QUICKMATCH.STATUS.ready_others", "You are ready. Waiting for the others to press the checkmark." },
        { "QUICKMATCH_REJOIN.title", "Rejoin Run" },
        { "QUICKMATCH_REJOIN.description", "Get back into your last Quick Match run, while it goes on or once its host reopens it." },
        { "QUICKMATCH_REJOIN.LOCKED.description", "Rejoining needs Steam." },
        { "QUICKMATCH.rejoin_waiting", "Looking for your run..." },
        { "QUICKMATCH.rejoin_held", "Your party is still in a room. You will join them as they enter the next one..." },
        { "QUICKMATCH.REJOIN_TIMEOUT.header", "Run Not Found" },
        { "QUICKMATCH.rejoin_reopening", "Nobody has reopened your run. Reopening it for your party..." },
        { "QUICKMATCH.REJOIN_TIMEOUT.body", "Your run was not found. A player who left it holding its save can reopen it with Load in the Multiplayer menu." },
        { "QUICKMATCH.REJOIN_TIMEOUT.yes", "Keep Looking" },
        { "QUICKMATCH.REJOIN_TIMEOUT.no", "Abandon Run" },
        { "QUICKMATCH.rejoin_waiting_for", "{host} left the run. Waiting for {player} to reopen it; you will join them automatically." },
        { "QUICKMATCH.TAKE_OVER.header", "Host Disconnected" },
        { "QUICKMATCH.TAKE_OVER.body", "{host} left the run. Keep it going as its host? The others will join you." },
        { "QUICKMATCH.TAKE_OVER.yes", "Host the Run" },
        { "QUICKMATCH.TAKE_OVER.no", "Pass" },
        { "QUICKMATCH.TAKE_OVER.time", "{time} left" },
        { "QUICKMATCH.TAKE_OVER.time_next", "{time} left, then the next player is asked" },
        { "QUICKMATCH.REGROUP.header", "Teammate Disconnected" },
        { "QUICKMATCH.REGROUP.body", "{player} disconnected, and you are the only player left. Save and wait: the run is saved and reopened so they can rejoin, and a fight in progress restarts. Or continue solo: enemies are scaled for one player from the next fight." },
        { "QUICKMATCH.REGROUP.yes", "Save and Wait" },
        { "QUICKMATCH.REGROUP.no", "Continue Solo" },
        { "QUICKMATCH.VOICE.title", "Voice" },
        { "QUICKMATCH.VOICE.close", "Close" },
        { "QUICKMATCH.VOICE.players", "Players in this match" },
        { "QUICKMATCH.VOICE.no_players", "Nobody else has joined yet." },
        { "QUICKMATCH.VOICE.mic", "Microphone" },
        { "QUICKMATCH.VOICE.hearing", "Hear others" },
        { "QUICKMATCH.VOICE.more", "More" },
        { "QUICKMATCH.VOICE.less", "Less" },
        { "QUICKMATCH.VOICE.more_hint", "Mic sensitivity, strangers start muted" },
        { "QUICKMATCH.VOICE.outside_match", "Saved for every match. Your mic and the other players show here once a match with voice starts." },
        { "QUICKMATCH.VOICE.off_this_match", "Voice chat is off for you in this match. Turn it on in Match Options before your next search." },
        { "QUICKMATCH.VOICE.sensitivity", "Mic sensitivity" },
        { "QUICKMATCH.VOICE.sensitivity_hint", "With your mic on, the bar turns gold while you would be heard." },
        { "QUICKMATCH.VOICE.volume", "Voice volume" },
        { "QUICKMATCH.VOICE.volume_hint", "Separate from the game's Master Volume: turn the game down, even to 0, and voice stays up. Voice keeps playing while the game is in the background." },
        { "QUICKMATCH.VOICE.strangers", "Strangers start muted" },
        { "QUICKMATCH.VOICE.mute", "Mute" },
        { "QUICKMATCH.EMOTES.title", "Emotes" },
        { "QUICKMATCH.EMOTES.hide", "Hide" },
        { "QUICKMATCH.EMOTES.show", "Show" },
        { "QUICKMATCH.VOICE.unmute", "Unmute" },
        { "QUICKMATCH.VOICE.mute_everyone", "Mute everyone else" },
        { "QUICKMATCH.VOICE.muted_list", "Muted players (remembered)" },
        { "QUICKMATCH.VOICE.private_note", "Only you know whom you muted. Their voice is thrown away before it plays." },
        { "QUICKMATCH.VOICE.status_speaking", "speaking" },
        { "QUICKMATCH.VOICE.status_live", "mic on" },
        { "QUICKMATCH.VOICE.status_muted", "mic off" },
        { "QUICKMATCH.VOICE.status_listen_only", "listening only" },
        { "QUICKMATCH.VOICE.status_off", "no voice" },
        { "QUICKMATCH.VOICE.status_unknown", "no voice yet" },
        { "QUICKMATCH.VOICE.status_muted_by_you", "muted by you" },
        { "QUICKMATCH.VOICE.status_playing_no_voice", "still playing, no voice" },
        { "QUICKMATCH.VOICE.status_left", "left the match" },
        { "QUICKMATCH.VOICE.relay_down", "Voice is down until the relay connects again. The match goes on without it." },
        { "QUICKMATCH.VOICE.status_too_loud", "too loud, turned down" },
        { "QUICKMATCH.VOICE.why_no_steam_voice", "Voice could not start over Steam for this match. The run carries on without it." },
        { "QUICKMATCH.VOICE.why_off", "Voice chat is off for you in this match. Turn it on in Match Options before your next search." },
        { "QUICKMATCH.VOICE.why_unavailable", "No teammate is connected to voice. It comes back when someone connects." },
        { "QUICKMATCH.VOICE.why_listen_only", "Steam cannot record your microphone, so you can only listen. Check the microphone in Steam's voice settings." },
        { "QUICKMATCH.MENU.title", "Quick Match" },
        { "QUICKMATCH.MENU.voice", "Voice" },
        { "QUICKMATCH.MENU.blocked", "Blocked players" },
        { "QUICKMATCH.MENU.recent", "Recent players" },
#if IPHONE
        { "QUICKMATCH.RECENT.hint", "The players you last finished a Quick Match run with." },
#else
        { "QUICKMATCH.RECENT.hint", "The players you last finished a Quick Match run with. Open a player's Steam profile to add them as a friend." },
#endif
        { "QUICKMATCH.RECENT.none", "Nobody yet. Finish a Quick Match run and the players you finished it with are listed here." },
        { "QUICKMATCH.RECENT.played_on", "played together {date}" },
        { "QUICKMATCH.RECENT.profile", "Steam Profile" },
        { "QUICKMATCH.BLOCK.hint", "Quick Match never puts you in a lobby or run with a player you blocked, or with one you blocked in Steam. A player you remove cannot be blocked again for 10 minutes." },
        { "QUICKMATCH.BLOCK.in_lobby", "Players in this lobby" },
        { "QUICKMATCH.BLOCK.in_run", "Players in this run" },
        { "QUICKMATCH.BLOCK.list", "Blocked" },
        { "QUICKMATCH.BLOCK.none", "Nobody is blocked." },
        { "QUICKMATCH.BLOCK.block", "Block" },
        { "QUICKMATCH.BLOCK.remove", "Remove" },
        { "QUICKMATCH.BLOCK.undo", "Undo" },
        { "QUICKMATCH.BLOCK.blocked", "blocked" },
        { "QUICKMATCH.BLOCK.blocked_on", "blocked {date}" },
        { "QUICKMATCH.BLOCK.removed", "removed" },
        { "QUICKMATCH.BLOCK.removed_until", "removed: can be blocked again at {time}" },
        { "QUICKMATCH.BLOCK.again_at", "can be blocked again at {time}" },
        { "QUICKMATCH.BLOCK.steam_id", "Steam ID {id}" },
        { "QUICKMATCH.BLOCK.ask_title", "Block {player}?" },
        { "QUICKMATCH.BLOCK.ask_body", "Quick Match will not match you with them again." },
        { "QUICKMATCH.BLOCK.ask_body_run", "Quick Match will not match you with them again. You are in a run with them: stay until it ends, or leave it now." },
        { "QUICKMATCH.BLOCK.stay", "Block, Stay Until the End" },
        { "QUICKMATCH.BLOCK.leave", "Block and Leave Now" },
        { "QUICKMATCH.BLOCK.already", "{player} is blocked." },
        { "QUICKMATCH.BLOCK.cooling", "You removed {player} from the list less than 10 minutes ago. You can block them again at {time}." },
        { "QUICKMATCH.BLOCK.by_id", "Block by Steam ID" },
        { "QUICKMATCH.BLOCK.by_id_placeholder", "SteamID64, [U:1:...] or profile link" },
        { "QUICKMATCH.BLOCK.paste", "Paste" },
        { "QUICKMATCH.BLOCK.not_an_id", "That is not a Steam ID. Use the number from a profile link (steamcommunity.com/profiles/...), or [U:1:...]." },
        { "QUICKMATCH.BLOCK.typed_added", "Blocked." },
        { "QUICKMATCH.BLOCK.typed_already", "That player is blocked already." },
        { "QUICKMATCH.BLOCK.typed_self", "That is your own Steam ID." },
        { "QUICKMATCH.BLOCK.typed_cooling", "You removed that player less than 10 minutes ago. You can block them again at {time}." },
        { "QUICKMATCH.BLOCK.private_note", "The list stays on this device and is never sent to anyone." },
        { "QUICKMATCH.VOTE.remove", "Vote to Remove" },
        { "QUICKMATCH.VOTE.start", "Start Vote" },
        { "QUICKMATCH.PLAYER.options", "Player Options" },
        { "QUICKMATCH.PLAYER.in_lobby", "In this lobby." },
        { "QUICKMATCH.PLAYER.in_run", "In this run." },
        { "QUICKMATCH.PLAYER.blocked", "Blocked: Quick Match will not match you with them again." },
        { "QUICKMATCH.PLAYER.cooling", "You removed them from your blocked list. You can block them again at {time}." },
        { "QUICKMATCH.PLAYER.no_voice", "Voice chat is off for you in this match, so there is nothing to mute." },
        { "QUICKMATCH.PLAYER.guest", "This player joined without Quick Match. They play without voice chat or status emotes." },
        { "QUICKMATCH.PLAYER.guest_label", "Guest" },
        { "QUICKMATCH.VOICE.players_hint", "To mute a player or change their volume, click their name in the lobby, or their row in a run and then Player Options." },
        { "QUICKMATCH.VOTE.hint", "If most of the other Quick Match players agree, {player} has to leave this lobby. It does not block them." },
        { "QUICKMATCH.VOTE.ask", "Remove {player} from this lobby?" },
        { "QUICKMATCH.VOTE.hint_run", "If most of the other Quick Match players agree, {player} has to leave this run and cannot come back to it. It does not block them." },
        { "QUICKMATCH.VOTE.ask_guest_run_host", "Remove the host, {player}, and end this run for everyone?" },
        { "QUICKMATCH.VOTE.passed_guest_run_host", "The vote passed. Removing {player} ends this run for everyone." },
        { "QUICKMATCH.VOTE.hint_guest_run_host", "If most of the other Quick Match players agree, {player} leaves and this run ends for everyone." },
        { "QUICKMATCH.VOTE.hint_run_host", "If most of the other Quick Match players agree, {player} has to leave this run and cannot come back to it, and the run goes on under a new host from its last save. It does not block them." },
        { "QUICKMATCH.VOTE.ask_run", "Remove {player} from this run? They cannot come back to it." },
        { "QUICKMATCH.VOTE.passed_run", "{player} was removed from this run." },
        { "QUICKMATCH.VOTE.count", "{yes} of {needed} votes" },
        { "QUICKMATCH.VOTE.time", "{seconds} s left" },
        { "QUICKMATCH.VOTE.yes", "Remove" },
        { "QUICKMATCH.VOTE.no", "Keep" },
        { "QUICKMATCH.VOTE.passed", "{player} was removed from this lobby." },
        { "QUICKMATCH.VOTE.failed", "Not enough votes. {player} stays." },
    };

    [HarmonyPrefix]
    public static void Prefix(Dictionary<string, LocTable> tables)
    {
        if (tables.TryGetValue(Table, out LocTable? table))
        {
            table.MergeWith(Entries);
        }
    }
}

internal static class Loc
{
    public static string Get(string key) => new LocString(LocSetup.Table, key).GetFormattedText();

    public static string With(string key, string name, string value)
    {
        var text = new LocString(LocSetup.Table, key);
        text.Add(name, value);
        return text.GetFormattedText();
    }

    public static string With(string key, string name, string value, string name2, string value2)
    {
        var text = new LocString(LocSetup.Table, key);
        text.Add(name, value);
        text.Add(name2, value2);
        return text.GetFormattedText();
    }

    public static string WithNumber(string key, string name, int value)
    {
        var text = new LocString(LocSetup.Table, key);
        text.Add(name, value);
        return text.GetFormattedText();
    }
}
