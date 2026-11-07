using System.Reflection;
using System.Runtime.InteropServices;
using Loadout.Core;
using static Loadout.Launching.LaunchingNative;

namespace Loadout.Launching;

/// <summary>What a running app's window must match: one of these exe paths, or this AppUserModelID.</summary>
internal sealed record MatchSet(IReadOnlyList<string> Paths, string? Aumid);

/// <summary>Finds existing windows: app windows (by exe path or AUMID) and Explorer windows (by folder).</summary>
internal static unsafe class WindowFinder
{
    private const int ExplorerSearchTimeoutMs = 250;

    // ---------- App identity ----------

    private static string AliasFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");

    /// <summary>Opens a path without needing any access to the file and returns its final path (junctions and symlinks resolved).</summary>
    internal static string? GetRealPath(string path)
    {
        var handle = CreateFile(path, 0, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle == -1 || handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            fixed (char* p = buffer)
            {
                var n = GetFinalPathNameByHandle(handle, p, (uint)buffer.Length, 0);
                if (n == 0 || n >= buffer.Length)
                {
                    return null;
                }

                return LaunchHelpers.StripExtendedPrefix(new string(p, 0, (int)n));
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Reads an app execution alias file. Null if the file isn't one.</summary>
    internal static LaunchHelpers.AppExecLink? ReadAppExecLink(string path)
    {
        var handle = CreateFile(path, 0, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, 0);
        if (handle == -1 || handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new byte[16 * 1024];
            fixed (byte* p = buffer)
            {
                if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, 0, 0, p, (uint)buffer.Length, out var returned, 0))
                {
                    return null;
                }

                return LaunchHelpers.ParseAppExecLink(new ReadOnlySpan<byte>(buffer, 0, (int)returned));
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Everything a running window of this exe could be recognised by.</summary>
    internal static MatchSet BuildMatchSet(string exePath)
    {
        var paths = new List<string>();
        string? aumid = null;

        void AddPath(string? p)
        {
            var n = LaunchHelpers.NormalizePath(p);
            if (n is not null && !paths.Contains(n, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(n);
            }
        }

        AddPath(exePath);
        var real = GetRealPath(exePath);
        AddPath(real);

        var normalized = LaunchHelpers.NormalizePath(exePath);
        var dir = normalized is null ? null : Path.GetDirectoryName(normalized);
        var aliasDir = LaunchHelpers.NormalizePath(AliasFolder);
        LaunchHelpers.AppExecLink? alias = null;
        if (dir is not null && aliasDir is not null)
        {
            if (LaunchHelpers.PathsEqual(dir, aliasDir))
            {
                alias = ReadAppExecLink(exePath);
            }
            else if (LaunchHelpers.PathsEqual(dir, Environment.SystemDirectory)
                || LaunchHelpers.PathsEqual(dir, Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)))
            {
                var candidate = Path.Combine(aliasDir, Path.GetFileName(normalized!));
                if (File.Exists(candidate))
                {
                    alias = ReadAppExecLink(candidate);
                }
            }
        }

        if (alias is not null)
        {
            aumid = alias.Aumid;
            AddPath(alias.TargetExe);
        }

        return new MatchSet(paths, aumid);
    }

    // ---------- App windows ----------

    private sealed class ProcessInfo
    {
        public string? Path;
        public string? Aumid;
    }

    /// <summary>The most recently used real window matching the set, or 0. EnumWindows walks top to bottom in z-order.</summary>
    internal static nint FindAppWindow(MatchSet match)
    {
        nint found = 0;
        var cache = new Dictionary<uint, ProcessInfo>();
        IVirtualDesktopManager? vdm = null;
        var vdmTried = false;

        IVirtualDesktopManager? GetVdm()
        {
            if (!vdmTried)
            {
                vdmTried = true;
                try
                {
                    vdm = (IVirtualDesktopManager)new VirtualDesktopManagerCoClass();
                }
                catch (Exception)
                {
                    vdm = null;
                }
            }

            return vdm;
        }

        try
        {
            EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsRealTopLevelWindow(hwnd, GetVdm) || IsHungAppWindow(hwnd))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid == 0)
                    {
                        return true;
                    }

                    if (!cache.TryGetValue(pid, out var info))
                    {
                        info = ReadProcessInfo(pid, match.Aumid is not null);
                        cache[pid] = info;
                    }

                    if (info.Path is not null && match.Paths.Contains(info.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        found = hwnd;
                        return false;
                    }

                    if (match.Aumid is not null
                        && (string.Equals(info.Aumid, match.Aumid, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(GetWindowAumid(hwnd), match.Aumid, StringComparison.OrdinalIgnoreCase)))
                    {
                        found = hwnd;
                        return false;
                    }
                }
                catch (Exception)
                {
                    // Skip windows we can't inspect.
                }

                return true;
            }, 0);
        }
        finally
        {
            if (vdm is not null)
            {
                Marshal.FinalReleaseComObject(vdm);
            }
        }

        return found;
    }

    /// <summary>Roughly what Alt+Tab lists.</summary>
    private static bool IsRealTopLevelWindow(nint hwnd, Func<IVirtualDesktopManager?> getVdm)
    {
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            // Windows on another virtual desktop are cloaked by the shell; those still count.
            if (cloaked != DWM_CLOAKED_SHELL || !IsOnOtherDesktop(hwnd, getVdm()))
            {
                return false;
            }
        }

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var appWindow = (style & WS_EX_APPWINDOW) != 0;
        if (GetWindow(hwnd, GW_OWNER) != 0 && !appWindow)
        {
            return false;
        }

        if ((style & WS_EX_TOOLWINDOW) != 0 && !appWindow)
        {
            return false;
        }

        return GetWindowTextLength(hwnd) > 0;
    }

    private static bool IsOnOtherDesktop(nint hwnd, IVirtualDesktopManager? vdm)
    {
        if (vdm is null)
        {
            return false;
        }

        try
        {
            vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent);
            return !onCurrent;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ProcessInfo ReadProcessInfo(uint pid, bool wantAumid)
    {
        var info = new ProcessInfo();
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return info;
        }

        try
        {
            var size = 1024;
            while (size <= 32 * 1024)
            {
                var buffer = new char[size];
                var length = (uint)size;
                fixed (char* p = buffer)
                {
                    if (QueryFullProcessImageName(handle, 0, p, ref length))
                    {
                        info.Path = LaunchHelpers.NormalizePath(new string(buffer, 0, (int)length));
                        break;
                    }
                }

                if (Marshal.GetLastPInvokeError() != ERROR_INSUFFICIENT_BUFFER)
                {
                    break;
                }

                size *= 2;
            }

            if (wantAumid)
            {
                var id = stackalloc char[256];
                uint idLength = 256;
                if (GetApplicationUserModelId(handle, ref idLength, id) == 0 && idLength > 1)
                {
                    info.Aumid = new string(id, 0, (int)idLength - 1);
                }
            }
        }
        finally
        {
            CloseHandle(handle);
        }

        return info;
    }

    /// <summary>The AppUserModelID of a window (covers Store app frames hosted by ApplicationFrameHost). Null if it has none.</summary>
    internal static string? GetWindowAumid(nint hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) < 0 || store is null)
            {
                return null;
            }

            var key = new PROPERTYKEY { fmtid = AppUserModelIdFormat, pid = 5 };
            store.GetValue(ref key, out var value);
            try
            {
                const ushort VT_LPWSTR = 31;
                return value.vt == VT_LPWSTR && value.p != 0 ? Marshal.PtrToStringUni(value.p) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.FinalReleaseComObject(store);
            }
        }
    }

    // ---------- Explorer windows ----------

    /// <summary>
    /// Finds an Explorer window whose active tab shows the folder. 0 if none, or if Explorer doesn't answer
    /// within a quarter of a second (the caller then just opens a new window).
    /// </summary>
    internal static nint FindExplorerWindow(string folder)
    {
        nint result = 0;
        try
        {
            var thread = new Thread(() => result = FindExplorerWindowCore(folder))
            {
                IsBackground = true,
                Name = "Loadout explorer search",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(ExplorerSearchTimeoutMs))
            {
                Log.Error("Explorer did not answer in time while looking for an open folder window.");
                return 0;
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Looking for an open Explorer window failed.", ex);
            return 0;
        }
    }

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static nint FindExplorerWindowCore(string folder)
    {
        object? windows = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ShellWindowsClsid);
            if (type is null)
            {
                return 0;
            }

            windows = Activator.CreateInstance(type);
            if (windows is null)
            {
                return 0;
            }

            var count = Convert.ToInt32(Get(windows, "Count"), System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = windows.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, windows, [i]);
                    if (item is null)
                    {
                        continue;
                    }

                    var path = LaunchHelpers.FileUrlToPath(Get(item, "LocationURL") as string);
                    if (path is null || !LaunchHelpers.PathsEqual(path, folder))
                    {
                        continue;
                    }

                    var top = (nint)Convert.ToInt64(Get(item, "HWND"), System.Globalization.CultureInfo.InvariantCulture);
                    if (top != 0 && !IsHungAppWindow(top) && IsActiveTab(item, top))
                    {
                        return top;
                    }
                }
                catch (Exception)
                {
                    // One odd window must not stop the search.
                }
                finally
                {
                    if (item is not null && Marshal.IsComObject(item))
                    {
                        Marshal.FinalReleaseComObject(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Looking for an open Explorer window failed.", ex);
        }
        finally
        {
            if (windows is not null && Marshal.IsComObject(windows))
            {
                Marshal.FinalReleaseComObject(windows);
            }
        }

        return 0;
    }

    /// <summary>Windows 11 Explorer lists every tab, all with the same top-level window. Only the visible tab counts.</summary>
    private static bool IsActiveTab(object item, nint top)
    {
        var activeTab = FindWindowEx(top, 0, "ShellTabWindowClass", null);
        if (activeTab == 0)
        {
            return true; // no tabs (older Windows)
        }

        nint raw = 0;
        object? browser = null;
        try
        {
            var service = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            if (((IServiceProviderCom)item).QueryService(ref service, ref iid, out raw) < 0 || raw == 0)
            {
                return true;
            }

            browser = Marshal.GetObjectForIUnknown(raw);
            ((IShellBrowserLite)browser).GetWindow(out var tabWindow);
            return tabWindow == activeTab;
        }
        catch (Exception)
        {
            return true;
        }
        finally
        {
            if (browser is not null)
            {
                Marshal.FinalReleaseComObject(browser);
            }

            if (raw != 0)
            {
                Marshal.Release(raw);
            }
        }
    }
}
