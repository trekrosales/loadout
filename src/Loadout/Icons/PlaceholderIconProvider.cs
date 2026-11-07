// PLACEHOLDER: returns a plain grey circle. Wave 2 (feature/icons) replaces it.

using System.Windows;
using System.Windows.Media;
using Loadout.Contracts;
using Loadout.Settings.Model;

namespace Loadout.Icons;

public sealed class PlaceholderIconProvider : IIconProvider
{
    private static readonly ImageSource s_icon = CreateIcon();

    public ImageSource GetIcon(SlotSettings slot, int pixelSize) => s_icon;

    public void Warm(IEnumerable<SlotSettings> slots, int pixelSize)
    {
    }

    public void ClearCache()
    {
    }

    private static DrawingImage CreateIcon()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0xA3));
        brush.Freeze();
        var drawing = new GeometryDrawing(brush, null, new EllipseGeometry(new Point(16, 16), 16, 16));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
