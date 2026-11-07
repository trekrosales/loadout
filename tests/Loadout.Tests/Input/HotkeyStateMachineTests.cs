using Loadout.Input;
using static Loadout.Input.VirtualKeys;

namespace Loadout.Tests.Input;

public class HotkeyStateMachineTests
{
    private const int KeyT = 0x54;
    private const int KeyW = 0x57;
    private const int KeyQ = 0x51;
    private const int F13 = 0x7C;
    private const int F14 = 0x7D;
    private const int F15 = 0x7E;

    private readonly FakeEffects _fx = new();
    private readonly HotkeyStateMachine _m;

    public HotkeyStateMachineTests()
    {
        _m = new HotkeyStateMachine(_fx);
    }

    private bool Down(int vk) => _m.OnKey(vk, down: true);

    private bool Up(int vk) => _m.OnKey(vk, down: false);

    /// <summary>Elapses the hold timer and lets the UI accept the open.</summary>
    private void Elapse(bool cancel = false, bool blockMouse = true)
    {
        _m.OnHoldElapsed();
        if (_fx.LastOpenId is int id)
        {
            _m.ApplyOpenDecision(id, cancel, blockMouse);
        }
    }

    private void OpenWheel(bool blockMouse = true)
    {
        Down(LControl);
        Down(LMenu);
        Elapse(blockMouse: blockMouse);
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
        Assert.Equal((100, 100), _fx.LastOpenAt);
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
    public void AltFirstThenCtrl_AlsoWorks()
    {
        Down(RMenu);
        Down(LControl);
        Elapse();

        Assert.Equal(HotkeyState.Open, _m.State);
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

        Elapse();

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
    public void FailedMaskInjection_LetsRealAltReleaseThrough()
    {
        OpenWheel();
        _fx.InjectSucceeds = false;

        Assert.False(Up(LMenu)); // never leave Alt stuck down
    }

    [Fact]
    public void WhileOpen_ClicksScrollAndNewKeysAreSwallowed()
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
    public void KeyHeldBeforeOpen_RepeatsAndReleasePassThrough()
    {
        Down(KeyW);
        OpenWheel();

        Assert.False(Down(KeyW)); // auto-repeat of a key that was already held
        Assert.False(Up(KeyW)); // its press reached the game, so its release must too
    }

    [Fact]
    public void MouseHotkey_KeyHeldAcrossOpen_IsNeverStuck()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["MouseX1"]));
        _fx.PhysicallyDown.Add(F14);
        Down(F14); // held, e.g. walking
        Assert.True(Down(XButton1));
        Elapse();
        Assert.Equal(HotkeyState.Open, _m.State);

        Assert.False(Down(F14)); // keeps auto-repeating while open
        Assert.True(Up(XButton1));
        Assert.False(Up(F14)); // the release reaches the app
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
    public void Scroll_HighResolutionWheel_AddsUp()
    {
        OpenWheel();

        for (var i = 0; i < 4; i++)
        {
            _m.OnMouseWheel(-30);
        }

        Assert.Equal([+1], _fx.Scrolls);
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

    [Fact]
    public void KeySwallowedWhileOpen_HeldPastRelease_StaysSwallowed()
    {
        OpenWheel();
        Assert.True(Down(F15));
        Up(LMenu);
        Up(LControl);
        Assert.Equal(HotkeyState.Idle, _m.State);

        Assert.True(Down(F15)); // repeat after the wheel closed
        Assert.True(Up(F15)); // release; the app never saw the press
        Assert.False(Down(F15)); // a fresh press later goes through normally
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
        _fx.Cursor = (500, 400);
        OpenWheel(blockMouse: true);

        // With movement blocked the cursor stays at 500,400, so each event is relative to it.
        Assert.True(_m.OnMouseMove(510, 400));
        Assert.True(_m.OnMouseMove(510, 395));

        Assert.Equal((20.0, -5.0), _fx.LastAim);
    }

    [Fact]
    public void MouseMove_UnblockedMode_FollowsCursor()
    {
        _fx.Cursor = (500, 400);
        OpenWheel(blockMouse: false);

        Assert.False(_m.OnMouseMove(510, 400));
        Assert.False(_m.OnMouseMove(520, 390));

        Assert.Equal((20.0, -10.0), _fx.LastAim);
    }

    [Fact]
    public void MouseMove_BeforeUiAnswers_IsBlockedByDefault()
    {
        Down(LControl);
        Down(LMenu);
        _m.OnHoldElapsed(); // UI hasn't answered yet

        Assert.True(_m.OnMouseMove(120, 100));
    }

    [Fact]
    public void MouseMove_WhenKeysSecretlyReleased_CancelsAndRecovers()
    {
        OpenWheel();
        _fx.PhysicallyDown.Clear(); // e.g. released while the lock screen was up

        Assert.False(_m.OnMouseMove(600, 600));
        Assert.Equal(1, _fx.CancelledCount);
        Assert.Equal(HotkeyState.Idle, _m.State);
        Assert.False(_m.NeedsMouseHook);

        // The next real press works.
        _fx.PhysicallyDown.UnionWith([LControl, LMenu]);
        Down(LControl);
        Down(LMenu);
        Assert.Equal(HotkeyState.Arming, _m.State);
    }

    [Fact]
    public void RefusedOpen_BehavesAsIfNeverOpened()
    {
        Down(LControl);
        Down(LMenu);

        Elapse(cancel: true);

        Assert.Equal(HotkeyState.WaitRelease, _m.State);
        Assert.False(_m.OnMouseMove(150, 150)); // mouse is free again
        Assert.False(Up(LMenu)); // never opened, so no mask
        Up(LControl);
        Assert.Equal(HotkeyState.Idle, _m.State);
        Assert.Equal(0, _fx.ReleasedCount);
    }

    [Fact]
    public void LateUiAnswer_AfterRelease_IsIgnored()
    {
        Down(LControl);
        Down(LMenu);
        _m.OnHoldElapsed();
        var id = _fx.LastOpenId!.Value;
        Up(LMenu);
        Up(LControl);
        Assert.Equal(HotkeyState.Idle, _m.State);

        _m.ApplyOpenDecision(id, cancel: false, blockMouse: true);

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
    public void ReEnabling_AfterReleasesWhileDisabled_NextPressWorks()
    {
        OpenWheel();
        _m.Enabled = false;
        Up(LMenu);
        Up(LControl);
        _fx.PhysicallyDown.ExceptWith([LControl, LMenu]);

        _m.Enabled = true;
        _fx.PhysicallyDown.UnionWith([LControl, LMenu]);
        Down(LControl);
        Down(LMenu);

        Assert.Equal(HotkeyState.Arming, _m.State);
    }

    [Fact]
    public void ChangingHotkeyWhileOpen_WaitsUntilReleased()
    {
        OpenWheel();

        _m.SetHotkey(HotkeyDefinition.Parse(["Ctrl", "Shift"]));
        Assert.Equal("Ctrl + Alt", _m.Hotkey.Text);

        Assert.True(Up(LMenu)); // still masked under the old hotkey
        Up(LControl);
        Assert.Equal("Ctrl + Shift", _m.Hotkey.Text);
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
    public void TriggerKey_PressedBeforeModifier_DoesNotArmOrStick()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["Ctrl", "F13"]));

        Assert.False(Down(F13)); // reaches the app
        Assert.False(Down(LControl));
        Assert.Equal(HotkeyState.Idle, _m.State);
        _m.OnHoldElapsed();

        Assert.False(Up(F13)); // its release reaches the app too
        Assert.False(Up(LControl));
    }

    [Fact]
    public void TriggerKey_AfterOpen_ReleaseSwallowed()
    {
        _m.SetHotkey(HotkeyDefinition.Parse(["Ctrl", "Q"]));
        Down(LControl);
        Down(KeyQ);
        Elapse();

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
        Elapse();
        Assert.Equal(HotkeyState.Open, _m.State);
        Assert.True(Up(XButton1));
        Assert.Equal(1, _fx.ReleasedCount);
    }

    private sealed class FakeEffects : IHotkeyEffects
    {
        public (int X, int Y) Cursor { get; set; } = (100, 100);

        public bool InjectSucceeds { get; set; } = true;

        public HashSet<int> PhysicallyDown { get; } = [LControl, RControl, LMenu, RMenu, KeyW];

        public int TimerStarts { get; private set; }

        public int OpenRequests { get; private set; }

        public int? LastOpenId { get; private set; }

        public (int, int) LastOpenAt { get; private set; }

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

        public bool Inject(IReadOnlyList<InjectedKey> keys)
        {
            if (!InjectSucceeds)
            {
                return false;
            }

            Injected.AddRange(keys);
            return true;
        }

        public bool IsPhysicallyDown(int vk) => PhysicallyDown.Contains(vk);

        public (int X, int Y) GetCursor() => Cursor;

        public void OpenRequested(int openId, int x, int y)
        {
            OpenRequests++;
            LastOpenId = openId;
            LastOpenAt = (x, y);
        }

        public void Released() => ReleasedCount++;

        public void Cancelled() => CancelledCount++;

        public void SlotKey(int index) => SlotKeys.Add(index);

        public void GroupScrolled(int direction) => Scrolls.Add(direction);

        public void AimChanged(double dx, double dy) => LastAim = (dx, dy);
    }
}
