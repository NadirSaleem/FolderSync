namespace FolderSync.Engine;

/// <summary>
/// Shared file locations for the CLI and GUI, so a pair added in one tool shows up in the other.
/// </summary>
public static class AppPaths
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderSync");

    public static string PairsFilePath => Path.Combine(ConfigDirectory, "pairs.json");

    static AppPaths()
    {
        Directory.CreateDirectory(ConfigDirectory);
    }
}
