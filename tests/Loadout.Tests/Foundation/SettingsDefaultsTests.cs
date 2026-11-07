using Loadout.Settings.Model;

namespace Loadout.Tests.Foundation;

/// <summary>The settings model's defaults must match SPEC.md.</summary>
public class SettingsDefaultsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var s = LoadoutSettings.CreateDefault();

        Assert.Equal(1, s.Version);
        Assert.Equal(["Ctrl", "Alt"], s.Hotkey.Keys);
        Assert.Equal(150, s.Hotkey.HoldDelayMs);
        Assert.Equal(48, s.Appearance.IconSize);
        Assert.Equal("#2B2D31", s.Appearance.TintColor);
        Assert.Equal(0.6, s.Appearance.TintOpacity);
        Assert.Equal("#8EC9E8", s.Appearance.HighlightColor);
        Assert.Equal("#000000", s.Appearance.DimColor);
        Assert.Equal(0.45, s.Appearance.DimOpacity);
        Assert.False(s.Appearance.DimAllMonitors);
        Assert.True(s.Appearance.ShowLastLaunchedMarker);
        Assert.True(s.Appearance.Animations);
        Assert.False(s.GameMode.Enabled);
        Assert.True(s.GameMode.BlockMouseInGame);
        Assert.True(s.General.StartWithWindows);
        Assert.Equal(0, s.LastGroupIndex);
    }

    [Fact]
    public void Defaults_HaveOneGroupOfEightEmptySlots()
    {
        var s = LoadoutSettings.CreateDefault();

        var group = Assert.Single(s.Groups);
        Assert.Equal("Group 1", group.Name);
        Assert.Equal(LoadoutSettings.SlotsPerGroup, group.Slots.Count);
        Assert.All(group.Slots, Assert.Null);
        Assert.Null(group.LastLaunchedSlot);
    }
}
