using Xunit;

namespace FolderSync.Engine.Tests;

/// <summary>
/// Renaming a pair must carry its state over; otherwise the next run looks like a
/// first run and can't tell "deleted since last run" from "never existed".
/// </summary>
public class StateStoreRenameTests : IDisposable
{
    private readonly string _stateDir;
    private readonly StateStore _store;

    public StateStoreRenameTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("foldersync-state-").FullName;
        _store = new StateStore(_stateDir);
    }

    public void Dispose() => Directory.Delete(_stateDir, recursive: true);

    [Fact]
    public void Rename_MovesStateToNewName()
    {
        _store.Save("Music Sync", Snapshot("song.mp3"));

        Assert.True(_store.Rename("Music Sync", "Music"));

        Assert.Contains("song.mp3", _store.Load("Music").Left.Keys);
        Assert.Null(_store.GetLastRunUtc("Music Sync"));
    }

    [Fact]
    public void Rename_CaseOnly_KeepsState()
    {
        _store.Save("music", Snapshot("song.mp3"));

        Assert.True(_store.Rename("music", "Music"));

        Assert.Contains("song.mp3", _store.Load("Music").Left.Keys);
    }

    [Fact]
    public void Rename_WhenTargetHasState_RefusesAndLeavesBothIntact()
    {
        _store.Save("A", Snapshot("a.txt"));
        _store.Save("B", Snapshot("b.txt"));

        Assert.False(_store.Rename("A", "B"));

        Assert.Contains("a.txt", _store.Load("A").Left.Keys);
        Assert.Contains("b.txt", _store.Load("B").Left.Keys);
    }

    [Fact]
    public void Rename_WithNoExistingState_IsANoOp()
    {
        Assert.True(_store.Rename("Never run", "Still never run"));

        Assert.Null(_store.GetLastRunUtc("Still never run"));
    }

    private static PairSnapshot Snapshot(string file) => new()
    {
        Left = { [file] = new FileState { RelativePath = file, SizeBytes = 1, LastWriteTimeUtc = DateTime.UtcNow } },
        LastRunUtc = DateTime.UtcNow
    };
}
