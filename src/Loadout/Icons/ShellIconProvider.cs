using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Loadout.Contracts;
using Loadout.Core;
using Loadout.Settings.Model;

namespace Loadout.Icons;

/// <summary>
/// Real icons from the Windows shell, custom images, website letter circles and a generic fallback (SPEC 3.4).
/// Everything is rendered and cached at 2 × the icon size in pixels. Two caches: PNG files on disk, and at most
/// 24 frozen images in memory. Warm-up runs on one short-lived background STA thread that exits when idle.
/// </summary>
public sealed class ShellIconProvider : IIconProvider
{
    internal const int MemoryCacheLimit = 24;
    private const string CacheFormatVersion = "v1";

    private readonly Lock _memoryGate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> _memoryIndex = [];
    private readonly LinkedList<(string Key, ImageSource Image)> _memoryOrder = new();

    private readonly Lock _warmGate = new();
    private readonly Queue<WarmItem> _warmQueue = new();
    private readonly HashSet<string> _warmQueued = [];
    private TaskCompletionSource? _warmIdle;

    private sealed record WarmItem(SlotType Type, string? Path, string? IconPath, int Px, string FileName);

    /// <summary>Number of images currently held in memory (for tests).</summary>
    internal int MemoryCount
    {
        get
        {
            lock (_memoryGate)
            {
                return _memoryOrder.Count;
            }
        }
    }

    public ImageSource GetIcon(SlotSettings slot, int iconSize)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(slot);
            var px = ToPixels(iconSize);

            if (slot.Type == SlotType.Website && !HasCustomImage(slot.IconPath))
            {
                return IconRenderer.CreateWebsite(slot.Url);
            }

            var fileName = CacheFileName(slot.Type, slot.Path, slot.IconPath, px);
            var memoryKey = fileName;

            var cached = GetFromMemory(memoryKey);
            if (cached is not null)
            {
                return cached;
            }

            var diskPath = System.IO.Path.Combine(AppPaths.IconCacheDirectory, fileName);
            var fromDisk = TryLoadPng(diskPath);
            if (fromDisk is not null)
            {
                PutInMemory(memoryKey, fromDisk);
                return fromDisk;
            }

            var extracted = Extract(slot.Type, slot.Path, slot.IconPath, px);
            if (extracted is null)
            {
                return IconRenderer.Fallback;
            }

            SavePng(extracted, diskPath);
            PutInMemory(memoryKey, extracted);
            return extracted;
        }
        catch (Exception ex)
        {
            Log.Error("Icon lookup failed; using the fallback icon.", ex);
            return IconRenderer.Fallback;
        }
    }

    public void Warm(IEnumerable<SlotSettings> slots, int iconSize)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(slots);
            var px = ToPixels(iconSize);

            foreach (var slot in slots)
            {
                if (slot is null)
                {
                    continue;
                }

                var custom = HasCustomImage(slot.IconPath);
                var needsFile = slot.Type is SlotType.App or SlotType.Folder && !string.IsNullOrWhiteSpace(slot.Path);
                if (!custom && !needsFile)
                {
                    continue; // websites without a custom image are vector and need no cache
                }

                var fileName = CacheFileName(slot.Type, slot.Path, slot.IconPath, px);
                if (File.Exists(System.IO.Path.Combine(AppPaths.IconCacheDirectory, fileName)))
                {
                    continue;
                }

                lock (_warmGate)
                {
                    if (_warmQueued.Add(fileName))
                    {
                        _warmQueue.Enqueue(new WarmItem(slot.Type, slot.Path, slot.IconPath, px, fileName));
                    }
                }
            }

            StartWorkerIfNeeded();
        }
        catch (Exception ex)
        {
            Log.Error("Could not queue icons for warm-up.", ex);
        }
    }

    public void ClearCache()
    {
        try
        {
            lock (_memoryGate)
            {
                _memoryIndex.Clear();
                _memoryOrder.Clear();
            }

            var dir = AppPaths.IconCacheDirectory;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not clear the icon cache.", ex);
        }
    }

    /// <summary>Completes when the background warm-up thread has finished (already complete if none is running).</summary>
    internal Task WhenWarmIdle()
    {
        lock (_warmGate)
        {
            return _warmIdle?.Task ?? Task.CompletedTask;
        }
    }

    private void StartWorkerIfNeeded()
    {
        lock (_warmGate)
        {
            if (_warmIdle is not null || _warmQueue.Count == 0)
            {
                return;
            }

            _warmIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(WarmLoop) { IsBackground = true, Name = "Loadout icon warm-up" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    private void WarmLoop()
    {
        while (true)
        {
            WarmItem item;
            lock (_warmGate)
            {
                if (!_warmQueue.TryDequeue(out item!))
                {
                    var done = _warmIdle;
                    _warmIdle = null;
                    done?.SetResult();
                    return;
                }
            }

            try
            {
                var diskPath = System.IO.Path.Combine(AppPaths.IconCacheDirectory, item.FileName);
                if (!File.Exists(diskPath))
                {
                    var image = Extract(item.Type, item.Path, item.IconPath, item.Px);
                    if (image is not null)
                    {
                        SavePng(image, diskPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Icon warm-up failed for one item.", ex);
            }
            finally
            {
                lock (_warmGate)
                {
                    _warmQueued.Remove(item.FileName);
                }
            }
        }
    }

    private static int ToPixels(int iconSize) => Math.Clamp(iconSize, 1, 512) * 2;

    private static bool HasCustomImage(string? iconPath) =>
        !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath);

    /// <summary>Custom image first, then the shell. Null means "use the fallback".</summary>
    private static BitmapSource? Extract(SlotType type, string? path, string? iconPath, int px)
    {
        if (HasCustomImage(iconPath))
        {
            try
            {
                return IconRenderer.LoadCustom(iconPath!, px);
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the custom icon image.", ex);
            }
        }

        if (type == SlotType.Website || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var target = Environment.ExpandEnvironmentVariables(path.Trim());
            var isShell = target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
            if (!isShell && !File.Exists(target) && !Directory.Exists(target))
            {
                return null;
            }

            var raw = IconsNative.GetShellImage(target, px);
            return raw is null ? null : IconRenderer.FromShellPixels(raw.Value.Pixels, raw.Value.Width, raw.Value.Height, px);
        }
        catch (Exception ex)
        {
            Log.Error("Could not extract an icon from the shell.", ex);
            return null;
        }
    }

    private static string CacheFileName(SlotType type, string? path, string? iconPath, int px)
    {
        var key = new StringBuilder()
            .Append(CacheFormatVersion).Append('|')
            .Append(type).Append('|')
            .Append(path).Append('|')
            .Append(iconPath).Append('|')
            .Append(WriteTicks(path)).Append('|')
            .Append(WriteTicks(iconPath))
            .ToString();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"{hash}-{px}.png";
    }

    private static string WriteTicks(string? file)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(file))
            {
                var expanded = Environment.ExpandEnvironmentVariables(file.Trim());
                if (File.Exists(expanded))
                {
                    return File.GetLastWriteTimeUtc(expanded).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception)
        {
            // Unreadable timestamp: the key just won't include it.
        }

        return string.Empty;
    }

    private static BitmapImage? TryLoadPng(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Error("A cached icon file was unreadable; it will be rebuilt.", ex);
            return null;
        }
    }

    private static void SavePng(BitmapSource image, string target)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            File.Move(temp, target, overwrite: true);
            temp = null;
        }
        catch (Exception ex)
        {
            Log.Error("Could not write an icon to the disk cache.", ex);
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // Leftover temp file; ClearCache removes it eventually.
                }
            }
        }
    }

    private ImageSource? GetFromMemory(string key)
    {
        lock (_memoryGate)
        {
            if (!_memoryIndex.TryGetValue(key, out var node))
            {
                return null;
            }

            _memoryOrder.Remove(node);
            _memoryOrder.AddFirst(node);
            return node.Value.Image;
        }
    }

    private void PutInMemory(string key, ImageSource image)
    {
        lock (_memoryGate)
        {
            if (_memoryIndex.TryGetValue(key, out var existing))
            {
                _memoryOrder.Remove(existing);
                _memoryIndex.Remove(key);
            }

            _memoryIndex[key] = _memoryOrder.AddFirst((key, image));
            while (_memoryOrder.Count > MemoryCacheLimit)
            {
                var last = _memoryOrder.Last!;
                _memoryIndex.Remove(last.Value.Key);
                _memoryOrder.RemoveLast();
            }
        }
    }
}
