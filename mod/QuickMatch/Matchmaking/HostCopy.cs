using System;
using System.IO;
using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuickMatch.Matchmaking;

// A client's own copy of the quick-match run it is in, from which it can reopen the run if the host goes (see
// MigrationPolicy). The game saves only on the host; a client takes the same save at the same moments (every save goes
// through SaveManager.SaveRun) and writes it next to the run record, never to the game's current_run_mp.save, so the
// game's Load and Abandon cards do not change on a client. It is plain local JSON, never written through the game's
// save store (which may be the Steam Cloud).
//
// The original host, back after a migration, joins the run as a client; its own save of the run, now behind, is moved
// aside when that run launches.
internal static class HostCopy
{
    private const string FileName = "quickmatch_host_copy.save";
    private const string OldHostFileName = "quickmatch_old_host.save";

    // Where a host voted out of its run moves its save of that run, so Load does not reopen it.
    public const string RemovedFileName = "quickmatch_removed.save";

    // Called at each of the game's save moments, before the game's own save.
    public static void OnSave(AbstractRoom? preFinishedRoom)
    {
        RunManager run = RunManager.Instance;
        if (!Regroup.UsesQuickMatchRecovery || !run.ShouldSave || run.NetService.Type != NetGameType.Client || RunRecord.Current == null)
        {
            return;
        }
        try
        {
            Write(FileName, run.ToSave(preFinishedRoom));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not keep a copy of the run: {e.Message}");
        }
    }

    // Called as a saved run is set up, before it launches. A client keeps the run it was given (by a load lobby, or by
    // the host's rejoin answer), so it holds a current copy from the first moment of the run.
    public static void OnSavedRunSetUp(LoadRunLobby lobby)
    {
        if (lobby.NetService.Type != NetGameType.Client || RunRecord.Current is not { HasGuests: false } record || RunRecord.KeyOf(lobby.Run) != record.Key)
        {
            return;
        }
        try
        {
            Write(FileName, lobby.Run);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not keep a copy of the run: {e.Message}");
        }
    }

    public static bool Has(string key) => Read(FileName) is { } copy && RunRecord.KeyOf(copy) == key;

    // Made ready to host as the game's Load does.
    public static SerializableRun? Load(string key, ulong local)
    {
        if (Read(FileName) is not { } copy || RunRecord.KeyOf(copy) != key)
        {
            return null;
        }
        try
        {
            return RunManager.CanonicalizeSave(copy, local);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"the copy of run {key} cannot be hosted: {e.Message}");
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            string path = RunRecord.ModdedPath(FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                MainFile.Logger.Info("copy of the run deleted");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not delete the copy of the run: {e.Message}");
        }
    }

    // When the game's multiplayer save is this same run, we hosted it before a migration and are back as a player: the
    // save is behind the run, so it is moved aside (kept, not deleted) and the Load card goes.
    public static void MoveOldSaveAside(string runKey, string fileName = OldHostFileName)
    {
        SaveManager saves = SaveManager.Instance;
        if (!saves.HasMultiplayerRunSave)
        {
            return;
        }
        ReadSaveResult<SerializableRun> save = saves.LoadAndCanonicalizeMultiplayerRunSave(RunRecord.LocalPlayerId());
        string? saveKey = save.Success && save.SaveData != null ? RunRecord.KeyOf(save.SaveData) : null;
        if (!MigrationPolicy.MovesSaveAside(isClient: true, saveKey, runKey))
        {
            return;
        }
        try
        {
            Write(fileName, save.SaveData!);
            saves.DeleteCurrentMultiplayerRun();
            MainFile.Logger.Info(fileName == OldHostFileName
                ? $"our old save of run {runKey} is behind the run; moved aside to {fileName}"
                : $"we were voted out of run {runKey}; our save of it moved aside to {fileName}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not move our old save aside: {e.Message}");
        }
    }

    private static void Write(string fileName, SerializableRun run)
    {
        string path = RunRecord.ModdedPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(run, JsonSerializationUtility.GetTypeInfo<SerializableRun>()));
        File.Move(temp, path, overwrite: true);
    }

    private static SerializableRun? Read(string fileName)
    {
        try
        {
            string path = RunRecord.ModdedPath(fileName);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllBytes(path), JsonSerializationUtility.GetTypeInfo<SerializableRun>()) : null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not read {fileName}: {e.Message}");
            return null;
        }
    }
}
