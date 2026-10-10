using FolderSync.Engine;

namespace FolderSync.Gui;

/// <summary>
/// Editable, bindable wrapper around a FolderPair. Converts back to the immutable
/// engine type via ToFolderPair() whenever the engine needs one.
/// </summary>
public sealed class FolderPairViewModel : ObservableObject
{
    private string _name;
    private string _leftPath;
    private string _rightPath;
    private SyncMode _mode;
    private bool _isSelected;
    private DateTime? _lastRunUtc;

    public FolderPairViewModel(FolderPair pair)
    {
        _name = pair.Name;
        SavedName = pair.Name;
        _leftPath = pair.LeftPath;
        _rightPath = pair.RightPath;
        _mode = pair.Mode;
    }

    /// <summary>Checked in the pairs list to include this pair in a batch Preview/Run.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>
    /// The name as last written to pairs.json. Sync state is keyed by this, not Name, so
    /// an unsaved rename in the edit box doesn't split the pair's history across two
    /// state files; SavePairs moves the state over when the rename is saved.
    /// </summary>
    public string SavedName { get; set; }

    public string LeftPath
    {
        get => _leftPath;
        set => SetField(ref _leftPath, value);
    }

    public string RightPath
    {
        get => _rightPath;
        set => SetField(ref _rightPath, value);
    }

    public SyncMode Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    /// <summary>When this pair was last run (GUI or CLI); null if never.</summary>
    public DateTime? LastRunUtc
    {
        get => _lastRunUtc;
        set
        {
            if (SetField(ref _lastRunUtc, value))
                OnPropertyChanged(nameof(LastRunText));
        }
    }

    public string LastRunText => _lastRunUtc is { } utc
        ? $"Last run: {utc.ToLocalTime():ddd d MMM yyyy, HH:mm} ({FormatAgo(DateTime.UtcNow - utc)})"
        : "Last run: never";

    private static string FormatAgo(TimeSpan span) => span switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)span.TotalMinutes} min ago",
        { TotalDays: < 1 } => $"{(int)span.TotalHours} hr ago",
        { TotalDays: < 2 } => "yesterday",
        _ => $"{(int)span.TotalDays} days ago"
    };

    /// <summary>Short one-line summary shown in the pairs list.</summary>
    public string Summary => $"{LeftPath}  →  {RightPath}";

    public FolderPair ToFolderPair() => new()
    {
        Name = Name,
        LeftPath = LeftPath,
        RightPath = RightPath,
        Mode = Mode
    };
}
