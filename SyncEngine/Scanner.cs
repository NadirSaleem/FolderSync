using System.IO.Enumeration;
using System.Security.Cryptography;

namespace FolderSync.Engine;

public static class Scanner
{
    /// <summary>
    /// Scans <paramref name="rootPath"/> and returns the current state of every matching file,
    /// keyed by path relative to the root.
    /// </summary>
    /// <param name="onFileScanned">
    /// Invoked once per file as it's scanned, so a caller can surface a running "N files
    /// scanned" status. There's no reliable total to report against — this is a live
    /// counter, not a percentage.
    /// </param>
    public static Dictionary<string, FileState> Scan(string rootPath, FolderPair pair, Action? onFileScanned = null)
    {
        var result = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(rootPath))
            return result;

        var searchOption = pair.Recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        foreach (var fullPath in Directory.EnumerateFiles(rootPath, "*", searchOption))
        {
            var relativePath = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');

            if (IsExcluded(relativePath, pair.ExcludePatterns))
                continue;

            var info = new FileInfo(fullPath);
            result[relativePath] = new FileState
            {
                RelativePath = relativePath,
                SizeBytes = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc
            };

            onFileScanned?.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Computes and caches the SHA-256 hash for a file, given its relative path under root.
    /// Call only when a definitive comparison is needed (e.g. resolving a Sync conflict) —
    /// hashing every file on every run does not scale to large trees.
    /// </summary>
    public static string ComputeHash(string rootPath, string relativePath)
    {
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        using var stream = File.OpenRead(fullPath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// True if the file at <paramref name="relativePath"/> genuinely differs in content
    /// between <paramref name="leftRoot"/> and <paramref name="rightRoot"/>, verified by hash
    /// rather than size/timestamp. Used to confirm real Sync conflicts instead of trusting
    /// the quick size+timestamp check, which can be fooled by coincidental matches/mismatches.
    /// </summary>
    public static bool FilesDiffer(string leftRoot, string rightRoot, string relativePath)
    {
        var leftHash = ComputeHash(leftRoot, relativePath);
        var rightHash = ComputeHash(rightRoot, relativePath);
        return !string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcluded(string relativePath, List<string> patterns)
    {
        if (patterns.Count == 0)
            return false;

        var fileName = Path.GetFileName(relativePath);

        foreach (var pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, fileName))
                return true;
        }

        return false;
    }
}
