# Loadout

A radial app launcher for Windows, inspired by the weapon wheels in action games.

Hold **Ctrl + Alt** and a small glass wheel appears at your cursor. Move the mouse toward an app, folder or website and let go to open it. Scroll to switch between groups of 8.

> **Download:** [Loadout 1.0](https://github.com/trekrosales/loadout/releases/latest). Unzip, run `Loadout.exe`, hold **Ctrl + Alt**. Windows may warn the first time because the app isn't signed: click **More info → Run anyway**.
>
> See [SPEC.md](SPEC.md) for the full design.

## Planned features

- **Hold, aim, release.** Hold the hotkey (Ctrl + Alt by default, changeable), point the mouse at a slot and release. No clicking.
- **Opens at your cursor** on any monitor.
- **8 slots per group.** Scroll the mouse wheel to change groups, and add as many groups as you like.
- **Apps, folders and websites.** If an app is already open, Loadout switches to it instead of opening another copy.
- **Built-in Cancel** that can't be removed.
- **Glass look** with a dark grey tint. The tint color, the screen-darkening color and opacity, and the icon size are all adjustable.
- **Icons pulled automatically** from your apps.
- **Optional game mode** for use inside games.
- **Settings window** to customize everything.
- **Lives in the system tray**, never in the taskbar.

## Privacy

Loadout is designed to work fully offline:

- It has no network code, update checker, analytics or crash reporting.
- Websites open in your own browser. Loadout itself never connects to the internet.
- Settings are stored in a plain text file on your PC.

## Goals

Loadout should stay light: no CPU or GPU use while the wheel is closed, and a small memory footprint.

## Built with

C# and WPF on .NET 10 (Windows only).

## License

[GPL-3.0](LICENSE)
