using System.Runtime.InteropServices;
using Loadout.Contracts;
using Loadout.Launching;
using Loadout.Settings.Model;
using Loadout.Tests.Settings;
using static Loadout.Launching.LaunchingNative;

namespace Loadout.Tests.Launching;

// None of these tests open a window or start a program: every case returns before anything is started.
public class ShellLauncherTests
{
    private const string Notepad = "C:\\Windows\\System32\\notepad.exe";

    [Fact]
    public void MissingExe_Fails_WithClearMessage()
    {
        using var sandbox = new TestSandbox();
        var missing = Path.Combine(sandbox.Dir, "nope", "missing.exe");

        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.App, Path = missing });

        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
        Assert.Equal("The app couldn't be found: " + missing, result.Message);
    }

    [Fact]
    public void MissingShortcut_Fails()
    {
        using var sandbox = new TestSandbox();
        var missing = Path.Combine(sandbox.Dir, "missing.lnk");

        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.App, Path = missing });

        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void EmptyAppPath_Fails()
    {
        using var sandbox = new TestSandbox();
        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.App, Path = "" });
        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void BadUrl_Fails(string? url)
    {
        using var sandbox = new TestSandbox();
        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.Website, Url = url });
        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Message));
    }

    [Fact]
    public void MissingFolder_Fails()
    {
        using var sandbox = new TestSandbox();
        var missing = Path.Combine(sandbox.Dir, "no-such-folder");

        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.Folder, Path = missing });

        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
        Assert.Contains(missing, result.Message);
    }

    [Fact]
    public void EmptyFolderPath_Fails()
    {
        using var sandbox = new TestSandbox();
        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.Folder, Path = null });
        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void ShellFolder_ResolvesToRealPath()
    {
        var resolved = ShellLauncher.ResolveShellFolder("shell:Downloads");
        Assert.NotNull(resolved);
        Assert.EndsWith("Downloads", resolved);
    }

    [Fact]
    public void UnknownShellFolder_ResolvesToNull()
    {
        Assert.Null(ShellLauncher.ResolveShellFolder("shell:ThisFolderDoesNotExist"));
    }

    [Fact]
    public void Shortcut_ResolvesToItsTarget()
    {
        if (!File.Exists(Notepad))
        {
            return;
        }

        using var sandbox = new TestSandbox();
        var lnk = Path.Combine(sandbox.Dir, "notepad.lnk");
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(Notepad);
            ((IPersistFile)link).Save(lnk, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }

        var target = ShellLauncher.ResolveShortcutTarget(lnk);

        Assert.True(LaunchHelpers.PathsEqual(Notepad, target), "Target was: " + target);
    }

    [Fact]
    public void Shortcut_BrokenFile_ResolvesToNull()
    {
        using var sandbox = new TestSandbox();
        var lnk = Path.Combine(sandbox.Dir, "broken.lnk");
        File.WriteAllText(lnk, "not a shortcut");

        Assert.Null(ShellLauncher.ResolveShortcutTarget(lnk));
    }
}
