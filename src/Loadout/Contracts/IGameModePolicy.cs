// CONTRACT (owned by the lead). Implemented in src/Loadout/GameMode/.

namespace Loadout.Contracts;

/// <summary>Decides whether the wheel may open over the app in front (SPEC section 7).</summary>
public interface IGameModePolicy
{
    /// <summary>
    /// Called on the UI thread when the hold delay has elapsed, just before the wheel opens.
    /// The user is waiting for the wheel, so it must take well under 1 ms.
    /// Returns false when a fullscreen app is in front and game mode is off, or when an
    /// exclusive-fullscreen app is in front.
    /// </summary>
    bool CanOpenNow();

    /// <summary>
    /// Whether to keep mouse <em>movement</em> from reaching other apps while the wheel is open
    /// (clicks and scrolling are always blocked). True on the desktop; in a fullscreen app it follows
    /// the "block mouse in game" setting. Called right after <see cref="CanOpenNow"/>; same speed rule.
    /// </summary>
    bool ShouldBlockMouse();
}
