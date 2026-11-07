// CONTRACT (owned by the lead). Implemented in src/Loadout/Settings/Window/.

namespace Loadout.Contracts;

/// <summary>The settings window (SPEC section 5). Called on the UI thread.</summary>
public interface ISettingsWindow
{
    /// <summary>
    /// Shows the window, or brings it to the front if already open.
    /// The window is created on demand and fully released when closed, then calls Core.MemoryTrimmer.Trim (SPEC section 8).
    /// It must never appear in the taskbar or Alt + Tab: call Core.WindowStyles.HideFromTaskSwitchers on it.
    /// </summary>
    void Show();
}
