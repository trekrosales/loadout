# Loadout: Multi-Agent Build Plan

> **Version:** 1.0 (draft for review)
> **Last updated:** 2026-11-07
> **Implements:** [SPEC.md](../SPEC.md)

This plan splits building Loadout among several AI agents:

- **Claude Opus** is the lead. It plans, builds the tricky core and checks everyone's work.
- **Haiku**, the smallest and cheapest model, does the simple, repetitive tasks.
- **Sonnet**, the middle model, does the medium-difficulty tasks.

---

## 1. Is splitting the work worth it?

**Yes, for about half the work.**

| Benefit | Cost |
|---|---|
| **Cheaper.** Simple tasks run on Haiku, which costs a fraction of Opus. | **Review time.** The lead has to check every agent's work, because cheaper models make more mistakes. |
| **Faster.** Independent tasks are built at the same time instead of one after another. | **Planning first.** Agents can't see each other's work, so the lead must define exactly how the pieces connect before anyone starts. |
| **Each task is its own Pull Request,** which fits our one-branch-per-feature workflow. | **Some work can't be split.** The hotkey, the wheel window and game mode are tightly connected and easy to get subtly wrong, so they stay with the lead. |

The project is small (an estimated 3,000–5,000 lines of code), so the gain is real but modest. The plan below uses agents only where the task is **independent** and **clearly defined**, and keeps the risky parts with the lead.

---

## 2. Who does what

| Role | Model | Used for |
|---|---|---|
| **Lead** | Opus | Project setup, the connections between parts, hotkey and mouse hooks, the wheel window, game mode, performance tuning, **reviewing and merging every Pull Request** |
| **Builder** | Sonnet | Medium tasks with some Windows-specific code: launching and switching apps, icons, groups, the settings window |
| **Helper** | Haiku | Simple, well-defined, repetitive tasks: the settings file, wheel math, automated checks, the installer script, documentation |

**Escalation rule:** if an agent's work fails review **twice**, the task moves up one level (Haiku → Sonnet → Opus).

---

## 3. How it works

1. **Lead sets up the foundation first.** It creates the project, the tray app and the **contracts**: short descriptions in code of how each part talks to the others (for example "the launcher receives a slot and opens it"). Agents build against these contracts, so their parts fit together without seeing each other's work.
2. **Each agent gets its own copy of the project** (a *git worktree*) on its own branch. Agents can't overwrite each other's files.
3. **Each agent owns specific folders** (see the tables below) and doesn't edit outside them. If it needs a change elsewhere, it reports back instead.
4. **Each agent must:** build without errors, pass its own tests and the automated checks, then push its branch.
5. **The lead reviews every branch,** fixes or sends back problems, and opens the Pull Request. **You approve each merge**, as with the spec.
6. **No more than 4 agents at once,** to keep reviews manageable.

---

## 4. The plan in waves

Tasks in the same wave run **at the same time**. Each wave starts after the one before it is merged.

### Wave 0: Foundation (lead only)

| Branch | Task | Model | Owns |
|---|---|---|---|
| `feature/tray-app` | Project and solution setup; the tray icon with Settings, Pause and Exit; never in the taskbar; only one copy running at a time; error log; **all contracts** between parts; empty test project | **Opus** | Everything (it creates the structure) |

**Done when:** it builds, runs from the tray at 0% CPU, and every contract exists with a placeholder version.

### Wave 1: Independent building blocks (4 agents in parallel)

| Branch | Task | Model | Owns | Why this model |
|---|---|---|---|---|
| `feature/hotkey` | Low-level keyboard and mouse hooks: Ctrl+Alt hold detection, the hold delay, letting other shortcuts through, reading mouse movement and scrolling while open | **Opus** (lead) | `src/Loadout/Input/` | Subtle Windows behavior; mistakes can freeze input |
| `feature/settings-store` | The `settings.json` model with defaults, loading and saving, recovering from a damaged file, export and import; unit tests | **Haiku** | `src/Loadout/Settings/Model/`, its tests | Straightforward data code, fully described in SPEC §10 |
| `feature/wheel-math` | Pure math: mouse direction → slot number, the center Cancel zone, slice shapes, sizing from icon size, keeping the wheel on screen at monitor edges; unit tests | **Haiku** | `src/Loadout/Wheel/Geometry/`, its tests | Self-contained math, easy to test |
| `chore/checks` | GitHub Actions that build and test every Pull Request, plus the **"no networking code" check** that fails the build if internet code appears | **Haiku** | `.github/`, `build/` | Short config files following a known pattern |

### Wave 2: Features (3 agents in parallel)

| Branch | Task | Model | Owns | Depends on |
|---|---|---|---|---|
| `feature/wheel` | The glass wheel window: at the cursor, the dark grey tint, highlight, center text, darkened screen, animations, one per monitor | **Opus** (lead) | `src/Loadout/Wheel/` (except `Geometry/`) | hotkey, wheel-math, settings-store |
| `feature/launcher` | Opening apps, folders and websites; switching to an app or folder that's already open; restoring minimized windows | **Sonnet** | `src/Loadout/Launching/` | settings-store |
| `feature/icons` | Extracting app and folder icons, first-letter circles for websites, custom images, the icon cache | **Sonnet** | `src/Loadout/Icons/` | settings-store |

### Wave 3: Putting it together (3 agents in parallel)

| Branch | Task | Model | Owns | Depends on |
|---|---|---|---|---|
| `feature/groups` | Scrolling between groups, wrap-around, remembering the last group, the last-launched marker; connecting release-to-launch | **Sonnet** | `src/Loadout/Wheel/Groups/` | wheel, launcher, icons |
| `feature/settings` | The settings window: shortcuts editor with drag and drop, hotkey recorder, appearance, games and general sections, live preview | **Sonnet** | `src/Loadout/Settings/Window/` | wheel, launcher, icons |
| `feature/installer` | The Inno Setup installer, start with Windows, the optional firewall rule, portable zip, keep-or-delete settings on uninstall | **Haiku** | `installer/` | tray-app |

The settings window is the biggest single task. If it turns out too large, it is split into one branch per section, with the repetitive sections (Appearance, General) going to **Haiku**.

### Wave 4: Finish (lead, plus one helper)

| Branch | Task | Model |
|---|---|---|
| `feature/game-mode` | Fullscreen detection, staying out of exclusive fullscreen, blocking mouse movement from the game | **Opus** |
| `docs/user-guide` | A README user guide with screenshots, troubleshooting tips and the SmartScreen explanation | **Haiku** |
| `release/v1.0` | Measuring against the SPEC §8 targets, fixing, a final check of everything, publishing the release | **Opus** |

---

## 5. Summary

| Model | Tasks |
|---|---|
| **Opus** (lead) | 5 build tasks, plus reviewing and merging all of them |
| **Sonnet** | 4 tasks |
| **Haiku** | 5 tasks (6 if the settings window is split) |

```
Wave 0   tray-app
            │
            ▼
Wave 1   hotkey  ·  settings-store  ·  wheel-math  ·  checks
            │
            ▼
Wave 2   wheel  ·  launcher  ·  icons
            │
            ▼
Wave 3   groups  ·  settings  ·  installer
            │
            ▼
Wave 4   game-mode  ·  user-guide  ·  release v1.0
```

The exact dependencies for each task are in the wave tables above.

---

## 6. What you'll do

- **Approve each Pull Request** when the lead says it's ready. Each one explains in plain language what it adds.
- **Try test versions** after waves 2, 3 and 4 and say what feels off.
- You never need to run git commands or write code.

---

## 7. Before Wave 0 starts

- [ ] This plan is approved and merged.
- [ ] The **.NET 10 SDK** (free, from Microsoft) is installed on this PC. Building needs it, and the agents need it too.
