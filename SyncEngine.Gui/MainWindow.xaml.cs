using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FolderSync.Gui;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "FolderSync";
        Content = new MainView(this);

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow.GetFromWindowId(windowId).SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
    }
}
