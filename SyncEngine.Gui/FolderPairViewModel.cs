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

    public FolderPairViewModel(FolderPair pair)
    {
        _name = pair.Name;
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
