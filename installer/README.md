# Building the Loadout installer

This folder holds the installer script (`Loadout.iss`). The build script that makes the program and the installer is `build/publish.ps1`, in the folder above this one.

## One-time setup: install Inno Setup 6

The installer is made with Inno Setup 6, a free tool from https://jrsoftware.org/isdl.php. Download it and run it yourself, then accept the default options. The build script finds it automatically afterwards. Without it, the script still builds the program and the portable zip, and says clearly that the installer was skipped.

## Building

Open PowerShell in the repo folder and run:

```
powershell -NoProfile -ExecutionPolicy Bypass -File build\publish.ps1
```

To use a different version number than the one in `src\Loadout\Loadout.csproj`, add `-Version 0.2.0`.

## What you get

All outputs go to the `artifacts` folder (it's ignored by git):

| File | What it is |
|---|---|
| `artifacts\publish\Loadout.exe` | The program as one file. It runs without installing anything. |
| `artifacts\Loadout-<version>-portable.zip` | The exe plus a short readme, for people who don't want an installer. |
| `artifacts\Loadout-<version>-setup.exe` | The installer. Only made when Inno Setup 6 is installed. |

The script ends by listing the file sizes. It exits with an error code if anything fails.

## What the installer does

- **Per user, no administrator needed.** It installs to your own account's folder, `%LocalAppData%\Programs\Loadout`.
- **Start with Windows** is checked by default. It adds Loadout to your own Windows startup list only.
- **Desktop shortcut** is optional and off by default. A Start menu entry is always added.
- **Firewall rule** is optional and off by default. If you tick it, Windows asks for administrator permission once, then adds a rule that blocks Loadout from using the internet. The app never uses the internet anyway; this is extra proof.
- **Closing Loadout:** if Loadout is running, setup asks you to close it first.
- **Uninstall** removes the program, the shortcuts, the startup entry and the firewall rule (if it was added). Then it asks: "Keep your Loadout settings?" Yes keeps your shortcuts and preferences. No deletes them, along with the icon cache and log. A silent uninstall always keeps your settings.

## Not yet tested

The installer script was written without a compiler at hand, so it hasn't been compiled or run on a clean PC yet. Test it on a PC you can afford to experiment on before giving it to anyone else.
