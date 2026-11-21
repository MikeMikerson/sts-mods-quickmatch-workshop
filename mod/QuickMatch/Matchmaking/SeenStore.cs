using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Saves;

namespace QuickMatch.Matchmaking;

internal sealed class SeenSettings
{
    public List<SeenPlayer> Players { get; set; } = new();
}

// Saved beside the block list so a cooldown outlasts a restart of the game. It stays on this device and is never sent
// anywhere; a player back on step 0 with no recent short stay is dropped from it. Game thread only.
internal static class SeenStore
{
    private const string FileName = "quickmatch_seen.json";

    private static JoinLeaveRecord? _record;

    public static JoinLeaveRecord Record
    {
        get
        {
            Load();
            return _record!;
        }
    }

    public static void NoteLeave(ulong player, long joinedAt)
    {
        long now = BlockStore.Now();
        if (Record.NoteLeave(player, joinedAt, now))
        {
            MainFile.Logger.Info($"join-and-leave: a player's third short stay; refused until {Record.RefusedUntil(player, now)}");
        }
    }

    public static void Strike(ulong player)
    {
        long now = BlockStore.Now();
        Record.Strike(player, now);
        MainFile.Logger.Info($"join-and-leave: a strike for a player voted out; refused until {Record.RefusedUntil(player, now)}");
    }

    public static bool Refuses(ulong player) => Record.RefusedUntil(player, BlockStore.Now()) != null;

    private static void Save()
    {
        if (_record == null)
        {
            return;
        }
        try
        {
            long now = BlockStore.Now();
            _record.Prune(now);
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var settings = new SeenSettings { Players = _record.ToKeep(now) };
            File.WriteAllText(path, JsonSerializer.Serialize(settings, SeenSettingsJson.Default.SeenSettings));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not save the join-and-leave record: {e.Message}");
        }
    }

    private static void Load()
    {
        if (_record != null)
        {
            return;
        }
        SeenSettings? settings = null;
        try
        {
            string path = FilePath();
            settings = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), SeenSettingsJson.Default.SeenSettings) : null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read the join-and-leave record: {e.Message}");
        }
        _record = new JoinLeaveRecord();
        _record.Load(settings?.Players ?? []);
        _record.Prune(BlockStore.Now());
        _record.Changed += Save;
    }

    private static string FilePath() =>
        Path.Combine(ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath("modded")), FileName);
}

[JsonSerializable(typeof(SeenSettings))]
internal sealed partial class SeenSettingsJson : JsonSerializerContext;
