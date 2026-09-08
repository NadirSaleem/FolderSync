namespace FolderSync.Engine;

/// <summary>
/// The recorded state of one file, keyed by its path relative to the folder-pair root.
/// </summary>
public sealed class FileState
{
    public required string RelativePath { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    /// <summary>
    /// SHA-256 hash, populated lazily only when needed (e.g. resolving Sync conflicts).
    /// Null means "not computed" — do not treat that as "empty file".
    /// </summary>
    public string? Sha256 { get; set; }

    public bool QuickEquals(FileState other) =>
        SizeBytes == other.SizeBytes &&
        LastWriteTimeUtc == other.LastWriteTimeUtc;
}
