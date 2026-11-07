using Loadout.Settings.Model;
using Loadout.Wheel;

namespace Loadout.Tests.Wheel;

public class WheelVisualTests
{
    /// <summary>Changing the icon size or colors rebuilds the wheel; doing it twice used to throw.</summary>
    [Fact]
    public void Configure_RebuildsRepeatedly()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var visual = new WheelVisual();
                var appearance = new AppearanceSettings();
                visual.Configure(appearance);
                appearance.IconSize = 64;
                visual.Configure(appearance);
                appearance.TintColor = "#112233";
                visual.Configure(appearance);
                appearance.IconSize = 32;
                visual.Configure(appearance);

                Assert.Equal(32, visual.Metrics.IconSize);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
