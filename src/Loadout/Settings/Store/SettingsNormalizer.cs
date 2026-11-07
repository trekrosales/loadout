using System.Text.RegularExpressions;
using Loadout.Settings.Model;

namespace Loadout.Settings.Store;

/// <summary>
/// Turns any settings object, however damaged, into one that satisfies the model's rules (SPEC section 10).
/// Out-of-range values are clamped, missing parts are filled with defaults, and bad slots become empty.
/// Never throws. Always returns a new object, so the input is never modified.
/// </summary>
public static class SettingsNormalizer
{
    private static readonly string[] DefaultModifierKeys = ["Ctrl", "Alt"];

    // \z instead of $ so that a trailing newline does not count as a match.
    private static readonly Regex s_colorPattern = new(@"^#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant);

    public static LoadoutSettings Normalize(LoadoutSettings? s)
    {
        if (s is null)
        {
            return LoadoutSettings.CreateDefault();
        }

        var groups = NormalizeGroups(s.Groups);

        return new LoadoutSettings
        {
            Version = s.Version >= 1 ? s.Version : LoadoutSettings.CurrentVersion,
            Hotkey = NormalizeHotkey(s.Hotkey),
            Appearance = NormalizeAppearance(s.Appearance),
            GameMode = s.GameMode is null ? new GameModeSettings() : new GameModeSettings
            {
                Enabled = s.GameMode.Enabled,
                BlockMouseInGame = s.GameMode.BlockMouseInGame,
            },
            General = s.General is null ? new GeneralSettings() : new GeneralSettings
            {
                StartWithWindows = s.General.StartWithWindows,
                HasShownWelcome = s.General.HasShownWelcome,
            },
            Groups = groups,
            LastGroupIndex = Math.Clamp(s.LastGroupIndex, 0, groups.Count - 1),
        };
    }

    private static bool IsValidHotkey(List<string> keys)
    {
        try
        {
            Loadout.Input.HotkeyDefinition.Parse(keys);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HotkeySettings NormalizeHotkey(HotkeySettings? hotkey)
    {
        if (hotkey is null)
        {
            return new HotkeySettings();
        }

        IEnumerable<string?> source = hotkey.Keys ?? [];
        var keys = source
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!.Trim())
            .ToList();

        // An empty or unusable hotkey (an unknown key name, two letter keys, ...) falls back to Ctrl + Alt here,
        // so the settings file and the settings window always show the hotkey that actually works.
        if (keys.Count == 0 || !IsValidHotkey(keys))
        {
            keys = [.. DefaultModifierKeys];
        }

        return new HotkeySettings
        {
            Keys = keys,
            HoldDelayMs = Math.Clamp(hotkey.HoldDelayMs, HotkeySettings.MinHoldDelayMs, HotkeySettings.MaxHoldDelayMs),
        };
    }

    private static AppearanceSettings NormalizeAppearance(AppearanceSettings? a)
    {
        var defaults = new AppearanceSettings();
        if (a is null)
        {
            return defaults;
        }

        return new AppearanceSettings
        {
            IconSize = Math.Clamp(a.IconSize, AppearanceSettings.MinIconSize, AppearanceSettings.MaxIconSize),
            TintColor = NormalizeColor(a.TintColor, defaults.TintColor),
            TintOpacity = ClampDouble(a.TintOpacity, AppearanceSettings.MinTintOpacity, AppearanceSettings.MaxTintOpacity, defaults.TintOpacity),
            HighlightColor = NormalizeColor(a.HighlightColor, defaults.HighlightColor),
            DimColor = NormalizeColor(a.DimColor, defaults.DimColor),
            DimOpacity = ClampDouble(a.DimOpacity, AppearanceSettings.MinDimOpacity, AppearanceSettings.MaxDimOpacity, defaults.DimOpacity),
            DimAllMonitors = a.DimAllMonitors,
            ShowLastLaunchedMarker = a.ShowLastLaunchedMarker,
            Animations = a.Animations,
        };
    }

    private static string NormalizeColor(string? color, string fallback) =>
        color is not null && s_colorPattern.IsMatch(color) ? color : fallback;

    private static double ClampDouble(double value, double min, double max, double fallback) =>
        double.IsNaN(value) ? fallback : Math.Clamp(value, min, max);

    private static List<GroupSettings> NormalizeGroups(IEnumerable<GroupSettings?>? source)
    {
        var groups = new List<GroupSettings>();
        if (source is not null)
        {
            foreach (var group in source)
            {
                if (group is not null)
                {
                    groups.Add(group);
                }
            }
        }

        if (groups.Count == 0)
        {
            return [GroupSettings.CreateEmpty("Group 1")];
        }

        var result = new List<GroupSettings>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            result.Add(NormalizeGroup(groups[i], i));
        }

        return result;
    }

    private static GroupSettings NormalizeGroup(GroupSettings group, int index)
    {
        var slots = new List<SlotSettings?>(LoadoutSettings.SlotsPerGroup);
        List<SlotSettings?> source = group.Slots ?? [];
        for (var i = 0; i < LoadoutSettings.SlotsPerGroup; i++)
        {
            slots.Add(i < source.Count ? NormalizeSlot(source[i]) : null);
        }

        var last = group.LastLaunchedSlot;
        if (last is null || last < 0 || last >= LoadoutSettings.SlotsPerGroup || slots[last.Value] is null)
        {
            last = null;
        }

        return new GroupSettings
        {
            Name = string.IsNullOrWhiteSpace(group.Name) ? $"Group {index + 1}" : group.Name,
            Slots = slots,
            LastLaunchedSlot = last,
        };
    }

    private static SlotSettings? NormalizeSlot(SlotSettings? slot)
    {
        if (slot is null || !Enum.IsDefined(slot.Type))
        {
            return null;
        }

        var valid = slot.Type switch
        {
            SlotType.App or SlotType.Folder => !string.IsNullOrWhiteSpace(slot.Path),
            SlotType.Website => IsHttpUrl(slot.Url),
            _ => false,
        };

        if (!valid)
        {
            return null;
        }

        return new SlotSettings
        {
            Type = slot.Type,
            Path = slot.Path,
            Url = slot.Url,
            Arguments = slot.Arguments,
            Label = slot.Label,
            IconPath = slot.IconPath,
        };
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
