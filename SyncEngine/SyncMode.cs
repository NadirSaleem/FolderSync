namespace FolderSync.Engine;

/// <summary>
/// Defines how a folder pair is kept in sync.
/// </summary>
public enum SyncMode
{
    /// <summary>
    /// Left is the master copy. Right is made to exactly mirror left,
    /// including deleting files on right that were deleted on left.
    /// </summary>
    Echo,

    /// <summary>
    /// Changes on either side are propagated to the other side.
    /// Files changed on both sides since the last run are reported as conflicts.
    /// </summary>
    Sync,

    /// <summary>
    /// Like Echo, but never deletes anything on the right — only adds and updates.
    /// </summary>
    Contribute
}
