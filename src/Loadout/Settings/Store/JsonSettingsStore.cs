using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Loadout.Contracts;
using Loadout.Core;
using Loadout.Settings.Model;

namespace Loadout.Settings.Store;

/// <summary>
/// Keeps settings in settings.json (SPEC section 10). Saves are debounced with a one-shot
/// dispatcher timer and written atomically (temp file, then move). See <see cref="ISettingsStore"/>.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(400);

    private const string BrokenFileName = "settings.broken.json";

    /// <summary>Files larger than this are treated as damaged (Load) or rejected (Import).</summary>
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly string _file;
    private readonly string _directory;
    private readonly string _brokenFile;
    private readonly TimeSpan _saveDelay;

    private DispatcherTimer? _timer;
    private bool _pending;

    // Set when the file could not be read or moved aside. Before the next overwrite, the file is copied
    // to settings.broken.json so the user's data survives. Cleared once that copy succeeds.
    private bool _preserveExisting;

    // Set by Load when the file was written by a newer Loadout. Copied once to settings.v{N}.bak before the first overwrite.
    private int? _newerVersionToBackUp;

    public JsonSettingsStore(string? settingsFile = null, TimeSpan? saveDelay = null)
    {
        _file = Path.GetFullPath(settingsFile ?? AppPaths.SettingsFile);
        _directory = Path.GetDirectoryName(_file)!;
        _brokenFile = Path.Combine(_directory, BrokenFileName);

        var delay = saveDelay ?? DefaultSaveDelay;
        _saveDelay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;

        Current = LoadoutSettings.CreateDefault();
    }

    public LoadoutSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(_file))
            {
                Current = LoadoutSettings.CreateDefault();
                return;
            }

            if (new FileInfo(_file).Length > MaxFileBytes)
            {
                MoveBrokenFileAside("larger than 5 MB", null);
                return;
            }

            var json = File.ReadAllText(_file, Encoding.UTF8);

            LoadoutSettings? parsed;
            try
            {
                parsed = SettingsJson.Deserialize(json);
            }
            catch (JsonException ex)
            {
                MoveBrokenFileAside("invalid JSON", ex);
                return;
            }

            if (parsed is null)
            {
                MoveBrokenFileAside("the file holds only null", null);
                return;
            }

            if (parsed.Version > LoadoutSettings.CurrentVersion)
            {
                _newerVersionToBackUp = parsed.Version;
            }

            Current = SettingsNormalizer.Normalize(parsed);
        }
        catch (Exception ex)
        {
            // Unreadable (locked, no access, ...). The file is kept; it is copied to settings.broken.json before any overwrite.
            Log.Error("Could not read settings.json. Default settings are used for now, and the file is kept.", ex);
            _preserveExisting = true;
            Current = LoadoutSettings.CreateDefault();
        }
    }

    public void Save()
    {
        if (_saveDelay == TimeSpan.Zero)
        {
            WriteNow();
        }
        else
        {
            _pending = true;
            _timer ??= CreateTimer();
            _timer.Stop();
            _timer.Start();
        }

        RaiseChanged();
    }

    public void Flush()
    {
        if (!_pending)
        {
            return;
        }

        _timer?.Stop();
        WriteNow();
    }

    public void Replace(LoadoutSettings settings)
    {
        Current = SettingsNormalizer.Normalize(settings);
        Save();
    }

    public void Export(string filePath)
    {
        try
        {
            WriteAtomic(filePath, Current);
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException($"Could not export settings to {filePath}.", ex);
        }
    }

    public bool Import(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            if (new FileInfo(filePath).Length > MaxFileBytes)
            {
                Log.Error($"Could not import settings from {filePath}: the file is larger than 5 MB. Nothing was changed.");
                return false;
            }

            var parsed = SettingsJson.Deserialize(File.ReadAllText(filePath, Encoding.UTF8));
            if (parsed is null)
            {
                return false;
            }

            Replace(parsed);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not import settings from {filePath}. Nothing was changed.", ex);
            return false;
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("A settings Changed listener threw.", ex);
        }
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _saveDelay,
        };
        timer.Tick += OnTimerTick;
        return timer;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        WriteNow();
    }

    private void WriteNow()
    {
        _pending = false;
        try
        {
            if (!PrepareToOverwrite())
            {
                return;
            }

            WriteAtomic(_file, Current);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.json.", ex);
        }
    }

    /// <summary>
    /// Runs before anything replaces settings.json. Returns false when the write must be skipped
    /// because the existing file could not be preserved.
    /// </summary>
    private bool PrepareToOverwrite()
    {
        if (!File.Exists(_file))
        {
            _preserveExisting = false;
            return true;
        }

        if (_newerVersionToBackUp is int version)
        {
            _newerVersionToBackUp = null;
            try
            {
                File.Copy(_file, Path.Combine(_directory, $"settings.v{version}.bak"), overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not back up the settings.json written by a newer version (v{version}). Continuing.", ex);
            }
        }

        if (!_preserveExisting)
        {
            return true;
        }

        try
        {
            File.Copy(_file, _brokenFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not copy the unreadable settings.json to settings.broken.json, so the save was skipped to keep the file.", ex);
            return false;
        }

        _preserveExisting = false;
        return true;
    }

    private void MoveBrokenFileAside(string reason, Exception? exception)
    {
        Log.Error($"settings.json is damaged ({reason}). It was moved to {BrokenFileName} and default settings are used.", exception);
        try
        {
            File.Move(_file, _brokenFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _preserveExisting = true;
            Log.Error("Could not move the damaged settings.json to settings.broken.json. The file is kept, and the next save copies it first.", ex);
        }

        Current = LoadoutSettings.CreateDefault();
    }

    /// <summary>Writes to a temp file in the same folder, then moves it over the target.</summary>
    private static void WriteAtomic(string path, LoadoutSettings settings)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = fullPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, SettingsJson.Serialize(settings), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ClearReadOnly(fullPath);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>Deletes a leftover temp file. Never throws, so a cleanup failure cannot hide the real error.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Leftover temp file; the next save overwrites it.
        }
    }
}
