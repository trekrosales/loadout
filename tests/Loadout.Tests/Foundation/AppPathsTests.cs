using Loadout.Core;

namespace Loadout.Tests.Foundation;

public class AppPathsTests
{
    [Fact]
    public void Override_PutsEverythingInOneFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loadout-test-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, dir);

            Assert.Equal(Path.Combine(dir, "settings.json"), AppPaths.SettingsFile);
            Assert.Equal(Path.Combine(dir, "icons"), AppPaths.IconCacheDirectory);
            Assert.Equal(Path.Combine(dir, "loadout.log"), AppPaths.LogFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, previous);
        }
    }

    [Fact]
    public void Log_WritesToLogFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loadout-test-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, dir);

            Log.Error("test message");

            Assert.Contains("test message", File.ReadAllText(AppPaths.LogFile), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, previous);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
