namespace FolderSync.Engine;

/// <summary>
/// Compares current folder state against the last-known snapshot and produces
/// the list of actions needed to bring the pair back in sync, according to its mode.
/// </summary>
public static class DiffEngine
{
    /// <summary>
    /// Compares current folder state against the last-known snapshot and produces
    /// the list of actions needed to bring the pair back in sync, according to its mode.
    /// </summary>
    /// <param name="contentsDiffer">
    /// Optional callback used only for Sync-mode conflict detection: given a relative path
    /// where both sides changed and both still have a file, returns true if the files'
    /// actual content differs. When omitted, falls back to the size+timestamp quick check,
    /// which can be fooled by coincidental matches/mismatches. Pass a hash-based comparer
    /// (see <see cref="Scanner.FilesDiffer"/>) for a real confirmation before flagging
    /// something a human has to resolve.
    /// </param>
    public static List<SyncAction> BuildPlan(
        FolderPair pair,
        Dictionary<string, FileState> currentLeft,
        Dictionary<string, FileState> currentRight,
        PairSnapshot lastSnapshot,
        Func<string, bool>? contentsDiffer = null)
    {
        var actions = new List<SyncAction>();
        var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(currentLeft.Keys);
        allPaths.UnionWith(currentRight.Keys);
        allPaths.UnionWith(lastSnapshot.Left.Keys);
        allPaths.UnionWith(lastSnapshot.Right.Keys);

        foreach (var relPath in allPaths)
        {
            currentLeft.TryGetValue(relPath, out var left);
            currentRight.TryGetValue(relPath, out var right);
            lastSnapshot.Left.TryGetValue(relPath, out var prevLeft);
            lastSnapshot.Right.TryGetValue(relPath, out var prevRight);

            var leftChanged = HasChanged(left, prevLeft);
            var rightChanged = HasChanged(right, prevRight);

            var action = pair.Mode switch
            {
                SyncMode.Echo => DecideEcho(relPath, left, right),
                SyncMode.Contribute => DecideContribute(relPath, left, right),
                SyncMode.Sync => DecideSync(relPath, left, right, prevLeft, prevRight, leftChanged, rightChanged, contentsDiffer),
                _ => null
            };

            if (action is not null)
                actions.Add(action);
        }

        return actions;
    }

    private static bool HasChanged(FileState? current, FileState? previous)
    {
        if (current is null && previous is null) return false;
        if (current is null || previous is null) return true;
        return !current.QuickEquals(previous);
    }

    private static SyncAction? DecideEcho(string relPath, FileState? left, FileState? right)
    {
        // Left is master: right must end up matching left exactly.
        if (left is not null && right is null)
            return new SyncAction { Type = ActionType.CopyLeftToRight, RelativePath = relPath, Reason = "new on left" };

        if (left is not null && right is not null && !left.QuickEquals(right))
            return new SyncAction { Type = ActionType.CopyLeftToRight, RelativePath = relPath, Reason = "modified on left" };

        if (left is null && right is not null)
            return new SyncAction { Type = ActionType.DeleteOnRight, RelativePath = relPath, Reason = "removed from left" };

        return null;
    }

    private static SyncAction? DecideContribute(string relPath, FileState? left, FileState? right)
    {
        // Same as Echo but never delete on the right.
        if (left is not null && right is null)
            return new SyncAction { Type = ActionType.CopyLeftToRight, RelativePath = relPath, Reason = "new on left" };

        if (left is not null && right is not null && !left.QuickEquals(right))
            return new SyncAction { Type = ActionType.CopyLeftToRight, RelativePath = relPath, Reason = "modified on left" };

        return null;
    }

    private static SyncAction? DecideSync(
        string relPath,
        FileState? left,
        FileState? right,
        FileState? prevLeft,
        FileState? prevRight,
        bool leftChanged,
        bool rightChanged,
        Func<string, bool>? contentsDiffer)
    {
        // Neither side changed since last run -> nothing to do.
        if (!leftChanged && !rightChanged)
            return null;

        if (leftChanged && rightChanged)
        {
            // Deleted on both sides -> already in sync, nothing left to reconcile.
            if (left is null && right is null)
                return null;

            if (left is not null && right is not null)
            {
                // Both sides still have a file. Confirm with a real hash comparison when
                // available; a size/timestamp match or mismatch alone isn't proof either way.
                var differs = contentsDiffer is not null
                    ? contentsDiffer(relPath)
                    : !left.QuickEquals(right);

                return differs
                    ? new SyncAction { Type = ActionType.Conflict, RelativePath = relPath, Reason = "modified on both sides" }
                    : null; // hash confirms identical content despite differing metadata -> nothing to do
            }

            // One side deleted the file while the other modified it. There's no file left
            // to hash-compare, and no safe automatic resolution — a delete and an edit are
            // genuinely different intents, so this always needs a human, not a heuristic.
            var reason = left is null ? "deleted on left, modified on right" : "modified on left, deleted on right";
            return new SyncAction { Type = ActionType.Conflict, RelativePath = relPath, Reason = reason };
        }

        // From here on, exactly one side changed since the last run.

        // Deleted on one side -> propagate the delete to the other.
        if (prevLeft is not null && left is null)
            return new SyncAction { Type = ActionType.DeleteOnRight, RelativePath = relPath, Reason = "deleted on left" };

        if (prevRight is not null && right is null)
            return new SyncAction { Type = ActionType.DeleteOnLeft, RelativePath = relPath, Reason = "deleted on right" };

        // New or modified on left only -> push right.
        if (leftChanged && left is not null)
            return new SyncAction { Type = ActionType.CopyLeftToRight, RelativePath = relPath, Reason = prevLeft is null ? "new on left" : "modified on left" };

        // New or modified on right only -> pull to left.
        if (rightChanged && right is not null)
            return new SyncAction { Type = ActionType.CopyRightToLeft, RelativePath = relPath, Reason = prevRight is null ? "new on right" : "modified on right" };

        return null;
    }
}
