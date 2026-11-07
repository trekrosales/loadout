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
/// Checks and window switching happen right away; the actual start is handed to a short-lived background
/// thread so the UI thread never waits for Windows. A start that fails later is only written to the log.
/// Never throws. When switching to a running app, a shortcut's own arguments are ignored (only its target is compared).
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

    private static LaunchResult SwitchTo(nint hwnd)
    {
        if (!FocusWindow(hwnd))
        {
            Log.Error("Windows would not let Loadout bring a window to the front (window " + hwnd + ").");
        }

        return new(LaunchOutcome.SwitchedToExisting);
    }

    private static string ExplorerExe =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    // ---------- Websites ----------

    private static LaunchResult LaunchWebsite(SlotSettings slot)
    {
        if (!LaunchHelpers.TryParseWebUrl(slot.Url, out var uri) || uri is null)
        {
            return Fail("That isn't a valid web address. It must start with http:// or https://.");
        }

        var address = uri.AbsoluteUri;
        StartWorker.Enqueue(address, () => Process.Start(new ProcessStartInfo(address) { UseShellExecute = true })?.Dispose());
        return new(LaunchOutcome.Started);
    }

    // ---------- Apps ----------

    private static LaunchResult LaunchApp(SlotSettings slot)
    {
        var raw = slot.Path?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return Fail("No app is set for this slot.");
        }

        var aumid = LaunchHelpers.ParseAppsFolder(raw);
        if (aumid is not null)
        {
            return LaunchPackagedApp(aumid, slot.Arguments);
        }

        var path = File.Exists(raw) ? raw : Environment.ExpandEnvironmentVariables(raw);
        var hasFolder = path.Contains('\\') || path.Contains('/');
        if (hasFolder && !File.Exists(path))
        {
            return Fail("The app couldn't be found: " + raw);
        }

        var isLink = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        var target = path;
        if (isLink)
        {
            target = ResolveShortcutTarget(path);
            if (!string.IsNullOrEmpty(target) && !File.Exists(target) && !Directory.Exists(target))
            {
                return Fail("The shortcut points to something that no longer exists: " + target);
            }
        }

        if (!string.IsNullOrEmpty(target) && (target.Contains('\\') || target.Contains('/')))
        {
            var hwnd = WindowFinder.FindAppWindow(WindowFinder.BuildMatchSet(target));
            if (hwnd != 0)
            {
                return SwitchTo(hwnd);
            }
        }

        var args = slot.Arguments;
        var workDir = !isLink && hasFolder ? Path.GetDirectoryName(Path.GetFullPath(path)) : null;
        StartWorker.Enqueue(path, () =>
        {
            var info = string.IsNullOrWhiteSpace(args) ? new ProcessStartInfo(path) : new ProcessStartInfo(path, args);
            info.UseShellExecute = true;
            if (!string.IsNullOrEmpty(workDir))
            {
                info.WorkingDirectory = workDir;
            }

            Process.Start(info)?.Dispose();
        });
        return new(LaunchOutcome.Started);
    }

    private static LaunchResult LaunchPackagedApp(string aumid, string? arguments)
    {
        var hwnd = WindowFinder.FindAppWindow(new MatchSet([], aumid));
        if (hwnd != 0)
        {
            return SwitchTo(hwnd);
        }

        var args = string.IsNullOrWhiteSpace(arguments) ? null : arguments;
        StartWorker.Enqueue("shell:AppsFolder\\" + aumid, () =>
        {
            try
            {
                var manager = (IApplicationActivationManager)new ApplicationActivationManagerCoClass();
                try
                {
                    var hr = manager.ActivateApplication(aumid, args, 0, out _);
                    if (hr >= 0)
                    {
                        return;
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

            Process.Start(new ProcessStartInfo("shell:AppsFolder\\" + aumid) { UseShellExecute = true })?.Dispose();
        });
        return new(LaunchOutcome.Started);
    }

    // ---------- Folders ----------

    private static LaunchResult LaunchFolder(SlotSettings slot)
    {
        var raw = slot.Path?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return Fail("No folder is set for this slot.");
        }

        string? resolved;
        if (LaunchHelpers.IsShellPath(raw))
        {
            if (!TryParseShellFolder(raw, out resolved))
            {
                return Fail("The folder couldn't be found: " + raw);
            }

            if (resolved is not null && !Directory.Exists(resolved))
            {
                return Fail("The folder couldn't be found: " + raw);
            }
        }
        else
        {
            resolved = LaunchHelpers.NormalizePath(raw);
            if (resolved is null || !Directory.Exists(resolved))
            {
                resolved = LaunchHelpers.NormalizePath(Environment.ExpandEnvironmentVariables(raw));
            }

            if (resolved is null || !Directory.Exists(resolved))
            {
                return Fail("The folder couldn't be found: " + raw);
            }
        }

        if (resolved is not null)
        {
            var hwnd = WindowFinder.FindExplorerWindow(resolved);
            if (hwnd != 0)
            {
                return SwitchTo(hwnd);
            }
        }

        var toOpen = resolved ?? raw;
        StartWorker.Enqueue(toOpen, () =>
            Process.Start(new ProcessStartInfo(ExplorerExe, "\"" + toOpen + "\"") { UseShellExecute = true })?.Dispose());
        return new(LaunchOutcome.Started);
    }

    /// <summary>
    /// Parses a "shell:" name. False if Windows doesn't know it. True with a path if it is a normal folder,
    /// or True with null for folders that have no file path (for example "shell:MyComputerFolder").
    /// </summary>
    internal static bool TryParseShellFolder(string shellPath, out string? fileSystemPath)
    {
        fileSystemPath = null;
        nint pidl = 0;
        nint name = 0;
        try
        {
            if (SHParseDisplayName(shellPath, 0, out pidl, 0, out _) < 0 || pidl == 0)
            {
                return false;
            }

            if (SHGetNameFromIDList(pidl, SIGDN_FILESYSPATH, out name) >= 0 && name != 0)
            {
                fileSystemPath = LaunchHelpers.NormalizePath(Marshal.PtrToStringUni(name));
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Resolving a shell folder failed: " + shellPath, ex);
            return false;
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

    /// <summary>Turns "shell:Downloads" into a real path. Null if Windows doesn't know it or it has no path.</summary>
    internal static string? ResolveShellFolder(string shellPath) =>
        TryParseShellFolder(shellPath, out var path) ? path : null;

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

    // ---------- Focus ----------

    /// <summary>Restores and raises a window, working around Windows' focus-stealing rules. True if it ended up in front.</summary>
    internal static bool FocusWindow(nint hwnd)
    {
        try
        {
            if (IsHungAppWindow(hwnd))
            {
                return false;
            }

            if (IsIconic(hwnd))
            {
                ShowWindowAsync(hwnd, SW_RESTORE);
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
            inputs[0].u.ki.dwExtraInfo = InjectionMarker;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_UNASSIGNED;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[1].u.ki.dwExtraInfo = InjectionMarker;
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
