// CONTRACT (owned by the lead). Implemented in src/Loadout/Startup/ (Wave 3, feature/settings).

namespace Loadout.Contracts;

/// <summary>
/// Starts Loadout with Windows (SPEC sections 5 and 6) through the per-user registry Run key
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run, value "Loadout", data "\"&lt;exe path&gt;\"".
/// <para>
/// The registry is the source of truth. Only two things change it: the installer (at install time) and the
/// settings window's "Start with Windows" toggle. The app never registers itself automatically at startup,
/// so development builds never end up in the Run key.
/// </para>
/// Implementations take the registry key path in their constructor so tests can use a throwaway key
/// (for example HKCU\Software\Loadout-Tests\Run) instead of the real one.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>True if the Run value exists and points at the current executable.</summary>
    bool IsEnabled { get; }

    /// <summary>Adds or removes the Run value for the current executable. Never throws; returns false on failure.</summary>
    bool SetEnabled(bool enabled);
}
