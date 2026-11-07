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
/// Threading: the hooks, the state machine and the hold timer live on their own small "input" thread with its own
/// message loop. So if the UI thread is ever busy (starting an app, opening settings), keyboard and mouse input
/// never lags, and Windows never silently removes the hooks for being slow. Every event is posted to the UI thread;
/// no handler ever runs inside a hook. Calls from the UI (Configure, IsPaused, Suspend) are posted to the input thread.
/// </para>
/// <para>
/// Lightness: the keyboard hook is always on (it only wakes on key presses). The mouse hook, which would otherwise
/// wake Loadout on every mouse movement, is installed only while the hotkey is held or the wheel is open,
/// unless the hotkey itself uses a mouse button.
/// </para>
/// </summary>
public sealed class LowLevelInputService : IInputService
{
    private readonly Dispatcher _ui;
    private readonly Thread _thread;
    private readonly Dispatcher _input;
    private readonly InputCore _core;
    private bool _paused;
    private bool _disposed;

    public LowLevelInputService()
    {
        _ui = Dispatcher.CurrentDispatcher;
        Dispatcher? input = null;
        InputCore? core = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            input = Dispatcher.CurrentDispatcher;
            core = new InputCore(this, input);
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Loadout input",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        _input = input!;
        _core = core!;
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
            _input.BeginInvoke(() => _core.SetPaused(value));
        }
    }

    public bool IsWheelOpen => _core.IsOpen;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.Invoke(_core.Start);
    }

    public void Configure(HotkeySettings hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        var keys = hotkey.Keys?.ToArray() ?? [];
        var delay = hotkey.HoldDelayMs;
        _input.BeginInvoke(() => _core.Configure(keys, delay));
    }

    public IDisposable Suspend()
    {
        _input.BeginInvoke(() => _core.ChangeSuspensions(+1));
        return new Suspension(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _input.Invoke(_core.Stop, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Log.Error("Stopping the input thread failed.", ex);
        }

        _input.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    // ---- Raising events on the UI thread (called from the input thread) ----

    private void PostToUi(Action action) => _ui.BeginInvoke(DispatcherPriority.Input, action);

    private void RaiseOpenRequested(int openId, int x, int y) => PostToUi(() =>
    {
        var args = new OpenRequestedEventArgs(new ScreenPoint(x, y));
        try
        {
            OpenRequested?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Log.Error("Opening the wheel failed.", ex);
            args.Cancel = true;
        }

        var cancel = args.Cancel;
        var block = args.BlockMouse;
        _input.BeginInvoke(() => _core.ApplyOpenDecision(openId, cancel, block));
    });

    private void RaiseAim(double dx, double dy) => PostToUi(() => AimChanged?.Invoke(this, new AimChangedEventArgs(dx, dy)));

    private void RaiseGroupScrolled(int direction) => PostToUi(() => GroupScrolled?.Invoke(this, direction));

    private void RaiseSlotKey(int index) => PostToUi(() => SlotKeyPressed?.Invoke(this, index));

    private void RaiseReleased() => PostToUi(() => Released?.Invoke(this, EventArgs.Empty));

    private void RaiseCancelled() => PostToUi(() => Cancelled?.Invoke(this, EventArgs.Empty));

    private sealed class Suspension(LowLevelInputService owner) : IDisposable
    {
        private LowLevelInputService? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?._input.BeginInvoke(() => owner._core.ChangeSuspensions(-1));
        }
    }

    /// <summary>Everything that runs on the input thread: hooks, state machine, timer, injection.</summary>
    private sealed class InputCore : IHotkeyEffects
    {
        private const uint LLKHF_INJECTED = 0x10;
        private const uint LLMHF_INJECTED = 0x01;
        private const int MaxHookErrorLogs = 20;

        private readonly LowLevelInputService _owner;
        private readonly Dispatcher _dispatcher;
        private readonly HotkeyStateMachine _machine;
        private readonly LowLevelProc _keyboardProc;
        private readonly LowLevelProc _mouseProc;
        private readonly DispatcherTimer _holdTimer;
        private readonly Lock _aimGate = new();
        private nint _keyboardHook;
        private nint _mouseHook;
        private bool _started;
        private bool _paused;
        private int _suspensions;
        private bool _mouseHookSyncPosted;
        private volatile bool _isOpen;
        private int _hookErrors;

        private double _pendingAimX;
        private double _pendingAimY;
        private bool _aimPosted;

        public InputCore(LowLevelInputService owner, Dispatcher dispatcher)
        {
            _owner = owner;
            _dispatcher = dispatcher;
            _machine = new HotkeyStateMachine(this);
            _keyboardProc = KeyboardProc;
            _mouseProc = MouseProc;
            _holdTimer = new DispatcherTimer(DispatcherPriority.Input, dispatcher);
            _holdTimer.Tick += OnHoldTimerTick;
        }

        public bool IsOpen => _isOpen;

        public void Start()
        {
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

        public void Stop()
        {
            _holdTimer.Stop();
            if (_keyboardHook != 0)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = 0;
            }

            RemoveMouseHook();
            _started = false;
        }

        public void Configure(string[] keys, int holdDelayMs)
        {
            var definition = HotkeyDefinition.ParseOrDefault(keys, out var error);
            if (error is not null)
            {
                Log.Error($"The saved hotkey is invalid ({error}); using Ctrl + Alt.");
            }

            _machine.SetHotkey(definition);
            var delay = Math.Clamp(holdDelayMs, HotkeySettings.MinHoldDelayMs, HotkeySettings.MaxHoldDelayMs);
            _holdTimer.Interval = TimeSpan.FromMilliseconds(delay);
            AfterStateChange();
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            UpdateEnabled();
        }

        public void ChangeSuspensions(int delta)
        {
            _suspensions = Math.Max(0, _suspensions + delta);
            UpdateEnabled();
        }

        public void ApplyOpenDecision(int openId, bool cancel, bool blockMouse)
        {
            _machine.ApplyOpenDecision(openId, cancel, blockMouse);
            AfterStateChange();
        }

        // ---- Hook callbacks: keep these fast. ----

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
                        AfterStateChange();
                        if (swallow)
                        {
                            return 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHookError("Keyboard hook error.", ex);
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
                    var ours = (data.flags & LLMHF_INJECTED) != 0 && data.dwExtraInfo == InjectionMarker;
                    if (!ours)
                    {
                        var swallow = HandleMouse((int)wParam, data);
                        AfterStateChange();
                        if (swallow)
                        {
                            return 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHookError("Mouse hook error.", ex);
                }
            }

            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        private bool HandleMouse(int msg, in MSLLHOOKSTRUCT data)
        {
            switch (msg)
            {
                case WM_MOUSEMOVE:
                    return _machine.OnMouseMove(data.pt.X, data.pt.Y);
                case WM_MOUSEWHEEL:
                    return _machine.OnMouseWheel((short)(data.mouseData >> 16));
                case WM_MOUSEHWHEEL:
                    return _machine.OnMouseHorizontalWheel();
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                    return _machine.OnKey(VirtualKeys.LButton, msg == WM_LBUTTONDOWN);
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                    return _machine.OnKey(VirtualKeys.RButton, msg == WM_RBUTTONDOWN);
                case WM_MBUTTONDOWN:
                case WM_MBUTTONUP:
                    return _machine.OnKey(VirtualKeys.MButton, msg == WM_MBUTTONDOWN);
                case WM_XBUTTONDOWN:
                case WM_XBUTTONUP:
                    var button = (data.mouseData >> 16) == XBUTTON2 ? VirtualKeys.XButton2 : VirtualKeys.XButton1;
                    return _machine.OnKey(button, msg == WM_XBUTTONDOWN);
                default:
                    return false;
            }
        }

        // ---- IHotkeyEffects ----

        void IHotkeyEffects.StartHoldTimer()
        {
            _holdTimer.Stop();
            if (_holdTimer.Interval <= TimeSpan.Zero)
            {
                // No delay: open on the next turn of the input thread, after the hook callback has returned.
                _dispatcher.BeginInvoke(DispatcherPriority.Input, OnHoldElapsed);
            }
            else
            {
                _holdTimer.Start();
            }
        }

        void IHotkeyEffects.StopHoldTimer() => _holdTimer.Stop();

        bool IHotkeyEffects.IsPhysicallyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        (int X, int Y) IHotkeyEffects.GetCursor()
        {
            GetCursorPos(out var cursor);
            return (cursor.X, cursor.Y);
        }

        bool IHotkeyEffects.Inject(IReadOnlyList<InjectedKey> keys)
        {
            var inputs = new INPUT[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                inputs[i] = ToInput(keys[i]);
            }

            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
            {
                LogHookError($"Could not send input (error {Marshal.GetLastPInvokeError()}).", null);
                return false;
            }

            return true;
        }

        void IHotkeyEffects.OpenRequested(int openId, int x, int y) => _owner.RaiseOpenRequested(openId, x, y);

        void IHotkeyEffects.Released() => _owner.RaiseReleased();

        void IHotkeyEffects.Cancelled() => _owner.RaiseCancelled();

        void IHotkeyEffects.SlotKey(int index) => _owner.RaiseSlotKey(index);

        void IHotkeyEffects.GroupScrolled(int direction) => _owner.RaiseGroupScrolled(direction);

        void IHotkeyEffects.AimChanged(double dx, double dy)
        {
            // Mice can report 1000 moves a second; send only the latest position once per UI turn.
            lock (_aimGate)
            {
                _pendingAimX = dx;
                _pendingAimY = dy;
                if (_aimPosted)
                {
                    return;
                }

                _aimPosted = true;
            }

            _owner.PostToUi(() =>
            {
                double x, y;
                lock (_aimGate)
                {
                    _aimPosted = false;
                    x = _pendingAimX;
                    y = _pendingAimY;
                }

                _owner.AimChanged?.Invoke(_owner, new AimChangedEventArgs(x, y));
            });
        }

        // ---- Helpers ----

        private void OnHoldTimerTick(object? sender, EventArgs e)
        {
            _holdTimer.Stop();
            OnHoldElapsed();
        }

        private void OnHoldElapsed()
        {
            _machine.OnHoldElapsed();
            AfterStateChange();
        }

        private void UpdateEnabled()
        {
            _machine.Enabled = !_paused && _suspensions == 0;
            AfterStateChange();
        }

        /// <summary>Publishes the open state and keeps the mouse hook in step (installed on the next turn, never inside a hook).</summary>
        private void AfterStateChange()
        {
            _isOpen = _machine.State == HotkeyState.Open;
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
            if (!_started)
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

        /// <summary>Writes to the log file off the input thread, and at most a few times, so a broken hook can't flood the disk.</summary>
        private void LogHookError(string message, Exception? ex)
        {
            if (Interlocked.Increment(ref _hookErrors) <= MaxHookErrorLogs)
            {
                ThreadPool.QueueUserWorkItem(_ => Log.Error(message, ex));
            }
        }

        /// <summary>Copies a hook struct without allocating (hook callbacks run for every key press and mouse move).</summary>
        private static unsafe T ReadStruct<T>(nint pointer)
            where T : unmanaged => *(T*)pointer;

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

        private static INPUT MouseInput(uint flags, uint data) => new()
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = InjectionMarker },
            },
        };
    }
}
