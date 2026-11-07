// CONTRACT (owned by the lead). Implemented in src/Loadout/Input/.

using Loadout.Settings.Model;

namespace Loadout.Contracts;

/// <summary>A point in physical screen pixels (not WPF device-independent units).</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>Raised when the hotkey has been held for the hold delay.</summary>
public sealed class OpenRequestedEventArgs(ScreenPoint cursor) : EventArgs
{
    /// <summary>Cursor position when the wheel should open, in physical pixels.</summary>
    public ScreenPoint Cursor { get; } = cursor;

    /// <summary>Set to true to refuse opening (for example, game mode is off and a game is in front).</summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Set to true to stop mouse movement, clicks and scrolling from reaching other apps while the wheel is open.
    /// The cursor then stays where the wheel opened. Defaults to true.
    /// </summary>
    public bool BlockMouse { get; set; } = true;
}

/// <summary>How far the mouse has moved since the wheel opened, in physical pixels. Positive Y is down.</summary>
public sealed class AimChangedEventArgs(double dx, double dy) : EventArgs
{
    public double Dx { get; } = dx;
    public double Dy { get; } = dy;
}

/// <summary>
/// Watches the keyboard and mouse with low-level hooks (SPEC section 2).
/// Must be created and used on the UI thread; all events are raised on the UI thread.
/// Hook callbacks must return quickly, so event handlers must be fast.
/// </summary>
public interface IInputService : IDisposable
{
    /// <summary>Installs the hooks. Idempotent.</summary>
    void Start();

    /// <summary>Applies a new hotkey. Safe to call at any time.</summary>
    void Configure(HotkeySettings hotkey);

    /// <summary>When true, the hotkey does nothing (tray menu "Pause").</summary>
    bool IsPaused { get; set; }

    /// <summary>True between a successful <see cref="OpenRequested"/> and <see cref="Released"/> or <see cref="Cancelled"/>.</summary>
    bool IsWheelOpen { get; }

    /// <summary>The hotkey was held for the hold delay with no other key pressed.</summary>
    event EventHandler<OpenRequestedEventArgs>? OpenRequested;

    /// <summary>The mouse moved while the wheel is open. Values are totals since opening.</summary>
    event EventHandler<AimChangedEventArgs>? AimChanged;

    /// <summary>Mouse wheel while open: +1 = scroll down (next group), -1 = scroll up (previous group).</summary>
    event EventHandler<int>? GroupScrolled;

    /// <summary>Number key 1-8 while open. Value is the slot index 0-7.</summary>
    event EventHandler<int>? SlotKeyPressed;

    /// <summary>The hotkey was released while open: launch the highlighted slot.</summary>
    event EventHandler? Released;

    /// <summary>Esc was pressed while open: close without launching.</summary>
    event EventHandler? Cancelled;
}
