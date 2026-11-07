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
    /// Whether mouse <em>movement</em> is kept from reaching other apps while the wheel is open; the cursor then stays
    /// where the wheel opened. Defaults to true. False is only used in games when "block mouse in game" is off (SPEC 7).
    /// Clicks and scrolling are <em>always</em> kept from other apps while the wheel is open (SPEC 2.2, 2.3), whatever this says.
    /// </summary>
    public bool BlockMouse { get; set; } = true;
}

/// <summary>How far the mouse has moved since the wheel opened, in physical pixels. Positive Y is down.</summary>
public sealed class AimChangedEventArgs(double dx, double dy) : EventArgs
{
    /// <summary>Total horizontal movement since opening (not since the previous event).</summary>
    public double Dx { get; } = dx;

    /// <summary>Total vertical movement since opening (not since the previous event).</summary>
    public double Dy { get; } = dy;
}

/// <summary>
/// Watches the keyboard and mouse with low-level hooks (SPEC section 2).
/// Must be created and used on the UI thread.
/// <para>
/// Threading rule: hook callbacks only record state and decide whether to swallow the input. They never run
/// event handlers. Events are raised on the UI thread <em>after</em> the hook callback has returned
/// (posted with Dispatcher.BeginInvoke, or raised from a dispatcher timer), so handlers may take a little time
/// without lagging the keyboard. The one synchronous answer, <see cref="OpenRequestedEventArgs.Cancel"/> and
/// <see cref="OpenRequestedEventArgs.BlockMouse"/>, is read when <see cref="OpenRequested"/> returns, which happens
/// when the hold-delay timer fires, not inside a hook.
/// </para>
/// </summary>
public interface IInputService : IDisposable
{
    /// <summary>Installs the hooks. Idempotent.</summary>
    void Start();

    /// <summary>Applies a new hotkey. Safe to call at any time; cheap if nothing changed.</summary>
    void Configure(HotkeySettings hotkey);

    /// <summary>When true, the hotkey does nothing (tray menu "Pause"). Owned by the tray menu only.</summary>
    bool IsPaused { get; set; }

    /// <summary>
    /// Temporarily makes the hotkey do nothing and swallows nothing, without touching <see cref="IsPaused"/>.
    /// Used by the settings window while recording a new hotkey. Suspensions nest: input resumes when every
    /// returned handle is disposed. If the wheel is open, it is cancelled first.
    /// </summary>
    IDisposable Suspend();

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
