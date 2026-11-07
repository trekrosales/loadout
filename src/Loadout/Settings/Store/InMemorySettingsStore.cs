// PLACEHOLDER: keeps settings in memory only. Wave 1 (feature/settings-store) adds JsonSettingsStore.
// Kept afterwards for tests.

using Loadout.Contracts;
using Loadout.Settings.Model;

namespace Loadout.Settings.Store;

public sealed class InMemorySettingsStore : ISettingsStore
{
    public LoadoutSettings Current { get; private set; } = LoadoutSettings.CreateDefault();

    public event EventHandler? Changed;

    public void Load() => Current = LoadoutSettings.CreateDefault();

    public void Save() => Changed?.Invoke(this, EventArgs.Empty);

    public void Flush()
    {
    }

    public void Replace(LoadoutSettings settings)
    {
        Current = settings;
        Save();
    }

    public void Export(string filePath)
    {
    }

    public bool Import(string filePath) => false;
}
