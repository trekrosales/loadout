// CONTRACT (owned by the lead). Implemented in src/Loadout/GameMode/.

namespace Loadout.Contracts;

/// <summary>Decides whether the wheel may open over the app in front (SPEC section 7).</summary>
public interface IGameModePolicy
{
    /// <summary>
    /// Called on the UI thread from inside the keyboard hook, so it must take well under 1 ms.
    /// Returns false when a fullscreen app is in front and game mode is off, or when an
    /// exclusive-fullscreen app is in front.
    /// </summary>
    bool CanOpenNow();

    /// <summary>
    /// Whether to block mouse input from reaching other apps while the wheel is open.
    /// True on the desktop; in a fullscreen app it follows the "block mouse in game" setting.
    /// </summary>
    bool ShouldBlockMouse();
}
