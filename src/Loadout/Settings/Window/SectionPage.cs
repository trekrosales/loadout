using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Loadout.Settings.Window;

/// <summary>Base of the five pages: a scrolling column with a title. Pages read the live settings, so Reload() refreshes them after an import or reset.</summary>
internal abstract class SectionPage : ScrollViewer
{
    protected SectionPage(SettingsContext ctx, string title, string description)
    {
        Ctx = ctx;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Body = new StackPanel
        {
            Margin = new Thickness(28, 22, 24, 24),
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Body.Children.Add(UiKit.Title(title));
        Body.Children.Add(UiKit.Caption(description));
        Content = Body;
    }

    protected SettingsContext Ctx { get; }

    protected StackPanel Body { get; }

    /// <summary>True while a page is copying settings into its controls, so change handlers must ignore events.</summary>
    protected bool Loading { get; set; }

    public abstract void Reload();

    /// <summary>Called when the window closes: stop timers and anything in progress.</summary>
    public virtual void OnWindowClosing() => _flashTimer?.Stop();

    private DispatcherTimer? _flashTimer;
    private TextBlock? _flashTarget;

    /// <summary>Shows a status message that fades away after about 4 seconds (one-shot timer, only while a message is showing).</summary>
    protected void Flash(TextBlock target, string text, Brush brush)
    {
        _flashTarget = target;
        target.Text = text;
        target.Foreground = brush;
        target.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _flashTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _flashTimer.Tick -= OnFlashDone;
        _flashTimer.Stop();
        if (text.Length > 0)
        {
            _flashTimer.Tick += OnFlashDone;
            _flashTimer.Start();
        }
    }

    private void OnFlashDone(object? sender, EventArgs e)
    {
        _flashTimer?.Stop();
        if (_flashTarget is { } target)
        {
            target.Text = string.Empty;
            target.Visibility = Visibility.Collapsed;
        }
    }

    protected System.Windows.Window? Owner => Ctx.Owner;
}
