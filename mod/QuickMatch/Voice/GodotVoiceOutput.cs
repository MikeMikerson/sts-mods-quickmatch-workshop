using System;
using Godot;

namespace QuickMatch.Voice;

// Voice plays through Godot's audio output at 16 kHz and the engine resamples it. The game's music
// and effects go through FMOD, so the system mixes voice beside them. While it plays, the game's Master
// Volume turns only the game down (GameMasterBus).
internal sealed class GodotVoiceOutput : IVoiceOutput
{
    private const string NodeName = "QuickMatchVoice";
    // Far more than QuickMatch ever keeps queued.
    private const float BufferSeconds = 0.25f;

    private AudioStreamPlayer? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private int _capacity;
    private Vector2[] _frames = Array.Empty<Vector2>();

    public int QueuedSamples => _playback == null ? 0 : Math.Max(0, _capacity - _playback.GetFramesAvailable());

    public void Play(ReadOnlySpan<short> samples)
    {
        if (_playback == null && !Start())
        {
            return;
        }
        int count = Math.Min(samples.Length, _playback!.GetFramesAvailable());
        if (_frames.Length != count)
        {
            _frames = new Vector2[count];
        }
        for (int i = 0; i < count; i++)
        {
            float value = samples[i] / 32768f;
            _frames[i] = new Vector2(value, value);
        }
        _playback.PushBuffer(_frames);
    }

    public void Stop()
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
        {
            _player.Stop();
            _player.QueueFree();
        }
        _player = null;
        _playback = null;
        GameMasterBus.Close();
    }

    private bool Start()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return false;
        }
        var stream = new AudioStreamGenerator { MixRate = VoiceFormat.SampleRate, BufferLength = BufferSeconds };
        _player = new AudioStreamPlayer { Name = NodeName, Stream = stream };
        tree.Root.AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        if (_playback == null)
        {
            Stop();
            return false;
        }
        _capacity = _playback.GetFramesAvailable();
        GameMasterBus.Open();
        return true;
    }
}
