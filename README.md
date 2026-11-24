# ResMan

A tiny Windows tray app (about 50 KB, nothing to install) that switches your monitor's resolution in one click, and switches automatically while a chosen game or app is running.

- **Left-click** the tray icon to toggle between native and your chosen preset.
- **Right-click** to pick any preset, go back to native, open Settings, toggle *Start with Windows*, or exit.
- **Per-app presets**: when a listed program starts, ResMan applies its preset. About 2 seconds after the program closes, the monitor goes back to native.
- **Status at a glance**: the icon is green at native and orange while switched.
- **Safe by design**: changes are temporary and never saved to Windows settings, so a restart or sign-out always brings back your normal resolution. *Preview* goes back to native on its own after 15 seconds, even if the screen goes blank.

## Requirements

Windows 10 or 11. It uses .NET Framework 4.8, which is built into Windows.

## Getting started

1. Run `ResMan.exe` (download it from Releases, or build it as shown below). On first run it turns on *Start with Windows* and opens Settings. You can untick that in Settings or in the tray menu.
2. **Presets** tab: use *Add a supported mode…* or *Add custom*, choose a scaling option, and click *Preview* to check it.
3. **Apps** tab: start your game, then use *Add running app…* (or *Browse for .exe…*) and pick a preset for it.
4. Choose which preset the left-click switches to, then click **Save**.

### Custom resolutions (e.g. 1440×1080 stretched)

Windows can only switch to modes your graphics driver offers. Add a custom resolution once in your driver's control panel, then add it as a preset:

| GPU | Where to add it |
|---|---|
| AMD | AMD Software: Adrenalin Edition → Display → Custom Resolutions |
| NVIDIA | NVIDIA Control Panel → Display → Change resolution → Customize |
| Intel | Intel Graphics Command Center → Display → Custom Resolution |
| Any | [Custom Resolution Utility (CRU)](https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU) |

For a stretched look, set the preset's scaling to **Stretch to fill**. If you still see black bars, turn on GPU scaling with a full-panel or full-screen scaling mode in your driver's settings.

## Where things are stored

- Settings: `%APPDATA%\ResMan\settings.xml`
- Autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `ResMan` (only while enabled)

## Build and test

Nothing to install. It uses the C# compiler that ships with Windows:

```powershell
.\build.ps1
```

This runs the logic tests in `tests\LogicTests.cs` and builds `build\ResMan.exe`. To regenerate the app icon from the tray glyph, run `.\tools\make-icon.ps1`, then build again.

| Path | What it is |
|---|---|
| `src\Display.cs` | Windows display API wrapper (list modes, apply, test, restore) |
| `src\AutoSwitch.cs` | Decides when a watched app started or stopped (pure logic, unit-tested) |
| `src\Settings.cs` | Presets, app rules and XML load/save |
| `src\TrayApp.cs` | Tray icon, menu, app watcher, autostart |
| `src\SettingsForm.cs` | Settings window, preview dialog and running-app picker |

## Uninstall

Untick *Start with Windows*, choose **Exit** from the tray menu, then delete `ResMan.exe` and `%APPDATA%\ResMan`.
