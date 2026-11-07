// CONTRACT (owned by the lead). Implemented in src/Loadout/Settings/Store/.

using Loadout.Settings.Model;

namespace Loadout.Contracts;

/// <summary>
/// Loads, holds and saves settings.json (SPEC section 10).
/// All members are called on the UI thread.
/// </summary>
public interface ISettingsStore
{
    /// <summary>The live settings. Never null after <see cref="Load"/>.</summary>
    LoadoutSettings Current { get; }

    /// <summary>
    /// Reads settings.json into <see cref="Current"/>. A missing file gives defaults.
    /// A damaged or invalid file is renamed to settings.broken.json, logged, and defaults are used.
    /// Out-of-range values are clamped and missing groups or slots are filled in, so
    /// <see cref="Current"/> always satisfies the model's documented rules.
    /// Never throws.
    /// </summary>
    void Load();

    /// <summary>
    /// Writes <see cref="Current"/> to disk atomically (write a temp file, then replace),
    /// then raises <see cref="Changed"/>. Call after changing anything in <see cref="Current"/>.
    /// </summary>
    void Save();

    /// <summary>Replaces all settings (used by reset and import), saves, and raises <see cref="Changed"/>.</summary>
    void Replace(LoadoutSettings settings);

    /// <summary>Writes the current settings to <paramref name="filePath"/>.</summary>
    void Export(string filePath);

    /// <summary>
    /// Reads settings from <paramref name="filePath"/>, validates them like <see cref="Load"/>,
    /// and on success calls <see cref="Replace"/>. Returns false (and changes nothing) if the file is unreadable.
    /// </summary>
    bool Import(string filePath);

    /// <summary>Raised after settings were saved or replaced.</summary>
    event EventHandler? Changed;
}
