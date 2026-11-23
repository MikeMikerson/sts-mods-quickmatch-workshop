using System;

namespace QuickMatch.Patches;

// What a group of patches gives the player. Each is patched as a unit: a game update that moves one of its targets
// turns the whole feature off (FeaturePlan), since half a feature is worse than none.
internal enum GameFeature
{
    // Essential: the Quick Match menu entry and screen, the lobby with its start gate, join refusals (blocks, the
    // ascension floor) and ascension lock, and the mod's text. Without any one of them the mod stays off.
    Core,
    // The lobby's Invite button hidden for a QuickMatch host, where vanilla's would throw.
    InviteButton,
    // Steam invites and invite links to a quick-match lobby join through Quick Match, and the lobby's Copy Invite Link.
    LinkJoin,
    // Knowing that the run under way is a quick-match run, which every feature in a run needs.
    RunTracking,
    // A group that lost its host regroups, reopens the run and carries on under a new host.
    Regroup,
    // A run goes on without a player who dropped, scaled for the players left in the fight.
    StandIn,
    // A player who dropped comes back into the run under way.
    Rejoin,
    // The player window (Block, Vote to Remove, Mute) from the lobby, the run's player panel and the pause menu.
    PlayerWindow,
    // Voice chat: the mic buttons, the speaking rings and the voice transport. Off means no voice choice at all,
    // so the player is only matched into lobbies without voice.
    Voice,
    // Emotes: the emote button on the player's own row and the status bubbles over each player.
    Emotes,
    // Quick matches in Blitz, through its own lobby screen.
    Blitz,
    // Quick matches in Dread, through its own lobby screen.
    Dread,
}

// The feature a patch class belongs to. On a nested patch class it overrides its container's.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class PatchFeatureAttribute(GameFeature feature) : Attribute
{
    public GameFeature Feature { get; } = feature;
}
