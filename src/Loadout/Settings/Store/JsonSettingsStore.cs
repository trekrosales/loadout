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

    private readonly string _file;
    private readonly string _brokenFile;
    private readonly TimeSpan _saveDelay;

    private DispatcherTimer? _timer;
    private bool _pending;

    public JsonSettingsStore(string? settingsFile = null, TimeSpan? saveDelay = null)
    {
        _file = Path.GetFullPath(settingsFile ?? AppPaths.SettingsFile);
        _brokenFile = Path.Combine(Path.GetDirectoryName(_file)!, BrokenFileName);

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

            Current = SettingsNormalizer.Normalize(parsed);
        }
        catch (Exception ex)
        {
            // Unreadable (locked, no access, ...). The file is left alone, so nothing is lost.
            Log.Error("Could not read settings.json. Default settings are used for now.", ex);
            Current = LoadoutSettings.CreateDefault();
        }
    }

    public void Save()
    {
        Changed?.Invoke(this, EventArgs.Empty);

        if (_saveDelay == TimeSpan.Zero)
        {
            WriteNow();
            return;
        }

        _pending = true;
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
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
            WriteAtomic(_file, Current);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.json.", ex);
        }
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
            Log.Error("Could not move the damaged settings.json aside.", ex);
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
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Leftover temp file; the next save overwrites it.
                }
            }
        }
    }
}
