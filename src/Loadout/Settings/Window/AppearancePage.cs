using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Loadout.Settings.Model;

namespace Loadout.Settings.Window;

/// <summary>Appearance section: sizes, colors and options. Every change redraws the preview.</summary>
internal sealed class AppearancePage : SectionPage
{
    private static readonly string[] s_tintPresets =
        ["#2B2D31", "#1E1F22", "#000000", "#1B2A41", "#2D1B3D", "#1F3D2B", "#3D1F1F", "#4A3B1F", "#0F3D4C", "#5A5D66"];

    private static readonly string[] s_accentPresets =
        ["#8EC9E8", "#7EE0A0", "#FFB86B", "#FF7B9C", "#C39BFF", "#FFE066", "#FFFFFF", "#5CC8FF", "#FF6B6B", "#4ADE80"];

    private static readonly string[] s_dimPresets =
        ["#000000", "#0B1020", "#1A0B1F", "#101010", "#1F1F2E", "#3A3340", "#0B2B2B", "#2B0B0B", "#202020", "#14213D"];

    private readonly Slider _iconSize;
    private readonly Slider _tintOpacity;
    private readonly Slider _dimOpacity;
    private readonly ColorField _tint = new(s_tintPresets);
    private readonly ColorField _highlight = new(s_accentPresets);
    private readonly ColorField _dim = new(s_dimPresets);
    private readonly ToggleButton _allMonitors = UiKit.Switch();
    private readonly ToggleButton _marker = UiKit.Switch();
    private readonly ToggleButton _animations = UiKit.Switch();

    public AppearancePage(SettingsContext ctx)
        : base(ctx, "Appearance", "Everything here changes the preview right away.")
    {
        var size = UiKit.SliderRow("Icon size", AppearanceSettings.MinIconSize, AppearanceSettings.MaxIconSize, 2, v => Px(v));
        _iconSize = size.Slider;
        var opacity = UiKit.SliderRow("Glass opacity", AppearanceSettings.MinTintOpacity * 100, AppearanceSettings.MaxTintOpacity * 100, 1, Percent);
        _tintOpacity = opacity.Slider;
        var dimStrength = UiKit.SliderRow("Dim strength", AppearanceSettings.MinDimOpacity * 100, AppearanceSettings.MaxDimOpacity * 100, 1, Percent);
        _dimOpacity = dimStrength.Slider;

        _iconSize.ValueChanged += (_, e) => Change(a => a.IconSize = (int)Math.Round(e.NewValue));
        _tintOpacity.ValueChanged += (_, e) => Change(a => a.TintOpacity = Math.Round(e.NewValue) / 100);
        _dimOpacity.ValueChanged += (_, e) => Change(a => a.DimOpacity = Math.Round(e.NewValue) / 100);
        _tint.ValueChanged += hex => Change(a => a.TintColor = hex);
        _highlight.ValueChanged += hex => Change(a => a.HighlightColor = hex);
        _dim.ValueChanged += hex => Change(a => a.DimColor = hex);
        _allMonitors.Click += (_, _) => Change(a => a.DimAllMonitors = _allMonitors.IsChecked == true);
        _marker.Click += (_, _) => Change(a => a.ShowLastLaunchedMarker = _marker.IsChecked == true);
        _animations.Click += (_, _) => Change(a => a.Animations = _animations.IsChecked == true);

        Body.Children.Add(UiKit.Card(UiKit.Stack(Heading("The wheel"), size.Root, opacity.Root)));
        Body.Children.Add(UiKit.Card(UiKit.Stack(
            Heading("Glass color"), _tint, Gap(), Heading("Highlight color"), _highlight)));
        Body.Children.Add(UiKit.Card(UiKit.Stack(
            Heading("Screen dimming"), _dim, Gap(), dimStrength.Root,
            UiKit.Row("Darken all monitors", "Off dims only the screen the wheel is on.", _allMonitors))));
        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Row("Show last-launched marker", "A small arc on the shortcut you opened most recently.", _marker),
            UiKit.Row("Animations", "Short fades and the icon pop when you aim.", _animations))));
        Reload();
    }

    public override void Reload()
    {
        var a = Ctx.Current.Appearance;
        Loading = true;
        _iconSize.Value = a.IconSize;
        _tintOpacity.Value = Math.Round(a.TintOpacity * 100);
        _dimOpacity.Value = Math.Round(a.DimOpacity * 100);
        _tint.Load(a.TintColor);
        _highlight.Load(a.HighlightColor);
        _dim.Load(a.DimColor);
        _allMonitors.IsChecked = a.DimAllMonitors;
        _marker.IsChecked = a.ShowLastLaunchedMarker;
        _animations.IsChecked = a.Animations;
        Loading = false;
    }

    private static string Px(double v) => v.ToString("0", CultureInfo.InvariantCulture) + " px";

    private static string Percent(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "%";

    private static TextBlock Heading(string text)
    {
        var t = UiKit.Label(text, 14, bold: true);
        t.Margin = new Thickness(0, 0, 0, 8);
        return t;
    }

    private static Border Gap() => new Border { Height = 14 };

    private void Change(Action<AppearanceSettings> edit)
    {
        if (Loading)
        {
            return;
        }

        edit(Ctx.Current.Appearance);
        Ctx.Commit();
    }
}
