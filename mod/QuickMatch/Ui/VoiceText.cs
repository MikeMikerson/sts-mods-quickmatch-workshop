using QuickMatch.Localization;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

internal static class VoiceText
{
    // Null when the mic can be turned on.
    public static string? WhyNoMic(VoiceSession? session)
    {
        if (session == null)
        {
            return Loc.Get("QUICKMATCH.VOICE.why_no_steam_voice");
        }
        return session.LocalState switch
        {
            LocalVoiceState.Off => Loc.Get("QUICKMATCH.VOICE.why_off"),
            LocalVoiceState.Unavailable when VoiceChat.RelayVoiceDown => Loc.Get("QUICKMATCH.VOICE.relay_down"),
            LocalVoiceState.Unavailable => Loc.Get("QUICKMATCH.VOICE.why_unavailable"),
            LocalVoiceState.ListenOnly => Loc.Get("QUICKMATCH.VOICE.why_listen_only"),
            _ => null,
        };
    }

    // Most important first.
    public static string StatusOf(VoiceSession session, ulong player)
    {
        if (session.IsMuted(player))
        {
            return Loc.Get("QUICKMATCH.VOICE.status_muted_by_you");
        }
        if (session.IsTooLoud(player))
        {
            return Loc.Get("QUICKMATCH.VOICE.status_too_loud");
        }
        if (session.IsSpeaking(player))
        {
            return Loc.Get("QUICKMATCH.VOICE.status_speaking");
        }
        return session.StatusOf(player) switch
        {
            VoiceStatus.Live => Loc.Get("QUICKMATCH.VOICE.status_live"),
            VoiceStatus.Muted => Loc.Get("QUICKMATCH.VOICE.status_muted"),
            VoiceStatus.ListenOnly => Loc.Get("QUICKMATCH.VOICE.status_listen_only"),
            VoiceStatus.Off => Loc.Get("QUICKMATCH.VOICE.status_off"),
            _ => Loc.Get("QUICKMATCH.VOICE.status_unknown"),
        };
    }
}
