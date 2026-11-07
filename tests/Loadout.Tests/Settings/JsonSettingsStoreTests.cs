using Loadout.Core;
using Loadout.Settings.Model;
using Loadout.Settings.Store;

namespace Loadout.Tests.Settings;

public class JsonSettingsStoreTests
{
    [Fact]
    public void MissingFile_GivesDefaults_AndCreatesNoFile()
    {
        using var sandbox = new TestSandbox();
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);

        store.Load();

        Assert.Equal(
            SettingsTestHelpers.Json(LoadoutSettings.CreateDefault()),
            SettingsTestHelpers.Json(store.Current));
        Assert.False(File.Exists(sandbox.SettingsFile));
    }

    [Fact]
    public void SaveThenLoad_InNewStore_RoundTrips()
    {
        using var sandbox = new TestSandbox();
        var first = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        first.Load();
        first.Current.Appearance.TintColor = "#112233";
        first.Current.Hotkey.Keys = ["Ctrl", "Shift", "Q"];
        first.Current.Groups[0].Slots[0] = new SlotSettings { Type = SlotType.Website, Url = "https://github.com" };
        first.Current.Groups[0].Slots[2] = new SlotSettings { Type = SlotType.App, Path = @"C:\Apps\tool.exe", Label = "Tool" };
        first.Save();

        var second = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        second.Load();

        Assert.Equal("#112233", second.Current.Appearance.TintColor);
        Assert.Equal(["Ctrl", "Shift", "Q"], second.Current.Hotkey.Keys);
        Assert.Equal(SlotType.Website, second.Current.Groups[0].Slots[0]!.Type);
        Assert.Equal("https://github.com", second.Current.Groups[0].Slots[0]!.Url);
        Assert.Null(second.Current.Groups[0].Slots[1]);
        Assert.Equal(@"C:\Apps\tool.exe", second.Current.Groups[0].Slots[2]!.Path);
        Assert.Equal("Tool", second.Current.Groups[0].Slots[2]!.Label);
    }

    [Fact]
    public void BrokenJson_IsMovedToBrokenFile_LoggedAndDefaultsUsed()
    {
        using var sandbox = new TestSandbox();
        File.WriteAllText(sandbox.BrokenFile, "old broken copy");
        File.WriteAllText(sandbox.SettingsFile, "{ this is not json");
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);

        store.Load();

        Assert.True(File.Exists(sandbox.BrokenFile));
        Assert.Equal("{ this is not json", File.ReadAllText(sandbox.BrokenFile));
        Assert.False(File.Exists(sandbox.SettingsFile));
        Assert.Equal(
            SettingsTestHelpers.Json(LoadoutSettings.CreateDefault()),
            SettingsTestHelpers.Json(store.Current));
        Assert.Contains("settings.json is damaged", File.ReadAllText(AppPaths.LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public void NullLiteralFile_IsTreatedAsBroken()
    {
        using var sandbox = new TestSandbox();
        File.WriteAllText(sandbox.SettingsFile, "null");
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);

        store.Load();

        Assert.True(File.Exists(sandbox.BrokenFile));
        Assert.Equal(
            SettingsTestHelpers.Json(LoadoutSettings.CreateDefault()),
            SettingsTestHelpers.Json(store.Current));
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        using var sandbox = new TestSandbox();
        File.WriteAllText(sandbox.SettingsFile, """{ "hotkey": { "holdDelayMs": 9999 }, "appearance": { "iconSize": 1 } }""");
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);

        store.Load();

        Assert.Equal(500, store.Current.Hotkey.HoldDelayMs);
        Assert.Equal(32, store.Current.Appearance.IconSize);
    }

    [Fact]
    public void Save_RaisesChanged_OnSaveAndOnReplace()
    {
        using var sandbox = new TestSandbox();
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Save();
        Assert.Equal(1, raised);

        store.Replace(LoadoutSettings.CreateDefault());
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Save_WritesAtomically_OverwritesAndLeavesNoTempFile()
    {
        using var sandbox = new TestSandbox();
        File.WriteAllText(sandbox.SettingsFile, "old content");
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        store.Load();
        store.Current.Hotkey.HoldDelayMs = 300;

        store.Save();

        Assert.False(File.Exists(sandbox.SettingsFile + ".tmp"));
        Assert.Equal(300, SettingsJson.Deserialize(File.ReadAllText(sandbox.SettingsFile))!.Hotkey.HoldDelayMs);
    }

    [Fact]
    public void Flush_WritesPendingSave_WithoutAnyDispatcherPumping()
    {
        using var sandbox = new TestSandbox();
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.FromHours(1));
        store.Load();
        store.Current.Hotkey.HoldDelayMs = 300;

        store.Save();
        Assert.False(File.Exists(sandbox.SettingsFile));

        store.Flush();

        var reloaded = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        reloaded.Load();
        Assert.Equal(300, reloaded.Current.Hotkey.HoldDelayMs);
    }

    [Fact]
    public void Flush_WithNothingPending_WritesNothing()
    {
        using var sandbox = new TestSandbox();
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.FromHours(1));

        store.Flush();

        Assert.False(File.Exists(sandbox.SettingsFile));
    }

    [Fact]
    public void Export_ThenImport_RoundTrips()
    {
        using var sandbox = new TestSandbox();
        var exporter = new JsonSettingsStore(Path.Combine(sandbox.Dir, "a", "settings.json"), TimeSpan.Zero);
        exporter.Load();
        exporter.Current.Appearance.DimColor = "#101010";
        exporter.Current.Groups[0].Slots[4] = new SlotSettings { Type = SlotType.Folder, Path = "shell:Downloads" };
        var exportPath = Path.Combine(sandbox.Dir, "backup.json");

        exporter.Export(exportPath);

        var importer = new JsonSettingsStore(Path.Combine(sandbox.Dir, "b", "settings.json"), TimeSpan.Zero);
        importer.Load();
        Assert.True(importer.Import(exportPath));
        Assert.Equal(SettingsTestHelpers.Json(exporter.Current), SettingsTestHelpers.Json(importer.Current));
    }

    [Fact]
    public void Import_BadFiles_ReturnFalseAndChangeNothing()
    {
        using var sandbox = new TestSandbox();
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        store.Load();
        store.Current.Hotkey.HoldDelayMs = 321;
        var before = store.Current;

        var badJson = Path.Combine(sandbox.Dir, "bad.json");
        File.WriteAllText(badJson, "{ nope");
        var nullFile = Path.Combine(sandbox.Dir, "null.json");
        File.WriteAllText(nullFile, "null");
        var missing = Path.Combine(sandbox.Dir, "missing.json");

        Assert.False(store.Import(badJson));
        Assert.False(store.Import(nullFile));
        Assert.False(store.Import(missing));

        Assert.Same(before, store.Current);
        Assert.Equal(321, store.Current.Hotkey.HoldDelayMs);
    }

    [Fact]
    public void Export_ToAPathThatIsADirectory_ThrowsIOException()
    {
        using var sandbox = new TestSandbox();
        var store = new JsonSettingsStore(sandbox.SettingsFile, TimeSpan.Zero);
        store.Load();

        Assert.Throws<IOException>(() => store.Export(sandbox.Dir));
    }
}
