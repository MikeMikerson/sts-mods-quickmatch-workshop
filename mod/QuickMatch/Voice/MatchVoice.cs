using QuickMatch.Steam;

namespace QuickMatch.Voice;

// The local player's voice choice for the current quick match. Voice and no voice are two pools whose players never
// meet: lobbies carry it and every pool search asks for it. With it off nothing is recorded, sent or played. A run gone
// back into has voice only if the run had it and the player still chooses it, so a later "no" always wins. Game thread
// only.
internal static class MatchVoice
{
    // A run recorded without a voice choice was a voice match.
    public static bool On { get; private set; } = true;

    public static string Text(bool on) => on ? "vc=on" : "vc=off";

    public static string LobbyValue(bool on) => on ? LobbyKeys.VoiceOn : LobbyKeys.VoiceOff;

    public static void Set(bool on) => On = on;

    // A run gone back into (rejoined, reopened, regrouped or loaded) has voice only if it was a voice run and the
    // player's own choice now is voice too. A player who turned voice off since the run began is never heard in it.
    public static bool ForRun(bool runVoice, bool choice) => runVoice && choice;
}
