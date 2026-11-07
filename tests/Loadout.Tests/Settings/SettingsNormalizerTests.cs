using Loadout.Settings.Model;
using Loadout.Settings.Store;

namespace Loadout.Tests.Settings;

/// <summary>One rule per test, following the bullets of SettingsNormalizer.Normalize.</summary>
public class SettingsNormalizerTests
{
    private static LoadoutSettings WithOneGroup(params SlotSettings?[] slots)
    {
        var s = LoadoutSettings.CreateDefault();
        s.Groups = [new GroupSettings { Name = "A", Slots = [.. slots] }];
        return s;
    }

    private static LoadoutSettings Normalize(LoadoutSettings? s) => SettingsNormalizer.Normalize(s);

    [Fact]
    public void NullInput_ReturnsDefaults()
    {
        var result = Normalize(null);

        Assert.Equal(SettingsTestHelpers.Json(LoadoutSettings.CreateDefault()), SettingsTestHelpers.Json(result));
    }

    [Fact]
    public void Input_IsNotModified()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Hotkey.Keys = ["  ", "Q"];

        var result = Normalize(input);

        Assert.Equal(["  ", "Q"], input.Hotkey.Keys);
        Assert.Equal(["Q"], result.Hotkey.Keys);
    }

    [Fact]
    public void NullSubObjects_GetNewDefaults()
    {
        var input = new LoadoutSettings
        {
            Hotkey = null!,
            Appearance = null!,
            GameMode = null!,
            General = null!,
        };

        var result = Normalize(input);

        Assert.Equal(["Ctrl", "Alt"], result.Hotkey.Keys);
        Assert.Equal(48, result.Appearance.IconSize);
        Assert.True(result.GameMode.BlockMouseInGame);
        Assert.True(result.General.StartWithWindows);
    }

    [Fact]
    public void Hotkey_NullKeys_UsesCtrlAlt()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Hotkey.Keys = null!;

        Assert.Equal(["Ctrl", "Alt"], Normalize(input).Hotkey.Keys);
    }

    [Fact]
    public void Hotkey_EmptyKeys_UsesCtrlAlt()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Hotkey.Keys = [];

        Assert.Equal(["Ctrl", "Alt"], Normalize(input).Hotkey.Keys);
    }

    [Fact]
    public void Hotkey_AllBlankKeys_UsesCtrlAlt()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Hotkey.Keys = ["", "   ", null!];

        Assert.Equal(["Ctrl", "Alt"], Normalize(input).Hotkey.Keys);
    }

    [Fact]
    public void Hotkey_BlankEntriesDroppedAndOthersTrimmed()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Hotkey.Keys = [" Ctrl ", "", "  ", "F13", null!];

        Assert.Equal(["Ctrl", "F13"], Normalize(input).Hotkey.Keys);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(9999, 500)]
    [InlineData(150, 150)]
    public void HoldDelay_IsClampedToRange(int given, int expected)
    {
        var input = LoadoutSettings.CreateDefault();
        input.Hotkey.HoldDelayMs = given;

        Assert.Equal(expected, Normalize(input).Hotkey.HoldDelayMs);
    }

    [Theory]
    [InlineData(10, 32)]
    [InlineData(200, 96)]
    [InlineData(64, 64)]
    public void IconSize_IsClampedToRange(int given, int expected)
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.IconSize = given;

        Assert.Equal(expected, Normalize(input).Appearance.IconSize);
    }

    [Theory]
    [InlineData(0.1, 0.30)]
    [InlineData(2.0, 0.90)]
    [InlineData(0.5, 0.5)]
    public void TintOpacity_IsClampedToRange(double given, double expected)
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.TintOpacity = given;

        Assert.Equal(expected, Normalize(input).Appearance.TintOpacity);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(5.0, 0.85)]
    [InlineData(0.2, 0.2)]
    public void DimOpacity_IsClampedToRange(double given, double expected)
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.DimOpacity = given;

        Assert.Equal(expected, Normalize(input).Appearance.DimOpacity);
    }

    [Fact]
    public void Opacity_NaN_BecomesDefault()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.TintOpacity = double.NaN;
        input.Appearance.DimOpacity = double.NaN;

        var result = Normalize(input);

        Assert.Equal(0.6, result.Appearance.TintOpacity);
        Assert.Equal(0.45, result.Appearance.DimOpacity);
    }

    [Theory]
    [InlineData("2B2D31")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#2B2D311")]
    [InlineData("#2B2D31\n")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidTintColor_BecomesDefault(string? color)
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.TintColor = color!;

        Assert.Equal("#2B2D31", Normalize(input).Appearance.TintColor);
    }

    [Fact]
    public void InvalidHighlightAndDimColors_BecomeDefaults()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.HighlightColor = "blue";
        input.Appearance.DimColor = null!;

        var result = Normalize(input);

        Assert.Equal("#8EC9E8", result.Appearance.HighlightColor);
        Assert.Equal("#000000", result.Appearance.DimColor);
    }

    [Fact]
    public void ValidColors_KeepTheirCase()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Appearance.TintColor = "#abcDEF";
        input.Appearance.HighlightColor = "#8ec9e8";

        var result = Normalize(input);

        Assert.Equal("#abcDEF", result.Appearance.TintColor);
        Assert.Equal("#8ec9e8", result.Appearance.HighlightColor);
    }

    [Fact]
    public void NullGroups_BecomeOneEmptyGroup()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Groups = null!;

        var result = Normalize(input);

        var group = Assert.Single(result.Groups);
        Assert.Equal("Group 1", group.Name);
        Assert.Equal(8, group.Slots.Count);
        Assert.All(group.Slots, Assert.Null);
    }

    [Fact]
    public void EmptyGroups_BecomeOneEmptyGroup()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Groups = [];

        var result = Normalize(input);

        var group = Assert.Single(result.Groups);
        Assert.Equal("Group 1", group.Name);
        Assert.All(group.Slots, Assert.Null);
    }

    [Fact]
    public void NullGroupEntries_AreRemoved()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Groups = [null!, new GroupSettings { Name = "Keep" }, null!];

        var result = Normalize(input);

        Assert.Equal("Keep", Assert.Single(result.Groups).Name);
    }

    [Fact]
    public void BlankGroupNames_BecomeNumberedByPosition()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Groups =
        [
            new GroupSettings { Name = "" },
            new GroupSettings { Name = "   " },
            new GroupSettings { Name = null! },
            new GroupSettings { Name = "Keep" },
        ];

        var names = Normalize(input).Groups.Select(g => g.Name).ToList();

        Assert.Equal(["Group 1", "Group 2", "Group 3", "Keep"], names);
    }

    [Fact]
    public void NullSlots_BecomeEightEmptySlots()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Groups[0].Slots = null!;

        var slots = Normalize(input).Groups[0].Slots;

        Assert.Equal(8, slots.Count);
        Assert.All(slots, Assert.Null);
    }

    [Fact]
    public void MoreThanEightSlots_KeepFirstEight()
    {
        var slots = Enumerable.Range(0, 10)
            .Select(i => (SlotSettings?)new SlotSettings { Type = SlotType.Folder, Path = $"C:\\p{i}" })
            .ToArray();

        var result = Normalize(WithOneGroup(slots)).Groups[0].Slots;

        Assert.Equal(8, result.Count);
        Assert.Equal("C:\\p7", result[7]!.Path);
    }

    [Fact]
    public void FewerThanEightSlots_ArePaddedWithEmpty()
    {
        var result = Normalize(WithOneGroup(new SlotSettings { Type = SlotType.Folder, Path = "C:\\a" })).Groups[0].Slots;

        Assert.Equal(8, result.Count);
        Assert.NotNull(result[0]);
        Assert.All(result.Skip(1), Assert.Null);
    }

    [Fact]
    public void AppOrFolderWithBlankPath_BecomesEmpty()
    {
        var result = Normalize(WithOneGroup(
            new SlotSettings { Type = SlotType.App, Path = "   " },
            new SlotSettings { Type = SlotType.Folder, Path = null })).Groups[0].Slots;

        Assert.Null(result[0]);
        Assert.Null(result[1]);
    }

    [Fact]
    public void Website_WithoutAbsoluteHttpUrl_BecomesEmpty()
    {
        var result = Normalize(WithOneGroup(
            new SlotSettings { Type = SlotType.Website, Url = "ftp://example.com" },
            new SlotSettings { Type = SlotType.Website, Url = "github.com" },
            new SlotSettings { Type = SlotType.Website, Url = "javascript:alert(1)" },
            new SlotSettings { Type = SlotType.Website, Url = "/relative/path" },
            new SlotSettings { Type = SlotType.Website, Url = null })).Groups[0].Slots;

        Assert.All(result.Take(5), Assert.Null);
    }

    [Fact]
    public void Website_WithHttpOrHttpsUrl_IsKept()
    {
        var result = Normalize(WithOneGroup(
            new SlotSettings { Type = SlotType.Website, Url = "http://example.com" },
            new SlotSettings { Type = SlotType.Website, Url = "https://github.com/x?y=1" })).Groups[0].Slots;

        Assert.Equal("http://example.com", result[0]!.Url);
        Assert.Equal("https://github.com/x?y=1", result[1]!.Url);
    }

    [Fact]
    public void UndefinedSlotType_BecomesEmpty()
    {
        var result = Normalize(WithOneGroup(
            new SlotSettings { Type = (SlotType)99, Path = "C:\\a.exe", Url = "https://a.com" })).Groups[0].Slots;

        Assert.Null(result[0]);
    }

    [Fact]
    public void ValidSlot_KeepsItsFields()
    {
        var result = Normalize(WithOneGroup(new SlotSettings
        {
            Type = SlotType.App,
            Path = "C:\\Program Files\\app.exe",
            Arguments = "--fast",
            Label = "Fast app",
            IconPath = "C:\\icon.png",
        })).Groups[0].Slots;

        Assert.Equal(SlotType.App, result[0]!.Type);
        Assert.Equal("C:\\Program Files\\app.exe", result[0]!.Path);
        Assert.Equal("--fast", result[0]!.Arguments);
        Assert.Equal("Fast app", result[0]!.Label);
        Assert.Equal("C:\\icon.png", result[0]!.IconPath);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void LastLaunchedSlot_OutOfRange_BecomesNull(int slot)
    {
        var input = WithOneGroup(new SlotSettings { Type = SlotType.Folder, Path = "C:\\a" });
        input.Groups[0].LastLaunchedSlot = slot;

        Assert.Null(Normalize(input).Groups[0].LastLaunchedSlot);
    }

    [Fact]
    public void LastLaunchedSlot_PointingAtEmptySlot_BecomesNull()
    {
        var input = WithOneGroup(
            new SlotSettings { Type = SlotType.Folder, Path = "C:\\a" },
            null);
        input.Groups[0].LastLaunchedSlot = 1;

        Assert.Null(Normalize(input).Groups[0].LastLaunchedSlot);
    }

    [Fact]
    public void LastLaunchedSlot_PointingAtFilledSlot_IsKept()
    {
        var input = WithOneGroup(
            null,
            new SlotSettings { Type = SlotType.Folder, Path = "C:\\a" });
        input.Groups[0].LastLaunchedSlot = 1;

        Assert.Equal(1, Normalize(input).Groups[0].LastLaunchedSlot);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(-3, 0)]
    [InlineData(1, 1)]
    public void LastGroupIndex_IsClampedIntoGroupRange(int given, int expected)
    {
        var input = LoadoutSettings.CreateDefault();
        input.Groups = [GroupSettings.CreateEmpty("One"), GroupSettings.CreateEmpty("Two")];
        input.LastGroupIndex = given;

        Assert.Equal(expected, Normalize(input).LastGroupIndex);
    }

    [Fact]
    public void Version_BelowOne_BecomesCurrentVersion()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Version = 0;

        Assert.Equal(LoadoutSettings.CurrentVersion, Normalize(input).Version);
    }

    [Fact]
    public void Version_OneOrAbove_IsKept()
    {
        var input = LoadoutSettings.CreateDefault();
        input.Version = 5;

        Assert.Equal(5, Normalize(input).Version);
    }
}
