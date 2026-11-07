using System.Runtime.InteropServices;
using System.Windows.Threading;
using Loadout.Contracts;
using Loadout.Core;
using Loadout.Settings.Model;
using static Loadout.Input.InputNative;

namespace Loadout.Input;

/// <summary>
/// The real <see cref="IInputService"/>: low-level keyboard and mouse hooks feeding <see cref="HotkeyStateMachine"/>.
/// <para>
/// Lightness: the keyboard hook is always on (it only wakes on key presses). The mouse hook, which would otherwise
/// wake Loadout on every mouse movement, is installed only while the hotkey is held or the wheel is open,
/// unless the hotkey itself uses a mouse button.
/// </para>
/// <para>
/// Threading: hooks run on the UI thread. Callbacks only update the state machine and decide what to swallow;
/// every event is posted to the dispatcher, so no handler ever runs inside a hook.
/// </para>
/// </summary>
public sealed class LowLevelInputService : IInputService, IHotkeyEffects
{
    private const uint LLKHF_INJECTED = 0x10;

    private readonly Dispatcher _dispatcher;
    private readonly HotkeyStateMachine _machine;
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private readonly DispatcherTimer _holdTimer;
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _started;
    private bool _disposed;
    private bool _paused;
    private int _suspensions;
    private bool _mouseHookSyncPosted;

    private double _pendingAimX;
    private double _pendingAimY;
    private bool _aimPosted;

    public LowLevelInputService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _machine = new HotkeyStateMachine(this);
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        _holdTimer = new DispatcherTimer(DispatcherPriority.Input, _dispatcher);
        _holdTimer.Tick += OnHoldTimerTick;
        Configure(new HotkeySettings());
    }

    public event EventHandler<OpenRequestedEventArgs>? OpenRequested;
    public event EventHandler<AimChangedEventArgs>? AimChanged;
    public event EventHandler<int>? GroupScrolled;
    public event EventHandler<int>? SlotKeyPressed;
    public event EventHandler? Released;
    public event EventHandler? Cancelled;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            UpdateEnabled();
        }
    }

    public bool IsWheelOpen => _machine.State == HotkeyState.Open;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
        if (_keyboardHook == 0)
        {
            Log.Error($"Could not install the keyboard hook (error {Marshal.GetLastPInvokeError()}). The hotkey won't work.");
        }

        _started = true;
        SyncMouseHook();
    }

    public void Configure(HotkeySettings hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        var definition = HotkeyDefinition.ParseOrDefault(hotkey.Keys, out var error);
        if (error is not null)
        {
            Log.Error($"The saved hotkey is invalid ({error}); using Ctrl + Alt.");
        }

        _machine.SetHotkey(definition);
        var delay = Math.Clamp(hotkey.HoldDelayMs, HotkeySettings.MinHoldDelayMs, HotkeySettings.MaxHoldDelayMs);
        _holdTimer.Interval = TimeSpan.FromMilliseconds(delay);
        SyncMouseHook();
    }

    public IDisposable Suspend()
    {
        _suspensions++;
        UpdateEnabled();
        return new Suspension(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _holdTimer.Stop();
        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }

        RemoveMouseHook();
    }

    // ---- Hook callbacks: keep these fast, never raise events from here. ----

    private nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                var data = ReadStruct<KBDLLHOOKSTRUCT>(lParam);
                var ours = (data.flags & LLKHF_INJECTED) != 0 && data.dwExtraInfo == InjectionMarker;
                if (!ours)
                {
                    var msg = (int)wParam;
                    var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                    var swallow = _machine.OnKey((int)data.vkCode, down);
                    SyncMouseHookSoon();
                    if (swallow)
                    {
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard hook error.", ex);
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                var data = ReadStruct<MSLLHOOKSTRUCT>(lParam);
                if (data.dwExtraInfo != InjectionMarker && HandleMouse((int)wParam, data))
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Mouse hook error.", ex);
            }
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private bool HandleMouse(int msg, in MSLLHOOKSTRUCT data)
    {
        bool swallow;
        switch (msg)
        {
            case WM_MOUSEMOVE:
                return _machine.OnMouseMove(data.pt.X, data.pt.Y);
            case WM_MOUSEWHEEL:
                swallow = _machine.OnMouseWheel((short)(data.mouseData >> 16));
                break;
            case WM_MOUSEHWHEEL:
                return _machine.OnMouseHorizontalWheel();
            case WM_LBUTTONDOWN:
            case WM_LBUTTONUP:
                swallow = _machine.OnKey(VirtualKeys.LButton, msg == WM_LBUTTONDOWN);
                break;
            case WM_RBUTTONDOWN:
            case WM_RBUTTONUP:
                swallow = _machine.OnKey(VirtualKeys.RButton, msg == WM_RBUTTONDOWN);
                break;
            case WM_MBUTTONDOWN:
            case WM_MBUTTONUP:
                swallow = _machine.OnKey(VirtualKeys.MButton, msg == WM_MBUTTONDOWN);
                break;
            case WM_XBUTTONDOWN:
            case WM_XBUTTONUP:
                var button = (data.mouseData >> 16) == XBUTTON2 ? VirtualKeys.XButton2 : VirtualKeys.XButton1;
                swallow = _machine.OnKey(button, msg == WM_XBUTTONDOWN);
                break;
            default:
                return false;
        }

        SyncMouseHookSoon();
        return swallow;
    }

    // ---- IHotkeyEffects ----

    void IHotkeyEffects.StartHoldTimer()
    {
        _holdTimer.Stop();
        if (_holdTimer.Interval <= TimeSpan.Zero)
        {
            // No delay: open on the next dispatcher turn, after the hook callback has returned.
            _dispatcher.BeginInvoke(DispatcherPriority.Input, _machine.OnHoldElapsed);
        }
        else
        {
            _holdTimer.Start();
        }
    }

    void IHotkeyEffects.StopHoldTimer() => _holdTimer.Stop();

    bool IHotkeyEffects.IsPhysicallyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    void IHotkeyEffects.Inject(IReadOnlyList<InjectedKey> keys)
    {
        var inputs = new INPUT[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            inputs[i] = ToInput(keys[i]);
        }

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            Log.Error($"Could not send input (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    OpenDecision IHotkeyEffects.RequestOpen()
    {
        GetCursorPos(out var cursor);
        var args = new OpenRequestedEventArgs(new ScreenPoint(cursor.X, cursor.Y));
        try
        {
            OpenRequested?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Log.Error("Opening the wheel failed.", ex);
            args.Cancel = true;
        }

        return new OpenDecision(!args.Cancel, args.BlockMouse, cursor.X, cursor.Y);
    }

    void IHotkeyEffects.Released() => Post(() => Released?.Invoke(this, EventArgs.Empty));

    void IHotkeyEffects.Cancelled() => Post(() => Cancelled?.Invoke(this, EventArgs.Empty));

    void IHotkeyEffects.SlotKey(int index) => Post(() => SlotKeyPressed?.Invoke(this, index));

    void IHotkeyEffects.GroupScrolled(int direction) => Post(() => GroupScrolled?.Invoke(this, direction));

    void IHotkeyEffects.AimChanged(double dx, double dy)
    {
        // Mice can report 1000 moves a second; send only the latest position once per dispatcher turn.
        _pendingAimX = dx;
        _pendingAimY = dy;
        if (_aimPosted)
        {
            return;
        }

        _aimPosted = true;
        Post(() =>
        {
            _aimPosted = false;
            AimChanged?.Invoke(this, new AimChangedEventArgs(_pendingAimX, _pendingAimY));
        });
    }

    // ---- Helpers ----

    private void OnHoldTimerTick(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        _machine.OnHoldElapsed();
        SyncMouseHook();
    }

    private void Post(Action action) => _dispatcher.BeginInvoke(DispatcherPriority.Input, action);

    private void UpdateEnabled()
    {
        _machine.Enabled = !_paused && _suspensions == 0;
        SyncMouseHook();
    }

    private void Resume()
    {
        if (_suspensions > 0)
        {
            _suspensions--;
        }

        UpdateEnabled();
    }

    /// <summary>Installing or removing a hook from inside a hook callback is avoided; do it on the next turn.</summary>
    private void SyncMouseHookSoon()
    {
        if (_mouseHookSyncPosted || _machine.NeedsMouseHook == (_mouseHook != 0))
        {
            return;
        }

        _mouseHookSyncPosted = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            _mouseHookSyncPosted = false;
            SyncMouseHook();
        });
    }

    private void SyncMouseHook()
    {
        if (_disposed || !_started)
        {
            return;
        }

        if (_machine.NeedsMouseHook && _mouseHook == 0)
        {
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_mouseHook == 0)
            {
                Log.Error($"Could not install the mouse hook (error {Marshal.GetLastPInvokeError()}).");
            }
        }
        else if (!_machine.NeedsMouseHook && _mouseHook != 0)
        {
            RemoveMouseHook();
        }
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    private static INPUT ToInput(InjectedKey key)
    {
        switch (key.Vk)
        {
            case VirtualKeys.MButton:
                return MouseInput(key.Up ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_MIDDLEDOWN, 0);
            case VirtualKeys.XButton1:
                return MouseInput(key.Up ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN, XBUTTON1);
            case VirtualKeys.XButton2:
                return MouseInput(key.Up ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN, XBUTTON2);
        }

        var scan = MapVirtualKey((uint)key.Vk, MAPVK_VK_TO_VSC_EX);
        var flags = key.Up ? KEYEVENTF_KEYUP : 0;
        if ((scan & 0xFF00) is 0xE000 or 0xE100)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)key.Vk,
                    wScan = (ushort)(scan & 0xFF),
                    dwFlags = flags,
                    dwExtraInfo = InjectionMarker,
                },
            },
        };
    }

    /// <summary>Copies a hook struct without allocating (hook callbacks run for every key press and mouse move).</summary>
    private static unsafe T ReadStruct<T>(nint pointer)
        where T : unmanaged => *(T*)pointer;

    private static INPUT MouseInput(uint flags, uint data) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion
        {
            mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = InjectionMarker },
        },
    };

    private sealed class Suspension(LowLevelInputService owner) : IDisposable
    {
        private LowLevelInputService? _owner = owner;

        public void Dispose()
        {
            _owner?.Resume();
            _owner = null;
        }
    }
}
