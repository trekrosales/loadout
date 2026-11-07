using Loadout.GameMode;
using static Loadout.GameMode.FullscreenGameModePolicy;

namespace Loadout.Tests.GameMode;

public class GameModeDecisionTests
{
    [Theory]
    // exclusive, fullscreen, gameMode => open?
    [InlineData(false, false, false, true)] // desktop or normal window: always
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)] // fullscreen game, game mode off: no
    [InlineData(false, true, true, true)] // fullscreen game, game mode on: yes
    [InlineData(true, true, false, false)] // exclusive fullscreen: never
    [InlineData(true, true, true, false)]
    public void Decide_FollowsSpec(bool exclusive, bool fullscreen, bool gameMode, bool expected)
    {
        Assert.Equal(expected, Decide(exclusive, fullscreen, gameMode));
    }

    [Fact]
    public void CoversMonitor_ExactAndLarger()
    {
        var monitor = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

        Assert.True(CoversMonitor(monitor, monitor));
        Assert.True(CoversMonitor(new RECT { Left = -8, Top = -8, Right = 1928, Bottom = 1088 }, monitor));
        Assert.False(CoversMonitor(new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 }, monitor)); // maximized, taskbar visible
    }

    [Theory]
    [InlineData(0x80000000L, false, true)] // WS_POPUP: a borderless game or fullscreen video
    [InlineData(0x16CF0000L, false, false)] // normal window with title bar and resize frame
    [InlineData(0x16CF0000L, true, false)] // maximized normal window (taskbar hidden or on another screen)
    [InlineData(0x80000000L, true, false)] // maximized, whatever its style
    [InlineData(0x80040000L, false, false)] // popup with a resize frame
    public void LooksLikeFullscreenApp_OnlyBorderlessUnmaximized(long style, bool maximized, bool expected)
    {
        Assert.Equal(expected, LooksLikeFullscreenApp(style, maximized));
    }

    [Fact]
    public void RealPolicy_AnswersQuickly()
    {
        var policy = new FullscreenGameModePolicy(new Loadout.Settings.Store.InMemorySettingsStore());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            policy.CanOpenNow();
            policy.ShouldBlockMouse();
        }

        Assert.True(watch.Elapsed.TotalMilliseconds / 100 < 2, $"took {watch.Elapsed.TotalMilliseconds / 100:0.00} ms per call");
    }
}
