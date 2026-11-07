// PLACEHOLDER: opens nothing. Wave 2 (feature/launcher) replaces it.

using Loadout.Contracts;
using Loadout.Settings.Model;

namespace Loadout.Launching;

public sealed class PlaceholderLauncher : ILauncher
{
    public LaunchResult Launch(SlotSettings slot) =>
        new(LaunchOutcome.Failed, "Launching is not built yet.");
}
