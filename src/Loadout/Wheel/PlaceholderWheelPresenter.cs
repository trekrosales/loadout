// PLACEHOLDER: shows nothing. Wave 2 (feature/wheel) replaces it.

using Loadout.Contracts;
using Loadout.Settings.Model;

namespace Loadout.Wheel;

public sealed class PlaceholderWheelPresenter : IWheelPresenter
{
    public bool IsOpen { get; private set; }

    public void Open(ScreenPoint cursor) => IsOpen = true;

    public void UpdateAim(double dx, double dy)
    {
    }

    public void ChangeGroup(int delta)
    {
    }

    public void HighlightSlot(int index)
    {
    }

    public SlotSettings? Close(bool launch)
    {
        IsOpen = false;
        return null;
    }
}
