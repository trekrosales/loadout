// CONTRACT (owned by the lead). The shape of settings.json, see SPEC section 10.
// Agents may read these types but must not change them without the lead.
// Serialize only with SettingsJson.Options so the file matches SPEC section 10.

using System.Text.Json.Serialization;

namespace Loadout.Settings.Model;

/// <summary>Root of settings.json. Defaults here are the SPEC defaults.</summary>
public sealed class LoadoutSettings
{
    /// <summary>Number of slots in every group. Fixed by the SPEC.</summary>
    public const int SlotsPerGroup = 8;

    /// <summary>Current file format version. Bump only with a migration.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public HotkeySettings Hotkey { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public GameModeSettings GameMode { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    /// <summary>Index into <see cref="Groups"/> of the group the wheel opens on (SPEC 2.5).</summary>
    public int LastGroupIndex { get; set; }

    /// <summary>Always at least one group. Each group always has exactly <see cref="SlotsPerGroup"/> slots.</summary>
    public List<GroupSettings> Groups { get; set; } = [GroupSettings.CreateEmpty("Group 1")];

    /// <summary>Creates settings with every SPEC default.</summary>
    public static LoadoutSettings CreateDefault() => new();
}

/// <summary>SPEC 2.1.</summary>
public sealed class HotkeySettings
{
    /// <summary>
    /// The keys that must all be held. Tokens:
    /// modifiers "Ctrl", "Alt", "Shift", "Win" (either side);
    /// any other keyboard key by its System.Windows.Input.Key name, e.g. "Space", "F13", "Q";
    /// mouse buttons "MouseMiddle", "MouseX1" (back), "MouseX2" (forward).
    /// </summary>
    public List<string> Keys { get; set; } = ["Ctrl", "Alt"];

    /// <summary>How long the hotkey must be held before the wheel opens. 0 to 500.</summary>
    public int HoldDelayMs { get; set; } = 150;

    public const int MinHoldDelayMs = 0;
    public const int MaxHoldDelayMs = 500;
}

/// <summary>SPEC sections 4.2 to 4.5. Colors are "#RRGGBB" strings.</summary>
public sealed class AppearanceSettings
{
    public int IconSize { get; set; } = 48;
    public const int MinIconSize = 32;
    public const int MaxIconSize = 96;

    public string TintColor { get; set; } = "#2B2D31";

    /// <summary>0.30 to 0.90.</summary>
    public double TintOpacity { get; set; } = 0.6;
    public const double MinTintOpacity = 0.30;
    public const double MaxTintOpacity = 0.90;

    public string HighlightColor { get; set; } = "#8EC9E8";

    public string DimColor { get; set; } = "#000000";

    /// <summary>0 (off) to 0.85.</summary>
    public double DimOpacity { get; set; } = 0.45;
    public const double MinDimOpacity = 0.0;
    public const double MaxDimOpacity = 0.85;

    public bool DimAllMonitors { get; set; }

    public bool ShowLastLaunchedMarker { get; set; } = true;

    public bool Animations { get; set; } = true;
}

/// <summary>SPEC section 7.</summary>
public sealed class GameModeSettings
{
    public bool Enabled { get; set; }

    public bool BlockMouseInGame { get; set; } = true;
}

/// <summary>SPEC sections 5 and 6.</summary>
public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = true;

    /// <summary>True once the first-run "Loadout is running" notification has been shown.</summary>
    public bool HasShownWelcome { get; set; }
}

/// <summary>SPEC section 3.1.</summary>
public sealed class GroupSettings
{
    public string Name { get; set; } = "Group";

    /// <summary>Exactly <see cref="LoadoutSettings.SlotsPerGroup"/> entries. null = empty slot. Index 0 is the top slot, then clockwise.</summary>
    public List<SlotSettings?> Slots { get; set; } = NewEmptySlots();

    /// <summary>Slot opened most recently from this group, for the last-launched marker (SPEC 4.3). null = none.</summary>
    public int? LastLaunchedSlot { get; set; }

    public static GroupSettings CreateEmpty(string name) => new() { Name = name };

    public static List<SlotSettings?> NewEmptySlots() =>
        [.. Enumerable.Repeat<SlotSettings?>(null, LoadoutSettings.SlotsPerGroup)];
}

/// <summary>What a slot opens. SPEC section 3.2.</summary>
public enum SlotType
{
    App,
    Folder,
    Website,
}

/// <summary>One filled slot. SPEC sections 3.2 to 3.4.</summary>
public sealed class SlotSettings
{
    public SlotType Type { get; set; }

    /// <summary>
    /// For App: path to an .exe or .lnk, or a shell path for apps without a file, such as Store apps:
    /// "shell:AppsFolder\&lt;AppUserModelID&gt;".
    /// For Folder: the folder path, or a shell folder such as "shell:Downloads".
    /// Unused for Website. The settings window, launcher and icon provider must all accept both forms.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>For Website: the full URL (http or https). Unused otherwise.</summary>
    public string? Url { get; set; }

    /// <summary>Optional launch arguments for App.</summary>
    public string? Arguments { get; set; }

    /// <summary>Optional custom label. null = derive from the app, folder or site name.</summary>
    public string? Label { get; set; }

    /// <summary>Optional path to a custom PNG, JPG or ICO image. null = automatic icon. Stored as "icon" in settings.json.</summary>
    [JsonPropertyName("icon")]
    public string? IconPath { get; set; }
}
