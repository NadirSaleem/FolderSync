using Xunit;

namespace FolderSync.Engine.Tests;

/// <summary>
/// Real-disk tests for stopping a run part-way: cancelled copies must never leave a
/// half-written file in place of the destination, and leftover temp files must not be synced.
/// </summary>
public class CancellationTests : IDisposable
{
    private readonly string _leftRoot;
    private readonly string _rightRoot;
    private readonly FolderPair _pair;

    public CancellationTests()
    {
        _leftRoot = Directory.CreateTempSubdirectory("foldersync-left-").FullName;
        _rightRoot = Directory.CreateTempSubdirectory("foldersync-right-").FullName;
        _pair = new FolderPair { Name = "test", LeftPath = _leftRoot, RightPath = _rightRoot, Mode = SyncMode.Echo };
    }

    public void Dispose()
    {
        Directory.Delete(_leftRoot, recursive: true);
        Directory.Delete(_rightRoot, recursive: true);
    }

    [Fact]
    public void Apply_CopiesFileAndPreservesTimestamp_WhenNotCancelled()
    {
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(_leftRoot, "a.txt"), "hello");
        File.SetLastWriteTimeUtc(Path.Combine(_leftRoot, "a.txt"), timestamp);

        var result = ActionExecutor.Apply(_pair, [Copy("a.txt")]);

        Assert.False(result.Cancelled);
        Assert.Equal(1, result.Applied);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_rightRoot, "a.txt")));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(_rightRoot, "a.txt")));
        Assert.Empty(Directory.GetFiles(_rightRoot, "*" + Scanner.TempFileSuffix));
    }

    [Fact]
    public void Apply_KeepsFinishedFiles_AndStopsTheRest_WhenCancelledPartWay()
    {
        File.WriteAllText(Path.Combine(_leftRoot, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_leftRoot, "b.txt"), "b");
        File.WriteAllText(Path.Combine(_leftRoot, "c.txt"), "c");
        using var cts = new CancellationTokenSource();

        // Cancel once the second action is about to start.
        var result = ActionExecutor.Apply(_pair, [Copy("a.txt"), Copy("b.txt"), Copy("c.txt")],
            a => { if (a.RelativePath == "b.txt") cts.Cancel(); }, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.Applied);
        Assert.True(File.Exists(Path.Combine(_rightRoot, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_rightRoot, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_rightRoot, "c.txt")));
    }

    [Fact]
    public void Apply_LeavesExistingDestinationIntact_WhenCancelledMidCopy()
    {
        // Larger than one copy buffer, so the loop gets a chance to observe the cancel.
        File.WriteAllBytes(Path.Combine(_leftRoot, "big.bin"), new byte[3 * 1024 * 1024]);
        File.WriteAllText(Path.Combine(_rightRoot, "big.bin"), "old version");
        using var cts = new CancellationTokenSource();

        var result = ActionExecutor.Apply(_pair, [Copy("big.bin")], _ => cts.Cancel(), cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal("old version", File.ReadAllText(Path.Combine(_rightRoot, "big.bin")));
        Assert.Empty(Directory.GetFiles(_rightRoot, "*" + Scanner.TempFileSuffix));
    }

    [Fact]
    public void Scan_IgnoresLeftoverTempFiles()
    {
        File.WriteAllText(Path.Combine(_leftRoot, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_leftRoot, "a.txt" + Scanner.TempFileSuffix), "partial");

        var state = Scanner.Scan(_leftRoot, _pair);

        Assert.Equal(["a.txt"], state.Keys);
    }

    [Fact]
    public void Scan_Throws_WhenAlreadyCancelled()
    {
        File.WriteAllText(Path.Combine(_leftRoot, "a.txt"), "a");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => Scanner.Scan(_leftRoot, _pair, cancellationToken: cts.Token));
    }

    private static SyncAction Copy(string relativePath) =>
        new() { Type = ActionType.CopyLeftToRight, RelativePath = relativePath, Reason = "test" };
}
