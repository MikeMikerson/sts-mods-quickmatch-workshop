using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Lobby;

namespace QuickMatch.Matchmaking;

internal sealed partial record RunRecord
{
    private const string FileName = "quickmatch_run.json";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private static RunRecord? _cached;
    private static bool _loaded;

    // Null if there is none or it has expired.
    public static RunRecord? Current
    {
        get
        {
            if (!_loaded)
            {
                _cached = Load();
                _loaded = true;
            }
            if (_cached is { HasGuests: false } && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _cached.StartedAt > MaxAge.TotalSeconds)
            {
                MainFile.Logger.Info($"run record {_cached.Key} expired");
                Clear("expired");
            }
            return _cached;
        }
    }

    [JsonIgnore]
    public bool IsHost => HostId == LocalPlayerId();

    public static string KeyOf(RunState state) =>
        MatchPolicy.RunKey(state.Rng.StringSeed, state.Players.Select(p => p.NetId));

    public static string? KeyOf(SerializableRun save) =>
        save.SerializableRng?.Seed is { } seed ? MatchPolicy.RunKey(seed, save.Players.Select(p => p.NetId)) : null;

    // True for a new or a reloaded quick-match run.
    public static bool Matches(RunState state) => Current is { } record && record.Key == KeyOf(state);

    public static void Save(RunState state, ulong hostId, int size, bool voice, ulong[]? desktopPlayers, ulong[] guestPlayers)
    {
        var record = new RunRecord(KeyOf(state), hostId, state.Players.Select(p => p.NetId).ToList(), size,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), !voice, DesktopPlayers: desktopPlayers, GuestPlayers: guestPlayers);
        if (Write(record))
        {
            MainFile.Logger.Info($"run record saved: key={record.Key} host={hostId} players={string.Join(",", record.PlayerIds)} seed={state.Rng.StringSeed} {(global::QuickMatch.Voice.MatchVoice.Text(voice))}");
        }
    }

    public static void RestoreGuests(string key, ulong host, List<ulong> players, bool voice, ulong[] guests)
    {
        RunRecord record = Current is { } existing && existing.Key == key ? existing
            : new RunRecord(key, host, players, players.Count, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Write(record with { HostId = host, NoVoice = !voice, GuestPlayers = guests });
    }

    // A player blocked mid-run with Stay: this run's rejoins let them through until the run ends.
    public static void StayWith(ulong player)
    {
        if (Current is not { } record || record.Stayed.Contains(player))
        {
            return;
        }
        if (Write(record with { StayedWith = [.. record.Stayed, player] }))
        {
            MainFile.Logger.Info($"run record {record.Key}: staying with a blocked player until the run ends");
        }
    }

    // A player voted out of this run: every later host of it refuses them.
    public static void Ban(ulong player)
    {
        if (Current is not { } record || player == 0 || record.Banned.Contains(player))
        {
            return;
        }
        if (Write(record with { Removed = [.. record.Banned, player] }))
        {
            MainFile.Logger.Info($"run record {record.Key}: a player voted out is banned from this run");
        }
    }

    // Set at every launch, so after a migration the record names the player who holds the game's save, and a client
    // that loses the host knows whom it lost.
    public static void SetHost(ulong host)
    {
        if (Current is not { } record || record.HostId == host || host == 0)
        {
            return;
        }
        if (Write(record with { HostId = host }))
        {
            MainFile.Logger.Info($"run record {record.Key}: host is now {host}");
        }
    }

    private static bool Write(RunRecord record)
    {
        _cached = record;
        _loaded = true;
        try
        {
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record, RunRecordJson.Default.RunRecord));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not save run record: {e.Message}");
            return false;
        }
    }

    public static void Clear(string reason)
    {
        bool had = _cached != null || !_loaded;
        _cached = null;
        _loaded = true;
        try
        {
            string path = FilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
                had = true;
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not delete run record: {e.Message}");
        }
        if (had)
        {
            MainFile.Logger.Info($"run record cleared ({reason})");
        }
        // A copy of the run is only used with its record.
        HostCopy.Delete();
    }

    // As the lobby backend knows it: quick-match runs record players by Steam ID.
    public static ulong LocalPlayerId() =>
        LobbyBackend.Current.IsAvailable ? LobbyBackend.Current.LocalId : PlatformUtil.GetLocalPlayerId(PlatformType.None);

    private static RunRecord? Load()
    {
        try
        {
            string path = FilePath();
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), RunRecordJson.Default.RunRecord) : null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read run record: {e.Message}");
            return null;
        }
    }

    private static string FilePath() => ModdedPath(FileName);

    public static string ModdedPath(string fileName) =>
        Path.Combine(ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath("modded")), fileName);
}
