using Xunit;
using static FolderSync.Engine.Tests.TestData;

namespace FolderSync.Engine.Tests;

public class DiffEngineTests
{
    // ---------- Echo mode ----------

    [Fact]
    public void Echo_NewFileOnLeft_CopiesLeftToRight()
    {
        var pair = Pair(SyncMode.Echo);
        var left = Map(File("a.txt"));
        var right = Empty();
        var snapshot = Snapshot();

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.CopyLeftToRight, action.Type);
        Assert.Equal("a.txt", action.RelativePath);
    }

    [Fact]
    public void Echo_FileModifiedOnLeft_CopiesLeftToRight()
    {
        var pair = Pair(SyncMode.Echo);
        var left = Map(File("a.txt", time: T2));
        var right = Map(File("a.txt", time: T1));
        var snapshot = Snapshot(left: Map(File("a.txt", time: T1)), right: Map(File("a.txt", time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.CopyLeftToRight, action.Type);
    }

    [Fact]
    public void Echo_FileRemovedFromLeft_DeletesOnRight()
    {
        var pair = Pair(SyncMode.Echo);
        var left = Empty();
        var right = Map(File("a.txt"));
        var snapshot = Snapshot(left: Map(File("a.txt")), right: Map(File("a.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.DeleteOnRight, action.Type);
    }

    [Fact]
    public void Echo_IdenticalOnBothSides_ProducesNoActions()
    {
        var pair = Pair(SyncMode.Echo);
        var left = Map(File("a.txt"));
        var right = Map(File("a.txt"));
        var snapshot = Snapshot();

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Empty(plan);
    }

    [Fact]
    public void Echo_FileExistsOnRightOnlyNeverOnLeft_IsDeletedToMatchMirror()
    {
        // Echo's contract is "right mirrors left exactly" — a right-only file
        // that was never on left doesn't belong there and gets removed,
        // even though it wasn't a tracked deletion.
        var pair = Pair(SyncMode.Echo);
        var left = Empty();
        var right = Map(File("orphan.txt"));
        var snapshot = Snapshot(); // right file was never known -> still gets echoed away

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.DeleteOnRight, action.Type);
    }

    // ---------- Contribute mode ----------

    [Fact]
    public void Contribute_NewFileOnLeft_CopiesLeftToRight()
    {
        var pair = Pair(SyncMode.Contribute);
        var left = Map(File("a.txt"));
        var right = Empty();
        var snapshot = Snapshot();

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.CopyLeftToRight, action.Type);
    }

    [Fact]
    public void Contribute_FileRemovedFromLeft_NeverDeletesOnRight()
    {
        var pair = Pair(SyncMode.Contribute);
        var left = Empty();
        var right = Map(File("a.txt"));
        var snapshot = Snapshot(left: Map(File("a.txt")), right: Map(File("a.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Empty(plan);
    }

    [Fact]
    public void Contribute_ExtraFileOnRightOnly_IsNeverTouched()
    {
        var pair = Pair(SyncMode.Contribute);
        var left = Empty();
        var right = Map(File("orphan.txt"));
        var snapshot = Snapshot();

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Empty(plan);
    }

    // ---------- Sync mode ----------

    [Fact]
    public void Sync_NewFileOnLeftOnly_PropagatesToRight()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt"));
        var right = Empty();
        var snapshot = Snapshot();

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.CopyLeftToRight, action.Type);
        Assert.Equal("new on left", action.Reason);
    }

    [Fact]
    public void Sync_NewFileOnRightOnly_PropagatesToLeft()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Empty();
        var right = Map(File("a.txt"));
        var snapshot = Snapshot();

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.CopyRightToLeft, action.Type);
        Assert.Equal("new on right", action.Reason);
    }

    [Fact]
    public void Sync_DeletedOnLeftOnly_PropagatesDeleteToRight()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Empty();
        var right = Map(File("a.txt"));
        var snapshot = Snapshot(left: Map(File("a.txt")), right: Map(File("a.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.DeleteOnRight, action.Type);
        Assert.Equal("deleted on left", action.Reason);
    }

    [Fact]
    public void Sync_DeletedOnRightOnly_PropagatesDeleteToLeft()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt"));
        var right = Empty();
        var snapshot = Snapshot(left: Map(File("a.txt")), right: Map(File("a.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.DeleteOnLeft, action.Type);
        Assert.Equal("deleted on right", action.Reason);
    }

    [Fact]
    public void Sync_ModifiedOnBothSidesDifferently_IsFlaggedAsConflict()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt", size: 200, time: T2));
        var right = Map(File("a.txt", size: 300, time: T2));
        var snapshot = Snapshot(
            left: Map(File("a.txt", size: 100, time: T1)),
            right: Map(File("a.txt", size: 100, time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.Conflict, action.Type);
    }

    [Fact]
    public void Sync_ModifiedOnBothSidesIdentically_IsNotAConflict()
    {
        // Both sides ended up with the exact same file (e.g. same edit applied
        // twice, or a previous sync already reconciled it) — nothing to do.
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt", size: 200, time: T2));
        var right = Map(File("a.txt", size: 200, time: T2));
        var snapshot = Snapshot(
            left: Map(File("a.txt", size: 100, time: T1)),
            right: Map(File("a.txt", size: 100, time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Empty(plan);
    }

    [Fact]
    public void Sync_UnchangedOnBothSides_ProducesNoActions()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt"));
        var right = Map(File("a.txt"));
        var snapshot = Snapshot(left: Map(File("a.txt")), right: Map(File("a.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Empty(plan);
    }

    [Fact]
    public void Sync_DeletedOnBothSidesSince_ProducesNoActions()
    {
        // File existed before, is now gone from both sides -> already in sync.
        var pair = Pair(SyncMode.Sync);
        var left = Empty();
        var right = Empty();
        var snapshot = Snapshot(left: Map(File("a.txt")), right: Map(File("a.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Empty(plan);
    }

    [Fact]
    public void Sync_DeletedOnLeftButAlsoModifiedOnRight_IsAConflictNotADelete()
    {
        // Right changed since last run while left deleted the file -> ambiguous,
        // must not silently delete the right's newer edit.
        var pair = Pair(SyncMode.Sync);
        var left = Empty();
        var right = Map(File("a.txt", size: 999, time: T2));
        var snapshot = Snapshot(left: Map(File("a.txt", time: T1)), right: Map(File("a.txt", time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.Conflict, action.Type);
    }

    [Fact]
    public void Sync_ModifiedOnLeftButAlsoDeletedOnRight_IsAConflictNotADelete()
    {
        // The mirror image of the above: left changed while right deleted the file.
        // Must not silently delete the left's newer edit.
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt", size: 999, time: T2));
        var right = Empty();
        var snapshot = Snapshot(left: Map(File("a.txt", time: T1)), right: Map(File("a.txt", time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.Conflict, action.Type);
    }

    // ---------- Hash-based conflict confirmation ----------

    [Fact]
    public void Sync_QuickCheckSaysDifferent_ButHashSaysIdentical_IsNotAConflict()
    {
        // Size/timestamp disagree (e.g. a re-save that didn't change content, or a
        // touch), but the supplied hash comparer proves the bytes are the same.
        // The hash comparer should win over the quick heuristic.
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt", size: 200, time: T2));
        var right = Map(File("a.txt", size: 300, time: T2)); // different size per FileState, but hash says same
        var snapshot = Snapshot(
            left: Map(File("a.txt", size: 100, time: T1)),
            right: Map(File("a.txt", size: 100, time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot, contentsDiffer: _ => false);

        Assert.Empty(plan);
    }

    [Fact]
    public void Sync_QuickCheckSaysIdentical_ButHashSaysDifferent_IsAConflict()
    {
        // Size/timestamp happen to match (a coincidence, or a filesystem with coarse
        // timestamp resolution), but the hash comparer proves the content really
        // differs. Trusting only QuickEquals here would silently drop a real conflict.
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt", size: 200, time: T2));
        var right = Map(File("a.txt", size: 200, time: T2)); // identical per FileState, but hash says different
        var snapshot = Snapshot(
            left: Map(File("a.txt", size: 100, time: T1)),
            right: Map(File("a.txt", size: 100, time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot, contentsDiffer: _ => true);

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.Conflict, action.Type);
    }

    [Fact]
    public void Sync_HashComparerOmitted_FallsBackToQuickEqualsHeuristic()
    {
        // No contentsDiffer supplied -> behavior matches the pre-hashing heuristic,
        // so DiffEngine stays usable (and fast) without any I/O for callers that
        // don't need the extra certainty.
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("a.txt", size: 200, time: T2));
        var right = Map(File("a.txt", size: 300, time: T2));
        var snapshot = Snapshot(
            left: Map(File("a.txt", size: 100, time: T1)),
            right: Map(File("a.txt", size: 100, time: T1)));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot); // no delegate

        var action = Assert.Single(plan);
        Assert.Equal(ActionType.Conflict, action.Type);
    }

    [Fact]
    public void Sync_HashComparer_IsOnlyInvokedWhenBothSidesChanged()
    {
        // The comparer does real disk I/O in production, so it must not be called
        // for paths where only one side changed — that path is unambiguous already.
        var pair = Pair(SyncMode.Sync);
        var left = Map(File("new-on-left.txt"));
        var right = Empty();
        var snapshot = Snapshot();
        var wasCalled = false;

        DiffEngine.BuildPlan(pair, left, right, snapshot, contentsDiffer: _ => { wasCalled = true; return true; });

        Assert.False(wasCalled);
    }

    [Fact]
    public void Sync_MultipleIndependentFiles_EachGetsItsOwnCorrectAction()
    {
        var pair = Pair(SyncMode.Sync);
        var left = Map(
            File("new-on-left.txt"),
            File("unchanged.txt"));
        var right = Map(
            File("new-on-right.txt"),
            File("unchanged.txt"));
        var snapshot = Snapshot(
            left: Map(File("unchanged.txt")),
            right: Map(File("unchanged.txt")));

        var plan = DiffEngine.BuildPlan(pair, left, right, snapshot);

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, a => a.RelativePath == "new-on-left.txt" && a.Type == ActionType.CopyLeftToRight);
        Assert.Contains(plan, a => a.RelativePath == "new-on-right.txt" && a.Type == ActionType.CopyRightToLeft);
    }
}
