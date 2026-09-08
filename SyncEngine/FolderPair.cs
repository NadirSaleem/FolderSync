namespace FolderSync.Engine;

/// <summary>
/// A named pair of folders to keep in sync, plus its mode and filters.
/// </summary>
public sealed class FolderPair
{
    /// <summary>Unique, user-friendly name (used as the state-store key and CLI identifier).</summary>
    public required string Name { get; init; }

    public required string LeftPath { get; init; }

    public required string RightPath { get; init; }

    public SyncMode Mode { get; init; } = SyncMode.Sync;

    /// <summary>
    /// Glob-style patterns (e.g. "*.tmp", "Thumbs.db") to exclude from sync.
    /// </summary>
    public List<string> ExcludePatterns { get; init; } = new();

    /// <summary>
    /// If true, subfolders are included. Defaults to true.
    /// </summary>
    public bool Recursive { get; init; } = true;
}
