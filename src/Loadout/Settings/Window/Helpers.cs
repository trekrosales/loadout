using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using Loadout.Input;
using Loadout.Settings.Model;

namespace Loadout.Settings.Window;

/// <summary>Checks and cleans up web addresses typed into the Website dialog.</summary>
internal static partial class UrlNormalizer
{
    /// <summary>Accepts an http or https address; adds https:// when the scheme is missing.</summary>
    public static bool TryNormalize(string? input, out string url)
    {
        url = string.Empty;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            // "mailto:x" or "javascript:x" are schemes; "localhost:3000" is a host and port.
            if (OtherSchemeRegex().IsMatch(text))
            {
                return false;
            }

            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        var hostOk = uri.Host.Contains('.')
            || uri.HostNameType is UriHostNameType.IPv6 or UriHostNameType.IPv4
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!hostOk)
        {
            return false;
        }

        url = text;
        return true;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:(?!\\d)")]
    private static partial Regex OtherSchemeRegex();
}

/// <summary>Parses and formats "#RRGGBB" colors.</summary>
internal static class HexColor
{
    /// <summary>Accepts "#RRGGBB", "RRGGBB", "#RGB" or "RGB".</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim();
        if (string.IsNullOrEmpty(hex))
        {
            return false;
        }

        if (hex[0] == '#')
        {
            hex = hex[1..];
        }

        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        var rgb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>The canonical "#RRGGBB" (upper case) form, or null if invalid.</summary>
    public static string? Normalize(string? text) => TryParse(text, out var c) ? ToHex(c) : null;

    public static string ToHex(Color c) =>
        string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
}

/// <summary>Reads Windows internet shortcut (.url) files.</summary>
internal static class InternetShortcutFile
{
    /// <summary>Finds the "URL=" line and returns the normalized address, or null.</summary>
    public static string? ParseUrl(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                return UrlNormalizer.TryNormalize(line[4..], out var url) ? url : null;
            }
        }

        return null;
    }

    public static string? ReadUrl(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= 64 * 1024 ? ParseUrl(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Turns dropped or browsed items into slots.</summary>
internal static class DropConverter
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";
    private const string AppsFolderClsidMarker = "::{4234D49B-0245-4DF3-B780-3893943456E1}\\";

    /// <summary>A program or shortcut becomes App, a folder becomes Folder, a .url becomes Website. Anything else is null.</summary>
    public static SlotSettings? FromPath(string? path)
    {
        var text = path?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (Directory.Exists(text))
        {
            var trimmed = text.Length > 3 ? text.TrimEnd('\\', '/') : text;
            return new SlotSettings { Type = SlotType.Folder, Path = trimmed };
        }

        var ext = Path.GetExtension(text);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return new SlotSettings { Type = SlotType.App, Path = text };
        }

        if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return InternetShortcutFile.ReadUrl(text) is { } url
                ? new SlotSettings { Type = SlotType.Website, Url = url }
                : null;
        }

        return null;
    }

    /// <summary>If a shell parsing name is an installed-app entry, returns its AppUserModelID.</summary>
    public static string? TryGetAumid(string? parsingName)
    {
        var name = parsingName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (name.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = name[AppsFolderPrefix.Length..];
            return rest.Length > 0 ? rest : null;
        }

        var marker = name.IndexOf(AppsFolderClsidMarker, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var rest = name[(marker + AppsFolderClsidMarker.Length)..];
            return rest.Length > 0 ? rest : null;
        }

        if (name.Contains('!') && name.IndexOfAny(['\\', '/', ':']) < 0)
        {
            return name;
        }

        return null;
    }

    /// <summary>An installed-app slot ("shell:AppsFolder\AUMID") with its display name as the label.</summary>
    public static SlotSettings FromAumid(string aumid, string? displayName) => new()
    {
        Type = SlotType.App,
        Path = AppsFolderPrefix + aumid,
        Label = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
    };

    /// <summary>Converts the first item of a Start menu drag (parsing name plus display name).</summary>
    public static SlotSettings? FromShellItem(string? parsingName, string? displayName)
    {
        if (TryGetAumid(parsingName) is { } aumid)
        {
            return FromAumid(aumid, displayName);
        }

        return FromPath(parsingName);
    }
}

/// <summary>Turns recorded key presses into hotkey tokens (see HotkeySettings.Keys).</summary>
internal static class HotkeyRecording
{
    private static readonly string[] s_modifierOrder = ["Ctrl", "Alt", "Shift", "Win"];

    /// <summary>The token for a key, or null for keys that mean nothing here (None, IME and dead-key markers, Key.System).</summary>
    public static string? TokenForKey(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LWin or Key.RWin => "Win",
        Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.DbeNoCodeInput => null,
        _ => key.ToString(),
    };

    /// <summary>Modifiers first (Ctrl, Alt, Shift, Win), then other keys in the order given, then mouse buttons.</summary>
    public static List<string> BuildTokens(IEnumerable<Key> keys, IEnumerable<string>? mouseTokens = null)
    {
        var modifiers = new HashSet<string>();
        var others = new List<string>();
        foreach (var key in keys)
        {
            var token = TokenForKey(key);
            if (token is null)
            {
                continue;
            }

            if (s_modifierOrder.Contains(token))
            {
                modifiers.Add(token);
            }
            else if (!others.Contains(token))
            {
                others.Add(token);
            }
        }

        var result = s_modifierOrder.Where(modifiers.Contains).ToList();
        result.AddRange(others);
        foreach (var mouse in mouseTokens ?? [])
        {
            if (!result.Contains(mouse))
            {
                result.Add(mouse);
            }
        }

        return result;
    }

    /// <summary>Null if the tokens form a valid hotkey, otherwise the reason.</summary>
    public static string? Validate(IReadOnlyList<string> tokens)
    {
        try
        {
            HotkeyDefinition.Parse(tokens);
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The text shown on a key cap.</summary>
    public static string DisplayName(string token) => token switch
    {
        "MouseMiddle" => "Middle click",
        "MouseX1" => "Mouse back",
        "MouseX2" => "Mouse forward",
        _ => token,
    };
}
