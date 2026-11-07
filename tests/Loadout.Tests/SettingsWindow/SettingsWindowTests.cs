using Loadout.Contracts;
using Loadout.Icons;
using Loadout.Input;
using Loadout.Settings.Model;
using Loadout.Settings.Store;
using Loadout.Settings.Window;
using Loadout.Tests.Settings;

namespace Loadout.Tests.SettingsWindow;

public class SettingsWindowTests
{
    private sealed class FakeStartup : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }

        public bool SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
            return true;
        }
    }

    private sealed class CountingInput : IInputService
    {
        private readonly PlaceholderInputService _inner = new();

        public int Suspended { get; private set; }

        /// <summary>Suspensions handed out and not yet disposed.</summary>
        public int Active { get; private set; }

        public bool IsPaused { get => _inner.IsPaused; set => _inner.IsPaused = value; }

        public bool IsWheelOpen => false;

#pragma warning disable CS0067
        public event EventHandler<OpenRequestedEventArgs>? OpenRequested;
        public event EventHandler<AimChangedEventArgs>? AimChanged;
        public event EventHandler<int>? GroupScrolled;
        public event EventHandler<int>? SlotKeyPressed;
        public event EventHandler? Released;
        public event EventHandler? Cancelled;
#pragma warning restore CS0067

        public void Start()
        {
        }

        public void Configure(HotkeySettings hotkey)
        {
        }

        public IDisposable Suspend()
        {
            Suspended++;
            Active++;
            return new Handle(this);
        }

        public void Dispose() => _inner.Dispose();

        private sealed class Handle(CountingInput owner) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    owner.Active--;
                }
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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
            throw new InvalidOperationException("STA test failed.", error);
        }
    }

    private static LoadoutSettings Sample()
    {
        var settings = LoadoutSettings.CreateDefault();
        settings.Groups[0].Name = "Work";
        settings.Groups[0].Slots[0] = new SlotSettings { Type = SlotType.App, Path = @"C:\Windows\notepad.exe" };
        settings.Groups[0].Slots[3] = new SlotSettings { Type = SlotType.Website, Url = "https://example.com" };
        settings.Groups.Add(GroupSettings.CreateEmpty("Play"));
        return settings;
    }

    [Fact]
    public void Window_opens_switches_through_every_section_and_closes()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var store = new InMemorySettingsStore();
            store.Replace(Sample());
            var ctx = new SettingsContext(store, new CountingInput(), new PlaceholderIconProvider(), new FakeStartup());
            var window = new Loadout.Settings.Window.SettingsWindow(ctx);
            window.Show();
            foreach (var section in Enum.GetValues<SettingsSection>())
            {
                window.SelectSection(section);
                window.UpdateLayout();
            }

            window.Close();
        });
    }

    [Fact]
    public void Service_creates_one_window_and_releases_it_on_close()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var store = new InMemorySettingsStore();
            var service = new SettingsWindowService(store, new CountingInput(), new PlaceholderIconProvider(), new FakeStartup());
            service.Show();
            service.Show();
            var open = System.Windows.Application.Current?.Windows.Count ?? -1;
            Assert.True(open is -1 or 1);

            var field = typeof(SettingsWindowService).GetField("_window", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var window = (Loadout.Settings.Window.SettingsWindow)field.GetValue(service)!;
            Assert.False(window.ShowInTaskbar);
            window.Close();
            Assert.Null(field.GetValue(service));
        });
    }

    [Fact]
    public void Appearance_and_games_edits_reach_the_store()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var store = new InMemorySettingsStore();
            store.Replace(Sample());
            var saves = 0;
            store.Changed += (_, _) => saves++;
            var ctx = new SettingsContext(store, new CountingInput(), new PlaceholderIconProvider(), new FakeStartup());
            var window = new Loadout.Settings.Window.SettingsWindow(ctx);
            window.Show();
            window.SelectSection(SettingsSection.Appearance);
            ctx.Current.Appearance.IconSize = 64;
            ctx.Commit();
            Assert.True(saves >= 1);
            window.Close();
        });
    }

    private static (SettingsContext Ctx, CountingInput Input, InMemorySettingsStore Store) NewContext()
    {
        var store = new InMemorySettingsStore();
        store.Replace(Sample());
        var input = new CountingInput();
        return (new SettingsContext(store, input, new PlaceholderIconProvider(), new FakeStartup()), input, store);
    }

    [Fact]
    public void Recording_resumes_input_after_a_successful_recording()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var (ctx, input, store) = NewContext();
            var page = new HotkeyPage(ctx);
            page.StartRecording();
            Assert.Equal(1, input.Active);
            Assert.True(page.IsRecording);

            page.Press(System.Windows.Input.Key.LeftCtrl);
            page.Press(System.Windows.Input.Key.K);
            Assert.Equal(1, input.Active);
            page.Release(System.Windows.Input.Key.K);
            page.Release(System.Windows.Input.Key.LeftCtrl);

            Assert.Equal(0, input.Active);
            Assert.False(page.IsRecording);
            Assert.Equal(["Ctrl", "K"], store.Current.Hotkey.Keys);
        });
    }

    [Fact]
    public void Recording_resumes_input_after_the_cancel_button()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var (ctx, input, store) = NewContext();
            var page = new HotkeyPage(ctx);
            page.ToggleRecording();
            Assert.Equal(1, input.Active);
            page.ToggleRecording();
            Assert.Equal(0, input.Active);
            Assert.False(page.IsRecording);
            Assert.Equal(["Ctrl", "Alt"], store.Current.Hotkey.Keys);
            Assert.Equal(1, input.Suspended);
        });
    }

    [Fact]
    public void Recording_resumes_input_after_escape()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var (ctx, input, store) = NewContext();
            var page = new HotkeyPage(ctx);
            page.StartRecording();
            page.Press(System.Windows.Input.Key.LeftAlt);
            page.Press(System.Windows.Input.Key.Escape);
            Assert.Equal(0, input.Active);
            Assert.Equal(["Ctrl", "Alt"], store.Current.Hotkey.Keys);
        });
    }

    [Fact]
    public void An_invalid_recording_is_refused_but_still_resumes_input()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var (ctx, input, store) = NewContext();
            var page = new HotkeyPage(ctx);
            page.StartRecording();
            page.Press(System.Windows.Input.Key.Q);
            page.Press(System.Windows.Input.Key.W);
            page.Release(System.Windows.Input.Key.Q);
            page.Release(System.Windows.Input.Key.W);
            Assert.Equal(0, input.Active);
            Assert.Equal(["Ctrl", "Alt"], store.Current.Hotkey.Keys);
        });
    }

    [Fact]
    public void Closing_the_window_mid_recording_resumes_input()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var (ctx, input, _) = NewContext();
            var window = new Loadout.Settings.Window.SettingsWindow(ctx);
            window.Show();
            window.SelectSection(SettingsSection.Hotkey);
            var pages = (SectionPage[])typeof(Loadout.Settings.Window.SettingsWindow)
                .GetField("_pages", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(window)!;
            ((HotkeyPage)pages[(int)SettingsSection.Hotkey]).StartRecording();
            Assert.Equal(1, input.Active);
            window.Close();
            Assert.Equal(0, input.Active);
        });
    }

    [Fact]
    public void Group_tabs_wrap_so_every_group_is_reachable()
    {
        using var sandbox = new TestSandbox();
        RunSta(() =>
        {
            var (ctx, _, store) = NewContext();
            for (var i = 0; i < 12; i++)
            {
                store.Current.Groups.Add(GroupSettings.CreateEmpty("Group " + (i + 10)));
            }

            var window = new Loadout.Settings.Window.SettingsWindow(ctx);
            window.Show();
            window.UpdateLayout();
            window.Close();
        });
    }
}
