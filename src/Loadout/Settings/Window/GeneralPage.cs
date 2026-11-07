using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Loadout.Core;
using Loadout.Settings.Model;
using Microsoft.Win32;

namespace Loadout.Settings.Window;

/// <summary>General section: start with Windows, export and import, reset, settings folder, About.</summary>
internal sealed class GeneralPage : SectionPage
{
    private readonly ToggleButton _startup = UiKit.Switch();
    private readonly TextBlock _message = UiKit.Note(string.Empty);

    public GeneralPage(SettingsContext ctx)
        : base(ctx, "General", "Start-up, backups and information about Loadout.")
    {
        _startup.Click += (_, _) => OnStartupClicked();
        Body.Children.Add(UiKit.Card(UiKit.Row("Start with Windows", "Open Loadout in the tray when you sign in.", _startup)));

        var export = UiKit.MakeButton("Export settings…", (_, _) => Export());
        var import = UiKit.MakeButton("Import settings…", (_, _) => Import());
        import.Margin = new Thickness(8, 0, 0, 0);
        var folder = UiKit.MakeButton("Open settings folder", (_, _) => OpenFolder());
        folder.Margin = new Thickness(8, 0, 0, 0);
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(export);
        row.Children.Add(import);
        row.Children.Add(folder);
        _message.Margin = new Thickness(0, 10, 0, 0);
        _message.Visibility = Visibility.Collapsed;
        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Label("Your settings", 14, bold: true),
            UiKit.Note("Export saves your wheel and options to a file you can back up or move to another PC."),
            row,
            _message)));

        var reset = UiKit.MakeButton("Reset everything to defaults…", (_, _) => ResetAll(), "DangerButton");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 10, 0, 0);
        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Label("Reset", 14, bold: true),
            UiKit.Note("Clears all your groups and shortcuts and puts every option back to how it was on first run."),
            reset)));

        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Label("About Loadout", 14, bold: true),
            Spaced(UiKit.Note("Version " + Version())),
            Spaced(UiKit.Note("Licensed under GPL-3.0")),
            Spaced(UiKit.Note("Loadout never connects to the internet.")))));
        Reload();
    }

    public override void Reload()
    {
        // The registry is the source of truth, so ask it each time the page is shown.
        var enabled = Ctx.Startup.IsEnabled;
        Loading = true;
        _startup.IsChecked = enabled;
        Loading = false;
        if (Ctx.Current.General.StartWithWindows != enabled)
        {
            Ctx.Current.General.StartWithWindows = enabled;
            Ctx.Store.Save();
        }
    }

    private static TextBlock Spaced(TextBlock text)
    {
        text.Margin = new Thickness(0, 6, 0, 0);
        return text;
    }

    private static string Version()
    {
        var info = typeof(GeneralPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
        {
            return typeof(GeneralPage).Assembly.GetName().Version?.ToString(3) ?? "?";
        }

        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? info[..plus] : info;
    }

    private void Say(string text, bool good)
    {
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
        _message.Foreground = good ? Palette.Good : Palette.Danger;
    }

    private void OnStartupClicked()
    {
        if (Loading)
        {
            return;
        }

        var want = _startup.IsChecked == true;
        if (Ctx.Startup.SetEnabled(want))
        {
            Ctx.Current.General.StartWithWindows = want;
            Ctx.Store.Save();
            return;
        }

        Loading = true;
        _startup.IsChecked = !want;
        Loading = false;
        SimpleDialog.Info(
            Owner,
            "Start with Windows",
            "Windows wouldn't let Loadout change this setting. Nothing was changed.");
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export settings",
            FileName = "loadout-settings.json",
            Filter = "Loadout settings (*.json)|*.json",
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        try
        {
            Ctx.Store.Export(dialog.FileName);
            Say("Exported to " + dialog.FileName, good: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Export failed.", ex);
            Say("Couldn't save the file: " + ex.Message, good: false);
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import settings",
            Filter = "Loadout settings (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        if (Ctx.Store.Import(dialog.FileName))
        {
            Ctx.SelectedGroup = 0;
            Ctx.RaiseReloaded();
            Say("Imported settings from " + dialog.FileName, good: true);
        }
        else
        {
            Say("That file couldn't be read as Loadout settings. Nothing was changed.", good: false);
        }
    }

    private void ResetAll()
    {
        if (!SimpleDialog.Confirm(
                Owner,
                "Reset everything",
                "Reset all shortcuts, groups and options to their defaults? This can't be undone.",
                "Reset"))
        {
            return;
        }

        var defaults = LoadoutSettings.CreateDefault();
        defaults.General.HasShownWelcome = Ctx.Current.General.HasShownWelcome;
        defaults.General.StartWithWindows = Ctx.Startup.IsEnabled;
        Ctx.Store.Replace(defaults);
        Ctx.SelectedGroup = 0;
        Ctx.RaiseReloaded();
        Say("Everything was reset to defaults.", good: true);
    }

    private void OpenFolder()
    {
        try
        {
            var dir = Path.GetFullPath(AppPaths.SettingsDirectory);
            Directory.CreateDirectory(dir);
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Could not open the settings folder.", ex);
            Say("Couldn't open the folder: " + ex.Message, good: false);
        }
    }
}
