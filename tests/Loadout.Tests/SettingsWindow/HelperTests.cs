using System.Windows.Input;
using Loadout.Settings.Model;
using Loadout.Settings.Window;

namespace Loadout.Tests.SettingsWindow;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("  www.example.com/path?x=1  ", "https://www.example.com/path?x=1")]
    [InlineData("http://example.com/a", "http://example.com/a")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("localhost:3000", "https://localhost:3000")]
    [InlineData("192.168.1.1", "https://192.168.1.1")]
    public void Accepts_and_adds_https(string input, string expected)
    {
        Assert.True(UrlNormalizer.TryNormalize(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.com")]
    [InlineData("file:///C:/x")]
    [InlineData("https://")]
    [InlineData("justaword")]
    public void Rejects_bad_input(string? input)
    {
        Assert.False(UrlNormalizer.TryNormalize(input, out var url));
        Assert.Equal(string.Empty, url);
    }
}

public class HexColorTests
{
    [Theory]
    [InlineData("#8EC9E8", 0x8E, 0xC9, 0xE8)]
    [InlineData("8ec9e8", 0x8E, 0xC9, 0xE8)]
    [InlineData(" #000000 ", 0, 0, 0)]
    [InlineData("#FFF", 255, 255, 255)]
    public void Parses(string text, int r, int g, int b)
    {
        Assert.True(HexColor.TryParse(text, out var c));
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    [InlineData("red")]
    public void Rejects(string? text) => Assert.False(HexColor.TryParse(text, out _));

    [Fact]
    public void Normalizes_to_upper_case_with_hash()
    {
        Assert.Equal("#8EC9E8", HexColor.Normalize("8ec9e8"));
        Assert.Equal("#AABBCC", HexColor.Normalize("#abc"));
        Assert.Null(HexColor.Normalize("nope"));
    }
}

public class InternetShortcutFileTests
{
    [Fact]
    public void Reads_the_URL_line()
    {
        const string content = "[InternetShortcut]\r\nURL=https://example.com/page\r\nIconIndex=0\r\n";
        Assert.Equal("https://example.com/page", InternetShortcutFile.ParseUrl(content));
    }

    [Fact]
    public void Url_key_is_case_insensitive_and_scheme_is_added()
    {
        Assert.Equal("https://example.com", InternetShortcutFile.ParseUrl("[InternetShortcut]\nurl=example.com\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[InternetShortcut]\r\nIconIndex=0")]
    [InlineData("[InternetShortcut]\r\nURL=ftp://example.com")]
    public void Returns_null_without_a_usable_url(string content) =>
        Assert.Null(InternetShortcutFile.ParseUrl(content));

    [Fact]
    public void Missing_file_returns_null() =>
        Assert.Null(InternetShortcutFile.ReadUrl(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".url")));
}

public sealed class DropConverterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "loadout-drop-" + Guid.NewGuid().ToString("N"));

    public DropConverterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(@"C:\Apps\tool.exe")]
    [InlineData(@"C:\Users\me\Desktop\Tool.LNK")]
    public void Programs_and_shortcuts_become_apps(string path)
    {
        var slot = DropConverter.FromPath(path);
        Assert.NotNull(slot);
        Assert.Equal(SlotType.App, slot.Type);
        Assert.Equal(path, slot.Path);
    }

    [Fact]
    public void Folder_becomes_folder()
    {
        var slot = DropConverter.FromPath(_dir + "\\");
        Assert.NotNull(slot);
        Assert.Equal(SlotType.Folder, slot.Type);
        Assert.Equal(_dir, slot.Path);
    }

    [Fact]
    public void Url_file_becomes_website()
    {
        var file = Path.Combine(_dir, "Site.url");
        File.WriteAllText(file, "[InternetShortcut]\r\nURL=https://example.org/\r\n");
        var slot = DropConverter.FromPath(file);
        Assert.NotNull(slot);
        Assert.Equal(SlotType.Website, slot.Type);
        Assert.Equal("https://example.org/", slot.Url);
    }

    [Fact]
    public void Broken_url_file_and_other_files_are_refused()
    {
        var bad = Path.Combine(_dir, "Bad.url");
        File.WriteAllText(bad, "garbage");
        Assert.Null(DropConverter.FromPath(bad));
        Assert.Null(DropConverter.FromPath(Path.Combine(_dir, "notes.txt")));
        Assert.Null(DropConverter.FromPath(""));
        Assert.Null(DropConverter.FromPath(null));
    }

    [Theory]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"::{4234D49B-0245-4DF3-B780-3893943456E1}\Microsoft.Photos_8wekyb3d8bbwe!App", "Microsoft.Photos_8wekyb3d8bbwe!App")]
    [InlineData("Microsoft.Photos_8wekyb3d8bbwe!App", "Microsoft.Photos_8wekyb3d8bbwe!App")]
    public void Recognizes_installed_app_names(string parsing, string aumid) =>
        Assert.Equal(aumid, DropConverter.TryGetAumid(parsing));

    [Theory]
    [InlineData(@"C:\Apps\tool.exe")]
    [InlineData(@"shell:AppsFolder\")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_names_are_not_apps_folder_items(string? parsing) =>
        Assert.Null(DropConverter.TryGetAumid(parsing));

    [Fact]
    public void Start_menu_item_stores_shell_path_and_display_name()
    {
        var slot = DropConverter.FromShellItem(@"shell:AppsFolder\Pkg_abc!App", "Calculator");
        Assert.NotNull(slot);
        Assert.Equal(SlotType.App, slot.Type);
        Assert.Equal(@"shell:AppsFolder\Pkg_abc!App", slot.Path);
        Assert.Equal("Calculator", slot.Label);
    }
}

public class HotkeyRecordingTests
{
    [Fact]
    public void Modifiers_come_first_in_a_fixed_order_and_sides_collapse()
    {
        var tokens = HotkeyRecording.BuildTokens([Key.RightAlt, Key.LeftCtrl, Key.RightCtrl, Key.LWin, Key.LeftShift]);
        Assert.Equal(["Ctrl", "Alt", "Shift", "Win"], tokens);
    }

    [Fact]
    public void Key_and_mouse_tokens_follow_modifiers()
    {
        Assert.Equal(["Ctrl", "Q"], HotkeyRecording.BuildTokens([Key.Q, Key.LeftCtrl]));
        Assert.Equal(["Alt", "MouseX1"], HotkeyRecording.BuildTokens([Key.LeftAlt], ["MouseX1"]));
        Assert.Equal(["MouseMiddle"], HotkeyRecording.BuildTokens([], ["MouseMiddle"]));
    }

    [Fact]
    public void Ignorable_keys_are_skipped()
    {
        Assert.Equal(["Ctrl"], HotkeyRecording.BuildTokens([Key.System, Key.None, Key.ImeProcessed, Key.LeftCtrl]));
    }

    [Fact]
    public void Valid_hotkeys_pass_and_invalid_ones_explain_why()
    {
        Assert.Null(HotkeyRecording.Validate(["Ctrl", "Alt"]));
        Assert.Null(HotkeyRecording.Validate(HotkeyRecording.BuildTokens([Key.LeftCtrl, Key.F13])));
        Assert.Null(HotkeyRecording.Validate(["MouseX2"]));
        Assert.NotNull(HotkeyRecording.Validate(HotkeyRecording.BuildTokens([Key.Q, Key.W])));
        Assert.NotNull(HotkeyRecording.Validate([]));
    }

    [Fact]
    public void Display_names_are_friendly()
    {
        Assert.Equal("Middle click", HotkeyRecording.DisplayName("MouseMiddle"));
        Assert.Equal("Ctrl", HotkeyRecording.DisplayName("Ctrl"));
    }
}
