namespace FolderSync.Engine;

public sealed class ExecutionResult
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Conflicts { get; set; }
    public List<string> Errors { get; } = new();
}

public static class ActionExecutor
{
    /// <summary>
    /// Applies the given actions to disk. Conflicts are never auto-applied — they're
    /// reported back so the caller (CLI/UI) can prompt the user or apply a resolution policy.
    /// </summary>
    public static ExecutionResult Apply(
        FolderPair pair,
        List<SyncAction> actions,
        Action<SyncAction>? onBeforeApply = null)
    {
        var result = new ExecutionResult();

        foreach (var action in actions)
        {
            if (action.Type == ActionType.Conflict)
            {
                result.Conflicts++;
                continue;
            }

            onBeforeApply?.Invoke(action);

            try
            {
                switch (action.Type)
                {
                    case ActionType.CopyLeftToRight:
                        CopyFile(pair.LeftPath, pair.RightPath, action.RelativePath);
                        break;

                    case ActionType.CopyRightToLeft:
                        CopyFile(pair.RightPath, pair.LeftPath, action.RelativePath);
                        break;

                    case ActionType.DeleteOnRight:
                        DeleteFile(pair.RightPath, action.RelativePath);
                        break;

                    case ActionType.DeleteOnLeft:
                        DeleteFile(pair.LeftPath, action.RelativePath);
                        break;
                }

                result.Applied++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{action.RelativePath}: {ex.Message}");
                result.Skipped++;
            }
        }

        return result;
    }

    private static void CopyFile(string sourceRoot, string destRoot, string relativePath)
    {
        var sourcePath = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var destPath = Path.Combine(destRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(sourcePath, destPath, overwrite: true);

        // Preserve the source's last-write time so the next scan's quick-compare
        // (size + timestamp) correctly treats this as "unchanged".
        File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    private static void DeleteFile(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
