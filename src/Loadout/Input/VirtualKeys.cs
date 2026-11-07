namespace Loadout.Input;

/// <summary>Windows virtual-key codes used by the input code. Mouse buttons use their VK codes too.</summary>
internal static class VirtualKeys
{
    public const int LButton = 0x01;
    public const int RButton = 0x02;
    public const int MButton = 0x04;
    public const int XButton1 = 0x05;
    public const int XButton2 = 0x06;

    public const int Escape = 0x1B;
    public const int Digit1 = 0x31;
    public const int Digit8 = 0x38;
    public const int Numpad1 = 0x61;
    public const int Numpad8 = 0x68;

    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;

    /// <summary>
    /// An unassigned key code. Tapping it before releasing Alt or Win stops Windows from treating the release
    /// as "Alt pressed alone" (which opens an app's menu bar) or "Win pressed alone" (which opens Start).
    /// </summary>
    public const int MenuMask = 0xE8;

    public static bool IsMouseButton(int vk) => vk is >= LButton and <= XButton2 and not 0x03;

    public static bool IsModifier(int vk) => vk is LShift or RShift or LControl or RControl or LMenu or RMenu or LWin or RWin;

    /// <summary>Alt or Win: releasing these alone triggers menus, so their release gets masked.</summary>
    public static bool NeedsReleaseMask(int vk) => vk is LMenu or RMenu or LWin or RWin;

    /// <summary>Returns the slot index 0-7 for keys 1-8 (top row or number pad), otherwise -1.</summary>
    public static int SlotIndexFor(int vk) => vk switch
    {
        >= Digit1 and <= Digit8 => vk - Digit1,
        >= Numpad1 and <= Numpad8 => vk - Numpad1,
        _ => -1,
    };
}
