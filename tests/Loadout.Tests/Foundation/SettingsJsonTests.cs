using System.Text.Json;
using Loadout.Settings.Model;

namespace Loadout.Tests.Foundation;

/// <summary>settings.json must look exactly like the example in SPEC section 10.</summary>
public class SettingsJsonTests
{
    // The example from SPEC.md section 10.
    private const string SpecExample = """
        {
          "version": 1,
          "hotkey": { "keys": ["Ctrl", "Alt"], "holdDelayMs": 150 },
          "appearance": {
            "iconSize": 48,
            "tintColor": "#2B2D31",
            "tintOpacity": 0.6,
            "highlightColor": "#8EC9E8",
            "dimColor": "#000000",
            "dimOpacity": 0.45,
            "dimAllMonitors": false,
            "showLastLaunchedMarker": true,
            "animations": true
          },
          "gameMode": { "enabled": false, "blockMouseInGame": true },
          "general": { "startWithWindows": true },
          "lastGroupIndex": 0,
          "groups": [
            {
              "name": "Group 1",
              "slots": [
                { "type": "app", "path": "C:\\Program Files\\Mozilla Firefox\\firefox.exe", "label": null, "icon": null },
                { "type": "folder", "path": "C:\\Users\\me\\Downloads" },
                { "type": "website", "url": "https://github.com" },
                null, null, null, null, null
              ]
            }
          ]
        }
        """;

    [Fact]
    public void SpecExample_Deserializes()
    {
        var s = SettingsJson.Deserialize(SpecExample);

        Assert.NotNull(s);
        Assert.Equal(["Ctrl", "Alt"], s.Hotkey.Keys);
        var slots = Assert.Single(s.Groups).Slots;
        Assert.Equal(8, slots.Count);
        Assert.Equal(SlotType.App, slots[0]!.Type);
        Assert.Equal(@"C:\Program Files\Mozilla Firefox\firefox.exe", slots[0]!.Path);
        Assert.Equal(SlotType.Folder, slots[1]!.Type);
        Assert.Equal(SlotType.Website, slots[2]!.Type);
        Assert.Equal("https://github.com", slots[2]!.Url);
        Assert.All(slots.Skip(3), Assert.Null);
    }

    [Fact]
    public void Serialize_UsesSpecNamesAndValues()
    {
        var s = LoadoutSettings.CreateDefault();
        s.Groups[0].Slots[0] = new SlotSettings { Type = SlotType.Website, Url = "https://github.com", IconPath = @"C:\x.png" };

        var json = SettingsJson.Serialize(s);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(150, root.GetProperty("hotkey").GetProperty("holdDelayMs").GetInt32());
        Assert.Equal("#2B2D31", root.GetProperty("appearance").GetProperty("tintColor").GetString());
        Assert.True(root.GetProperty("gameMode").GetProperty("blockMouseInGame").GetBoolean());
        Assert.Equal(0, root.GetProperty("lastGroupIndex").GetInt32());

        var slots = root.GetProperty("groups")[0].GetProperty("slots");
        Assert.Equal(8, slots.GetArrayLength());
        Assert.Equal("website", slots[0].GetProperty("type").GetString());
        Assert.Equal(@"C:\x.png", slots[0].GetProperty("icon").GetString());
        Assert.Equal(JsonValueKind.Null, slots[1].ValueKind);
    }

    [Fact]
    public void RoundTrip_KeepsEverything()
    {
        var s = SettingsJson.Deserialize(SpecExample)!;

        var again = SettingsJson.Deserialize(SettingsJson.Serialize(s))!;

        Assert.Equal(SettingsJson.Serialize(s), SettingsJson.Serialize(again));
    }

    [Fact]
    public void UnknownSlotType_OnlyEmptiesThatSlot()
    {
        const string json = """
            {
              "hotkey": { "holdDelayMs": 333 },
              "groups": [ { "name": "Work", "slots": [
                { "type": "script", "path": "x" },
                { "type": "APP", "path": "C:\\a.exe" }
              ] } ]
            }
            """;

        var raw = SettingsJson.Deserialize(json)!;
        var s = Loadout.Settings.Store.SettingsNormalizer.Normalize(raw);

        Assert.Equal(333, s.Hotkey.HoldDelayMs);
        Assert.Null(s.Groups[0].Slots[0]);
        Assert.Equal(SlotType.App, s.Groups[0].Slots[1]!.Type);
    }

    [Fact]
    public void Read_IsForgiving()
    {
        const string json = """
            {
              // people may edit the file by hand
              "Hotkey": { "Keys": ["Shift", "Space"], },
            }
            """;

        var s = SettingsJson.Deserialize(json)!;

        Assert.Equal(["Shift", "Space"], s.Hotkey.Keys);
    }
}
