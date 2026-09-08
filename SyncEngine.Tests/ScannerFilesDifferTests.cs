using Xunit;

namespace FolderSync.Engine.Tests;

/// <summary>
/// Unlike DiffEngineTests (pure, in-memory), these tests touch real files on disk
/// to confirm Scanner.FilesDiffer — the thing DiffEngine's contentsDiffer delegate
/// is meant to be backed by in production — actually behaves correctly.
/// </summary>
public class ScannerFilesDifferTests : IDisposable
{
    private readonly string _leftRoot;
    private readonly string _rightRoot;

    public ScannerFilesDifferTests()
    {
        _leftRoot = Directory.CreateTempSubdirectory("foldersync-left-").FullName;
        _rightRoot = Directory.CreateTempSubdirectory("foldersync-right-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_leftRoot, recursive: true);
        Directory.Delete(_rightRoot, recursive: true);
    }

    [Fact]
    public void IdenticalContent_ReturnsFalse_EvenWithDifferentTimestamps()
    {
        WriteFile(_leftRoot, "a.txt", "same content", DateTime.UtcNow.AddDays(-5));
        WriteFile(_rightRoot, "a.txt", "same content", DateTime.UtcNow);

        var differ = Scanner.FilesDiffer(_leftRoot, _rightRoot, "a.txt");

        Assert.False(differ);
    }

    [Fact]
    public void DifferentContent_ReturnsTrue_EvenWithSameSizeAndTimestamp()
    {
        var timestamp = DateTime.UtcNow;
        // Same length, same timestamp, different bytes — exactly the case QuickEquals
        // (size + timestamp only) cannot catch.
        WriteFile(_leftRoot, "a.txt", "aaaaaaaaaa", timestamp);
        WriteFile(_rightRoot, "a.txt", "bbbbbbbbbb", timestamp);

        var differ = Scanner.FilesDiffer(_leftRoot, _rightRoot, "a.txt");

        Assert.True(differ);
    }

    [Fact]
    public void DifferentContentAndSize_ReturnsTrue()
    {
        WriteFile(_leftRoot, "a.txt", "short", DateTime.UtcNow);
        WriteFile(_rightRoot, "a.txt", "a much longer piece of content", DateTime.UtcNow);

        var differ = Scanner.FilesDiffer(_leftRoot, _rightRoot, "a.txt");

        Assert.True(differ);
    }

    private static void WriteFile(string root, string relativePath, string content, DateTime lastWriteUtc)
    {
        var fullPath = Path.Combine(root, relativePath);
        File.WriteAllText(fullPath, content);
        File.SetLastWriteTimeUtc(fullPath, lastWriteUtc);
    }
}
