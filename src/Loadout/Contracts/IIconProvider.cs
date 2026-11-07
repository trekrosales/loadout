// CONTRACT (owned by the lead). Implemented in src/Loadout/Icons/.

using System.Windows.Media;
using Loadout.Settings.Model;

namespace Loadout.Contracts;

/// <summary>
/// Supplies slot icons (SPEC section 3.4). Called on the UI thread.
/// Never downloads anything: website icons are first-letter circles unless a custom image is set.
/// <para>
/// Size rule: callers always pass the icon size in WPF device-independent pixels (DIPs), which is
/// <see cref="AppearanceSettings.IconSize"/>. The provider always renders and caches at
/// <c>2 × iconSize</c> physical pixels, so one cached image stays sharp up to 200% display scaling,
/// and the same cache entry serves every monitor.
/// </para>
/// </summary>
public interface IIconProvider
{
    /// <summary>
    /// Returns a frozen image for the slot (rendered at 2 × <paramref name="iconSize"/> pixels).
    /// Never null and never throws: falls back to a generic icon if extraction fails.
    /// Must be fast when the icon is already cached on disk, because it is called while the wheel opens.
    /// </summary>
    ImageSource GetIcon(SlotSettings slot, int iconSize);

    /// <summary>
    /// Prepares and disk-caches icons for these slots ahead of time, so opening the wheel is instant.
    /// Called at startup and after slots or icon size change. May do work in the background,
    /// but must hand any WPF objects back to the UI thread frozen.
    /// </summary>
    void Warm(IEnumerable<SlotSettings> slots, int iconSize);

    /// <summary>Deletes the disk cache and any in-memory icons.</summary>
    void ClearCache();
}
