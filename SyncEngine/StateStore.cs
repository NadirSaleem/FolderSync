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
