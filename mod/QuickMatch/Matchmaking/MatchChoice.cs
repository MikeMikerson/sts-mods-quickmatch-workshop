using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using QuickMatch.Patches;

namespace QuickMatch.Matchmaking;

internal sealed class MatchChoiceSettings
{
    // "random" or "choose"; anything else is read as random.
    public string Mode { get; set; } = "random";
    public List<int> Chosen { get; set; } = new();
    // 2, 3 or 4, or 0 for Any (also when the file has none).
    public int Size { get; set; }
    // Off for a player who never chose, so nobody lands in a voice pool unasked.
    public bool Voice { get; set; }
    // A lobby value of MatchMode; none means Standard.
    public string? GameMode { get; set; }
}

// Kept next to the modded saves of the signed-in Steam account, like the voice settings. Each choice is saved as it is
// made. Game thread only.
internal static class MatchChoice
{
    private const string FileName = "quickmatch_match.json";

    private static MatchChoiceSettings? _settings;

    // As the game sends it when joining, for the chosen game mode.
    public static int MaxAscension => MaxAscensionFor(GameMode);

    public static int MaxAscensionFor(RunMode mode) => Math.Clamp(
        MatchMode.MaxAscension(mode, SaveManager.Instance?.Progress?.MaxMultiplayerAscension ?? 0, SaveManager.Instance?.Progress is { } progress ? GameVersions.MaxBlitzAscension(progress) : 0),
        0, MatchPolicy.MaxAscension);

    public static RunMode GameMode => MatchMode.FromSaved(Load().GameMode, StartScreens.IsPlayable);

    public static AscensionChoice Last
    {
        get
        {
            MatchChoiceSettings settings = Load();
            return settings.Mode switch
            {
                "choose" when settings.Chosen.Count > 0 => new AscensionChoice(AscensionMode.Choose, settings.Chosen.ToList()),
                _ => AscensionChoice.Random,
            };
        }
    }

    public static AscensionChoice PlayNow => MatchPolicy.PlayNowChoice(Last, MaxAscension);

    public static int Size => Load().Size is 2 or 3 or 4 ? Load().Size : 0;

    // Off when this game version turned voice off, so the player is matched without voice.
    public static bool Voice => Load().Voice && GamePatches.IsOn(GameFeature.Voice);

    public static void Remember(AscensionChoice choice)
    {
        MatchChoiceSettings settings = Load();
        settings.Mode = choice.Mode == AscensionMode.Choose ? "choose" : "random";
        settings.Chosen = choice.Mode == AscensionMode.Choose ? (choice.Chosen ?? []).Distinct().Order().ToList() : new List<int>();
        Save(settings);
    }

    public static void RememberSize(int size)
    {
        MatchChoiceSettings settings = Load();
        settings.Size = size is 2 or 3 or 4 ? size : 0;
        Save(settings);
    }

    public static void RememberGameMode(RunMode mode)
    {
        MatchChoiceSettings settings = Load();
        settings.GameMode = MatchMode.LobbyValue(mode);
        Save(settings);
    }

    public static void RememberVoice(bool voice)
    {
        MatchChoiceSettings settings = Load();
        settings.Voice = voice;
        Save(settings);
    }

    private static void Save(MatchChoiceSettings settings)
    {
        try
        {
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, MatchChoiceJson.Default.MatchChoiceSettings));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not save the match options: {e.Message}");
        }
    }

    private static MatchChoiceSettings Load()
    {
        if (_settings != null)
        {
            return _settings;
        }
        try
        {
            string path = FilePath();
            _settings = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), MatchChoiceJson.Default.MatchChoiceSettings) : null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read the match options: {e.Message}");
        }
        _settings ??= new MatchChoiceSettings();
        return _settings;
    }

    private static string FilePath() =>
        Path.Combine(ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath("modded")), FileName);
}

[JsonSerializable(typeof(MatchChoiceSettings))]
internal sealed partial class MatchChoiceJson : JsonSerializerContext;
