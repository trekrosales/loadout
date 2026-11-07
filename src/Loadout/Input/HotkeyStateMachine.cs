namespace Loadout.Input;

/// <summary>A key or mouse button event to inject, in order.</summary>
internal readonly record struct InjectedKey(int Vk, bool Up);

/// <summary>Everything the state machine asks the outside world to do. Implemented by the hook service and by tests.</summary>
internal interface IHotkeyEffects
{
    /// <summary>Start (or restart) the hold-delay timer. When it fires, call <see cref="HotkeyStateMachine.OnHoldElapsed"/>.</summary>
    void StartHoldTimer();

    void StopHoldTimer();

    /// <summary>Inject these key or button events, in order, marked so the hooks ignore them. Returns false if it failed.</summary>
    bool Inject(IReadOnlyList<InjectedKey> keys);

    /// <summary>True if the key is physically down right now (GetAsyncKeyState). Only asked for keys that were passed through.</summary>
    bool IsPhysicallyDown(int vk);

    /// <summary>The cursor position in physical pixels.</summary>
    (int X, int Y) GetCursor();

    /// <summary>
    /// The wheel is opening. Tell the UI (without waiting); it answers later through
    /// <see cref="HotkeyStateMachine.ApplyOpenDecision"/> with the same <paramref name="openId"/>.
    /// </summary>
    void OpenRequested(int openId, int x, int y);

    void Released();

    void Cancelled();

    void SlotKey(int index);

    void GroupScrolled(int direction);

    void AimChanged(double dx, double dy);
}

internal enum HotkeyState
{
    /// <summary>Waiting for the hotkey.</summary>
    Idle,

    /// <summary>The hotkey is held; the hold-delay timer is running.</summary>
    Arming,

    /// <summary>The wheel is open.</summary>
    Open,

    /// <summary>Done for this press (launched, cancelled or interrupted); waiting until every hotkey key is released.</summary>
    WaitRelease,
}

/// <summary>
/// Decides what every key, button, wheel and mouse movement means (SPEC section 2), with no Windows calls of its own,
/// so it can be unit-tested. Each On* method returns true when the input must be swallowed (kept from other apps).
/// Single-threaded: called only on the input thread, from the hook callbacks, the hold timer and posted work.
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>The hotkey arms when all its groups are held. Keys held <em>before</em> that (for example W in a game) don't matter.
/// With a trigger key (Ctrl + Q, a mouse button), only pressing the trigger last arms, like a normal shortcut.</item>
/// <item>Any new key or button pressed, or wheel scrolled, while arming cancels the attempt and that input goes through normally,
/// so shortcuts like Ctrl + Alt + T keep working.</item>
/// <item>The trigger key is held back while in use and replayed if the wheel never opens.</item>
/// <item>While open: movement is swallowed when BlockMouse; clicks, scrolling and newly pressed keys are always swallowed;
/// Esc cancels; 1-8 pick a slot; releasing the hotkey launches. Keys already held before opening keep passing through,
/// so their release always reaches the app.</item>
/// <item>After the wheel was open, releasing Alt or Win is masked so the app in front doesn't open its menu or Start.</item>
/// </list>
/// </summary>
internal sealed class HotkeyStateMachine
{
    public const int WheelNotch = 120;

    private readonly IHotkeyEffects _effects;
    private readonly HashSet<int> _down = [];
    private readonly HashSet<int> _swallowed = [];
    private readonly HashSet<int> _swallowUntilReleased = [];
    private readonly Func<int, bool> _isDown;
    private readonly Func<int, bool> _isHeldOrPhysical;
    private HotkeyDefinition _hotkey = HotkeyDefinition.Default;
    private HotkeyDefinition? _pendingHotkey;
    private bool _enabled = true;

    private bool _triggerHeldBack;
    private bool _openedThisPress;
    private bool _blockMouse;
    private int _openId;
    private int _lastX;
    private int _lastY;
    private double _aimX;
    private double _aimY;
    private int _wheelRemainder;

    public HotkeyStateMachine(IHotkeyEffects effects)
    {
        _effects = effects;
        _isDown = _down.Contains;
        _isHeldOrPhysical = vk => _down.Contains(vk) && (IsHeldBack(vk) || _effects.IsPhysicallyDown(vk));
    }

    public HotkeyState State { get; private set; } = HotkeyState.Idle;

    public HotkeyDefinition Hotkey => _hotkey;

    /// <summary>Whether the mouse hook is needed right now (always for a mouse-button hotkey, otherwise only while in use).</summary>
    public bool NeedsMouseHook => _enabled && (_hotkey.HasMouseButton || State is HotkeyState.Arming or HotkeyState.Open);

    /// <summary>When false (paused or suspended) everything passes through untouched.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            if (!value)
            {
                Interrupt();
                _enabled = false;
                return;
            }

            // Releases that happened while disabled were never seen: start clean.
            _enabled = true;
            ForgetKeysNotPhysicallyDown(except: -1);
            if (State == HotkeyState.WaitRelease && !AnyHotkeyKeyDown())
            {
                ResetToIdle();
            }
        }
    }

    /// <summary>Applies a new hotkey now if idle, otherwise as soon as the current press is over.</summary>
    public void SetHotkey(HotkeyDefinition hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (hotkey.Text == _hotkey.Text)
        {
            _pendingHotkey = null;
            return;
        }

        if (State == HotkeyState.Idle)
        {
            _hotkey = hotkey;
            _pendingHotkey = null;
            return;
        }

        if (State == HotkeyState.Arming)
        {
            Interrupt();
        }

        _pendingHotkey = hotkey;
        if (State == HotkeyState.Idle)
        {
            ApplyPendingHotkey();
        }
    }

    /// <summary>
    /// Stops whatever is in progress: cancels an open wheel, gives back a held trigger key,
    /// and waits for the hotkey to be released before arming again.
    /// </summary>
    public void Interrupt()
    {
        switch (State)
        {
            case HotkeyState.Arming:
                _effects.StopHoldTimer();
                GiveBackTrigger();
                State = HotkeyState.WaitRelease;
                break;
            case HotkeyState.Open:
                State = HotkeyState.WaitRelease;
                _effects.Cancelled();
                break;
        }

        if (State == HotkeyState.WaitRelease && !AnyHotkeyKeyDown())
        {
            ResetToIdle();
        }
    }

    /// <summary>A key or mouse button went down or up. Returns true to swallow it.</summary>
    public bool OnKey(int vk, bool down)
    {
        bool isRepeat;
        if (down)
        {
            isRepeat = !_down.Add(vk);
            if (!isRepeat)
            {
                _swallowUntilReleased.Remove(vk);
            }
        }
        else
        {
            isRepeat = false;
            _down.Remove(vk);
        }

        if (!_enabled)
        {
            return false;
        }

        // A key whose press was swallowed while the wheel was open, still held after it closed:
        // keep swallowing its repeats and its release so the app never sees half a key press.
        if (_swallowUntilReleased.Contains(vk) && (isRepeat || !down))
        {
            if (!down)
            {
                _swallowUntilReleased.Remove(vk);
            }

            return true;
        }

        return State switch
        {
            HotkeyState.Idle => down && !isRepeat && OnIdleKeyDown(vk),
            HotkeyState.Arming => OnArmingKey(vk, down, isRepeat),
            HotkeyState.Open => OnOpenKey(vk, down, isRepeat),
            _ => OnWaitReleaseKey(vk, down),
        };
    }

    /// <summary>The mouse wheel turned (positive = away from the user, i.e. scroll up). Returns true to swallow it.</summary>
    public bool OnMouseWheel(int delta)
    {
        if (!_enabled)
        {
            return false;
        }

        switch (State)
        {
            case HotkeyState.Arming:
                Spoil();
                return false;
            case HotkeyState.Open:
                _wheelRemainder += delta;
                while (Math.Abs(_wheelRemainder) >= WheelNotch)
                {
                    var up = _wheelRemainder > 0;
                    _effects.GroupScrolled(up ? -1 : +1);
                    _wheelRemainder -= up ? WheelNotch : -WheelNotch;
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>Horizontal wheel. Swallowed while open, otherwise passed.</summary>
    public bool OnMouseHorizontalWheel() => _enabled && State == HotkeyState.Open;

    /// <summary>The mouse moved to (x, y) in physical pixels. Returns true to swallow it.</summary>
    public bool OnMouseMove(int x, int y)
    {
        if (!_enabled || State != HotkeyState.Open)
        {
            return false;
        }

        // Safety net: if the hotkey's keys were released somewhere we couldn't see
        // (lock screen, an admin window), close instead of holding the mouse hostage.
        // Note: a held-back trigger (for example a mouse-button hotkey) always counts as held,
        // because its state can't be checked; Esc still cancels in that case.
        if (!_hotkey.IsSatisfied(_isHeldOrPhysical))
        {
            State = HotkeyState.WaitRelease;
            _effects.Cancelled();
            ForgetKeysNotPhysicallyDown(except: -1);
            if (!AnyHotkeyKeyDown())
            {
                ResetToIdle();
            }

            return false;
        }

        _aimX += x - _lastX;
        _aimY += y - _lastY;
        if (!_blockMouse)
        {
            _lastX = x;
            _lastY = y;
        }

        _effects.AimChanged(_aimX, _aimY);
        return _blockMouse;
    }

    /// <summary>The hold-delay timer fired: open the wheel (the UI confirms later via <see cref="ApplyOpenDecision"/>).</summary>
    public void OnHoldElapsed()
    {
        if (!_enabled || State != HotkeyState.Arming)
        {
            return;
        }

        if (!_hotkey.IsSatisfied(_isHeldOrPhysical))
        {
            // A release was missed; start over.
            GiveBackTrigger();
            ForgetKeysNotPhysicallyDown(except: -1);
            ResetToIdle();
            return;
        }

        var (x, y) = _effects.GetCursor();
        State = HotkeyState.Open;
        _openedThisPress = true;
        _blockMouse = true;
        _openId++;
        _lastX = x;
        _lastY = y;
        _aimX = _aimY = 0;
        _wheelRemainder = 0;
        _effects.OpenRequested(_openId, x, y);
    }

    /// <summary>
    /// The UI's answer to <see cref="IHotkeyEffects.OpenRequested"/>. Ignored if that open is already over.
    /// When refused, the press behaves as if the wheel never opened.
    /// </summary>
    public void ApplyOpenDecision(int openId, bool cancel, bool blockMouse)
    {
        if (openId != _openId || State != HotkeyState.Open)
        {
            return;
        }

        if (!cancel)
        {
            _blockMouse = blockMouse;
            return;
        }

        _openedThisPress = false;
        if (_hotkey.Trigger is { } trigger && _down.Contains(trigger))
        {
            _triggerHeldBack = true;
            GiveBackTrigger();
        }

        State = HotkeyState.WaitRelease;
        _swallowed.Clear();
        if (!AnyHotkeyKeyDown())
        {
            ResetToIdle();
        }
    }

    private bool OnIdleKeyDown(int vk)
    {
        if (!_hotkey.Contains(vk))
        {
            return false;
        }

        // With a trigger key, only pressing the trigger last arms (its earlier press already reached the app).
        if (_hotkey.Trigger is { } trigger && vk != trigger)
        {
            return false;
        }

        // Missed key-ups (for example while an admin window had focus) must not leave keys "stuck down".
        ForgetKeysNotPhysicallyDown(except: vk);

        if (!_hotkey.IsSatisfied(_isDown))
        {
            return false;
        }

        State = HotkeyState.Arming;
        _openedThisPress = false;
        _effects.StartHoldTimer();

        if (vk == _hotkey.Trigger)
        {
            _triggerHeldBack = true;
            return true;
        }

        return false;
    }

    private bool OnArmingKey(int vk, bool down, bool isRepeat)
    {
        if (down)
        {
            if (isRepeat)
            {
                return vk == _hotkey.Trigger && _triggerHeldBack;
            }

            if (_hotkey.Contains(vk))
            {
                return false;
            }

            Spoil();
            return false;
        }

        if (!_hotkey.Contains(vk) || _hotkey.IsSatisfied(_isDown))
        {
            return false;
        }

        // Released before the wheel opened: behave as if Loadout wasn't there.
        _effects.StopHoldTimer();
        var swallow = false;
        if (vk == _hotkey.Trigger && _triggerHeldBack)
        {
            swallow = _effects.Inject([new InjectedKey(vk, Up: false), new InjectedKey(vk, Up: true)]);
            _triggerHeldBack = false;
        }
        else
        {
            GiveBackTrigger();
        }

        ResetToIdle();
        return swallow;
    }

    private bool OnOpenKey(int vk, bool down, bool isRepeat)
    {
        if (down)
        {
            if (_hotkey.Contains(vk))
            {
                return vk == _hotkey.Trigger;
            }

            // Auto-repeat of a key held since before the wheel opened: its press reached the app,
            // so let it keep going and never swallow its release.
            if (isRepeat && !_swallowed.Contains(vk))
            {
                return false;
            }

            if (vk == VirtualKeys.Escape)
            {
                _swallowed.Add(vk);
                State = HotkeyState.WaitRelease;
                _effects.Cancelled();
                return true;
            }

            var slot = VirtualKeys.SlotIndexFor(vk);
            if (slot >= 0 && !isRepeat)
            {
                _effects.SlotKey(slot);
            }

            // Newly pressed keys are kept from other apps while the wheel is open.
            _swallowed.Add(vk);
            return true;
        }

        if (_swallowed.Remove(vk))
        {
            return true;
        }

        if (!_hotkey.Contains(vk) || _hotkey.IsSatisfied(_isDown))
        {
            return false;
        }

        State = HotkeyState.WaitRelease;
        _effects.Released();
        var swallowRelease = ReleaseHotkeyKeyAfterOpen(vk);
        if (!AnyHotkeyKeyDown())
        {
            ResetToIdle();
        }

        return swallowRelease;
    }

    private bool OnWaitReleaseKey(int vk, bool down)
    {
        if (down)
        {
            ForgetKeysNotPhysicallyDown(except: vk);

            // Keep holding back trigger auto-repeat after the wheel was used.
            var swallowDown = vk == _hotkey.Trigger && _openedThisPress;
            if (!AnyHotkeyKeyDown())
            {
                ResetToIdle();
            }

            return swallowDown;
        }

        var swallow = _swallowed.Remove(vk);
        if (!swallow && _hotkey.Contains(vk))
        {
            if (_openedThisPress)
            {
                swallow = ReleaseHotkeyKeyAfterOpen(vk);
            }
            else if (vk == _hotkey.Trigger && _triggerHeldBack)
            {
                swallow = _effects.Inject([new InjectedKey(vk, Up: false), new InjectedKey(vk, Up: true)]);
                _triggerHeldBack = false;
            }
        }

        if (!AnyHotkeyKeyDown())
        {
            ResetToIdle();
        }

        return swallow;
    }

    /// <summary>After the wheel was open: the trigger's release is swallowed (its press was), Alt and Win are masked.</summary>
    private bool ReleaseHotkeyKeyAfterOpen(int vk)
    {
        if (vk == _hotkey.Trigger)
        {
            return true;
        }

        if (VirtualKeys.NeedsReleaseMask(vk))
        {
            // Only swallow the real release if the masked replacement was actually sent.
            return _effects.Inject(
            [
                new InjectedKey(VirtualKeys.MenuMask, Up: false),
                new InjectedKey(VirtualKeys.MenuMask, Up: true),
                new InjectedKey(vk, Up: true),
            ]);
        }

        return false;
    }

    /// <summary>Another input arrived while arming: give up on this press and let everything through.</summary>
    private void Spoil()
    {
        _effects.StopHoldTimer();
        GiveBackTrigger();
        State = HotkeyState.WaitRelease;
    }

    /// <summary>If the trigger's press was held back, send it now so the app in front sees it in order.</summary>
    private void GiveBackTrigger()
    {
        if (_triggerHeldBack && _hotkey.Trigger is { } trigger && _down.Contains(trigger))
        {
            _effects.Inject([new InjectedKey(trigger, Up: false)]);
        }

        _triggerHeldBack = false;
    }

    private bool IsHeldBack(int vk) =>
        (vk == _hotkey.Trigger && (_triggerHeldBack || _openedThisPress)) || _swallowed.Contains(vk);

    private bool AnyHotkeyKeyDown()
    {
        foreach (var vk in _down)
        {
            if (_hotkey.Contains(vk))
            {
                return true;
            }
        }

        return false;
    }

    private void ForgetKeysNotPhysicallyDown(int except)
    {
        _down.RemoveWhere(vk => vk != except && !IsHeldBack(vk) && !_effects.IsPhysicallyDown(vk));
    }

    private void ResetToIdle()
    {
        State = HotkeyState.Idle;
        _triggerHeldBack = false;
        _openedThisPress = false;

        // Keys swallowed while open and still held: keep swallowing their repeats and release.
        foreach (var vk in _swallowed)
        {
            if (_down.Contains(vk))
            {
                _swallowUntilReleased.Add(vk);
            }
        }

        _swallowed.Clear();
        _wheelRemainder = 0;
        ApplyPendingHotkey();
    }

    private void ApplyPendingHotkey()
    {
        if (_pendingHotkey is not null)
        {
            _hotkey = _pendingHotkey;
            _pendingHotkey = null;
        }
    }
}
