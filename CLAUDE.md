# Loadout: rules for AI agents

Loadout is a radial app launcher for Windows (C#, WPF, .NET 10). Read these first:

- **SPEC.md**: what the app must do. It's the source of truth for behavior.
- **docs/BUILD-PLAN.md**: who builds what, in which wave, and which folders each task owns.

The owner doesn't code. Explain things in plain language in anything they'll read (PR descriptions, reports).

## Build and test

```
dotnet build Loadout.slnx
dotnet test Loadout.slnx
```

If `dotnet` isn't on PATH, use `"C:\Program Files\dotnet\dotnet.exe"`.

- **Warnings are errors.** A task is only done with **0 warnings, 0 errors**, and all tests passing.
- To run the app, **always** set `LOADOUT_DATA_DIR` to a temp folder first so the owner's real settings are never touched, and stop the app when you're done (`Stop-Process -Name Loadout`).
- `tools/measure.ps1` checks idle CPU and memory against SPEC §8.
- `tools/screenshot.ps1` saves a PNG of the screen, which you can then view to check the UI.

## Hard rules

1. **No networking code, ever.** Nothing from `System.Net.*` (HttpClient, WebClient, WebRequest, sockets, Dns), no WebView, no telemetry. Websites are opened by handing the URL to the default browser via the shell.
2. **No NuGet packages in `src/Loadout`.** Only the .NET and Windows APIs. Test-only packages in `tests/` are fine.
3. **Stay in your folders.** Only edit the folders your task owns (see BUILD-PLAN). These are **owned by the lead** and must not be changed by agents:
   - `src/Loadout/Contracts/`
   - `src/Loadout/Settings/Model/`
   - `src/Loadout/Core/`
   - `src/Loadout/App.xaml(.cs)`
   - `Directory.Build.props`, `Loadout.slnx`, `src/Loadout/Loadout.csproj`
   - the signatures in `src/Loadout/Wheel/Geometry/WheelMath.cs`

   If you need a change there, say so in your report instead.
4. **Implement the contracts exactly.** Follow the doc comments in `src/Loadout/Contracts/`. Don't add new public members to the contracts.
5. **Wiring is the lead's job.** Don't edit `App.xaml.cs` to switch from a placeholder to your class; name your class in your report.
6. **Win32 calls:** keep P/Invoke declarations `internal` inside your own folder, and prefer `[LibraryImport]` over `[DllImport]`.
7. **Threading:** contracts are called on the UI thread. Never block it. Low-level hook callbacks must return in well under 1 ms.
8. **Light and idle:** no timers or polling loops while idle. Freeze WPF brushes, geometries and images you create.
9. **Tests:** put them in `tests/Loadout.Tests/<Area>/` (for example `tests/Loadout.Tests/Settings/`). Tests must not touch the real user folders; set `LOADOUT_DATA_DIR`.
10. **Never** write to the registry `Run` key, the real `%AppData%\Loadout`, or anything outside the repo and temp folders. The startup-registration code (`src/Loadout/Startup/`) may contain code that writes the Run key, but its tests must use a throwaway key such as `HKCU\Software\Loadout-Tests\Run`.
11. **Keep windows out of the taskbar and Alt+Tab.** Use `Core.WindowStyles.HideFromTaskSwitchers`, or `Core.WindowStyles.MakeOverlay` for the wheel.
12. **Serialize settings only with `SettingsJson.Options`.**

## Git

- Work on the branch you were given. **Never commit to `main`, and never merge.**
- Small, clear commits. End every commit message with:
  `Co-Authored-By: Claude <noreply@anthropic.com>`
- Push your branch when done (`git push -u origin <branch>`). The lead opens and merges Pull Requests after review.

## Report back

When finished, report:

- what you built
- which classes the lead should wire into `App.xaml.cs`
- test results
- anything you couldn't do or had to assume
