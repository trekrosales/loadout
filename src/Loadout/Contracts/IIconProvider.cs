// CONTRACT (owned by the lead). Implemented in src/Loadout/Icons/.

using System.Windows.Media;
using Loadout.Settings.Model;

namespace Loadout.Contracts;

/// <summary>
/// Supplies slot icons (SPEC section 3.4). Called on the UI thread.
/// Never downloads anything: website icons are first-letter circles unless a custom image is set.
/// </summary>
public interface IIconProvider
{
    /// <summary>
    /// Returns a frozen image for the slot at <paramref name="pixelSize"/> physical pixels square.
    /// Never null and never throws: falls back to a generic icon if extraction fails.
    /// Must be fast when the icon is already cached on disk (it is called while the wheel opens).
    /// </summary>
    ImageSource GetIcon(SlotSettings slot, int pixelSize);

    /// <summary>
    /// Prepares and disk-caches icons for these slots ahead of time, so opening the wheel is instant.
    /// Called after settings change. May do work in the background.
    /// </summary>
    void Warm(IEnumerable<SlotSettings> slots, int pixelSize);

    /// <summary>Deletes the disk cache and any in-memory icons.</summary>
    void ClearCache();
}
