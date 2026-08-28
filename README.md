![Tarkovy — by Anomaly Labs](docs/tarkovy-banner.png)

# TARKOVY · Dev 0.1.6

**Minimap overlay companion for Escape from Tarkov**  
by **Anomaly Labs**

<p align="left">
  <a href="https://github.com/leandrovieiraa/Tarkovy/releases"><img height="28" src="https://img.shields.io/badge/Dev-0.1.6-E8A317?style=for-the-badge&labelColor=111111" alt="Dev 0.1.6"/></a>&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="https://www.escapefromtarkov.com/"><img height="28" src="https://img.shields.io/badge/EFT-1.1.0%20KORD%20BREACH-333333?style=for-the-badge&labelColor=111111" alt="EFT 1.1.0 KORD BREACH"/></a>&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="#requirements"><img height="28" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&labelColor=111111" alt="Windows 10 / 11"/></a>&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="#build"><img height="28" src="https://img.shields.io/badge/.NET-8%20WPF-512BD4?style=for-the-badge&labelColor=111111" alt=".NET 8 WPF"/></a>
  <br/><br/>
  <a href="#credits"><img height="28" src="https://img.shields.io/badge/Anomaly%20Labs-studio-111111?style=for-the-badge&labelColor=222222" alt="Anomaly Labs"/></a>&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="https://www.virustotal.com/gui/file/B618044EAE6E3CE737E3F23F0098D6682AE2DFDFA7316560F80EBE613393D3C3"><img height="28" src="https://img.shields.io/badge/VirusTotal-SHA--256-555555?style=for-the-badge&labelColor=111111" alt="VirusTotal SHA-256"/></a>&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="https://buymeacoffee.com/anomalylabs"><img height="28" src="https://img.shields.io/badge/Buy%20me%20a%20beer-craft%20%F0%9F%8D%BA-FFDD00?style=for-the-badge&labelColor=111111" alt="Buy me a craft beer"/></a>&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="mailto:anomalylabstudio@gmail.com"><img height="28" src="https://img.shields.io/badge/Support-email-555555?style=for-the-badge&labelColor=111111" alt="Support: anomalylabstudio@gmail.com"/></a>
</p>

[Overview](#overview) · [Features](#features) · [Download](#download) · [Report a bug](#report-a-bug) · [Support](#support) · [Buy me a beer](#buy-me-a-beer) · [Interface](#interface) · [Requirements](#requirements) · [Usage](#usage) · [Screenshot bind](#screenshot-bind) · [Build](#build) · [Virus scan](#virus-scan) · [Changelog](#changelog) · [License](#license--terms-of-use) · [Credits](#credits)

---

## Overview

**Tarkovy** is a **Windows** companion for [Escape from Tarkov](https://www.escapefromtarkov.com/). A tactical minimap overlay that only reads what the game already writes to disk:

- application logs (map / raid state)
- screenshot **filenames** (coordinates + heading)

No memory reading, no injection, no client modification.


|                  |                                  |
| ---------------- | -------------------------------- |
| **Product**      | Tarkovy                          |
| **Version**      | **Dev 0.1.6**                    |
| **EFT target**   | **1.1.0** (`1.1.0.1.46699`) · Season 1 **KORD BREACH** (Aug 2026) |
| **Studio**       | Anomaly Labs                     |
| **Stack**        | .NET 8 · WPF · WebView2          |
| **Distribution** | `Tarkovy-0.1.6.zip` (exe + assets + runtimes) |


> **Disclaimer** — Not affiliated with Battlestate Games. Use at your own risk; BSG rules may change.

> **Game compatibility** — Quest names, map SVGs, extracts, and screenshot filename parsing were validated against **Escape from Tarkov 1.1.0** (Season 1 — KORD BREACH). A major EFT update can change any of that; the target patch is also shown in the app header.

---

## Features


|                                                                        | Feature              | Detail                                                                 |
| ---------------------------------------------------------------------- | -------------------- | ---------------------------------------------------------------------- |
| ![map](https://img.shields.io/badge/-map-222?style=flat-square)        | Map detection        | Reads `application.log` and swaps the SVG automatically                |
| ![pos](https://img.shields.io/badge/-position-222?style=flat-square)   | Position + heading   | Parses the EFT screenshot filename                                     |
| ![follow](https://img.shields.io/badge/-follow-222?style=flat-square)  | Follow player        | Minimap zooms in and tracks you as screenshots update                  |
| ![hud](https://img.shields.io/badge/-overlay-222?style=flat-square)    | Overlay HUD          | Compact minimap (`F8`); optional exits panel (`F9`)                  |
| ![wp](https://img.shields.io/badge/-waypoint-222?style=flat-square)   | Pencil waypoint      | ✎ click anywhere on map or minimap — route line to your position      |
| ![floor](https://img.shields.io/badge/-floors-222?style=flat-square)  | Map floors           | ▲▼ layer switch (Factory, Interchange, Ground Zero); auto from Y      |
| ![exfil](https://img.shields.io/badge/-extracts-222?style=flat-square) | Extracts & mines     | Toggleable layers + click extract to set a waypoint                    |
| ![spawn](https://img.shields.io/badge/-pmc-222?style=flat-square)     | PMC spawns           | Toggleable respawn markers (cyan triangles) per map                    |
| ![quest](https://img.shields.io/badge/-quests-222?style=flat-square)   | Map quest catalog    | Track missions; mark complete (○) to hide markers and stop tracking   |
| ![layers](https://img.shields.io/badge/-layers-222?style=flat-square)  | Side tools           | EX / MN / RS / QT / LB · floors · ✎ waypoint · rotation on minimap    |
| ![icons](https://img.shields.io/badge/-icons-222?style=flat-square)   | Game-style markers   | tarkov.dev icons per type (PMC/Scav exfil, hazard, spawn, quest…)   |
| ![i18n](https://img.shields.io/badge/-i18n-222?style=flat-square)      | EN / PT UI           | App + quest titles follow your language setting                        |
| ![clean](https://img.shields.io/badge/-cleanup-222?style=flat-square)  | Screenshot cleanup   | Deletes the print after reading (and sweeps leftovers at raid end)     |
| ![safe](https://img.shields.io/badge/-safe-222?style=flat-square)      | Safe approach        | Filesystem only — no memory read / inject                              |


> Quests are a **map catalog** you toggle manually. Tarkov logs do not expose your live PMC journal.

---

## Download

Grab the latest **ZIP** from [GitHub Releases](https://github.com/leandrovieiraa/Tarkovy/releases) (`Tarkovy-0.1.6.zip`), extract, and run `Tarkovy.exe`.

---

## Report a bug

Found something broken? Please open a GitHub Issue — we want the reports.

1. Go to **[Issues → New issue](https://github.com/leandrovieiraa/Tarkovy/issues/new)**
2. Describe what you expected vs what happened
3. Include **EFT version** (see in-game / Tarkovy header target) and **Tarkovy version** (`Dev 0.1.6`)
4. **Attach screenshots or short clips** of the main window, overlay, and/or the in-game situation — images help a lot
5. Steps to reproduce if you have them

For general questions or feedback (not a bug report), see **[Support](#support)**.

---

## Support

[![Email support](https://img.shields.io/badge/email-anomalylabstudio%40gmail.com-E8A317?style=for-the-badge&logo=gmail&logoColor=white&labelColor=111111)](mailto:anomalylabstudio@gmail.com)

Questions, feedback, or collaboration? Reach out at **[anomalylabstudio@gmail.com](mailto:anomalylabstudio@gmail.com)**.

For bugs and crashes, prefer **[GitHub Issues](#report-a-bug)** so we can track fixes with screenshots and repro steps.

---

## Buy me a beer

Long raids, cold hops, and map SVG wrestling — that’s the Anomaly Labs diet.

If Tarkovy saved you a death or two (or just made the mall less confusing), you can [**buy me a craft beer**](https://buymeacoffee.com/anomalylabs) on Buy Me a Coffee. IPA, stout, sour, whatever’s on tap — much appreciated.

[![Buy me a craft beer](https://img.shields.io/badge/%F0%9F%8D%BA_Buy%20me%20a%20craft%20beer-anomalylabs-FFDD00?style=for-the-badge&labelColor=111111)](https://buymeacoffee.com/anomalylabs)

*No pressure. Shipping pixels is already fun. Beer just makes the next patch notes easier to read.*

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

1. Extract `Tarkovy-0.1.6.zip` and run `Tarkovy.exe`
2. Open **CONFIG** (gear in the header) and set your folders:
  - **Logs:** `<EFT install>\Logs` (example: `D:\Battlestate Games\Escape from Tarkov\Logs`)
  - **Screenshots:** `Documents\Escape from Tarkov\Screenshots`
3. Bind **Screenshot** in EFT (see [Screenshot bind](#screenshot-bind)) — on **EFT 1.x**, middle mouse or a quick side key works better than sharing **W**
4. Enter a raid — the map switches automatically; each screenshot updates the arrow and the file is removed
5. Hotkeys:
  - **F8** — show / hide minimap overlay (off by default)
  - **F9** — toggle optional exits/quests side panel on the minimap (stays compact)
6. **Waypoint:** click **✎** on the map toolbar → click destination on the full map or minimap → yellow route line toward your position. **✕** clears it. **Esc** cancels pencil mode.

On first run, assets are extracted to `%AppData%\Tarkovy`. Windows may show SmartScreen: *More info* → *Run anyway*.

---

## Screenshot bind

Tarkovy does **not** simulate key presses. It only reads the **filename** EFT writes when you take an **in-game screenshot** (coordinates + heading).

### In Escape from Tarkov

1. **Settings** → **Controls**
2. Find **Screenshot** (not Windows Print Screen)
3. Bind it to something you can hit **while moving** without breaking gameplay
4. Keep the game in **Borderless Windowed**

### Recommended binds (EFT 1.x / Season 1)

Binding Screenshot on the **same key as W** (the classic “dual-bind”) used to feel fine on older patches, but on **EFT 1.x** the screenshot toast, flash, and input handling make it **much less pleasant** — we no longer recommend it as the default.

Better options:

- **Middle mouse button (scroll wheel click)** — easy to tap with your index finger while holding **W**; doesn’t steal a keyboard key
- **A dedicated key nearby** — e.g. **C**, **V**, **X**, or a thumb mouse button — whatever feels fastest for you
- **Tap as you go** — press your screenshot bind every few steps while rotating; no perfect rhythm needed, just fresh prints when the arrow drifts

Pick what fits your hand and mouse. There is no single “best” bind.

### In-raid flow

Your bind → EFT saves a screenshot → Tarkovy reads X/Y/Z + yaw from the filename → updates the arrow → **deletes the file** (if cleanup is enabled).

### Notes

- The screenshot flash/toast is from EFT, not Tarkovy
- Windows Print Screen alone will **not** work: Tarkovy needs the **in-game** screenshot filename with coordinates
- Dual-bind on **W** is still *possible* if you tolerate the EFT 1.x feedback — but most players prefer **middle-click** or a quick side key

---

## Build

### Publish

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download):

```powershell
dotnet publish src\Tarkovy\Tarkovy.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Output folder: `dist\` (exe + `Assets` + WebView2 / runtimes). Ship as a **ZIP** of that folder.

### Development

```powershell
dotnet run --project src\Tarkovy\Tarkovy.csproj
```

---

## Virus scan

Official builds are meant to be checked on [VirusTotal](https://www.virustotal.com/) before you run them. Windows SmartScreen may still warn on unsigned apps — that is normal for new open-source tools.

### Automated scan (local API key)

Zip the current `dist\` folder and upload via the [VirusTotal files API](https://docs.virustotal.com/reference/files-scan) (large builds use [upload_url](https://docs.virustotal.com/reference/files-upload-url)). The API key stays **only on your machine**.

```powershell
# once
copy tools\vt.local.env.example tools\vt.local.env
# edit tools\vt.local.env → VT_API_KEY=...

# publish + zip dist + upload
.\tools\publish-and-vt.ps1 -Wait

# or scan an existing dist\ without rebuilding
.\tools\vt-scan-dist.ps1 -Wait
```

`tools\vt.local.env` and `tools\_vt-out\` are gitignored.

### Release `v0.0.6` (`Tarkovy-0.0.6.zip`)


|                |                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Download**   | [Tarkovy-0.0.6.zip (GitHub Release)](https://github.com/leandrovieiraa/Tarkovy/releases/download/v0.0.6/Tarkovy-0.0.6.zip)           |
| **VirusTotal** | [Open scan report](https://www.virustotal.com/gui/file/B618044EAE6E3CE737E3F23F0098D6682AE2DFDFA7316560F80EBE613393D3C3)             |
| **SHA-256**    | `B618044EAE6E3CE737E3F23F0098D6682AE2DFDFA7316560F80EBE613393D3C3`                                                                   |


### Previous: `v0.0.5` (`Tarkovy-0.0.5.zip`)


|                |                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Download**   | [Tarkovy-0.0.5.zip (GitHub Release)](https://github.com/leandrovieiraa/Tarkovy/releases/download/v0.0.5/Tarkovy-0.0.5.zip)           |
| **VirusTotal** | [Open scan report](https://www.virustotal.com/gui/file/C9F4ECD092DB921B7BC8BDD93BC90BDD5B6876ABACD5864C2C3E993162126E89)             |
| **SHA-256**    | `C9F4ECD092DB921B7BC8BDD93BC90BDD5B6876ABACD5864C2C3E993162126E89`                                                                   |


### Previous: `v0.0.4` (`Tarkovy-0.0.4.zip`)


|                |                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Download**   | [Tarkovy-0.0.4.zip (GitHub Release)](https://github.com/leandrovieiraa/Tarkovy/releases/download/v0.0.4/Tarkovy-0.0.4.zip)           |
| **VirusTotal** | [Open scan report](https://www.virustotal.com/gui/file/A920FD6B30904039743F258A5B8F5EE40BF9CC31ECF8F362048C752F4337A400)             |
| **SHA-256**    | `A920FD6B30904039743F258A5B8F5EE40BF9CC31ECF8F362048C752F4337A400`                                                                   |


### Previous: `v0.0.3` (`Tarkovy-0.0.3.zip`)


|                |                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Download**   | [Tarkovy-0.0.3.zip (GitHub Release)](https://github.com/leandrovieiraa/Tarkovy/releases/download/v0.0.3/Tarkovy-0.0.3.zip)           |
| **VirusTotal** | [Open scan report](https://www.virustotal.com/gui/file/10828E02BC0806C32E32181ECD86394BEE678AB23EC0B5F7A2D1F3D336B2DA1D)             |
| **SHA-256**    | `10828E02BC0806C32E32181ECD86394BEE678AB23EC0B5F7A2D1F3D336B2DA1D`                                                                   |


### Previous: `v0.0.2` (`Tarkovy.exe`)

|                |                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Download**   | [Tarkovy.exe (GitHub Release)](https://github.com/leandrovieiraa/Tarkovy/releases/download/v0.0.2/Tarkovy.exe)                       |
| **VirusTotal** | [Open scan report](https://www.virustotal.com/gui/file/46f5026d6bfbfaee7f2510edb3a78fa3323c0828341c4d296b07558874655f35) |
| **SHA-256**    | `F36744FE4E6EC06F83191B294B58E182CAD8C6B81946A9A66103FE9E63F78D69`                                                                   |

Verify locally (PowerShell):

```powershell
Get-FileHash .\Tarkovy-0.0.6.zip -Algorithm SHA256
```

> Prefer the **GitHub Releases** build. Rebuilds change the hash — scan that file again.

---

## Changelog

<details open>
<summary><strong>Dev 0.1.6</strong> (latest)</summary>

- **Item Lens** — click-to-scan like RatScanner: Shift+click stash/inventory icon, click inspect name (OCR); flea/trader/slot prices from tarkov.dev (screen capture only, no memory read)
- **F10** — show/hide Item Lens overlay (same white border as the minimap)
- Compact shortcut chips on the main window (tooltips for each hotkey)
- Minimap tools eye toggle — show/hide side icons; layout adapts to window size (min **260×180**, same as Item Lens)
- Persist main / minimap / Item Lens window position and size
- Screenshot bind docs updated for **EFT 1.x** — prefer middle-click or a quick side key over dual-bind on **W**

</details>

<details>
<summary><strong>Dev 0.0.6</strong></summary>

- **Pencil waypoint (✎)** — click anywhere on the full map or compact minimap; route line to your position
- **Compact overlay** — minimap stays small in-raid; **F9** toggles optional exits panel (no giant window)
- **Map floors** — ▲▼ + layer label for Factory, Interchange, Ground Zero; markers filtered by floor
- **Auto floor** — optional switch in CONFIG uses screenshot **Y** height; manual override with ▲▼

</details>

<details>
<summary><strong>Dev 0.0.5</strong></summary>

- **PMC spawns** — toggle layer + `RS` on minimap; cyan triangle markers from `spawns.json`
- **Quest complete** — circle (○) marks done; tracking disabled and markers removed from map
- **Markers UI** — compact 4-column grid (EX / MN / PMC / NOMES)
- **Spawn pulse** — animation/glow only on the icon, not label text
- **Window resize** — enforce minimum **1180×720** so the layout does not break (borderless fix)

</details>

<details>
<summary><strong>Dev 0.0.4</strong></summary>

- Overlay: hide/show side panel (« / ») for map-only view while expanded
- Config: follow-player toggle moved to dedicated **MAP** section with hint text
- Fix maximized window overlapping the Windows taskbar (footer no longer clipped)
- Follow-player default in map engine defers to saved setting (no flash on load)

</details>

<details>
<summary><strong>Dev 0.0.3</strong></summary>

- Map quest catalog with EN/PT titles; toggle missions and show markers on the map
- Click extracts or quest objectives to set a waypoint + route line toward the player
- Minimap side tools: layers (extracts / mines / quests / labels) and rotation
- Follow-player camera on the minimap (zoomed tracking as screenshots update)
- Cleaner HUD (no duplicate map coords / status clutter); compact icon actions
- EFT target patch shown in the app header (validated against **1.1.0 / KORD BREACH**)
- Release package is a **ZIP** of `dist\` (exe + assets + runtimes)
- Local VirusTotal upload script for dist ZIP scans
- README: bug reports via Issues (screenshots welcome) + Buy Me a Coffee / craft beer

</details>

<details>
<summary><strong>Dev 0.0.2</strong></summary>

- Fixed mines toggle: populated minefield data and update markers immediately when enabling/disabling **MINES**
- Updated branding assets (banner / logo)

</details>

<details>
<summary><strong>Dev 0.0.1</strong></summary>

- Initial public release

</details>

---

## What this does not do

- No loot, enemies, or continuous tracking without screenshots
- Does not read your live PMC quest journal from logs
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
| **Support**             | [anomalylabstudio@gmail.com](mailto:anomalylabstudio@gmail.com)                                                                                                                                     |
| **Product**             | Tarkovy · Dev 0.1.6                                                                                                                                                                                 |
| **SVG maps / extracts** | [tarkov.dev](https://tarkov.dev) · [tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)                                                                                        |
| **Map marker icons**    | [tarkov.dev](https://tarkov.dev) · [tarkov-dev](https://github.com/the-hideout/tarkov-dev/tree/main/public/maps/interactive)                                                                          |
| **Approach references** | [TarkovMapTracker](https://github.com/M4elstr0m/TarkovMapTracker) · [TarkovMonitor](https://github.com/the-hideout/TarkovMonitor) · [Sayser TarkovTracker](https://github.com/sayser/TarkovTracker) |


Escape from Tarkov © Battlestate Games. Tarkovy is an independent, unofficial project.

---

<p align="center">
  <img src="docs/tarkovy-icon.png" alt="Tarkovy" width="72" />
  <br />
  <sub>Tarkovy · by Anomaly Labs</sub>
</p>
