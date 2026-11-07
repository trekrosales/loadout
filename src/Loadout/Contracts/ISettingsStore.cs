// CONTRACT (owned by the lead). Implemented in src/Loadout/Settings/Store/.

using Loadout.Settings.Model;

namespace Loadout.Contracts;

/// <summary>
/// Loads, holds and saves settings.json (SPEC section 10).
/// All members are called on the UI thread. Read and write the file only with
/// <see cref="SettingsJson.Options"/> (or <see cref="SettingsJson.Serialize"/> and <see cref="SettingsJson.Deserialize"/>).
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
    /// Call after changing anything in <see cref="Current"/>. Raises <see cref="Changed"/> immediately,
    /// but writes to disk at most once per 400 ms (debounced, using a one-shot dispatcher timer, not polling),
    /// so dragging a slider doesn't write the file constantly.
    /// Writes are atomic: write a temp file in the same folder, then replace.
    /// </summary>
    void Save();

    /// <summary>Writes any pending debounced change to disk now. Called on exit. Never throws.</summary>
    void Flush();

    /// <summary>Replaces all settings (used by reset and import), saves, and raises <see cref="Changed"/>.</summary>
    void Replace(LoadoutSettings settings);

    /// <summary>Writes the current settings to <paramref name="filePath"/> immediately.</summary>
    void Export(string filePath);

    /// <summary>
    /// Reads settings from <paramref name="filePath"/>, validates them like <see cref="Load"/>,
    /// and on success calls <see cref="Replace"/>. Returns false (and changes nothing) if the file is unreadable.
    /// </summary>
    bool Import(string filePath);

    /// <summary>
    /// Raised after <see cref="Save"/> or <see cref="Replace"/>. Listeners must be cheap, or check
    /// what actually changed before doing expensive work (for example, the host only re-warms icons when slots or icon size changed).
    /// </summary>
    event EventHandler? Changed;
}
