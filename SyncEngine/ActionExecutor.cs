namespace FolderSync.Engine;

public sealed class ExecutionResult
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Conflicts { get; set; }

    /// <summary>True if the run was stopped via its cancellation token before every action ran.</summary>
    public bool Cancelled { get; set; }
    public List<string> Errors { get; } = new();
}

public static class ActionExecutor
{
    /// <summary>
    /// Applies the given actions to disk. Conflicts are never auto-applied — they're
    /// reported back so the caller (CLI/UI) can prompt the user or apply a resolution policy.
    /// Cancelling stops between files or part-way through a copy (that destination is left
    /// untouched); the returned result then has <see cref="ExecutionResult.Cancelled"/> set.
    /// </summary>
    public static ExecutionResult Apply(
        FolderPair pair,
        List<SyncAction> actions,
        Action<SyncAction>? onBeforeApply = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ExecutionResult();

        foreach (var action in actions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }

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
                        CopyFile(pair.LeftPath, pair.RightPath, action.RelativePath, cancellationToken);
                        break;

                    case ActionType.CopyRightToLeft:
                        CopyFile(pair.RightPath, pair.LeftPath, action.RelativePath, cancellationToken);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{action.RelativePath}: {ex.Message}");
                result.Skipped++;
            }
        }

        return result;
    }

    private const int CopyBufferSize = 1024 * 1024;

    private static void CopyFile(string sourceRoot, string destRoot, string relativePath, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var destPath = Path.Combine(destRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        // Copy to a temp file and swap it in only once complete, so a copy cancelled (or
        // failing) part-way never leaves a truncated file where the real destination was —
        // in Sync mode a damaged destination could otherwise be mistaken for a user edit.
        var tempPath = destPath + Scanner.TempFileSuffix;
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
            using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize))
            {
                dest.SetLength(source.Length);
                var buffer = new byte[CopyBufferSize];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    dest.Write(buffer, 0, read);
                }
            }

            File.SetAttributes(tempPath, File.GetAttributes(sourcePath));
            // Preserve the source's last-write time so the next scan's quick-compare
            // (size + timestamp) correctly treats this as "unchanged".
            File.SetLastWriteTimeUtc(tempPath, File.GetLastWriteTimeUtc(sourcePath));
            File.Move(tempPath, destPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.SetAttributes(tempPath, FileAttributes.Normal);
                File.Delete(tempPath);
            }
        }
    }

    private static void DeleteFile(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
