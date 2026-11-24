using System.Globalization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Steam;

// Steam lobby data keys written by a quick-match host and filtered on by searchers. Version, database hash and mods
// hash mirror what the multiplayer handshake compares, so any lobby a search returns should also pass the game's own
// join checks.
internal static class LobbyKeys
{
    public const string Proto = "qm_proto";
    // Bumped whenever the lobby keys, the game messages (new ones shift the message ids) or a rule every player must
    // follow change, so players of different releases never find each other's lobbies.
    public const string ProtoValue = "25";

    // "2" to "4", or SizeAny.
    public const string Size = "qm_size";

    // An Any lobby holds up to 4 and starts with 2 or more once everyone present has pressed Embark, the game's own
    // rule. Searches for one exact size filter on that size and so never see these.
    public const string SizeAny = "any";

    // The game mode (MatchMode). Searches filter on it, so a new mode needs no protocol bump of its own.
    public const string Mode = "qm_mode";
    public const string ModeStandard = "standard";
    public const string ModeBlitz = "blitz";
    public const string ModeDread = "dread";

    // Every pool search asks for the player's own voice choice, so voice and no-voice players never meet. A resume
    // lobby needs none: the run's players find it by its key.
    public const string Voice = "qm_vc";
    public const string VoiceOn = "on";
    public const string VoiceOff = "off";

    // The idle countdown's end, in Steam server seconds: while it is set, a player here who has not pressed Embark by
    // then is sent back to searching. Written by the host only; absent when no countdown runs.
    public const string Deadline = "qm_dl";
    public const string Version = "qm_ver";
    public const string Db = "qm_db";
    public const string Mods = "qm_mods";
    public const string CreatedAt = "qm_t";
    public const string Members = "qm_n";

    // Fixed when the host opens the lobby ("0" to "10"). Searches keep a lobby only when it is in the player's chosen
    // set, never above their own maximum (the lobby search filters on equal values only, so this is done on the
    // client); the host refuses a joiner below it.
    public const string Ascension = "qm_asc";

    // Every host accepts lobby chat. A mixed host also accepts the game's Steam sockets.
    public const string Link = "qm_link";
    public const string LinkMixed = "mixed";
    public const string LinkChat = "chat";

    // Marker of a host reopening a saved quick-match run. Kept apart from Proto, so normal quick-match searches never
    // list these lobbies.
    public const string ResumeProto = "qm_rproto";
    // Bumped with ProtoValue whenever a change also affects resume lobbies.
    public const string ResumeProtoValue = "24";

    // Seed and players of the saved run; only players of that run search for it.
    public const string ResumeKey = "qm_resume";

    // Steam names a lobby's owner only to its members, so the rejoin search reads this to pass by a lobby hosted by a
    // player voted out of the run before joining it; a lobby without it is checked once connected.
    public const string ResumeHost = "qm_rhost";

    // (0, false) when unknown.
    public static (int Size, bool Any) ParseSize(string? text) =>
        text == SizeAny ? (MatchPolicy.AnySizeMax, true) : (ParseInRange(text, 2, 4, 0), false);

    public static int ParseInRange(string? text, int min, int max, int missing) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max ? value : missing;
}
