using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Loadout.Contracts;
using Loadout.Core;
using Loadout.Settings.Model;
using static Loadout.Launching.LaunchingNative;

namespace Loadout.Launching;

/// <summary>
/// Opens a slot's app, folder or website (SPEC 3.2, 3.3). Switches to an already-open window when there is one.
/// Never throws and never waits on the process it starts.
/// </summary>
public sealed class ShellLauncher : ILauncher
{
    public LaunchResult Launch(SlotSettings slot)
    {
        try
        {
            return slot.Type switch
            {
                SlotType.App => LaunchApp(slot),
                SlotType.Folder => LaunchFolder(slot),
                SlotType.Website => LaunchWebsite(slot),
                _ => Fail("This slot type isn't supported."),
            };
        }
        catch (Exception ex)
        {
            Log.Error("Launching a slot failed.", ex);
            return Fail("Something went wrong while opening this slot.");
        }
    }

    private static LaunchResult Fail(string message) => new(LaunchOutcome.Failed, message);

    // ---------- Websites ----------

    private static LaunchResult LaunchWebsite(SlotSettings slot)
    {
        if (!LaunchHelpers.TryParseWebUrl(slot.Url, out var uri) || uri is null)
        {
            return Fail("That isn't a valid web address. It must start with http:// or https://.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return new(LaunchOutcome.Started);
        }
        catch (Exception ex)
        {
            Log.Error("Opening a website failed: " + uri, ex);
            return Fail("The website couldn't be opened in your browser.");
        }
    }

    // ---------- Apps ----------

    private static LaunchResult LaunchApp(SlotSettings slot)
    {
        var raw = slot.Path;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("No app is set for this slot.");
        }

        var aumid = LaunchHelpers.ParseAppsFolder(raw);
        if (aumid is not null)
        {
            return LaunchPackagedApp(aumid, slot.Arguments);
        }

        var path = Environment.ExpandEnvironmentVariables(raw.Trim());
        var hasFolder = path.Contains('\\') || path.Contains('/');
        if (hasFolder && !File.Exists(path))
        {
            return Fail("The app couldn't be found: " + raw);
        }

        var isLink = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        var target = isLink ? ResolveShortcutTarget(path) : path;

        if (!string.IsNullOrEmpty(target) && hasFolder)
        {
            var hwnd = FindWindowForExecutable(target);
            if (hwnd != 0)
            {
                FocusWindow(hwnd);
                return new(LaunchOutcome.SwitchedToExisting);
            }
        }

        try
        {
            var args = slot.Arguments;
            var info = string.IsNullOrWhiteSpace(args)
                ? new ProcessStartInfo(path)
                : new ProcessStartInfo(path, args);
            info.UseShellExecute = true;
            if (!isLink && hasFolder)
            {
                info.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            }

            Process.Start(info)?.Dispose();
            return new(LaunchOutcome.Started);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Fail("Windows asked for permission to open this app and it was declined.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return Fail("The app couldn't be found: " + raw);
        }
        catch (Exception ex)
        {
            Log.Error("Starting an app failed: " + path, ex);
            return Fail("The app couldn't be started: " + raw);
        }
    }

    private static LaunchResult LaunchPackagedApp(string aumid, string? arguments)
    {
        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManagerCoClass();
            try
            {
                var hr = manager.ActivateApplication(aumid, string.IsNullOrWhiteSpace(arguments) ? null : arguments, 0, out _);
                if (hr >= 0)
                {
                    return new(LaunchOutcome.Started);
                }

                Log.Error("ActivateApplication failed for " + aumid + " (HRESULT 0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + ").");
            }
            finally
            {
                Marshal.FinalReleaseComObject(manager);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Activating a packaged app failed: " + aumid, ex);
        }

        try
        {
            Process.Start(new ProcessStartInfo("shell:AppsFolder\\" + aumid) { UseShellExecute = true })?.Dispose();
            return new(LaunchOutcome.Started);
        }
        catch (Exception ex)
        {
            Log.Error("Fallback start of a packaged app failed: " + aumid, ex);
            return Fail("The app couldn't be started: shell:AppsFolder\\" + aumid);
        }
    }

    // ---------- Folders ----------

    private static LaunchResult LaunchFolder(SlotSettings slot)
    {
        var raw = slot.Path;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("No folder is set for this slot.");
        }

        string? resolved;
        if (LaunchHelpers.IsShellPath(raw))
        {
            resolved = ResolveShellFolder(raw.Trim());
        }
        else
        {
            resolved = LaunchHelpers.NormalizePath(Environment.ExpandEnvironmentVariables(raw.Trim()));
            if (resolved is null || !Directory.Exists(resolved))
            {
                return Fail("The folder couldn't be found: " + raw);
            }
        }

        if (resolved is not null)
        {
            var hwnd = FindExplorerWindow(resolved);
            if (hwnd != 0)
            {
                FocusWindow(hwnd);
                return new(LaunchOutcome.SwitchedToExisting);
            }
        }

        try
        {
            var toOpen = resolved ?? raw.Trim();
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + toOpen + "\"") { UseShellExecute = true })?.Dispose();
            return new(LaunchOutcome.Started);
        }
        catch (Exception ex)
        {
            Log.Error("Opening a folder failed: " + raw, ex);
            return Fail("The folder couldn't be opened: " + raw);
        }
    }

    /// <summary>Turns "shell:Downloads" into a real path. Null if it has none (for example "shell:MyComputerFolder").</summary>
    internal static string? ResolveShellFolder(string shellPath)
    {
        nint pidl = 0;
        nint name = 0;
        try
        {
            if (SHParseDisplayName(shellPath, 0, out pidl, 0, out _) < 0 || pidl == 0)
            {
                return null;
            }

            if (SHGetNameFromIDList(pidl, SIGDN_FILESYSPATH, out name) < 0 || name == 0)
            {
                return null;
            }

            return LaunchHelpers.NormalizePath(Marshal.PtrToStringUni(name));
        }
        catch (Exception ex)
        {
            Log.Error("Resolving a shell folder failed: " + shellPath, ex);
            return null;
        }
        finally
        {
            if (name != 0)
            {
                CoTaskMemFree(name);
            }

            if (pidl != 0)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    /// <summary>Finds an open File Explorer window showing the folder. 0 if none.</summary>
    private static nint FindExplorerWindow(string folder)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return 0;
            }

            shell = Activator.CreateInstance(type);
            if (shell is null)
            {
                return 0;
            }

            dynamic app = shell;
            windows = app.Windows();
            dynamic list = windows!;
            int count = list.Count;
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = list.Item(i);
                    if (item is null)
                    {
                        continue;
                    }

                    dynamic window = item;
                    string? url = window.LocationURL;
                    var path = LaunchHelpers.FileUrlToPath(url);
                    if (path is not null && LaunchHelpers.PathsEqual(path, folder))
                    {
                        return (nint)(long)window.HWND;
                    }
                }
                catch (Exception)
                {
                    // One odd window (for example an Internet Explorer frame) must not stop the search.
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

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        return 0;
    }

    // ---------- Shortcuts ----------

    /// <summary>Returns the file a .lnk points at, or null if it can't be read or has no file target.</summary>
    internal static string? ResolveShortcutTarget(string lnkPath)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLinkCoClass();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, 0, 0);
            var target = sb.ToString();
            return target.Length == 0 ? null : target;
        }
        catch (Exception ex)
        {
            Log.Error("Reading a shortcut failed: " + lnkPath, ex);
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    // ---------- Windows ----------

    /// <summary>The most recently used real window of the given exe, or 0.</summary>
    private static nint FindWindowForExecutable(string exePath)
    {
        var wanted = LaunchHelpers.NormalizePath(exePath);
        if (wanted is null)
        {
            return 0;
        }

        nint found = 0;
        var cache = new Dictionary<uint, string?>();
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsRealTopLevelWindow(hwnd))
                {
                    return true;
                }

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                {
                    return true;
                }

                if (!cache.TryGetValue(pid, out var path))
                {
                    path = GetProcessPath(pid);
                    cache[pid] = path;
                }

                if (path is not null && LaunchHelpers.PathsEqual(path, wanted))
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
        return found;
    }

    /// <summary>Roughly what Alt+Tab lists.</summary>
    private static bool IsRealTopLevelWindow(nint hwnd)
    {
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        if (GetWindow(hwnd, GW_OWNER) != 0)
        {
            return false;
        }

        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        return GetWindowTextLength(hwnd) > 0;
    }

    private static unsafe string? GetProcessPath(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            fixed (char* p = buffer)
            {
                return QueryFullProcessImageName(handle, 0, p, ref size)
                    ? new string(buffer, 0, (int)size)
                    : null;
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Restores and raises a window, working around Windows' focus-stealing rules. True if it ended up in front.</summary>
    internal static bool FocusWindow(nint hwnd)
    {
        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            SetForegroundWindow(hwnd);
            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            // A harmless key press (unassigned key) makes Windows treat us as having just had input.
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_UNASSIGNED;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_UNASSIGNED;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

            SetForegroundWindow(hwnd);
            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
            var thisThread = GetCurrentThreadId();
            var attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != thisThread)
                {
                    attached = AttachThreadInput(foregroundThread, thisThread, true);
                }

                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(foregroundThread, thisThread, false);
                }
            }

            return GetForegroundWindow() == hwnd;
        }
        catch (Exception ex)
        {
            Log.Error("Bringing a window to the front failed.", ex);
            return false;
        }
    }
}
