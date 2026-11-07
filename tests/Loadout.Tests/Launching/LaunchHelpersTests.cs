using Loadout.Launching;

namespace Loadout.Tests.Launching;

public class LaunchHelpersTests
{
    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com/path?q=1", true)]
    [InlineData("  https://example.com  ", true)]
    [InlineData("HTTPS://EXAMPLE.COM", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows", false)]
    [InlineData("example.com", false)]
    [InlineData("/relative/path", false)]
    [InlineData("https://", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void TryParseWebUrl_AcceptsOnlyAbsoluteHttpAndHttps(string? text, bool expected)
    {
        Assert.Equal(expected, LaunchHelpers.TryParseWebUrl(text, out var uri));
        Assert.Equal(expected, uri is not null);
    }

    [Theory]
    [InlineData("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("SHELL:APPSFOLDER\\Some.App!Main", "Some.App!Main")]
    [InlineData("shell:AppsFolder/Some.App!Main", "Some.App!Main")]
    [InlineData("shell:AppsFolder\\", null)]
    [InlineData("shell:Downloads", null)]
    [InlineData("C:\\Windows\\notepad.exe", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseAppsFolder_ExtractsTheId(string? path, string? expected)
    {
        Assert.Equal(expected, LaunchHelpers.ParseAppsFolder(path));
    }

    [Fact]
    public void IsShellPath_DetectsShellPrefix()
    {
        Assert.True(LaunchHelpers.IsShellPath("shell:Downloads"));
        Assert.True(LaunchHelpers.IsShellPath("SHELL:Downloads"));
        Assert.False(LaunchHelpers.IsShellPath("C:\\shell:thing"));
        Assert.False(LaunchHelpers.IsShellPath(null));
    }

    [Fact]
    public void PathsEqual_IgnoresCase()
    {
        Assert.True(LaunchHelpers.PathsEqual("C:\\Windows\\System32\\NOTEPAD.EXE", "c:\\windows\\system32\\notepad.exe"));
    }

    [Fact]
    public void PathsEqual_IgnoresTrailingSlashes()
    {
        Assert.True(LaunchHelpers.PathsEqual("C:\\Users\\Test\\", "C:\\Users\\Test"));
        Assert.True(LaunchHelpers.PathsEqual("C:\\Users\\Test/", "C:\\Users\\Test"));
    }

    [Fact]
    public void PathsEqual_CollapsesDotDot()
    {
        Assert.True(LaunchHelpers.PathsEqual("C:\\Windows\\System32\\..\\notepad.exe", "C:\\Windows\\notepad.exe"));
    }

    [Fact]
    public void PathsEqual_DifferentPathsAreNotEqual()
    {
        Assert.False(LaunchHelpers.PathsEqual("C:\\Windows\\notepad.exe", "C:\\Windows\\System32\\notepad.exe"));
    }

    [Fact]
    public void PathsEqual_NullOrEmptyIsNeverEqual()
    {
        Assert.False(LaunchHelpers.PathsEqual(null, null));
        Assert.False(LaunchHelpers.PathsEqual("", ""));
        Assert.False(LaunchHelpers.PathsEqual("C:\\a", null));
    }

    [Fact]
    public void NormalizePath_KeepsDriveRoot()
    {
        Assert.Equal("C:\\", LaunchHelpers.NormalizePath("C:\\"));
    }

    [Fact]
    public void NormalizePath_RejectsIllegalCharacters()
    {
        Assert.Null(LaunchHelpers.NormalizePath("C:\\bad\0name"));
    }

    [Theory]
    [InlineData("file:///C:/Users/Test/Documents", "C:\\Users\\Test\\Documents")]
    [InlineData("file:///C:/My%20Folder/Sub%20Dir", "C:\\My Folder\\Sub Dir")]
    [InlineData("file://server/share/folder", "\\\\server\\share\\folder")]
    [InlineData("file://server/share/My%20Folder", "\\\\server\\share\\My Folder")]
    public void FileUrlToPath_ConvertsFileUrls(string url, string expected)
    {
        Assert.Equal(expected, LaunchHelpers.FileUrlToPath(url));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    public void FileUrlToPath_ReturnsNullForOtherInput(string? url)
    {
        Assert.Null(LaunchHelpers.FileUrlToPath(url));
    }

    [Fact]
    public void FileUrlToPath_ResultMatchesNormalPath()
    {
        var path = LaunchHelpers.FileUrlToPath("file:///C:/My%20Folder/");
        Assert.True(LaunchHelpers.PathsEqual(path, "C:\\My Folder"));
    }
}
