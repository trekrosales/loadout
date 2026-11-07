using System.Windows.Input;

namespace Loadout.Input;

/// <summary>
/// A parsed hotkey (SPEC 2.1). Made of groups: each group is satisfied when any one of its keys is down,
/// so "Ctrl" accepts either Ctrl key. The hotkey is held when every group is satisfied.
/// At most one non-modifier key (a letter, F-key, or mouse button) is allowed: the "trigger".
/// The trigger is held back from other apps while the hotkey is in use and replayed if the wheel doesn't open.
/// </summary>
internal sealed class HotkeyDefinition
{
    private static readonly int[] s_ctrl = [VirtualKeys.LControl, VirtualKeys.RControl];
    private static readonly int[] s_alt = [VirtualKeys.LMenu, VirtualKeys.RMenu];
    private static readonly int[] s_shift = [VirtualKeys.LShift, VirtualKeys.RShift];
    private static readonly int[] s_win = [VirtualKeys.LWin, VirtualKeys.RWin];

    private readonly HashSet<int> _all;

    private HotkeyDefinition(IReadOnlyList<int[]> groups, int? trigger, string text)
    {
        Groups = groups;
        Trigger = trigger;
        Text = text;
        _all = [.. groups.SelectMany(g => g)];
    }

    /// <summary>The default hotkey, Ctrl + Alt.</summary>
    public static HotkeyDefinition Default { get; } = Parse(["Ctrl", "Alt"]);

    public IReadOnlyList<int[]> Groups { get; }

    /// <summary>The one non-modifier key or mouse button, or null for modifier-only hotkeys like Ctrl + Alt.</summary>
    public int? Trigger { get; }

    public bool HasMouseButton => Trigger is { } t && VirtualKeys.IsMouseButton(t);

    /// <summary>Human-readable form, e.g. "Ctrl + Alt".</summary>
    public string Text { get; }

    public bool Contains(int vk) => _all.Contains(vk);

    /// <summary>True when every group has at least one key for which <paramref name="isDown"/> is true.</summary>
    public bool IsSatisfied(Func<int, bool> isDown)
    {
        // Plain loops: this runs on every mouse move while the wheel is open.
        for (var g = 0; g < Groups.Count; g++)
        {
            var group = Groups[g];
            var any = false;
            for (var i = 0; i < group.Length; i++)
            {
                if (isDown(group[i]))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses the settings tokens (see HotkeySettings.Keys). Throws <see cref="FormatException"/> for unknown names,
    /// duplicates, no keys, more than one non-modifier key, or the left or right mouse button.
    /// </summary>
    public static HotkeyDefinition Parse(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var groups = new List<int[]>();
        var names = new List<string>();
        int? trigger = null;

        foreach (var raw in tokens)
        {
            var token = raw?.Trim() ?? string.Empty;
            if (token.Length == 0)
            {
                continue;
            }

            var (group, name) = ParseToken(token);
            if (groups.Any(g => g.Intersect(group).Any()))
            {
                throw new FormatException($"The key \"{token}\" is listed twice.");
            }

            if (group.Length == 1 && !VirtualKeys.IsModifier(group[0]))
            {
                if (trigger is not null)
                {
                    throw new FormatException("A hotkey can have only one key that isn't Ctrl, Alt, Shift or Win.");
                }

                trigger = group[0];
            }

            groups.Add(group);
            names.Add(name);
        }

        if (groups.Count == 0)
        {
            throw new FormatException("The hotkey has no keys.");
        }

        return new HotkeyDefinition(groups, trigger, string.Join(" + ", names));
    }

    /// <summary>Parses, falling back to Ctrl + Alt (and reporting why) if the tokens are invalid.</summary>
    public static HotkeyDefinition ParseOrDefault(IEnumerable<string>? tokens, out string? error)
    {
        error = null;
        if (tokens is null)
        {
            return Default;
        }

        try
        {
            return Parse(tokens);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return Default;
        }
    }

    private static (int[] Group, string Name) ParseToken(string token)
    {
        switch (token.ToUpperInvariant())
        {
            case "CTRL":
            case "CONTROL":
                return (s_ctrl, "Ctrl");
            case "ALT":
                return (s_alt, "Alt");
            case "SHIFT":
                return (s_shift, "Shift");
            case "WIN":
            case "WINDOWS":
                return (s_win, "Win");
            case "MOUSEMIDDLE":
                return ([VirtualKeys.MButton], "Middle mouse button");
            case "MOUSEX1":
                return ([VirtualKeys.XButton1], "Mouse back button");
            case "MOUSEX2":
                return ([VirtualKeys.XButton2], "Mouse forward button");
            case "MOUSELEFT":
            case "MOUSERIGHT":
                throw new FormatException("The left and right mouse buttons can't be part of the hotkey.");
        }

        if (Enum.TryParse<Key>(token, ignoreCase: true, out var key) && key != Key.None && Enum.IsDefined(key))
        {
            var vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0)
            {
                return ([vk], key.ToString());
            }
        }

        throw new FormatException($"\"{token}\" isn't a key Loadout knows.");
    }
}
