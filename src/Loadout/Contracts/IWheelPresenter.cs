// CONTRACT (owned by the lead). Implemented in src/Loadout/Wheel/.

using Loadout.Settings.Model;

namespace Loadout.Contracts;

/// <summary>
/// Shows the wheel and tracks which slot is highlighted (SPEC sections 2 and 4).
/// Reads groups and appearance from <see cref="ISettingsStore"/> and icons from <see cref="IIconProvider"/>.
/// All members are called on the UI thread and must be fast.
/// </summary>
public interface IWheelPresenter
{
    bool IsOpen { get; }

    /// <summary>Shows the wheel centered on <paramref name="cursor"/> (nudged to stay on screen), on the last used group.</summary>
    void Open(ScreenPoint cursor);

    /// <summary>Updates the highlight from the total mouse movement since opening.</summary>
    void UpdateAim(double dx, double dy);

    /// <summary>+1 = next group, -1 = previous group, wrapping around.</summary>
    void ChangeGroup(int delta);

    /// <summary>Highlights slot <paramref name="index"/> (0-7) directly, as with number keys.</summary>
    void HighlightSlot(int index);

    /// <summary>
    /// Hides the wheel (animating if enabled) and returns at once.
    /// When <paramref name="launch"/> is true, returns the highlighted filled slot and records it as last launched;
    /// returns null for Cancel, an empty slot, or when <paramref name="launch"/> is false.
    /// </summary>
    SlotSettings? Close(bool launch);
}
