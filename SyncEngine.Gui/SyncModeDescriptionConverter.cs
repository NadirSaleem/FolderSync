using Microsoft.UI.Xaml.Data;
using FolderSync.Engine;

namespace FolderSync.Gui;

public sealed class SyncModeDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is SyncMode mode ? Describe(mode) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static string Describe(SyncMode mode) => mode switch
    {
        SyncMode.Echo =>
            "Left is the master copy. New and updated files are copied left to right. " +
            "Deletes on the left are repeated on the right, so right always ends up matching left exactly.",
        SyncMode.Sync =>
            "New and updated files are copied both ways. Deletes on either side are repeated on the other. " +
            "A file changed on both sides since the last run is flagged as a conflict instead of being overwritten.",
        SyncMode.Contribute =>
            "New and updated files are copied left to right, like Echo. Nothing is ever deleted on the right, " +
            "even if it was deleted on the left.",
        _ => string.Empty
    };
}
