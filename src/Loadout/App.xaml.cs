using System.Windows;
using System.Windows.Threading;
using Loadout.Core;
using Loadout.GameMode;
using Loadout.Icons;
using Loadout.Input;
using Loadout.Launching;
using Loadout.Settings.Store;
using Loadout.Settings.Window;
using Loadout.Tray;
using Loadout.Wheel;

namespace Loadout;

/// <summary>
/// Entry point and composition root (owned by the lead).
/// This is the only place that chooses which implementation of each contract is used.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001", Justification = "App lives for the whole process; OnExit disposes everything.")]
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled error.", args.ExceptionObject as Exception);

        try
        {
            _instance = SingleInstance.TryAcquire();
            if (_instance is null)
            {
                // Another copy is already running; it has been asked to open settings.
                Shutdown();
                return;
            }

            var settings = new JsonSettingsStore();
            var icons = new PlaceholderIconProvider();
            var input = new LowLevelInputService();
            var wheel = new WheelPresenter(settings, icons);
            _host = new AppHost(
                settings,
                input,
                wheel,
                new PlaceholderLauncher(),
                icons,
                new PlaceholderGameModePolicy(),
                new PlaceholderSettingsWindow(),
                new TrayIcon());
            _host.ExitRequested += (_, _) => Shutdown();
            _host.Start();
            wheel.Prepare();

            _instance.ListenForOtherInstances(() => AppHost.RunOnUiThread(() => _host?.ShowSettings()));

            // Start-up allocates a lot that is never needed again; give it back once idle.
            Dispatcher.BeginInvoke(MemoryTrimmer.Trim, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            // Never leave a half-started, invisible app holding the single-instance lock.
            Log.Error("Loadout could not start.", ex);
            MessageBox.Show(
                "Loadout couldn't start. Details were saved to:\n" + AppPaths.LogFile,
                "Loadout",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _host?.Dispose();
            _host = null;
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _host?.Dispose();
        _host = null;
        base.OnSessionEnding(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI error.", e.Exception);

        // Keep running: a launcher in the tray should survive a bad slot or a glitch.
        e.Handled = true;
    }
}
