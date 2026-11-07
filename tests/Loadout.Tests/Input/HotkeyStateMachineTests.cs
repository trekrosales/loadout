using Loadout.Input;
using static Loadout.Input.VirtualKeys;

namespace Loadout.Tests.Input;

public class HotkeyStateMachineTests
{
    private const int KeyT = 0x54;
    private const int KeyW = 0x57;
    private const int KeyQ = 0x51;

    private readonly FakeEffects _fx = new();
    private readonly HotkeyStateMachine _m;

    public HotkeyStateMachineTests()
    {
        _m = new HotkeyStateMachine(_fx);
    }

    private bool Down(int vk) => _m.OnKey(vk, down: true);

    private bool Up(int vk) => _m.OnKey(vk, down: false);

    private void OpenWheel()
    {
        Down(LControl);
        Down(LMenu);
        _m.OnHoldElapsed();
        Assert.Equal(HotkeyState.Open, _m.State);
    }

    [Fact]
    public void HoldingCtrlAlt_ArmsThenOpens()
    {
        Assert.False(Down(LControl));
        Assert.Equal(HotkeyState.Idle, _m.State);
        Assert.False(Down(LMenu));
        Assert.Equal(HotkeyState.Arming, _m.State);
        Assert.Equal(1, _fx.TimerStarts);

        _m.OnHoldElapsed();

        Assert.Equal(HotkeyState.Open, _m.State);
        Assert.Equal(1, _fx.OpenRequests);
        Assert.True(_m.NeedsMouseHook);
    }

    [Fact]
    public void QuickTap_NeverOpens_AndPassesEverything()
    {
        Assert.False(Down(LControl));
        Assert.False(Down(LMenu));
        Assert.False(Up(LMenu));
        Assert.False(Up(LControl));

        Assert.Equal(HotkeyState.Idle, _m.State);
        Assert.Equal(0, _fx.OpenRequests);
        Assert.Empty(_fx.Injected);
        Assert.False(_m.NeedsMouseHook);
    }

    [Fact]
    public void OtherKeyWhileArming_KeepsShortcutWorking()
    {
        Down(LControl);
        Down(LMenu);

        Assert.False(Down(KeyT)); // Ctrl + Alt + T goes through
        Assert.Equal(HotkeyState.WaitRelease, _m.State);
        _m.OnHoldElapsed();

        Assert.Equal(0, _fx.OpenRequests);
        Up(KeyT);
        Up(LMenu);
        Up(LControl);
        Assert.Equal(HotkeyState.Idle, _m.State);
    }

    [Fact]
    public void KeyHeldBeforeHotkey_DoesNotBlockIt()
    {
        Down(KeyW); // e.g. walking forward in a game
        Down(LControl);
        Down(LMenu);
        Down(KeyW); // auto-repeat

        _m.OnHoldElapsed();

        Assert.Equal(HotkeyState.Open, _m.State);
    }

    [Fact]
    public void ClickOrScrollWhileArming_Spoils()
    {
        Down(LControl);
        Down(LMenu);
        Assert.False(_m.OnMouseWheel(120));
        Assert.Equal(HotkeyState.WaitRelease, _m.State);
    }

    [Fact]
    public void ReleasingHotkey_LaunchesAndMasksAlt()
    {
        OpenWheel();

        Assert.True(Up(LMenu)); // Alt release is replaced by mask + Alt up
        Assert.Equal(1, _fx.ReleasedCount);
        Assert.Equal(
            [new InjectedKey(MenuMask, false), new InjectedKey(MenuMask, true), new InjectedKey(LMenu, true)],
            _fx.Injected);

        Assert.False(Up(LControl)); // Ctrl release passes
        Assert.Equal(HotkeyState.Idle, _m.State);
    }

    [Fact]
    public void ReleasingCtrlFirst_StillMasksAltLater()
    {
        OpenWheel();

        Assert.False(Up(LControl));
        Assert.Equal(1, _fx.ReleasedCount);
        Assert.True(Up(LMenu));
        Assert.Contains(new InjectedKey(MenuMask, false), _fx.Injected);
        Assert.Equal(HotkeyState.Idle, _m.State);
    }

    [Fact]
    public void WhileOpen_ClicksScrollAndOtherKeysAreSwallowed()
    {
        OpenWheel();

        Assert.True(Down(LButton));
        Assert.True(Up(LButton));
        Assert.True(Down(KeyT));
        Assert.True(Up(KeyT));
        Assert.True(_m.OnMouseWheel(120));
        Assert.True(_m.OnMouseHorizontalWheel());
    }

    [Fact]
    public void KeyHeldBeforeOpen_ReleasePassesThrough()
    {
        Down(KeyW);
        OpenWheel();

        Assert.False(Up(KeyW)); // its press reached the game, so its release must too
    }

    [Fact]
    public void Scroll_OneEventPerNotch_UpIsPrevious()
    {
        OpenWheel();

        _m.OnMouseWheel(120);
        _m.OnMouseWheel(-240);
        _m.OnMouseWheel(60);
        _m.OnMouseWheel(60);

        Assert.Equal([-1, +1, +1, -1], _fx.Scrolls);
    }

    [Fact]
    public void Esc_Cancels_AndIsSwallowed()
    {
        OpenWheel();

        Assert.True(Down(Escape));
        Assert.Equal(1, _fx.CancelledCount);
        Assert.True(Up(Escape));
        Up(LMenu);
        Up(LControl);

        Assert.Equal(0, _fx.ReleasedCount);
        Assert.Equal(HotkeyState.Idle, _m.State);
    }

    [Theory]
    [InlineData(Digit1, 0)]
    [InlineData(Digit8, 7)]
    [InlineData(Numpad1 + 2, 2)]
    public void NumberKeys_PickSlots(int vk, int slot)
    {
        OpenWheel();

        Assert.True(Down(vk));
        Assert.True(Up(vk));

        Assert.Equal([slot], _fx.SlotKeys);
    }

    [Fact]
    public void MouseMove_BlockedMode_KeepsCursorAndSumsMovement()
    {
        _fx.Decision = new OpenDecision(true, BlockMouse: true, 500, 400);
        OpenWheel();

        // With movement blocked the cursor stays at 500,400, so each event is relative to it.
        Assert.True(_m.OnMouseMove(510, 400));
        Assert.True(_m.OnMouseMove(510, 395));

        Assert.Equal((20.0, -5.0), _fx.LastAim);
    }

    [Fact]
    public void MouseMove_UnblockedMode_FollowsCursor()
    {
        _fx.Decision = new OpenDecision(true, BlockMouse: false, 500, 400);
        OpenWheel();

        Assert.False(_m.OnMouseMove(510, 400));
        Assert.False(_m.OnMouseMove(520, 390));

        Assert.Equal((20.0, -10.0), _fx.LastAim);
    }

    [Fact]
    public void MouseMove_WhenKeysSecretlyReleased_CancelsInsteadOfTrappingMouse()
    {
        OpenWheel();
        _fx.PhysicallyDown.Clear(); // e.g. released while the lock screen was up

        Assert.False(_m.OnMouseMove(600, 600));
        Assert.Equal(1, _fx.CancelledCount);
        Assert.NotEqual(HotkeyState.Open, _m.State);
    }

    [Fact]
    public void RefusedOpen_WaitsForRelease()
    {
        _fx.Decision = _fx.Decision with { Opened = false };
        Down(LControl);
        Down(LMenu);

        _m.OnHoldElapsed();

        Assert.Equal(HotkeyState.WaitRelease, _m.State);
        Assert.False(Up(LMenu)); // never opened, so no mask
        Up(LControl);
        Assert.Equal(HotkeyState.Idle, _m.State);
    }

    [Fact]
    public void StaleKeyFromMissedRelease_IsForgotten()
    {
        Down(LControl);
        Up(LControl);
        Down(RControl);
        _fx.PhysicallyDown.Remove(RControl); // its release was never seen
        Down(LMenu);

        Assert.Equal(HotkeyState.Idle, _m.State); // Alt alone must not arm
    }

    [Fact]
    public void Disabled_PassesEverything()
    {
        _m.Enabled = false;

        Assert.False(Down(LControl));
        Assert.False(Down(LMenu));
        _m.OnHoldElapsed();

        Assert.Equal(0, _fx.OpenRequests);
    }

    [Fact]
    public void DisablingWhileOpen_Cancels()
    {
        OpenWheel();

        _m.Enabled = false;

        Assert.Equal(1, _fx.CancelledCount);
        Assert.False(_m.NeedsMouseHook);
    }

    [Fact]
    public void TriggerKey_HeldBackThenReplayedOnQuickTap()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["Ctrl", "Q"]));

        Assert.False(Down(LControl));
        Assert.True(Down(KeyQ)); // held back
        Assert.True(Down(KeyQ)); // auto-repeat held back too
        Assert.True(Up(KeyQ)); // released early: replay a full press

        Assert.Equal([new InjectedKey(KeyQ, false), new InjectedKey(KeyQ, true)], _fx.Injected);
        Assert.Equal(HotkeyState.Idle, _m.State);
    }

    [Fact]
    public void TriggerKey_GivenBackInOrderWhenSpoiled()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["Ctrl", "Q"]));
        Down(LControl);
        Down(KeyQ);

        Assert.False(Down(KeyW));

        Assert.Equal([new InjectedKey(KeyQ, false)], _fx.Injected);
        Assert.False(Up(KeyQ)); // the replayed press is matched by the real release
    }

    [Fact]
    public void TriggerKey_AfterOpen_ReleaseSwallowed()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["Ctrl", "Q"]));
        Down(LControl);
        Down(KeyQ);
        _m.OnHoldElapsed();

        Assert.True(Up(KeyQ));
        Assert.Equal(1, _fx.ReleasedCount);
        Assert.Empty(_fx.Injected);
    }

    [Fact]
    public void MouseButtonHotkey_NeedsMouseHookAlways()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["MouseX1"]));

        Assert.True(_m.NeedsMouseHook);
        Assert.True(Down(XButton1));
        _m.OnHoldElapsed();
        Assert.Equal(HotkeyState.Open, _m.State);
        Assert.True(Up(XButton1));
        Assert.Equal(1, _fx.ReleasedCount);
    }

    private sealed class FakeEffects : IHotkeyEffects
    {
        public OpenDecision Decision { get; set; } = new(true, true, 100, 100);

        public HashSet<int> PhysicallyDown { get; } = [LControl, RControl, LMenu, RMenu, KeyW];

        public int TimerStarts { get; private set; }

        public int OpenRequests { get; private set; }

        public int ReleasedCount { get; private set; }

        public int CancelledCount { get; private set; }

        public List<InjectedKey> Injected { get; } = [];

        public List<int> SlotKeys { get; } = [];

        public List<int> Scrolls { get; } = [];

        public (double, double) LastAim { get; private set; }

        public void StartHoldTimer() => TimerStarts++;

        public void StopHoldTimer()
        {
        }

        public void Inject(IReadOnlyList<InjectedKey> keys) => Injected.AddRange(keys);

        public bool IsPhysicallyDown(int vk) => PhysicallyDown.Contains(vk);

        public OpenDecision RequestOpen()
        {
            OpenRequests++;
            return Decision;
        }

        public void Released() => ReleasedCount++;

        public void Cancelled() => CancelledCount++;

        public void SlotKey(int index) => SlotKeys.Add(index);

        public void GroupScrolled(int direction) => Scrolls.Add(direction);

        public void AimChanged(double dx, double dy) => LastAim = (dx, dy);
    }
}
