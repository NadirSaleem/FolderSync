using System.Text.Json;

namespace FolderSync.Engine;

/// <summary>
/// Stores the last-known state of both sides of a folder pair between runs.
/// This is what lets the diff engine distinguish "deleted since last run"
/// from "never existed on this side".
/// </summary>
public sealed class StateStore
{
    private readonly string _stateDirectory;

    public StateStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FolderSync", "State");

        Directory.CreateDirectory(_stateDirectory);
    }

    public PairSnapshot Load(string pairName)
    {
        var path = SnapshotPath(pairName);

        if (!File.Exists(path))
            return new PairSnapshot();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PairSnapshot>(json) ?? new PairSnapshot();
    }

    public void Save(string pairName, PairSnapshot snapshot)
    {
        var path = SnapshotPath(pairName);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Carries a pair's state over to its new name, so renaming a pair doesn't make the
    /// next run look like a first run (which can't detect deletions). Returns false — and
    /// moves nothing — if some other pair already has state under the new name.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        var oldPath = SnapshotPath(oldName);
        var newPath = SnapshotPath(newName);

        if (oldPath == newPath || !File.Exists(oldPath))
            return true;

        // A case-only rename is the same file on Windows, so "exists" doesn't mean a clash.
        var caseOnly = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && File.Exists(newPath))
            return false;

        File.Move(oldPath, newPath);
        return true;
    }

    /// <summary>
    /// When the pair was last run, or null if it never has been. Uses the snapshot file's
    /// write time (it's only written at the end of a run) so we don't have to deserialize
    /// what can be a multi-megabyte file just to read one timestamp.
    /// </summary>
    public DateTime? GetLastRunUtc(string pairName)
    {
        var path = SnapshotPath(pairName);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    private string SnapshotPath(string pairName) =>
        Path.Combine(_stateDirectory, $"{SanitizeFileName(pairName)}.json");

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}

/// <summary>
/// The last-known state of both sides of a folder pair, as of the previous run.
/// </summary>
public sealed class PairSnapshot
{
    public Dictionary<string, FileState> Left { get; set; } = new();
    public Dictionary<string, FileState> Right { get; set; } = new();
    public DateTime LastRunUtc { get; set; }
}
