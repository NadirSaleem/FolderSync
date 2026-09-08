using FolderSync.Engine;

namespace FolderSync.Gui;

/// <summary>
/// Read-only display wrapper around a SyncAction, for binding in the preview ListView.
/// </summary>
public sealed class ActionRow
{
    public ActionRow(SyncAction action, string pairName)
    {
        PairName = pairName;
        Path = action.RelativePath;
        Reason = action.Reason;
        Type = action.Type;
    }

    public string PairName { get; }
    public string Path { get; }
    public string Reason { get; }
    public ActionType Type { get; }

    /// <summary>Short glyph-friendly label for the action column.</summary>
    public string TypeLabel => Type switch
    {
        ActionType.CopyLeftToRight => "→ Copy to right",
        ActionType.CopyRightToLeft => "← Copy to left",
        ActionType.DeleteOnRight => "✕ Delete on right",
        ActionType.DeleteOnLeft => "✕ Delete on left",
        ActionType.Conflict => "⚠ Conflict",
        _ => Type.ToString()
    };
}
