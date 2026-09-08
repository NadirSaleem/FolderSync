namespace FolderSync.Engine.Tests;

/// <summary>
/// Test-only helpers for building FileState maps and snapshots tersely.
/// Timestamps are deliberately distinct fixed values so "changed" vs "unchanged"
/// comparisons in tests are unambiguous.
/// </summary>
internal static class TestData
{
    public static readonly DateTime T1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime T2 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    public static FileState File(string path, long size = 100, DateTime? time = null) =>
        new()
        {
            RelativePath = path,
            SizeBytes = size,
            LastWriteTimeUtc = time ?? T1
        };

    public static Dictionary<string, FileState> Map(params FileState[] files) =>
        files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, FileState> Empty() =>
        new(StringComparer.OrdinalIgnoreCase);

    public static FolderPair Pair(SyncMode mode) =>
        new() { Name = "test", LeftPath = "L", RightPath = "R", Mode = mode };

    public static PairSnapshot Snapshot(
        Dictionary<string, FileState>? left = null,
        Dictionary<string, FileState>? right = null) =>
        new() { Left = left ?? Empty(), Right = right ?? Empty() };
}
