// PLACEHOLDER: never touches the registry. Wave 3 (feature/settings) replaces it.

using Loadout.Contracts;

namespace Loadout.Startup;

public sealed class PlaceholderStartupRegistration : IStartupRegistration
{
    public bool IsEnabled => false;

    public bool SetEnabled(bool enabled) => false;
}
