using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Loadout.Icons;

/// <summary>Builds the frozen images the provider hands out: shell bitmaps, custom images, website circles, the fallback.</summary>
internal static class IconRenderer
{
    private static readonly Color[] s_palette =
    [
        Color.FromRgb(0x5B, 0x8D, 0xEF), // blue
        Color.FromRgb(0x3F, 0xB0, 0x9A), // teal
        Color.FromRgb(0x4C, 0xAF, 0x6E), // green
        Color.FromRgb(0xE0, 0x9F, 0x3E), // amber
        Color.FromRgb(0xE2, 0x6D, 0x5A), // coral
        Color.FromRgb(0xD0, 0x5F, 0x8F), // pink
        Color.FromRgb(0x9B, 0x6B, 0xD6), // purple
        Color.FromRgb(0x6E, 0x7F, 0xD9), // indigo
    ];

    private static readonly Lazy<ImageSource> s_fallback = new(CreateFallback);

    public static ImageSource Fallback => s_fallback.Value;

    /// <summary>Turns shell pixels (top-down BGRA) into a frozen px × px image.</summary>
    public static BitmapSource FromShellPixels(byte[] pixels, int width, int height, int px)
    {
        var any = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                any = true;
                break;
            }
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (!any)
            {
                pixels[i + 3] = 255; // the bitmap has no alpha channel at all: treat as opaque
            }
            else if (pixels[i + 3] == 0)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 0; // keep premultiplied data valid
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return FitToSquare(bitmap, px);
    }

    /// <summary>Loads a PNG, JPG or ICO without locking the file, scaled to fit px × px.</summary>
    public static BitmapSource LoadCustom(string path, int px)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The image has no frames.");
        }

        // Prefer the smallest frame that is at least px; otherwise the largest one.
        BitmapFrame? best = null;
        foreach (var frame in decoder.Frames)
        {
            if (best is null || IsBetter(frame, best, px))
            {
                best = frame;
            }
        }

        return FitToSquare(best!, px);
    }

    private static bool IsBetter(BitmapFrame candidate, BitmapFrame current, int px)
    {
        var c = Math.Max(candidate.PixelWidth, candidate.PixelHeight);
        var b = Math.Max(current.PixelWidth, current.PixelHeight);
        if (c >= px && b >= px)
        {
            return c < b;
        }

        if (c >= px)
        {
            return true;
        }

        return b < px && c > b;
    }

    /// <summary>Returns the image unchanged if it is already px × px, otherwise draws it centered, aspect kept.</summary>
    public static BitmapSource FitToSquare(BitmapSource source, int px)
    {
        if (source.PixelWidth == px && source.PixelHeight == px)
        {
            if (!source.IsFrozen)
            {
                source.Freeze();
            }

            return source;
        }

        var scale = Math.Min((double)px / source.PixelWidth, (double)px / source.PixelHeight);
        var w = source.PixelWidth * scale;
        var h = source.PixelHeight * scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect((px - w) / 2, (px - h) / 2, w, h));
        }

        var target = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>The text shown in a website's circle: first letter or digit of the host, without "www.".</summary>
    public static string WebsiteLetter(string? url)
    {
        foreach (var c in HostOf(url))
        {
            if (char.IsLetterOrDigit(c))
            {
                return char.ToUpperInvariant(c).ToString();
            }
        }

        return "?";
    }

    /// <summary>A stable color for the host (same host, same color, on every run).</summary>
    public static Color WebsiteColor(string? url)
    {
        // FNV-1a: stable across runs, unlike string.GetHashCode.
        var hash = 2166136261u;
        foreach (var c in HostOf(url))
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return s_palette[hash % (uint)s_palette.Length];
    }

    private static string HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var text = url.Trim();
        string host;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host;
        }
        else if (Uri.TryCreate("https://" + text, UriKind.Absolute, out var guess) && !string.IsNullOrEmpty(guess.Host))
        {
            host = guess.Host;
        }
        else
        {
            host = text;
        }

        host = host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static ImageSource CreateWebsite(string? url)
    {
        var letter = WebsiteLetter(url);
        var circleBrush = new SolidColorBrush(WebsiteColor(url));
        circleBrush.Freeze();

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(circleBrush, null, new EllipseGeometry(new Point(50, 50), 50, 50)));

        var text = new FormattedText(
            letter,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            56,
            Brushes.White,
            1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        if (!bounds.IsEmpty)
        {
            geometry.Transform = new TranslateTransform(
                50 - (bounds.X + (bounds.Width / 2)),
                50 - (bounds.Y + (bounds.Height / 2)));
        }

        geometry.Freeze();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, geometry));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static DrawingImage CreateFallback()
    {
        var back = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x5D, 0x66)));
        var light = Frozen(new SolidColorBrush(Color.FromRgb(0xD5, 0xD8, 0xDE)));
        var pen = new Pen(light, 5) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(back, null, new RectangleGeometry(new Rect(0, 0, 100, 100), 22, 22)));

        // A simple window glyph: outline, title bar line and two dots.
        group.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(24, 28, 52, 44), 6, 6)));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(24, 42), new Point(76, 42))));
        group.Children.Add(new GeometryDrawing(light, null, new EllipseGeometry(new Point(33, 35), 2.5, 2.5)));
        group.Children.Add(new GeometryDrawing(light, null, new EllipseGeometry(new Point(42, 35), 2.5, 2.5)));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
