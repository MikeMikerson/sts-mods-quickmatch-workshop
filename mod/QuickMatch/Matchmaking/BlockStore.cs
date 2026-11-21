using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Lobby;

namespace QuickMatch.Matchmaking;

internal sealed class BlockSettings
{
    public List<BlockedPlayer> Blocked { get; set; } = new();

    public List<RemovedPlayer> Removed { get; set; } = new();
}

// Kept next to the modded saves of the signed-in Steam account and never sent anywhere. A player blocked in Steam
// itself counts as blocked too. Game thread only.
internal static class BlockStore
{
    private const string FileName = "quickmatch_blocks.json";

    private static BlockList? _list;

    public static BlockList List
    {
        get
        {
            Load();
            return _list!;
        }
    }

    // QuickMatch's list is checked first: it needs no Steam call.
    public static bool IsBlocked(ulong player)
    {
        return BlockPolicy.IsBlocked(player, List, SteamBlocked);
    }

    private static bool SteamBlocked(ulong player)
    {
        try
        {
            return LobbyBackend.Current.BlockedOnSteam(player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read Steam's block for a player: {e.Message}");
            return false;
        }
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static BlockResult Block(ulong player, string? name)
    {
        BlockResult result = List.TryAdd(player, name, Now(), LobbyBackend.Current.LocalId);
        MainFile.Logger.Info(result == BlockResult.Added ? $"blocked a player ({List.Count} on the list)" : $"did not block a player: {result}");
        return result;
    }

    // The returned entry is for Undo.
    public static BlockedPlayer? Remove(ulong player)
    {
        BlockedPlayer? removed = List.Remove(player, Now());
        if (removed != null)
        {
            MainFile.Logger.Info($"removed a player from the block list ({List.Count} left)");
        }
        return removed;
    }

    public static bool Restore(BlockedPlayer player)
    {
        bool restored = List.Restore(player);
        if (restored)
        {
            MainFile.Logger.Info($"put a player back on the block list ({List.Count} on the list)");
        }
        return restored;
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
            var settings = new BlockSettings { Blocked = _list.Players.ToList(), Removed = _list.CoolingDown(Now()).ToList() };
            File.WriteAllText(path, JsonSerializer.Serialize(settings, BlockSettingsJson.Default.BlockSettings));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not save the block list: {e.Message}");
        }
    }

    private static void Load()
    {
        if (_list != null)
        {
            return;
        }
        BlockSettings? settings = null;
        try
        {
            string path = FilePath();
            settings = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), BlockSettingsJson.Default.BlockSettings) : null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read the block list: {e.Message}");
        }
        _list = new BlockList();
        _list.Load(settings?.Blocked ?? [], settings?.Removed);
        _list.Changed += Save;
    }

    private static string FilePath() =>
        Path.Combine(ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath("modded")), FileName);
}

[JsonSerializable(typeof(BlockSettings))]
internal sealed partial class BlockSettingsJson : JsonSerializerContext;
