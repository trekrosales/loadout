using Loadout.Core;
using Loadout.Settings.Model;

namespace Loadout.Tests.Settings;

/// <summary>
/// A throwaway folder that LOADOUT_DATA_DIR points at for the life of one test.
/// Keeps tests away from the real %AppData%\Loadout and %LocalAppData%\Loadout, including the error log.
/// </summary>
internal sealed class TestSandbox : IDisposable
{
    private readonly string? _previousOverride;

    public TestSandbox()
    {
        Dir = Path.Combine(Path.GetTempPath(), "loadout-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);

        _previousOverride = Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, Dir);
    }

    public string Dir { get; }

    public string SettingsFile => Path.Combine(Dir, "settings.json");

    public string BrokenFile => Path.Combine(Dir, "settings.broken.json");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, _previousOverride);
        if (Directory.Exists(Dir))
        {
            Directory.Delete(Dir, recursive: true);
        }
    }
}

internal static class SettingsTestHelpers
{
    /// <summary>Canonical text form, so two settings objects can be compared as whole values.</summary>
    public static string Json(LoadoutSettings settings) => SettingsJson.Serialize(settings);
}
