namespace FolderSync.Engine;

public enum ActionType
{
    CopyLeftToRight,
    CopyRightToLeft,
    DeleteOnRight,
    DeleteOnLeft,
    Conflict
}

/// <summary>
/// One planned change produced by the diff engine. The full plan can be shown to the
/// user as a preview before <see cref="ActionExecutor"/> touches anything on disk.
/// </summary>
public sealed class SyncAction
{
    public required ActionType Type { get; init; }

    public required string RelativePath { get; init; }

    /// <summary>Human-readable reason, e.g. "new on left", "modified on both sides".</summary>
    public required string Reason { get; init; }
}
