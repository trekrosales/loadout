using Loadout.Input;

namespace Loadout.Tests.Input;

public class HotkeyDefinitionTests
{
    [Fact]
    public void Default_IsCtrlAlt_WithNoTrigger()
    {
        var h = HotkeyDefinition.Default;

        Assert.Equal("Ctrl + Alt", h.Text);
        Assert.Null(h.Trigger);
        Assert.False(h.HasMouseButton);
        Assert.True(h.Contains(VirtualKeys.LControl));
        Assert.True(h.Contains(VirtualKeys.RControl));
        Assert.True(h.Contains(VirtualKeys.LMenu));
        Assert.True(h.Contains(VirtualKeys.RMenu));
        Assert.False(h.Contains(VirtualKeys.LShift));
    }

    [Fact]
    public void EitherSideSatisfiesAGroup()
    {
        var h = HotkeyDefinition.Default;

        Assert.True(h.IsSatisfied(vk => vk is VirtualKeys.RControl or VirtualKeys.LMenu));
        Assert.False(h.IsSatisfied(vk => vk is VirtualKeys.RControl));
    }

    [Theory]
    [InlineData("F13", 0x7C)]
    [InlineData("q", 0x51)]
    [InlineData("Space", 0x20)]
    public void KeyNames_BecomeTheTrigger(string name, int vk)
    {
        var h = HotkeyDefinition.Parse(["Ctrl", name]);

        Assert.Equal(vk, h.Trigger);
    }

    [Theory]
    [InlineData("MouseMiddle", VirtualKeys.MButton)]
    [InlineData("MouseX1", VirtualKeys.XButton1)]
    [InlineData("mousex2", VirtualKeys.XButton2)]
    public void MouseButtons_AreTriggers(string name, int vk)
    {
        var h = HotkeyDefinition.Parse([name]);

        Assert.Equal(vk, h.Trigger);
        Assert.True(h.HasMouseButton);
    }

    [Theory]
    [InlineData("Ctrl", "Ctrl")]
    [InlineData("Ctrl", "Control")]
    [InlineData("A", "B")]
    [InlineData("MouseLeft")]
    [InlineData("NotAKey")]
    [InlineData("")]
    public void Invalid_Throws(params string[] tokens)
    {
        Assert.Throws<FormatException>(() => HotkeyDefinition.Parse(tokens));
    }

    [Fact]
    public void ParseOrDefault_FallsBackWithReason()
    {
        var h = HotkeyDefinition.ParseOrDefault(["Bogus"], out var error);

        Assert.Equal("Ctrl + Alt", h.Text);
        Assert.NotNull(error);
    }
}
