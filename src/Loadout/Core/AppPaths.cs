namespace Loadout.Core;

/// <summary>
/// Where Loadout keeps its files (SPEC section 10).
/// Set the environment variable LOADOUT_DATA_DIR to put everything in one folder instead,
/// which tests and agents use so they never touch the real settings.
/// </summary>
public static class AppPaths
{
    public const string DataDirOverrideVariable = "LOADOUT_DATA_DIR";

    /// <summary>Roaming folder holding settings.json.</summary>
    public static string SettingsDirectory => Resolve(Environment.SpecialFolder.ApplicationData);

    /// <summary>Local folder holding the icon cache and log.</summary>
    public static string LocalDirectory => Resolve(Environment.SpecialFolder.LocalApplicationData);

    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public static string BrokenSettingsFile => Path.Combine(SettingsDirectory, "settings.broken.json");

    public static string IconCacheDirectory => Path.Combine(LocalDirectory, "icons");

    public static string LogFile => Path.Combine(LocalDirectory, "loadout.log");

    private static string Resolve(Environment.SpecialFolder folder)
    {
        var overrideDir = Environment.GetEnvironmentVariable(DataDirOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return overrideDir;
        }

        return Path.Combine(Environment.GetFolderPath(folder), "Loadout");
    }
}
