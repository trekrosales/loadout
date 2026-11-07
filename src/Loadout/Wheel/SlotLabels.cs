using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Loadout.Settings.Model;

namespace Loadout.Wheel;

/// <summary>
/// The name shown in the wheel's center for a slot: the custom label if set, otherwise a name derived from
/// the app, folder or site. Derived names are cached per slot object so disk and shell lookups happen once.
/// </summary>
internal static class SlotLabels
{
    private static readonly ConditionalWeakTable<SlotSettings, string> s_cache = [];

    public static string For(SlotSettings slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.Label))
        {
            return slot.Label.Trim();
        }

        return s_cache.GetValue(slot, Derive);
    }

    internal static string Derive(SlotSettings slot)
    {
        try
        {
            return slot.Type switch
            {
                SlotType.Website => SiteName(slot.Url),
                SlotType.Folder => ShellDisplayName(slot.Path) ?? LastPart(slot.Path),
                _ => AppName(slot.Path),
            };
        }
        catch (Exception)
        {
            return LastPart(slot.Path ?? slot.Url);
        }
    }

    internal static string SiteName(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return url ?? "Website";
        }

        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string AppName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "App";
        }

        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }

            if (!string.IsNullOrWhiteSpace(info.ProductName))
            {
                return info.ProductName.Trim();
            }

            return Path.GetFileNameWithoutExtension(path);
        }

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        return ShellDisplayName(path) ?? LastPart(path);
    }

    internal static string LastPart(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "?";
        }

        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/', '!']);
        var part = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        return part.Length > 0 ? part : trimmed;
    }

    /// <summary>The shell's own display name (handles "shell:Downloads", Store apps and localized folder names).</summary>
    private static string? ShellDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var iid = typeof(IShellItem).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var item) != 0 || item is null)
        {
            return null;
        }

        try
        {
            return item.GetDisplayName(SIGDN_NORMALDISPLAY, out var name) == 0 ? name : null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(item);
        }
    }

    private const uint SIGDN_NORMALDISPLAY = 0;

#pragma warning disable SYSLIB1054, SYSLIB1096 // Built-in COM interop is needed for the IShellItem interface.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(nint bindContext, ref Guid handler, ref Guid riid, out nint result);

        [PreserveSig]
        int GetParent(out IShellItem parent);

        [PreserveSig]
        int GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);

        [PreserveSig]
        int GetAttributes(uint mask, out uint attributes);

        [PreserveSig]
        int Compare(IShellItem other, uint hint, out int order);
    }
#pragma warning restore SYSLIB1054, SYSLIB1096
}
