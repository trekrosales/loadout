using System.Windows.Controls.Primitives;

namespace Loadout.Settings.Window;

/// <summary>Games section: game mode and mouse blocking (SPEC 7).</summary>
internal sealed class GamesPage : SectionPage
{
    private readonly ToggleButton _gameMode = UiKit.Switch();
    private readonly ToggleButton _block = UiKit.Switch();

    public GamesPage(SettingsContext ctx)
        : base(ctx, "Games", "Control how the wheel behaves over full-screen apps and games.")
    {
        _gameMode.Click += (_, _) =>
        {
            if (Loading)
            {
                return;
            }

            Ctx.Current.GameMode.Enabled = _gameMode.IsChecked == true;
            _block.IsEnabled = Ctx.Current.GameMode.Enabled;
            Ctx.Commit();
        };
        _block.Click += (_, _) =>
        {
            if (Loading)
            {
                return;
            }

            Ctx.Current.GameMode.BlockMouseInGame = _block.IsChecked == true;
            Ctx.Commit();
        };

        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Row("Game mode", "Let the wheel open over a full-screen app or game. When this is off, the wheel stays away from full-screen apps.", _gameMode, 10),
            UiKit.Row("Block mouse movement in games", "Keeps your camera from spinning while you aim at the wheel. Turn this off if a game doesn't like it.", _block))));

        Body.Children.Add(UiKit.Card(UiKit.Stack(
            UiKit.Label("Good to know", 14, bold: true),
            Spaced(UiKit.Note("Exclusive full-screen. Nothing can draw over a game running in exclusive full-screen, so the wheel can't open there. Switch the game to borderless or windowed full-screen to use it.")),
            Spaced(UiKit.Note("Anti-cheat. Some competitive games with strict anti-cheat may object to tools that block mouse input. That is one reason game mode starts switched off.")),
            Spaced(UiKit.Note("Leaving the game. Opening an app from inside a game switches you out of the game, just like Alt + Tab.")))));
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        _gameMode.IsChecked = Ctx.Current.GameMode.Enabled;
        _block.IsChecked = Ctx.Current.GameMode.BlockMouseInGame;
        _block.IsEnabled = Ctx.Current.GameMode.Enabled;
        Loading = false;
    }

    private static System.Windows.Controls.TextBlock Spaced(System.Windows.Controls.TextBlock text)
    {
        text.Margin = new System.Windows.Thickness(0, 8, 0, 0);
        return text;
    }
}
