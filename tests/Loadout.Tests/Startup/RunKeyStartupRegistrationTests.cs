using Loadout.Startup;
using Microsoft.Win32;

namespace Loadout.Tests.Startup;

public sealed class RunKeyStartupRegistrationTests : IDisposable
{
    private const string Exe = @"C:\Apps\Loadout\Loadout.exe";
    private readonly string _keyPath = @"Software\Loadout-Tests\Run-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\Loadout-Tests");
        if (parent is { SubKeyCount: 0 })
        {
            Registry.CurrentUser.DeleteSubKey(@"Software\Loadout-Tests", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Disabled_when_nothing_is_registered()
    {
        Assert.False(new RunKeyStartupRegistration(_keyPath, Exe).IsEnabled);
    }

    [Fact]
    public void Enable_writes_quoted_path_and_disable_removes_it()
    {
        var reg = new RunKeyStartupRegistration(_keyPath, Exe);
        Assert.True(reg.SetEnabled(true));
        Assert.True(reg.IsEnabled);

        using (var key = Registry.CurrentUser.OpenSubKey(_keyPath))
        {
            Assert.Equal("\"" + Exe + "\"", key!.GetValue("Loadout"));
        }

        Assert.True(reg.SetEnabled(false));
        Assert.False(reg.IsEnabled);
    }

    [Fact]
    public void Disable_when_missing_succeeds()
    {
        Assert.True(new RunKeyStartupRegistration(_keyPath, Exe).SetEnabled(false));
    }

    [Fact]
    public void Value_for_another_exe_is_not_enabled()
    {
        new RunKeyStartupRegistration(_keyPath, @"C:\Other\Loadout.exe").SetEnabled(true);
        Assert.False(new RunKeyStartupRegistration(_keyPath, Exe).IsEnabled);
    }

    [Fact]
    public void Matching_ignores_case_and_quotes()
    {
        Assert.True(RunKeyStartupRegistration.Matches("\"c:\\apps\\loadout\\LOADOUT.EXE\"", Exe));
        Assert.True(RunKeyStartupRegistration.Matches(Exe, Exe));
        Assert.False(RunKeyStartupRegistration.Matches(null, Exe));
    }
}
