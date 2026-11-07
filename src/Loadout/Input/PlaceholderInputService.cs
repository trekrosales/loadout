// PLACEHOLDER: never raises events. Wave 1 (feature/hotkey) replaces it with the real hooks.

using Loadout.Contracts;
using Loadout.Settings.Model;

namespace Loadout.Input;

public sealed class PlaceholderInputService : IInputService
{
    public bool IsPaused { get; set; }

    public bool IsWheelOpen => false;

#pragma warning disable CS0067 // Events are never raised by the placeholder.
    public event EventHandler<OpenRequestedEventArgs>? OpenRequested;
    public event EventHandler<AimChangedEventArgs>? AimChanged;
    public event EventHandler<int>? GroupScrolled;
    public event EventHandler<int>? SlotKeyPressed;
    public event EventHandler? Released;
    public event EventHandler? Cancelled;
#pragma warning restore CS0067

    public void Start()
    {
    }

    public void Configure(HotkeySettings hotkey)
    {
    }

    public void Dispose()
    {
    }
}
