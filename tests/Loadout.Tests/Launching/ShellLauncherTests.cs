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

    private static string MakeShortcut(string folder, string name, string target)
    {
        var lnk = Path.Combine(folder, name);
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(target);
            ((IPersistFile)link).Save(lnk, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }

        return lnk;
    }

    [Fact]
    public void UnknownShellFolder_Fails_AndOpensNothing()
    {
        using var sandbox = new TestSandbox();
        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.Folder, Path = "shell:ThisFolderDoesNotExist" });
        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void ShortcutWithMissingTarget_Fails_WithClearMessage()
    {
        using var sandbox = new TestSandbox();
        var gone = Path.Combine(sandbox.Dir, "gone", "app.exe");
        var lnk = MakeShortcut(sandbox.Dir, "broken.lnk", gone);

        var result = new ShellLauncher().Launch(new SlotSettings { Type = SlotType.App, Path = lnk });

        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
        Assert.Equal("The shortcut points to something that no longer exists: " + gone, result.Message);
    }

    [Fact]
    public void GetRealPath_OfExistingFile_ReturnsAPlainPath()
    {
        var real = WindowFinder.GetRealPath(Notepad);
        Assert.NotNull(real);
        Assert.DoesNotContain("\\\\?\\", real);
        Assert.EndsWith("notepad.exe", real, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetRealPath_OfMissingFile_ReturnsNull()
    {
        Assert.Null(WindowFinder.GetRealPath("C:\\definitely\\not\\here.exe"));
    }

    [Fact]
    public void BuildMatchSet_SystemNotepad_UsesItsAliasWhenOneExists()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "notepad.exe");
        var match = WindowFinder.BuildMatchSet(Notepad);
        Assert.Contains(LaunchHelpers.NormalizePath(Notepad), match.Paths);
        if (File.Exists(alias))
        {
            Assert.False(string.IsNullOrEmpty(match.Aumid));
        }
        else
        {
            Assert.Null(match.Aumid);
        }
    }

    [Fact]
    public void StartWorker_RunsWorkOnAnStaThread()
    {
        using var done = new ManualResetEventSlim();
        ApartmentState? state = null;
        StartWorker.Enqueue("test", () =>
        {
            state = Thread.CurrentThread.GetApartmentState();
            done.Set();
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(ApartmentState.STA, state);
    }

    [Fact]
    public void Shortcut_ResolvesToItsTarget()
    {
        Assert.True(File.Exists(Notepad), "This test needs " + Notepad);

        using var sandbox = new TestSandbox();
        var lnk = MakeShortcut(sandbox.Dir, "notepad.lnk", Notepad);

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
