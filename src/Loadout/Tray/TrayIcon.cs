using System.Windows.Interop;
using Loadout.Core;
using static Loadout.Tray.TrayNative;

namespace Loadout.Tray;

/// <summary>
/// The system tray icon and its menu (SPEC section 6), using the Windows tray API directly.
/// Left-click opens settings; right-click shows Settings, Pause / Resume and Exit.
/// Owns a hidden tool window that receives tray messages; it never appears in the taskbar or Alt + Tab.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = WM_APP + 1;
    private const int IconId = 1;

    private const int CmdSettings = 1;
    private const int CmdPause = 2;
    private const int CmdExit = 3;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly HwndSource _window;
    private readonly int _taskbarCreatedMessage;
    private nint _icon;
    private bool _ownsIcon;
    private bool _added;
    private bool _paused;
    private bool _disposed;

    public TrayIcon()
    {
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var parameters = new HwndSourceParameters("Loadout.TrayWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        };
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);
    }

    /// <summary>Left-click, or "Settings" in the menu.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>"Pause" or "Resume" in the menu. The argument is the new paused state.</summary>
    public event EventHandler<bool>? PauseToggled;

    /// <summary>"Exit" in the menu.</summary>
    public event EventHandler? ExitRequested;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (_added)
            {
                var data = NewData(NIF_TIP | NIF_SHOWTIP);
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
        }
    }

    /// <summary>Adds the icon to the tray (or refreshes it if the shell still has it).</summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon == 0)
        {
            (_icon, _ownsIcon) = LoadTrayIcon();
        }

        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added)
        {
            // The shell may still know the icon (for example after a taskbar rebuild message): update it instead.
            _added = Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        if (!_added)
        {
            Log.Error("Could not add the tray icon.");
            return;
        }

        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    /// <summary>Shows a Windows notification from the tray icon. Returns false if it could not be sent.</summary>
    public bool ShowNotification(string title, string text)
    {
        if (!_added)
        {
            return false;
        }

        var data = NewData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_INFO | NIIF_RESPECT_QUIET_TIME;
        return Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != 0 && _ownsIcon)
        {
            DestroyIcon(_icon);
        }

        _icon = 0;

        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private NOTIFYICONDATAW NewData(int flags) => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _paused ? "Loadout (paused)" : "Loadout",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // With NOTIFYICON_VERSION_4 the event is in the low word of lParam.
            var trayEvent = (int)(lParam & 0xFFFF);
            switch (trayEvent)
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                case NIN_BALLOONUSERCLICK:
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case WM_CONTEXTMENU:
                    // With NOTIFYICON_VERSION_4, wParam holds the anchor point (x low word, y high word),
                    // which is right for both mouse and keyboard (Shift+F10) opening.
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    break;
            }

            handled = true;
        }
        else if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // The taskbar was rebuilt (for example Explorer restarted): add the icon again.
            _added = false;
            Show();
        }

        return 0;
    }

    private void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MF_STRING, CmdSettings, "Settings");
            AppendMenu(menu, MF_STRING, CmdPause, _paused ? "Resume" : "Pause");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdExit, "Exit");
            SetMenuDefaultItem(menu, CmdSettings, 0);

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_window.Handle);
            var command = TrackPopupMenuEx(
                menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, _window.Handle, 0);
            PostMessage(_window.Handle, WM_NULL, 0, 0);

            switch (command)
            {
                case CmdSettings:
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdPause:
                    IsPaused = !_paused;
                    PauseToggled?.Invoke(this, _paused);
                    break;
                case CmdExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>Returns the icon handle and whether this class owns it (and must destroy it).</summary>
    private static (nint Icon, bool Owned) LoadTrayIcon()
    {
        var dpi = GetDpiForSystem();
        var cx = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        var cy = GetSystemMetricsForDpi(SM_CYSMICON, dpi);

        var exe = Environment.ProcessPath;
        if (exe is not null
            && PrivateExtractIcons(exe, 0, cx, cy, out var icon, out _, 1, 0) == 1
            && icon != 0)
        {
            return (icon, true);
        }

        Log.Error("Could not load the Loadout icon; using the default application icon.");
        return (LoadIcon(0, IDI_APPLICATION), false);
    }
}
