using System.Windows;
using System.Windows.Controls;

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

    protected System.Windows.Window? Owner => Ctx.Owner;
}
