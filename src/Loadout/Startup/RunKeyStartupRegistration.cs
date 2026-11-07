using Loadout.Contracts;
using Loadout.Core;
using Microsoft.Win32;

namespace Loadout.Startup;

/// <summary>
/// Starts Loadout with Windows through the per-user Run key (value "Loadout", data the quoted exe path).
/// The key path is a constructor argument so tests can use a throwaway key.
/// </summary>
public sealed class RunKeyStartupRegistration : IStartupRegistration
{
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Loadout";

    private readonly string _runKeyPath;
    private readonly string? _exePath;

    public RunKeyStartupRegistration(string runKeyPath = DefaultRunKeyPath, string? exePath = null)
    {
        _runKeyPath = runKeyPath;
        _exePath = exePath ?? Environment.ProcessPath;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
                return Matches(key?.GetValue(ValueName) as string, _exePath);
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the Run key.", ex);
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(_exePath))
                {
                    return false;
                }

                using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
                key.SetValue(ValueName, "\"" + _exePath + "\"", RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the Run key.", ex);
            return false;
        }
    }

    /// <summary>True when the stored data points at <paramref name="exe"/> (case-insensitive, quotes ignored).</summary>
    internal static bool Matches(string? data, string? exe)
    {
        if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        return string.Equals(data.Trim().Trim('"'), exe.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }
}
