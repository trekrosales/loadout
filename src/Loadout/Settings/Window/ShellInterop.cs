using System.Runtime.InteropServices;
using System.Windows;
using Loadout.Core;
using Loadout.Settings.Model;

namespace Loadout.Settings.Window;

/// <summary>One installed app from the Start menu list.</summary>
internal sealed record InstalledApp(string Name, string Aumid);

/// <summary>Shell calls for the settings window: the installed-app list and Start menu drops. Declarations stay internal here.</summary>
internal static unsafe partial class ShellInterop
{
    private const uint SIGDN_NORMALDISPLAY = 0;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    private static Guid s_iidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static Guid s_iidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    private static Guid s_bhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    public const string ShellIdListFormat = "Shell IDList Array";

    /// <summary>Lists the apps in shell:AppsFolder (what the Start menu shows). Call on a background STA thread.</summary>
    public static List<InstalledApp> EnumerateInstalledApps()
    {
        var result = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IShellItem? folder = null;
        IEnumShellItems? items = null;
        try
        {
            folder = Create("shell:AppsFolder");
            if (folder is null)
            {
                return result;
            }

            if (folder.BindToHandler(0, ref s_bhidEnumItems, ref s_iidEnumShellItems, out var enumPtr) != 0 || enumPtr == 0)
            {
                return result;
            }

            items = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);

            while (items.Next(1, out var child, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    var name = Name(child, SIGDN_NORMALDISPLAY);
                    var aumid = Name(child, SIGDN_PARENTRELATIVEPARSING);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(aumid) && seen.Add(aumid))
                    {
                        result.Add(new InstalledApp(name, aumid));
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(child);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not list installed apps.", ex);
        }
        finally
        {
            if (items is not null)
            {
                Marshal.FinalReleaseComObject(items);
            }

            if (folder is not null)
            {
                Marshal.FinalReleaseComObject(folder);
            }
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>Reads the first item of a Start menu / taskbar drag ("Shell IDList Array") as a slot, or null.</summary>
    public static SlotSettings? SlotFromShellIdList(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(ShellIdListFormat) || data.GetData(ShellIdListFormat) is not MemoryStream stream)
            {
                return null;
            }

            var bytes = stream.ToArray();
            if (bytes.Length < 12)
            {
                return null;
            }

            var count = BitConverter.ToUInt32(bytes, 0);
            if (count < 1 || count > 4096)
            {
                return null;
            }

            var headerSize = 4L + ((count + 1L) * 4L);
            if (bytes.Length < headerSize)
            {
                return null;
            }

            var parentOffset = BitConverter.ToUInt32(bytes, 4);
            var childOffset = BitConverter.ToUInt32(bytes, 8);
            if (!IsValidIdList(bytes, parentOffset, headerSize) || !IsValidIdList(bytes, childOffset, headerSize))
            {
                return null;
            }

            nint absolute;
            fixed (byte* p = bytes)
            {
                absolute = ILCombine((nint)(p + parentOffset), (nint)(p + childOffset));
            }

            if (absolute == 0)
            {
                return null;
            }

            try
            {
                if (SHCreateItemFromIDList(absolute, ref s_iidShellItem, out var itemPtr) != 0 || itemPtr == 0)
                {
                    return null;
                }

                var item = (IShellItem)Marshal.GetObjectForIUnknown(itemPtr);
                Marshal.Release(itemPtr);
                try
                {
                    return DropConverter.FromShellItem(Name(item, SIGDN_DESKTOPABSOLUTEPARSING), Name(item, SIGDN_NORMALDISPLAY));
                }
                finally
                {
                    Marshal.FinalReleaseComObject(item);
                }
            }
            finally
            {
                ILFree(absolute);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the dropped Start menu item.", ex);
            return null;
        }
    }

    /// <summary>
    /// True if a complete item ID list (a chain of size-prefixed pieces ending in a zero size) starts at
    /// <paramref name="offset"/> and ends inside the buffer.
    /// </summary>
    internal static bool IsValidIdList(byte[] bytes, long offset, long minOffset)
    {
        if (offset < minOffset || offset > bytes.Length - 2L)
        {
            return false;
        }

        var position = offset;
        while (true)
        {
            if (position > bytes.Length - 2L)
            {
                return false;
            }

            var size = BitConverter.ToUInt16(bytes, (int)position);
            if (size == 0)
            {
                return true;
            }

            if (size < 2)
            {
                return false;
            }

            position += size;
        }
    }

    private static IShellItem? Create(string parsingName)
    {
        if (SHCreateItemFromParsingName(parsingName, 0, ref s_iidShellItem, out var ptr) != 0 || ptr == 0)
        {
            return null;
        }

        var item = (IShellItem)Marshal.GetObjectForIUnknown(ptr);
        Marshal.Release(ptr);
        return item;
    }

    private static string? Name(IShellItem item, uint kind)
    {
        if (item.GetDisplayName(kind, out var ptr) != 0 || ptr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, out nint item);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateItemFromIDList(nint pidl, ref Guid riid, out nint item);

    [LibraryImport("shell32.dll")]
    private static partial nint ILCombine(nint pidl1, nint pidl2);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(nint pidl);

#pragma warning disable SYSLIB1096 // These plain COM interfaces are used with the built-in interop.
    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(nint bindContext, ref Guid handler, ref Guid riid, out nint result);

        [PreserveSig]
        int GetParent(out nint parent);

        [PreserveSig]
        int GetDisplayName(uint sigdn, out nint name);

        [PreserveSig]
        int GetAttributes(uint mask, out uint attributes);

        [PreserveSig]
        int Compare(nint other, uint hint, out int order);
    }

    [ComImport]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out nint enumerator);
    }
#pragma warning restore SYSLIB1096
}
