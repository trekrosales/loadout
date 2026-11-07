namespace Loadout.Launching;

/// <summary>Small pure helpers (no windows, no processes) so they can be unit-tested.</summary>
internal static class LaunchHelpers
{
    private const string AppsFolderPrefix = "shell:AppsFolder\\";

    /// <summary>True when the text is an absolute http or https address.</summary>
    internal static bool TryParseWebUrl(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>Extracts the AppUserModelID from "shell:AppsFolder\ID". Null if it isn't that form.</summary>
    internal static string? ParseAppsFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var text = path.Trim();
        if (text.Length <= AppsFolderPrefix.Length)
        {
            return null;
        }

        var head = text[..AppsFolderPrefix.Length].Replace('/', '\\');
        if (!head.Equals(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = text[AppsFolderPrefix.Length..].Trim();
        return id.Length == 0 ? null : id;
    }

    internal static bool IsShellPath(string? path) =>
        path is not null && path.TrimStart().StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Full path without a trailing separator (roots like C:\ are kept). Null if the text isn't a usable path.</summary>
    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);
            if (full.Length > (root?.Length ?? 0))
            {
                full = full.TrimEnd('\\', '/');
            }

            return full;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    internal static bool PathsEqual(string? a, string? b)
    {
        var left = NormalizePath(a);
        var right = NormalizePath(b);
        return left is not null && right is not null
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Data inside an App Execution Alias (Windows 11 Notepad, Paint, wt ...).</summary>
    internal sealed record AppExecLink(string PackageId, string Aumid, string TargetExe);

    internal const uint AppExecLinkTag = 0x8000001B;

    /// <summary>
    /// Parses a FSCTL_GET_REPARSE_POINT result for an app execution alias: tag (uint32), data length (uint16),
    /// reserved (uint16), version (uint32), then null-separated UTF-16 strings: package id, AUMID, target exe, ...
    /// Null if it isn't such a buffer.
    /// </summary>
    internal static AppExecLink? ParseAppExecLink(ReadOnlySpan<byte> buffer)
    {
        const int headerSize = 12;
        if (buffer.Length < headerSize + 2 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer) != AppExecLinkTag)
        {
            return null;
        }

        var strings = new List<string>(3);
        var pos = headerSize;
        while (pos + 1 < buffer.Length && strings.Count < 3)
        {
            var start = pos;
            while (pos + 1 < buffer.Length && (buffer[pos] != 0 || buffer[pos + 1] != 0))
            {
                pos += 2;
            }

            if (pos + 1 >= buffer.Length)
            {
                return null; // string not terminated
            }

            strings.Add(System.Text.Encoding.Unicode.GetString(buffer[start..pos]));
            pos += 2;
        }

        if (strings.Count < 3 || strings[1].Length == 0)
        {
            return null;
        }

        return new AppExecLink(strings[0], strings[1], strings[2]);
    }

    /// <summary>Strips the \\?\ and \\?\UNC\ prefixes that GetFinalPathNameByHandle adds.</summary>
    internal static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
        {
            return "\\\\" + path[8..];
        }

        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            return path[4..];
        }

        return path;
    }

    /// <summary>Converts "file:///C:/My%20Dir/" or "file://server/share/x" to a normal path. Null if not a file URL.</summary>
    internal static string? FileUrlToPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return null;
        }

        var local = uri.LocalPath;
        return string.IsNullOrEmpty(local) ? null : local;
    }
}
