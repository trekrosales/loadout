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
    [InlineData("file:///C:/Users/Test/a%23b", "C:\\Users\\Test\\a#b")]
    [InlineData("file:///C:/Users/Test/a,b", "C:\\Users\\Test\\a,b")]
    [InlineData("file:///C:/Users/Test/a%2Cb%20c", "C:\\Users\\Test\\a,b c")]
    public void FileUrlToPath_HandlesHashAndComma(string url, string expected)
    {
        Assert.Equal(expected, LaunchHelpers.FileUrlToPath(url));
    }

    private static byte[] AliasBuffer(uint tag, params string[] strings)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(3u)); // version
        foreach (var s in strings)
        {
            body.AddRange(System.Text.Encoding.Unicode.GetBytes(s));
            body.AddRange([0, 0]);
        }

        var all = new List<byte>();
        all.AddRange(BitConverter.GetBytes(tag));
        all.AddRange(BitConverter.GetBytes((ushort)body.Count));
        all.AddRange([0, 0]);
        all.AddRange(body);
        return [.. all];
    }

    [Fact]
    public void ParseAppExecLink_ReadsPackageAumidAndTarget()
    {
        var buffer = AliasBuffer(0x8000001B, "Microsoft.WindowsNotepad_8wekyb3d8bbwe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "C:\\Program Files\\WindowsApps\\X\\Notepad.exe", "0");

        var link = LaunchHelpers.ParseAppExecLink(buffer);

        Assert.NotNull(link);
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe", link.PackageId);
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", link.Aumid);
        Assert.Equal("C:\\Program Files\\WindowsApps\\X\\Notepad.exe", link.TargetExe);
    }

    [Fact]
    public void ParseAppExecLink_RejectsOtherTagsAndShortOrBrokenData()
    {
        Assert.Null(LaunchHelpers.ParseAppExecLink(AliasBuffer(0xA000000C, "a", "b", "c")));
        Assert.Null(LaunchHelpers.ParseAppExecLink(AliasBuffer(0x8000001B, "only", "two")));
        Assert.Null(LaunchHelpers.ParseAppExecLink([]));
        Assert.Null(LaunchHelpers.ParseAppExecLink(new byte[8]));
        var unterminated = AliasBuffer(0x8000001B, "a", "b", "c");
        Assert.Null(LaunchHelpers.ParseAppExecLink(unterminated.AsSpan(0, unterminated.Length - 2)));
    }

    [Theory]
    [InlineData("\\\\?\\C:\\Tools\\app.exe", "C:\\Tools\\app.exe")]
    [InlineData("\\\\?\\UNC\\server\\share\\app.exe", "\\\\server\\share\\app.exe")]
    [InlineData("C:\\plain\\app.exe", "C:\\plain\\app.exe")]
    public void StripExtendedPrefix_RemovesDevicePrefixes(string input, string expected)
    {
        Assert.Equal(expected, LaunchHelpers.StripExtendedPrefix(input));
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
