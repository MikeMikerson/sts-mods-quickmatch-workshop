namespace QuickMatch.Voice;

// The one audio format every player records and plays: Steam's compressed voice, decompressed at 16 kHz mono. A
// packet holds about 60 ms of it, which keeps the message rate low (about 17 messages a second per speaker, and none
// during silence); the game is turn based, so the delay does not matter.
internal static class VoiceFormat
{
    public const int SampleRate = 16000;
    // Playback, the gate and the limiter work in frames of this length.
    public const int FrameMs = 20;
    public const int FrameSamples = SampleRate * FrameMs / 1000;
    public const int PacketMs = 60;
    public const int PacketSamples = SampleRate * PacketMs / 1000;
    // One read of Steam's voice is far smaller; more from another player is a malformed packet.
    public const int MaxChunkBytes = 2048;
    public const int MaxChunksPerPacket = 8;
    // What one chunk can decompress to: a second, the most a speaker's buffer keeps.
    public const int MaxChunkSamples = SampleRate;
}
