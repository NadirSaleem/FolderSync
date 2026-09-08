using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using FolderSync.Engine;

namespace FolderSync.Gui;

public sealed partial class MainView : UserControl
{
    private readonly Window _owner;

    public MainViewModel ViewModel { get; } = new();

    /// <summary>Bound to the mode ComboBox — every value of the SyncMode enum.</summary>
    public SyncMode[] SyncModes { get; } = Enum.GetValues<SyncMode>();

    /// <param name="owner">
    /// The hosting Window. Needed only for the folder pickers below — WinUI 3's
    /// picker APIs require a real HWND, which a UserControl doesn't have on its own.
    /// </param>
    public MainView(Window owner)
    {
        _owner = owner;
        InitializeComponent();
    }

    private async void BrowseLeft_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null && ViewModel.SelectedPair is not null)
            ViewModel.SelectedPair.LeftPath = path;
    }

    private async void BrowseRight_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null && ViewModel.SelectedPair is not null)
            ViewModel.SelectedPair.RightPath = path;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");

        // WinUI 3 desktop apps aren't tied to a CoreWindow, so pickers need the
        // underlying HWND explicitly — this is the standard incantation for it.
        var hwnd = WindowNative.GetWindowHandle(_owner);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
