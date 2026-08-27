![Tarkovy — by Anomaly Labs](docs/tarkovy-banner.png)

# TARKOVY

**Minimap overlay companion for Escape from Tarkov**  
by **Anomaly Labs**

![Dev 0.0.1](https://img.shields.io/badge/version-Dev%200.0.1-111111?style=flat-square&labelColor=000000)![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-111111?style=flat-square&labelColor=000000)![.NET 8](https://img.shields.io/badge/.NET-8%20WPF-111111?style=flat-square&labelColor=000000)![Anomaly Labs](https://img.shields.io/badge/by-Anomaly%20Labs-111111?style=flat-square&labelColor=000000)![VirusTotal](https://img.shields.io/badge/VirusTotal-scan%20v0.0.1-111111?style=flat-square&labelColor=000000)

[Overview](#overview) · [Features](#features) · [Interface](#interface) · [Requirements](#requirements) · [Usage](#usage) · [Dual-bind](#dual-bind) · [Build](#build) · [Virus scan](#virus-scan) · [License](#license--terms-of-use) · [Credits](#credits)

---

## Overview

**Tarkovy** is a **Windows** companion for [Escape from Tarkov](https://www.escapefromtarkov.com/). A tactical minimap overlay that only reads what the game already writes to disk:

- application logs (map / raid state)
- screenshot **filenames** (coordinates + heading)

No memory reading, no injection, no client modification.


|                  |                                  |
| ---------------- | -------------------------------- |
| **Product**      | Tarkovy                          |
| **Version**      | Dev 0.0.1                        |
| **Studio**       | Anomaly Labs                     |
| **Stack**        | .NET 8 · WPF · WebView2          |
| **Distribution** | `dist\Tarkovy.exe` (single-file) |


> **Disclaimer** — Not affiliated with Battlestate Games. Use at your own risk; BSG rules may change.

---

## Features


|                                                                        | Feature            | Detail                                                             |
| ---------------------------------------------------------------------- | ------------------ | ------------------------------------------------------------------ |
| ![map](https://img.shields.io/badge/-map-222?style=flat-square)        | Map detection      | Reads `application.log` and swaps the SVG automatically            |
| ![pos](https://img.shields.io/badge/-position-222?style=flat-square)   | Position + heading | Parses the EFT screenshot filename                                 |
| ![hud](https://img.shields.io/badge/-overlay-222?style=flat-square)    | Overlay HUD        | Corner minimap and expanded mode (`F8` / `F9`)                     |
| ![exfil](https://img.shields.io/badge/-extracts-222?style=flat-square) | Extracts & mines   | Marker layer (data from [tarkov.dev](https://tarkov.dev))          |
| ![clean](https://img.shields.io/badge/-cleanup-222?style=flat-square)  | Screenshot cleanup | Deletes the print after reading (and sweeps leftovers at raid end) |
| ![safe](https://img.shields.io/badge/-safe-222?style=flat-square)      | Safe approach      | Filesystem only — no memory read / inject                          |


---

## Interface

![Tarkovy app screenshot](docs/tarkovy-app.png)

*Main window — map preview, markers, and controls · Anomaly Labs*

---

## Requirements

- Windows 10 / 11
- Escape from Tarkov in **Borderless Windowed** (exclusive fullscreen hides any overlay)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already installed on Windows)

**UI languages:** English (default) and Portuguese — switch in **CONFIG → Language**.

---

## Usage

1. Run `dist\Tarkovy.exe`
2. Open **CONFIG** and set your folders:
  - **Logs:** `<EFT install>\Logs` (example: `D:\Battlestate Games\Escape from Tarkov\Logs`)
  - **Screenshots:** `Documents\Escape from Tarkov\Screenshots`
3. Set up the **dual-bind** in EFT (see below)
4. Enter a raid — the map switches automatically; each screenshot updates the arrow and the file is removed
5. Hotkeys:
  - **F8** — show / hide overlay (off by default)
  - **F9** — minimap / expanded (mouse works on the map when expanded)

On first run, assets are extracted to `%AppData%\Tarkovy`. Windows may show SmartScreen: *More info* → *Run anyway*.

---

## Dual-bind

Tarkovy does **not** simulate key presses. It only reads the **filename** the game writes when taking a screenshot. To refresh position while moving, your movement key must also trigger EFT’s Screenshot action.

### In Escape from Tarkov

1. **Settings** → **Controls**
2. Find **Screenshot** (not Windows Print Screen)
3. Bind it to the **same key you use to move forward** — usually **W**
4. Confirm that key is bound to both **forward movement** and **Screenshot**
5. Keep the game in **Borderless Windowed**

### In-raid flow

`W` → EFT writes a screenshot → Tarkovy reads X/Y/Z + yaw from the filename → updates the arrow → **deletes the file** (if cleanup is enabled).

### Notes

- The screenshot flash/toast is from EFT, not Tarkovy
- If it is annoying, try binding on **key press** rather than continuous hold
- Other movement keys (A/S/D) work the same — Screenshot must share the **same** bind
- Windows Print Screen alone will **not** work: Tarkovy needs the **in-game** screenshot filename with coordinates

---

## Build

### Publish the `.exe`

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download):

```powershell
dotnet publish src\Tarkovy\Tarkovy.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Output: `dist\Tarkovy.exe` (~70 MB, self-contained).

### Development

```powershell
dotnet run --project src\Tarkovy\Tarkovy.csproj
```

---

## Virus scan

Official builds are meant to be checked on [VirusTotal](https://www.virustotal.com/) before you run them. Windows SmartScreen may still warn on unsigned apps — that is normal for new open-source tools.

### Release `v0.0.1` (`Tarkovy.exe`)


|                |                                                                                                                          |
| -------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **Download**   | [Tarkovy.exe (GitHub Release)](https://github.com/leandrovieiraa/Tarkovy/releases/download/v0.0.1/Tarkovy.exe)           |
| **VirusTotal** | [Open scan report](https://www.virustotal.com/gui/file/755c6719f02fac75ef3e213e6f221ae83783c1519cbcd6b16571f5c8d03175a8) |
| **SHA-256**    | `755C6719F02FAC75EF3E213E6F221AE83783C1519CBCD6B16571F5C8D03175A8`                                                       |


Verify locally (PowerShell):

```powershell
Get-FileHash .\Tarkovy.exe -Algorithm SHA256
```

If the VirusTotal page has no report yet, upload the release `Tarkovy.exe` once at [VirusTotal → Upload](https://www.virustotal.com/gui/home/upload) (file is ~156 MB). After engines finish, the hash link above shows the results.

> Prefer the **GitHub Releases** build. Rebuilds change the hash — scan that file again.

---

## What this does not do

- No loot, enemies, or continuous tracking without screenshots
- Does not work over exclusive fullscreen
- Does not fire PrintScreen automatically

---

## License / Terms of use

Tarkovy is provided by **Anomaly Labs** for **personal and educational use only**.

You **may**:

- Download and run the software
- Study the source code
- Fork, modify, and experiment with it for learning / non-commercial projects

You **may not**:

- Sell, license, or otherwise **commercialize** Tarkovy (or modified versions)
- Use it as part of a paid product, paid service, or commercial redistribution
- Remove or obscure Anomaly Labs attribution in redistributed study copies

Provided **as-is**, without warranty. Not affiliated with Battlestate Games. Use at your own risk.

---

## Credits


|                         |                                                                                                                                                                                                     |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Studio**              | Anomaly Labs                                                                                                                                                                                        |
| **Product**             | Tarkovy · Dev 0.0.1                                                                                                                                                                                 |
| **SVG maps / extracts** | [tarkov.dev](https://tarkov.dev) · [tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)                                                                                        |
| **Approach references** | [TarkovMapTracker](https://github.com/M4elstr0m/TarkovMapTracker) · [TarkovMonitor](https://github.com/the-hideout/TarkovMonitor) · [Sayser TarkovTracker](https://github.com/sayser/TarkovTracker) |


Escape from Tarkov © Battlestate Games. Tarkovy is an independent, unofficial project.

---

![](docs/tarkovy-icon.png)  
Tarkovy · by Anomaly Labs