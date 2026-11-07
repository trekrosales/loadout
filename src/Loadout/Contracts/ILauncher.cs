// CONTRACT (owned by the lead). Implemented in src/Loadout/Launching/.

using Loadout.Settings.Model;

namespace Loadout.Contracts;

public enum LaunchOutcome
{
    /// <summary>A new process or window was started.</summary>
    Started,

    /// <summary>An existing window was brought to the front.</summary>
    SwitchedToExisting,

    /// <summary>Nothing could be opened (missing file, bad URL, access denied). See the message.</summary>
    Failed,
}

public readonly record struct LaunchResult(LaunchOutcome Outcome, string? Message = null);

/// <summary>
/// Opens what a slot points at (SPEC sections 3.2 and 3.3).
/// Called on the UI thread right after the wheel closes; must not block for long
/// (start processes without waiting for them). Never throws: failures return <see cref="LaunchOutcome.Failed"/>.
/// Must never make network requests itself: websites are handed to the default browser.
/// </summary>
public interface ILauncher
{
    LaunchResult Launch(SlotSettings slot);
}
