using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Saves;

namespace QuickMatch.Matchmaking;

internal sealed class RecentSettings
{
    public List<RecentPlayer> Players { get; set; } = new();
}

// Saved beside the block list for the signed-in Steam account. It stays on this device and is never sent anywhere.
// Game thread only.
internal static class RecentStore
{
    private const string FileName = "quickmatch_recent.json";

    private static RecentPlayers? _list;

    public static RecentPlayers List
    {
        get
        {
            Load();
            return _list!;
        }
    }

    // The players still in a run when it ended, without ourselves and anyone we blocked.
    public static void NoteRunEnded(IEnumerable<(ulong Id, string? Name)> present)
    {
        ulong self = RunRecord.LocalPlayerId();
        List.Note(present.Where(p => p.Id != self && !BlockStore.IsBlocked(p.Id)), BlockStore.Now());
    }

    private static void Save()
    {
        if (_list == null)
        {
            return;
        }
        try
        {
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var settings = new RecentSettings { Players = _list.Players.ToList() };
            File.WriteAllText(path, JsonSerializer.Serialize(settings, RecentSettingsJson.Default.RecentSettings));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not save the recent players: {e.Message}");
        }
    }

    private static void Load()
    {
        if (_list != null)
        {
            return;
        }
        RecentSettings? settings = null;
        try
        {
            string path = FilePath();
            settings = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), RecentSettingsJson.Default.RecentSettings) : null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read the recent players: {e.Message}");
        }
        _list = new RecentPlayers();
        _list.Load(settings?.Players ?? []);
        _list.Changed += Save;
    }

    private static string FilePath() =>
        Path.Combine(ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath("modded")), FileName);
}

[JsonSerializable(typeof(RecentSettings))]
internal sealed partial class RecentSettingsJson : JsonSerializerContext;
