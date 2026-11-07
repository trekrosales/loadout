using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Loadout.Settings.Model;

namespace Loadout.Settings.Window;

/// <summary>Hotkey section: shows the hotkey as key caps, records a new one, and sets the hold delay.</summary>
internal sealed class HotkeyPage : SectionPage
{
    private readonly WrapPanel _caps = new();
    private readonly Border _recorder = new();
    private readonly TextBlock _hint = UiKit.Note(string.Empty);
    private readonly TextBlock _message = UiKit.Note(string.Empty);
    private readonly Button _recordButton;
    private readonly Slider _delay;
    private readonly HashSet<Key> _keys = [];
    private readonly HashSet<string> _mouse = [];
    private readonly HashSet<Key> _down = [];
    private readonly HashSet<string> _mouseDown = [];
    private IDisposable? _suspension;

    public HotkeyPage(SettingsContext ctx)
        : base(ctx, "Hotkey", "Hold this key combination to open the wheel, aim, then let go to open the highlighted shortcut.")
    {
        _caps.VerticalAlignment = VerticalAlignment.Center;

        _recorder.Focusable = true;
        _recorder.FocusVisualStyle = null;
        _recorder.MinHeight = 76;
        _recorder.CornerRadius = new CornerRadius(10);
        _recorder.BorderThickness = new Thickness(1.5);
        _recorder.Padding = new Thickness(18, 14, 18, 14);
        _recorder.Margin = new Thickness(0, 0, 0, 10);
        _recorder.Child = UiKit.Stack(_caps, _hint);
        _recorder.PreviewKeyDown += OnKeyDown;
        _recorder.PreviewKeyUp += OnKeyUp;
        _recorder.PreviewMouseDown += OnMouseDown;
        _recorder.PreviewMouseUp += OnMouseUp;
        _recorder.LostKeyboardFocus += (_, e) =>
        {
            // Clicking the Cancel button must not count as losing focus (it would stop, then Click would restart).
            if (e.NewFocus != _recordButton)
            {
                StopRecording(cancelled: true);
            }
        };
        _recorder.Unloaded += (_, _) => StopRecording(cancelled: true);

        _recordButton = UiKit.MakeButton("Record new hotkey", (_, _) => ToggleRecording(), "AccentButton");
        _recordButton.Focusable = false;
        var reset = UiKit.MakeButton("Reset to Ctrl + Alt", (_, _) => ResetHotkey());
        reset.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(_recordButton);
        buttons.Children.Add(reset);

        _message.Margin = new Thickness(0, 10, 0, 0);
        _message.Visibility = Visibility.Collapsed;
        Body.Children.Add(UiKit.Card(UiKit.Stack(_recorder, buttons, _message)));

        var delay = UiKit.SliderRow("Hold delay", HotkeySettings.MinHoldDelayMs, HotkeySettings.MaxHoldDelayMs, 10, v => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        _delay = delay.Slider;
        _delay.ValueChanged += (_, e) =>
        {
            if (Loading)
            {
                return;
            }

            Ctx.Current.Hotkey.HoldDelayMs = (int)Math.Round(e.NewValue);
            Ctx.Commit();
        };
        var delayStack = UiKit.Stack(
            delay.Root,
            UiKit.Note("How long you must hold the keys before the wheel appears. A short delay stops it popping up by accident when you use the same keys for something else."));
        Body.Children.Add(UiKit.Card(delayStack));

        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Label("Keyboards with an AltGr key", 14, bold: true),
            Spaced(UiKit.Note("On layouts with an AltGr key (common in Europe), Windows treats AltGr as Ctrl + Alt. Holding AltGr would then open the wheel. If that gets in the way, choose a different hotkey.")))));

        Reload();
    }

    public override void Reload()
    {
        StopRecording(cancelled: true);
        Loading = true;
        _delay.Value = Ctx.Current.Hotkey.HoldDelayMs;
        Loading = false;
        ShowCurrent();
        SetRecordingLook(false);
    }

    /// <summary>True while a recording is in progress.</summary>
    public bool IsRecording => _suspension is not null;

    public override void OnWindowClosing() => StopRecording(cancelled: true);

    /// <summary>Shows the hotkey that is really in effect: an invalid saved one falls back to Ctrl + Alt.</summary>
    private void ShowCurrent()
    {
        var keys = Ctx.Current.Hotkey.Keys;
        if (HotkeyRecording.Validate(keys) is null)
        {
            ShowCaps(keys);
            if (_message.Foreground == Palette.Danger && _message.Text.StartsWith("Your saved", StringComparison.Ordinal))
            {
                Say(string.Empty, Palette.DimText);
            }

            return;
        }

        ShowCaps(["Ctrl", "Alt"]);
        Say("Your saved hotkey wasn't valid, so Ctrl + Alt is used.", Palette.Danger);
    }

    private static TextBlock Spaced(TextBlock text)
    {
        text.Margin = new Thickness(0, 6, 0, 0);
        return text;
    }

    private void ShowCaps(IEnumerable<string> tokens)
    {
        _caps.Children.Clear();
        var first = true;
        foreach (var token in tokens)
        {
            if (!first)
            {
                _caps.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontSize = 16,
                    Foreground = Palette.FaintText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 10, 0),
                });
            }

            first = false;
            _caps.Children.Add(UiKit.Cap(HotkeyRecording.DisplayName(token)));
        }
    }

    private void SetRecordingLook(bool recording)
    {
        _recorder.BorderBrush = recording ? Palette.Accent : Palette.Line;
        _recorder.Background = recording ? Palette.AccentSoft : Palette.Bg;
        _recordButton.Content = recording ? "Cancel" : "Record new hotkey";
        _hint.Text = recording ? "Press and hold your keys (or a middle, back or forward mouse button), then let go. Esc cancels." : string.Empty;
        _hint.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        _hint.Margin = new Thickness(0, 10, 0, 0);
    }

    /// <summary>What the Record / Cancel button does.</summary>
    internal void ToggleRecording()
    {
        if (_suspension is null)
        {
            StartRecording();
        }
        else
        {
            StopRecording(cancelled: true);
        }
    }

    internal void StartRecording()
    {
        _keys.Clear();
        _mouse.Clear();
        _down.Clear();
        _mouseDown.Clear();
        _suspension = Ctx.Input.Suspend();
        Say(string.Empty, Palette.DimText);
        SetRecordingLook(true);
        _caps.Children.Clear();
        _recorder.Focus();
        Keyboard.Focus(_recorder);
    }

    private void StopRecording(bool cancelled)
    {
        if (_suspension is null)
        {
            return;
        }

        _suspension.Dispose();
        _suspension = null;
        SetRecordingLook(false);
        if (cancelled)
        {
            ShowCurrent();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_suspension is null)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        Press(key);
    }

    /// <summary>A key went down while recording.</summary>
    internal void Press(Key key)
    {
        if (_suspension is null)
        {
            return;
        }

        if (key == Key.Escape)
        {
            StopRecording(cancelled: true);
            Keyboard.ClearFocus();
            return;
        }

        if (HotkeyRecording.TokenForKey(key) is null)
        {
            return;
        }

        _down.Add(key);
        _keys.Add(key);
        ShowLive();
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (_suspension is null)
        {
            return;
        }

        e.Handled = true;
        Release(e.Key == Key.System ? e.SystemKey : e.Key);
    }

    /// <summary>A key came up while recording.</summary>
    internal void Release(Key key)
    {
        if (_suspension is null)
        {
            return;
        }

        _down.Remove(key);

        // Alt and Ctrl are reported by side; clear both sides if the matching token is released.
        if (HotkeyRecording.TokenForKey(key) is { } token)
        {
            _down.RemoveWhere(k => HotkeyRecording.TokenForKey(k) == token && !Keyboard.IsKeyDown(k));
        }

        FinishIfReleased();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_suspension is null)
        {
            return;
        }

        var token = e.ChangedButton switch
        {
            MouseButton.Middle => "MouseMiddle",
            MouseButton.XButton1 => "MouseX1",
            MouseButton.XButton2 => "MouseX2",
            _ => null,
        };
        if (token is null)
        {
            return;
        }

        e.Handled = true;
        _mouseDown.Add(token);
        _mouse.Add(token);
        ShowLive();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_suspension is null)
        {
            return;
        }

        var token = e.ChangedButton switch
        {
            MouseButton.Middle => "MouseMiddle",
            MouseButton.XButton1 => "MouseX1",
            MouseButton.XButton2 => "MouseX2",
            _ => null,
        };
        if (token is not null)
        {
            e.Handled = true;
            _mouseDown.Remove(token);
            FinishIfReleased();
        }
    }

    private void ShowLive() => ShowCaps(HotkeyRecording.BuildTokens(_keys, _mouse));

    private void FinishIfReleased()
    {
        if (_down.Count > 0 || _mouseDown.Count > 0 || (_keys.Count == 0 && _mouse.Count == 0))
        {
            return;
        }

        var tokens = HotkeyRecording.BuildTokens(_keys, _mouse);
        var error = HotkeyRecording.Validate(tokens);
        StopRecording(cancelled: false);
        Keyboard.ClearFocus();
        if (error is not null)
        {
            ShowCurrent();
            Say(error + " The hotkey was not changed.", Palette.Danger);
            return;
        }

        Ctx.Current.Hotkey.Keys = tokens;
        Ctx.Commit();
        ShowCaps(tokens);
        Say("Hotkey saved.", Palette.Good);
    }

    private void Say(string text, System.Windows.Media.Brush brush)
    {
        if (brush == Palette.Good)
        {
            Flash(_message, text, brush);
            return;
        }

        _message.Text = text;
        _message.Foreground = brush;
        _message.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetHotkey()
    {
        StopRecording(cancelled: true);
        Ctx.Current.Hotkey.Keys = ["Ctrl", "Alt"];
        Ctx.Commit();
        ShowCaps(Ctx.Current.Hotkey.Keys);
        Say("Reset to Ctrl + Alt.", Palette.Good);
    }
}
