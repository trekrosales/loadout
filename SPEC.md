# Loadout: Specification

> **Version:** 1.0
> **Last updated:** 2026-11-07
> **Status:** implemented in Loadout 1.0.

This document describes what Loadout does and how it should behave. Every feature branch is built against it. If we change our minds, we update this file first, in its own Pull Request.

Items marked **(proposed)** are defaults that haven't been confirmed yet. They're collected in [Open decisions](#14-open-decisions) at the end.

---

## 1. What Loadout is

Loadout is a small radial launcher for Windows. You hold a hotkey, a glass wheel of 8 shortcuts appears at your mouse cursor, you move the mouse toward the one you want, and you let go to open it. Scrolling the mouse wheel switches between groups of 8.

It's inspired by the weapon wheels in action games, but it's an original design built for opening apps, folders and websites.

### Goals

1. **Fast.** The wheel appears instantly, and opening something takes one gesture.
2. **Light.** No CPU or GPU use while the wheel is closed, and very little memory.
3. **Private.** It never connects to the internet.
4. **Simple.** Everything can be set up from a settings window, with no files to edit.

### Not in scope (for v1.0)

- More than one wheel or more than one hotkey.
- Wheels that change depending on which app you're in.
- Running scripts, sending keystrokes or controlling media.
- Cloud sync, accounts or automatic updates.
- Platforms other than Windows 10 and 11 (64-bit).

---

## 2. Using the wheel

### 2.1 Opening it

| | |
|---|---|
| Default hotkey | **Hold Ctrl + Alt** (either Ctrl and either Alt) |
| Changeable to | Any key combination, a single key, or a mouse button (middle, back or forward) |
| Hold delay | The wheel opens after the hotkey has been held for **150 ms** (proposed). This is adjustable from 0 to 500 ms. |
| Other shortcuts still work | If you press any other key while holding Ctrl + Alt (for example Ctrl + Alt + T or Ctrl + Alt + Del), the wheel doesn't open and that shortcut works as normal. |
| Where it appears | Centered on the mouse cursor, on whichever monitor the cursor is on. |
| Near a screen edge | The wheel is nudged inward so it's always fully visible. |
| Paused | When Loadout is paused from the tray menu, the hotkey does nothing. |

### 2.2 Choosing a slot

- The 8 slots sit around the wheel like a clock: **slot 1 at the top**, then clockwise. Each slot covers 45°.
- **Move the mouse toward a slot** to highlight it. Only the direction matters, not the distance. Moving further in the same direction keeps the same slot highlighted.
- Your aim is tracked from where the wheel opened, so it works the same on the desktop and in games.
- **No clicking.** Clicks while the wheel is open are ignored, and they don't reach the app behind it.
- **Number keys 1–8** also highlight a slot directly (proposed).

### 2.3 Opening, cancelling and switching groups

| Action | Result |
|---|---|
| Release the hotkey with a slot highlighted | Opens that slot's app, folder or website, then the wheel closes. |
| Release with nothing highlighted (aim still in the center) | **Cancel.** The wheel closes and nothing happens. |
| Press **Esc** | Cancel. |
| Release on an empty slot | Cancel. |
| **Scroll down** | Next group. |
| **Scroll up** | Previous group. |
| Scroll past the last group | Wraps around to the first group (proposed). |

Scrolling while the wheel is open never scrolls the window behind it.

### 2.4 Cancel

- Cancel is the **center circle** of the wheel, marked with ✕ (proposed). This keeps all 8 slots free for your shortcuts.
- Cancel is built in and can't be removed or moved.

### 2.5 Which group opens

The wheel always opens on **the last group you used** (proposed), like a game remembering your last weapon category.

---

## 3. Slots and groups

### 3.1 Groups

- A group has **8 slots** and an editable **name** (default "Group 1", "Group 2" and so on).
- You can have **as many groups as you like**. A new install starts with one group.
- Groups can be added, renamed, reordered and deleted in settings. The last group can't be deleted.

### 3.2 What a slot can hold

| Type | How you add it | What happens on release |
|---|---|---|
| **App** | Drag in a program, a shortcut, or a Start menu or taskbar entry, or browse for an `.exe` | If the app is **already running**, switch to its window (restoring it if minimized). Otherwise start it. |
| **Folder** | Drag in a folder, or browse for one | If a File Explorer window already shows that folder, switch to it. Otherwise open a new one. |
| **Website** | Type or paste a web address | Open it in your **default browser**. (Loadout can't detect an existing browser tab, so this always opens a new one.) |
| **Empty** | Default | Nothing happens. |

Each slot also has an optional **custom label** and an optional **custom icon** (any PNG, JPG or ICO image).

### 3.3 Switching to an app that's already running

- Loadout looks for a running copy of the same program with a visible window, brings its most recently used window to the front, and restores it if it was minimized.
- If the app is running but has **no visible window** (for example it's hidden in the tray, like Discord or Steam), Loadout starts it again. Apps like these normally just show their existing window.
- If the shortcut has extra settings (launch arguments), those are passed when the app starts.

### 3.4 Icons

- **Apps and folders:** the real icon is taken automatically from Windows at high resolution.
- **Websites:** a colored circle with the site's first letter (proposed). Fetching a site's real icon would need the internet, which Loadout never uses. You can set a custom image instead.
- Icons are prepared once and stored as small images in the icon cache (see [section 10](#10-where-data-is-stored)), so opening the wheel never has to load them again.

---

## 4. How it looks

### 4.1 Layout

```
                    [ 1 ]
          [ 8 ]               [ 2 ]

                 ╭─────────╮
                 │ Firefox │
      [ 7 ]      │  Work   │      [ 3 ]
                 │ ‹ 2/4 › │
                 ╰─────────╯

          [ 6 ]               [ 4 ]
                    [ 5 ]
```

- An **outer ring** of 8 slices, each with an icon in the middle and thin gaps between slices.
- A **center circle** showing:
  - the **name** of the highlighted shortcut, or "Cancel" when nothing is highlighted
  - the **group name**
  - the **group position**, for example `‹ 2 / 4 ›`. The arrows hint that you can scroll.
- No text under the icons, and no extra lines like "No Attachments".

### 4.2 Size

| Setting | Default | Range |
|---|---|---|
| Icon size | **48 px** (the size of Windows "medium" desktop icons) | 32–96 px |
| Wheel diameter | Grows with icon size: about **5.5 × the icon size**, so about 264 px by default | — |

The wheel also follows Windows' display scaling, so it looks right on high-resolution screens.

### 4.3 Glass style

The wheel always looks like tinted glass. You choose the tint color.

| Element | Default | Changeable |
|---|---|---|
| Glass tint | Dark grey `#2B2D31` at 60% opacity | Any color, 30–90% opacity |
| Glass sheen | A soft light gradient across the top half of the ring | — |
| Edge | A thin 1 px light line around the ring and the center circle | — |
| Highlighted slot | The slot fills with a **light blue glow** `#8EC9E8` and its icon grows slightly | Any color |
| Last-launched marker | A thin arc on the outer edge of the slot you opened last (proposed) | On or off |
| Empty slot | Shown faintly, with no icon | — |

The frosted look comes from the tint, sheen and edge rather than a real blur of what's behind the wheel (proposed). This looks nearly the same, since the screen behind is darkened anyway, and costs almost no graphics work. A real blur could be added later as an option.

### 4.4 Darkened screen

| Setting | Default | Changeable |
|---|---|---|
| Dim color | Black | Any color |
| Dim strength | 45% | 0% (off) to 85% |
| Which screens | The monitor the wheel is on | That monitor only, or all monitors |

### 4.5 Animations

Animations are kept simple and quick, with **no sound**.

| Moment | Animation |
|---|---|
| Open | Fades in and grows from 92% to 100% size in 120 ms. The dimming fades in at the same time. |
| Highlight a slot | The glow fades in and the icon grows slightly, in 80 ms. |
| Change group | Icons cross-fade in 100 ms. |
| Close | Fades out in 80 ms. Your app opens immediately rather than waiting for the fade. |

If animations are turned off in Windows (Settings → Accessibility → Visual effects), Loadout turns them off too.

---

## 5. Settings window

Open it by clicking the tray icon, or from the tray menu. All changes **save and take effect immediately**; there's no Save button. A **live preview** of the wheel is shown while you edit.

| Section | What you can do |
|---|---|
| **Shortcuts** | See your groups as tabs. Each shows the 8 slots in a wheel layout. You can: drag apps, shortcuts or folders onto a slot from the desktop, File Explorer or the Start menu; click a slot to choose an app, folder or website, or clear it; set a custom label or icon; drag slots to reorder them; and add, rename, reorder or delete groups. |
| **Hotkey** | Record a new hotkey by pressing it. Set the hold delay. Reset to Ctrl + Alt. |
| **Appearance** | Icon size, glass tint color and opacity, highlight color, dim color and strength, dim all monitors, last-launched marker, animations on or off. |
| **Games** | Game mode on or off (see [section 7](#7-game-mode)). |
| **General** | Start with Windows. Export or import settings, to back them up or move to another PC. Reset everything to defaults. Open the settings folder. Version and license information. |

---

## 6. System tray

- Loadout lives **only in the system tray**. It never appears in the taskbar or in Alt + Tab, the wheel and settings window included.
- **Left-click** the tray icon to open settings.
- **Right-click** for a menu: **Settings**, **Pause / Resume**, **Exit**.
- The first time it starts, a Windows notification says: *"Loadout is running. Hold Ctrl + Alt to open your wheel."*
- **Start with Windows** is on by default. The installer asks.

---

## 7. Game mode

| | Game mode **off** (default) | Game mode **on** |
|---|---|---|
| A fullscreen app is in front | The wheel doesn't open | The wheel opens over the game |
| Mouse movement while the wheel is open | — | **Blocked from the game** (proposed), so your camera doesn't spin while you aim |

**Limitations:**

- **Exclusive fullscreen.** Nothing can draw over games running in *exclusive* fullscreen. Loadout notices this and doesn't open there. Switch the game to *borderless* or *windowed fullscreen* to use the wheel.
- **Anti-cheat.** Some competitive games with strict anti-cheat may object to tools that block mouse input. That's one reason game mode is off by default.
- **Leaving the game.** Opening an app from inside a game switches you out of the game, just like Alt + Tab.

---

## 8. Performance

These are the targets, checked before each release:

| Measure | Target |
|---|---|
| CPU while the wheel is closed | **0%**. Nothing runs on a timer; Loadout only wakes up when a key is pressed. |
| GPU while the wheel is closed | **0%**. The wheel isn't drawn when hidden. |
| Memory while idle | **≤ 50 MB** in Task Manager |
| Memory while the wheel is open | ≤ 80 MB |
| Memory with settings open | ≤ 150 MB, back to idle levels after settings is closed |
| Time from hotkey to wheel on screen | **≤ 50 ms** after the hold delay |
| Time from release to app starting | Immediate. The close animation doesn't delay it. |
| Startup with Windows | Under 1 second, and it doesn't slow down Windows starting up |

How it stays light:

- The wheel window is created once at startup and kept hidden, ready to show instantly.
- The settings window is created only when opened and **fully freed** when closed.
- Only the current group's 8 icons are kept in memory.
- After the wheel or settings closes, unused memory is handed back to Windows.

---

## 9. Privacy and security

- **No internet. Ever.** Loadout contains no networking code: no update checks, analytics, crash reports or icon downloads.
  - An automated check in the project **fails the build** if networking code is ever added.
  - The installer can optionally add a **Windows Firewall rule** that blocks Loadout from the internet, as extra proof.
- **Websites** are handed to your own browser, which goes online. Loadout doesn't.
- **No outside code.** The app uses only Microsoft's own .NET libraries. Test tools are used while building it but never shipped.
- **Keyboard and mouse.** Loadout watches only for its hotkey, plus mouse movement and scrolling while the wheel is open. It **never records, stores or sends** what you type.
- **Open source** (GPL-3.0), so anyone can check the code.
- **Unsigned at first.** The v1.0 installer isn't digitally signed, so Windows SmartScreen shows "Windows protected your PC" the first time someone runs it. They click **More info → Run anyway**. Signing may be added later.

---

## 10. Where data is stored

| What | Where |
|---|---|
| Settings | `%AppData%\Loadout\settings.json`, a plain text file you can read and back up |
| Icon cache | `%LocalAppData%\Loadout\icons\` (safe to delete; it's rebuilt automatically) |
| Error log | `%LocalAppData%\Loadout\loadout.log`, for troubleshooting only. Errors only, at most 1 MB, never sent anywhere. |

Uninstalling removes the program and asks whether to keep or delete your settings.

Example `settings.json`:

```json
{
  "version": 1,
  "hotkey": { "keys": ["Ctrl", "Alt"], "holdDelayMs": 150 },
  "appearance": {
    "iconSize": 48,
    "tintColor": "#2B2D31",
    "tintOpacity": 0.6,
    "highlightColor": "#8EC9E8",
    "dimColor": "#000000",
    "dimOpacity": 0.45,
    "dimAllMonitors": false,
    "showLastLaunchedMarker": true,
    "animations": true
  },
  "gameMode": { "enabled": false, "blockMouseInGame": true },
  "general": { "startWithWindows": true },
  "lastGroupIndex": 0,
  "groups": [
    {
      "name": "Group 1",
      "slots": [
        { "type": "app", "path": "C:\\Program Files\\Mozilla Firefox\\firefox.exe", "label": null, "icon": null },
        { "type": "folder", "path": "C:\\Users\\me\\Downloads" },
        { "type": "website", "url": "https://github.com" },
        null, null, null, null, null
      ]
    }
  ]
}
```

---

## 11. Under the hood

This section is for whoever builds Loadout. You can skip it.

| Part | Choice |
|---|---|
| Language and framework | **C# on .NET 10 (LTS), WPF** for the wheel and settings windows |
| Hotkey and mouse | Windows low-level keyboard and mouse hooks (event-driven, no polling). Mouse aim uses raw movement from where the wheel opened. |
| Wheel window | One borderless, see-through, always-on-top window per monitor, hidden from the taskbar and Alt + Tab, that never takes keyboard focus away from your app until it launches something |
| Icons | Windows Shell image API at 2× the icon size, cached as PNG |
| Switching apps | Find the window by process path, then restore it and bring it to the front |
| Tray | The built-in Windows tray icon API |
| Tests | xUnit unit tests (development only) |
| Packaging | One self-contained `.exe` that doesn't need .NET installed, plus an **Inno Setup** installer. A portable `.zip` is also offered. |

Planned folder layout:

```
loadout/
├─ src/Loadout/            the app
│  ├─ Input/               hotkey and mouse hooks
│  ├─ Wheel/               wheel window and drawing
│  ├─ Launching/           opening and switching to apps, folders and websites
│  ├─ Settings/            settings window and settings.json
│  └─ Tray/                tray icon and menu
├─ tests/Loadout.Tests/    automated tests
├─ installer/              Inno Setup script
├─ README.md
├─ SPEC.md
└─ LICENSE
```

---

## 12. Known limitations

- **Admin windows.** When a program running *as administrator* is in front, Windows stops normal programs from seeing the keyboard, so the hotkey won't work there. A future option could start Loadout as administrator.
- **Exclusive-fullscreen games** (see [section 7](#7-game-mode)).
- **Website tabs.** Loadout can't switch to a tab that's already open in your browser.
- **Secure screens.** The wheel can't appear on Windows' own secure screens, such as the Ctrl + Alt + Del screen, the lock screen or permission prompts.
- **AltGr keyboards.** On keyboard layouts with an **AltGr** key (common in Europe), Windows treats AltGr as Ctrl + Alt. Typing symbols like `@` is quick, so it shouldn't open the wheel, but holding AltGr would. If that's a problem, change the hotkey.

---

## 13. How it gets built

Each step is built on its own branch and merged into `main` through a Pull Request once it works. `main` always holds a working version.

The detailed order, including which AI model builds each part and which parts are built in parallel, is in [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md). The table below is the feature-level overview.

| # | Branch | What it adds | Done when |
|---|---|---|---|
| 0 | `docs/spec` | This document | You've approved it |
| 1 | `feature/tray-app` | Project setup and tray icon (Settings, Pause, Exit). Never shows in the taskbar. | It runs from the tray with 0% CPU at idle |
| 2 | `feature/hotkey` | Ctrl + Alt hold detection, hold delay, other shortcuts still working | Holding opens a placeholder; Ctrl + Alt + T still works |
| 3 | `feature/wheel` | The glass wheel at the cursor, direction aiming, center Cancel, darkened screen, animations | It looks and feels right on every monitor |
| 4 | `feature/launcher` | Opening apps, folders and websites, switching to running apps, icons | Releasing opens or switches to the right thing |
| 5 | `feature/groups` | Scroll to change groups, wrap-around, remembering the last group | Scrolling changes groups smoothly |
| 6 | `feature/settings` | The settings window with every option above | Everything can be changed without touching files |
| 7 | `feature/game-mode` | Game mode and fullscreen detection | Works in a borderless game and stays out of exclusive fullscreen |
| 8 | `feature/installer` | Installer, start with Windows, firewall rule option, portable zip | A clean PC can install, use and uninstall it |
| — | `release/v1.0` | Final checks against [section 8](#8-performance) | Published on GitHub Releases |

Each finished step can be tagged as a test version (`v0.1`, `v0.2` and so on) so you can try it on your PC.

---

## 14. Open decisions

These are defaults chosen because they weren't decided yet. Change any of them by commenting on the Pull Request.

| # | Decision | Proposed default |
|---|---|---|
| 1 | Where Cancel lives | The **center circle**, keeping all 8 slots free |
| 2 | Hold delay before the wheel opens | **150 ms**, adjustable |
| 3 | Number keys 1–8 to pick a slot | **Yes** |
| 4 | Scrolling past the last group | **Wraps** to the first |
| 5 | Which group opens | The **last used** group |
| 6 | Website icons | **First-letter circle**, with an optional custom image |
| 7 | Glass effect | **Tint, sheen and edge** (no real blur) to stay light |
| 8 | Last-launched marker arc | **On** |
| 9 | Game mode default | **Off** |
| 10 | Block mouse movement from games while the wheel is open | **Yes** |
| 11 | Dim one monitor or all | **Only the wheel's monitor** |
| 12 | Start with Windows | **On** |
| 13 | Code signing | **Unsigned** for v1.0 |
