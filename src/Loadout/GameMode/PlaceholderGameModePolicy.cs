// PLACEHOLDER: always allows opening and always blocks the mouse. Wave 4 (feature/game-mode) replaces it.

using Loadout.Contracts;

namespace Loadout.GameMode;

public sealed class PlaceholderGameModePolicy : IGameModePolicy
{
    public bool CanOpenNow() => true;

    public bool ShouldBlockMouse() => true;
}
