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
