using System.Windows.Media;
using System.Windows.Media.Imaging;
using Loadout.Core;
using Loadout.Icons;
using Loadout.Settings.Model;

namespace Loadout.Tests.Icons;

/// <summary>Points LOADOUT_DATA_DIR at a throwaway folder so the icon cache and log stay in temp.</summary>
internal sealed class IconSandbox : IDisposable
{
    private readonly string? _previous;

    public IconSandbox()
    {
        Dir = Path.Combine(Path.GetTempPath(), "loadout-icons-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        _previous = Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, Dir);
    }

    public string Dir { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, _previous);
        try
        {
            if (Directory.Exists(Dir))
            {
                Directory.Delete(Dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A late background write; the folder is in temp anyway.
        }
    }
}

public class ShellIconProviderTests
{
    private const int IconSize = 48;
    private const int Px = 96;

    private static readonly string s_notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

    /// <summary>Runs the body on an STA thread (shell and WPF need it) and rethrows any failure.</summary>
    private static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var sandbox = new IconSandbox();
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static SlotSettings App(string path) => new() { Type = SlotType.App, Path = path };

    private static byte[] ToPbgra(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    [Fact]
    public void Notepad_is_96_pixels_square_and_keeps_transparency()
    {
        RunSta(() =>
        {
            var image = Assert.IsAssignableFrom<BitmapSource>(new ShellIconProvider().GetIcon(App(s_notepad), IconSize));
            Assert.Equal(Px, image.PixelWidth);
            Assert.Equal(Px, image.PixelHeight);
            Assert.True(image.IsFrozen);

            var pixels = ToPbgra(image);
            Assert.Equal(0, pixels[3]); // top-left corner is fully transparent
            var transparent = 0;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] == 0)
                {
                    transparent++;
                }
            }

            Assert.True(transparent > 0);
            Assert.Contains(pixels.Where((_, i) => i % 4 == 3), a => a > 0);
        });
    }

    [Fact]
    public void Windows_folder_gives_an_image()
    {
        RunSta(() =>
        {
            var slot = new SlotSettings { Type = SlotType.Folder, Path = @"C:\Windows" };
            var image = Assert.IsAssignableFrom<BitmapSource>(new ShellIconProvider().GetIcon(slot, IconSize));
            Assert.Equal(Px, image.PixelWidth);
        });
    }

    [Fact]
    public void Shell_downloads_folder_gives_an_image()
    {
        RunSta(() =>
        {
            var slot = new SlotSettings { Type = SlotType.Folder, Path = "shell:Downloads" };
            var image = Assert.IsAssignableFrom<BitmapSource>(new ShellIconProvider().GetIcon(slot, IconSize));
            Assert.Equal(Px, image.PixelWidth);
            Assert.Equal(Px, image.PixelHeight);
        });
    }

    [Fact]
    public void Missing_exe_gives_the_shared_fallback_without_throwing()
    {
        RunSta(() =>
        {
            var provider = new ShellIconProvider();
            var a = provider.GetIcon(App(@"C:\definitely\not\here.exe"), IconSize);
            var b = provider.GetIcon(App(@"C:\also\missing.exe"), IconSize);
            Assert.NotNull(a);
            Assert.True(a.IsFrozen);
            Assert.Same(a, b);
            Assert.IsType<DrawingImage>(a);
        });
    }

    [Fact]
    public void Custom_png_is_scaled_to_96_pixels()
    {
        RunSta(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), "loadout-icon-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var pixels = new byte[10 * 10 * 4];
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i + 2] = 255; // red
                    pixels[i + 3] = 255;
                }

                var red = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgra32, null, pixels, 40);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(red));
                using (var stream = File.Create(file))
                {
                    encoder.Save(stream);
                }

                var slot = new SlotSettings { Type = SlotType.App, Path = s_notepad, IconPath = file };
                var image = Assert.IsAssignableFrom<BitmapSource>(new ShellIconProvider().GetIcon(slot, IconSize));
                Assert.Equal(Px, image.PixelWidth);
                Assert.Equal(Px, image.PixelHeight);

                var result = ToPbgra(image);
                var center = ((48 * Px) + 48) * 4;
                Assert.Equal(255, result[center + 2]); // red in the middle
                Assert.Equal(0, result[center]);

                File.Delete(file); // the file must not have been locked
            }
            finally
            {
                File.Delete(file);
            }
        });
    }

    [Fact]
    public void Website_letter_uses_first_letter_or_digit_of_host()
    {
        Assert.Equal("G", IconRenderer.WebsiteLetter("https://www.github.com"));
        Assert.Equal("1", IconRenderer.WebsiteLetter("http://123.example"));
        Assert.Equal("?", IconRenderer.WebsiteLetter(""));
        Assert.Equal("?", IconRenderer.WebsiteLetter(null));
    }

    [Fact]
    public void Website_color_is_stable_and_varies_between_hosts()
    {
        Assert.Equal(IconRenderer.WebsiteColor("https://github.com"), IconRenderer.WebsiteColor("https://www.github.com/x"));

        var hosts = new[] { "github.com", "youtube.com", "example.org", "reddit.com", "wikipedia.org", "news.ycombinator.com" };
        var colors = hosts.Select(h => IconRenderer.WebsiteColor("https://" + h)).Distinct().Count();
        Assert.True(colors > 1);
    }

    [Fact]
    public void Website_icon_is_a_frozen_vector_image()
    {
        RunSta(() =>
        {
            var slot = new SlotSettings { Type = SlotType.Website, Url = "https://github.com" };
            var image = new ShellIconProvider().GetIcon(slot, IconSize);
            Assert.IsType<DrawingImage>(image);
            Assert.True(image.IsFrozen);
            Assert.False(Directory.Exists(AppPaths.IconCacheDirectory) && Directory.GetFiles(AppPaths.IconCacheDirectory).Length > 0);
        });
    }

    [Fact]
    public void GetIcon_writes_a_disk_cache_file_and_a_second_provider_loads_it()
    {
        RunSta(() =>
        {
            var first = new ShellIconProvider();
            first.GetIcon(App(s_notepad), IconSize);

            var files = Directory.GetFiles(AppPaths.IconCacheDirectory, "*.png");
            var file = Assert.Single(files);
            Assert.EndsWith("-96.png", file, StringComparison.Ordinal);
            var written = File.GetLastWriteTimeUtc(file);

            var second = new ShellIconProvider();
            var image = Assert.IsAssignableFrom<BitmapSource>(second.GetIcon(App(s_notepad), IconSize));
            Assert.Equal(Px, image.PixelWidth);
            Assert.Equal(written, File.GetLastWriteTimeUtc(file)); // loaded, not rewritten
            Assert.Single(Directory.GetFiles(AppPaths.IconCacheDirectory, "*.png"));
        });
    }

    [Fact]
    public void Warm_creates_cache_files_and_the_thread_finishes()
    {
        RunSta(() =>
        {
            var provider = new ShellIconProvider();
            var slots = new[]
            {
                App(s_notepad),
                new SlotSettings { Type = SlotType.Folder, Path = @"C:\Windows" },
                new SlotSettings { Type = SlotType.Website, Url = "https://github.com" },
                App(@"C:\missing\nothing.exe"),
            };

            provider.Warm(slots, IconSize);
            provider.Warm(slots, IconSize); // a second call just adds to the queue
            Assert.True(provider.WhenWarmIdle().Wait(TimeSpan.FromSeconds(30)));

            Assert.Equal(2, Directory.GetFiles(AppPaths.IconCacheDirectory, "*.png").Length);
            Assert.True(provider.WhenWarmIdle().IsCompleted);
            Assert.Equal(0, provider.MemoryCount); // the thread never touches the memory cache
        });
    }

    [Fact]
    public void ClearCache_removes_the_folder()
    {
        RunSta(() =>
        {
            var provider = new ShellIconProvider();
            provider.GetIcon(App(s_notepad), IconSize);
            Assert.True(Directory.Exists(AppPaths.IconCacheDirectory));

            provider.ClearCache();

            Assert.False(Directory.Exists(AppPaths.IconCacheDirectory));
            Assert.Equal(0, provider.MemoryCount);
        });
    }

    [Fact]
    public void Memory_cache_never_holds_more_than_24_images()
    {
        RunSta(() =>
        {
            var provider = new ShellIconProvider();
            var dir = Path.Combine(AppPaths.LocalDirectory, "pngs");
            Directory.CreateDirectory(dir);
            var bytes = new byte[4 * 4 * 4];
            var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, bytes, 16);

            for (var i = 0; i < 40; i++)
            {
                var file = Path.Combine(dir, $"{i}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var stream = File.Create(file))
                {
                    encoder.Save(stream);
                }

                provider.GetIcon(new SlotSettings { Type = SlotType.App, Path = @"C:\x.exe", IconPath = file }, IconSize);
                Assert.True(provider.MemoryCount <= ShellIconProvider.MemoryCacheLimit);
            }

            Assert.Equal(ShellIconProvider.MemoryCacheLimit, provider.MemoryCount);
        });
    }
}
