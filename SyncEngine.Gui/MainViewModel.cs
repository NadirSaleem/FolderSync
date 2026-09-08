using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using FolderSync.Engine;
using Microsoft.UI.Dispatching;

namespace FolderSync.Gui;

public sealed class MainViewModel : ObservableObject
{
    // Shared with the CLI: both read/write the same pairs.json and state snapshots,
    // so a pair added in one tool shows up in the other.
    private readonly string _pairsFilePath;
    private readonly StateStore _stateStore = new();

    // Captured on construction, which always happens on the UI thread — lets background
    // scan callbacks post status updates back without touching StatusText off-thread.
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    // Keyed by pair view-model instance (identity is stable even if Name is edited) so
    // Run can re-apply exactly what Preview last computed for each checked pair.
    private readonly Dictionary<FolderPairViewModel, List<SyncAction>> _lastPlans = new();

    private FolderPairViewModel? _selectedPair;
    private string _statusText = "Check one or more pairs, then Preview to see what would change.";

    public MainViewModel()
    {
        _pairsFilePath = AppPaths.PairsFilePath;
        Pairs = new ObservableCollection<FolderPairViewModel>(LoadPairs().Select(p => new FolderPairViewModel(p)));
        foreach (var pair in Pairs)
            pair.PropertyChanged += OnPairPropertyChanged;
        Pairs.CollectionChanged += OnPairsCollectionChanged;

        PreviewCommand = new RelayCommand(PreviewAsync, () => Pairs.Any(p => p.IsSelected));
        RunCommand = new RelayCommand(RunAsync, () => Pairs.Any(p => p.IsSelected) && _lastPlans.Values.Any(plan => plan.Count > 0));
        AddPairCommand = new RelayCommand(AddPairAsync);
        RemovePairCommand = new RelayCommand(RemovePairAsync, () => SelectedPair is not null);
        SaveEditsCommand = new RelayCommand(SaveEditsAsync, () => SelectedPair is not null);
    }

    private void OnPairsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (FolderPairViewModel pair in e.OldItems)
            {
                pair.PropertyChanged -= OnPairPropertyChanged;
                _lastPlans.Remove(pair);
            }

        if (e.NewItems is not null)
            foreach (FolderPairViewModel pair in e.NewItems)
                pair.PropertyChanged += OnPairPropertyChanged;

        RaiseCanExecuteChanged();
    }

    private void OnPairPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderPairViewModel.IsSelected))
            return;

        // A stale plan could otherwise be Run against a selection the user just changed.
        Plan.Clear();
        _lastPlans.Clear();

        var checkedCount = Pairs.Count(p => p.IsSelected);
        StatusText = checkedCount == 0
            ? "Check one or more pairs, then Preview to see what would change."
            : $"{checkedCount} pair(s) checked — click Preview to see what would change.";

        RaiseCanExecuteChanged();
    }

    public ObservableCollection<FolderPairViewModel> Pairs { get; }

    public ObservableCollection<ActionRow> Plan { get; } = new();

    /// <summary>The pair currently open in the detail-edit form. Independent of which
    /// pairs are checked for batch Preview/Run.</summary>
    public FolderPairViewModel? SelectedPair
    {
        get => _selectedPair;
        set
        {
            if (SetField(ref _selectedPair, value))
                RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public RelayCommand PreviewCommand { get; }
    public RelayCommand RunCommand { get; }
    public RelayCommand AddPairCommand { get; }
    public RelayCommand RemovePairCommand { get; }
    public RelayCommand SaveEditsCommand { get; }

    private async Task AddPairAsync()
    {
        var vm = new FolderPairViewModel(new FolderPair
        {
            Name = $"New pair {Pairs.Count + 1}",
            LeftPath = "",
            RightPath = "",
            Mode = SyncMode.Sync
        });
        Pairs.Add(vm);
        SelectedPair = vm;
        SavePairs();
        await Task.CompletedTask;
    }

    private async Task RemovePairAsync()
    {
        if (SelectedPair is null)
            return;

        Pairs.Remove(SelectedPair);
        SelectedPair = null;
        SavePairs();
        await Task.CompletedTask;
    }

    private async Task SaveEditsAsync()
    {
        // Names/paths/mode are edited in place on the view model; just persist.
        SavePairs();
        StatusText = "Saved.";
        await Task.CompletedTask;
    }

    private async Task PreviewAsync()
    {
        var checkedPairs = Pairs.Where(p => p.IsSelected).ToList();
        if (checkedPairs.Count == 0)
            return;

        StatusText = "Scanning…";
        Plan.Clear();
        _lastPlans.Clear();

        var totalActions = 0;
        var totalConflicts = 0;
        var failures = new List<string>();

        foreach (var pairVm in checkedPairs)
        {
            var pair = pairVm.ToFolderPair();
            ReportStatus(checkedPairs.Count == 1 ? "Scanning…" : $"Scanning '{pair.Name}'…");

            try
            {
                var (plan, conflictCount) = await Task.Run(() => BuildPlan(pair));
                _lastPlans[pairVm] = plan;

                foreach (var action in plan)
                    Plan.Add(new ActionRow(action, pair.Name));

                totalActions += plan.Count;
                totalConflicts += conflictCount;
            }
            catch (Exception ex)
            {
                failures.Add($"'{pair.Name}': {ex.Message}");
            }
        }

        StatusText = BuildScanSummary(checkedPairs.Count, totalActions, totalConflicts, failures);
        RaiseCanExecuteChanged();
    }

    private static string BuildScanSummary(int pairCount, int totalActions, int totalConflicts, List<string> failures)
    {
        var summary = totalActions == 0 && failures.Count == 0
            ? "Already in sync — nothing to do."
            : pairCount == 1
                ? $"{totalActions} change(s) planned."
                : $"{totalActions} change(s) planned across {pairCount} pair(s).";

        if (totalConflicts > 0)
            summary += $" {totalConflicts} conflict(s) need manual resolution.";

        if (failures.Count > 0)
            summary += $" Error scanning {failures.Count}: {string.Join("; ", failures)}";

        return summary;
    }

    private async Task RunAsync()
    {
        var runnablePairs = Pairs.Where(p => p.IsSelected && _lastPlans.GetValueOrDefault(p)?.Count > 0).ToList();
        if (runnablePairs.Count == 0)
            return;

        StatusText = runnablePairs.Count == 1 ? "Applying changes…" : $"Applying changes to {runnablePairs.Count} pair(s)…";

        var totalApplied = 0;
        var totalSkipped = 0;
        var totalConflicts = 0;
        var failures = new List<string>();

        foreach (var pairVm in runnablePairs)
        {
            var pair = pairVm.ToFolderPair();
            var plan = _lastPlans[pairVm];

            try
            {
                var result = await Task.Run(() =>
                {
                    var appliedSoFar = 0;
                    void OnBeforeApply(SyncAction action)
                    {
                        appliedSoFar++;
                        var verb = action.Type switch
                        {
                            ActionType.CopyLeftToRight or ActionType.CopyRightToLeft => "Copying",
                            ActionType.DeleteOnLeft or ActionType.DeleteOnRight => "Deleting",
                            _ => "Applying"
                        };
                        ReportStatus($"{verb} '{action.RelativePath}' ({appliedSoFar:N0}/{plan.Count:N0}) — '{pair.Name}'");
                    }

                    var applyResult = ActionExecutor.Apply(pair, plan, OnBeforeApply);

                    var scanned = 0;
                    void OnFileScanned()
                    {
                        scanned++;
                        if (scanned % 25 == 0)
                            ReportStatus($"Applying changes to '{pair.Name}'… rescanning ({scanned:N0} file(s) so far)");
                    }

                    var newSnapshot = new PairSnapshot
                    {
                        Left = Scanner.Scan(pair.LeftPath, pair, OnFileScanned),
                        Right = Scanner.Scan(pair.RightPath, pair, OnFileScanned),
                        LastRunUtc = DateTime.UtcNow
                    };
                    _stateStore.Save(pair.Name, newSnapshot);

                    return applyResult;
                });

                totalApplied += result.Applied;
                totalSkipped += result.Skipped;
                totalConflicts += result.Conflicts;
                if (result.Errors.Count > 0)
                    failures.Add($"'{pair.Name}': {result.Errors.Count} error(s)");
            }
            catch (Exception ex)
            {
                failures.Add($"'{pair.Name}': {ex.Message}");
            }
        }

        StatusText = $"Done. Applied {totalApplied}, skipped {totalSkipped}, conflicts {totalConflicts}."
            + (failures.Count > 0 ? $" Errors — {string.Join("; ", failures)}" : "");

        // Re-preview so the list reflects the post-run state.
        await PreviewAsync();
    }

    private (List<SyncAction> plan, int conflictCount) BuildPlan(FolderPair pair)
    {
        var scanned = 0;
        void OnFileScanned()
        {
            scanned++;
            if (scanned % 25 == 0)
                ReportStatus($"Scanning… {scanned:N0} file(s) found so far");
        }

        var currentLeft = Scanner.Scan(pair.LeftPath, pair, OnFileScanned);
        var currentRight = Scanner.Scan(pair.RightPath, pair, OnFileScanned);
        var snapshot = _stateStore.Load(pair.Name);

        var plan = DiffEngine.BuildPlan(pair, currentLeft, currentRight, snapshot,
            relPath => ContentsDiffer(pair, relPath));

        var conflictCount = plan.Count(a => a.Type == ActionType.Conflict);
        return (plan, conflictCount);
    }

    private static bool ContentsDiffer(FolderPair pair, string relativePath)
    {
        try
        {
            return Scanner.FilesDiffer(pair.LeftPath, pair.RightPath, relativePath);
        }
        catch
        {
            // Can't prove they're identical — treat as a conflict rather than guess.
            return true;
        }
    }

    private List<FolderPair> LoadPairs()
    {
        if (!File.Exists(_pairsFilePath))
            return new List<FolderPair>();

        var json = File.ReadAllText(_pairsFilePath);
        return JsonSerializer.Deserialize<List<FolderPair>>(json) ?? new List<FolderPair>();
    }

    private void SavePairs()
    {
        var pairs = Pairs.Select(vm => vm.ToFolderPair()).ToList();
        var json = JsonSerializer.Serialize(pairs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_pairsFilePath, json);
    }

    /// <summary>Sets StatusText safely whether called from the UI thread or a background scan.</summary>
    private void ReportStatus(string text)
    {
        if (_dispatcherQueue.HasThreadAccess)
            StatusText = text;
        else
            _dispatcherQueue.TryEnqueue(() => StatusText = text);
    }

    private void RaiseCanExecuteChanged()
    {
        PreviewCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        RemovePairCommand.RaiseCanExecuteChanged();
        SaveEditsCommand.RaiseCanExecuteChanged();
    }
}
