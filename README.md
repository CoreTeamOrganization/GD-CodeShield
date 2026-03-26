# ⚡ GD CodeShield

**One package. Two tools. Ship cleaner games.**

GD CodeShield is Game District's internal Unity editor toolkit that catches code quality issues and SDK misconfigurations before they reach production. Open it from `Tools → GD CodeShield` and pick your tool from the hub launcher.

---

## Install via UPM (Git URL)

In Unity: `Window → Package Manager → + → Add package from git URL`

```
https://github.com/GameDistrict/gd-codeshield.git
```

**Requirements:**
- Unity 2021.3 or newer
- `com.unity.nuget.newtonsoft-json` 3.0.2 (auto-installed as dependency)
- Claude API key for AI fix generation in SOLID Review (optional — scanning is always free)

---

## Open

```
Tools → GD CodeShield
```

The hub launcher opens. Click either card to open the tool.

---

## Tool 1 — SOLID Review

> *Scans your C# scripts for SOLID principle violations and generates AI-powered fixes.*

### What it checks

SOLID Review analyses every `.cs` file under your Assets folder (or a subfolder you choose) against four SOLID principles:

| Principle | What it catches |
|---|---|
| **SRP** — Single Responsibility | Classes doing too many unrelated things — God classes, bloated managers |
| **OCP** — Open/Closed | `switch`/`if-else` chains that need touching every time you add a feature |
| **LSP** — Liskov Substitution | Subclasses that break the contract of their base class |
| **ISP** — Interface Segregation | Fat interfaces forcing classes to implement methods they don't need |

> DIP (Dependency Inversion) is intentionally excluded — tight coupling is acceptable in casual mobile games.

Each file gets a score of **1–5 per principle** based on the GD Easy Rating Guide. The sidebar lists all files ranked by total violations so you know where to focus first.

### How to use

1. Open `Tools → GD CodeShield` → click **SOLID REVIEW**
2. Optionally pick a subfolder to limit the scan scope (default: all Assets)
3. Click **START SOLID REVIEW** — scanning is free, no API key needed
4. Click any file in the sidebar to see its violations in the detail panel

### Results view

- **Sidebar** — all scanned files with per-file violation count and colour-coded severity
- **Detail panel** — exact line number, which rule is broken, and why
- **Principle badges** — SRP · OCP · LSP · ISP pills in the top bar show at a glance which principles have issues

### AI Fix Generation

With a Claude API key set, each violation gets a **Generate Fix** button:

1. Claude reads the violating code in context
2. Proposes a refactored version with an explanation
3. You review the diff before applying anything
4. A **Behavioural Contract Check** runs automatically — verifies all public methods are preserved before applying, protecting against breaking changes
5. After applying, a **Regression Test** compares behaviour before and after

> Cost confirmation is shown before every API call — no surprise charges.

### PDF Export

- **Project PDF** — full summary report across all scanned files, matching the GD DriveToDeliver format
- **File PDF** — per-file detailed report you can share directly with a developer

### Settings

- API key stored in `EditorPrefs` — never committed to source control
- Scan root persists between sessions
- SDK files auto-excluded from scanning (Adjust, AppMetrica, MaxSdk, Firebase, etc.)

---

## Tool 2 — SDK Checklist

> *Verifies that all SDK keys, App IDs, and network configurations are correctly set in your project.*

### What it checks

SDK Checklist scans your project's `.asset` files and validates configuration across six SDK categories:

| Tab | What it validates |
|---|---|
| **AppLovin / AdMob** | App IDs, banner / interstitial / rewarded Ad Unit IDs per platform |
| **Metica** | API keys and environment configuration |
| **Adjust** | App tokens, environment mode (sandbox vs production), event tokens |
| **AppMetrica** | API key presence and activation state |
| **Firebase** | `google-services.json` / `GoogleService-Info.plist` presence, project ID |
| **Ad Units** | All ad unit IDs populated, no placeholder values left |

Each field shows one of three states:

- ✅ **Match** — value present and valid
- ⚠️ **Mismatch** — value present but looks wrong (e.g. still in sandbox mode, placeholder detected)
- ❌ **Empty** — required field is missing entirely

### How to use

1. Open `Tools → GD CodeShield` → click **SDK CHECKLIST**
2. Complete the one-time setup (auto-skipped for GD SDK projects)
3. Click **Run Scan**
4. Review results per SDK tab — each field shows its current value and pass/fail status

### First-time setup

On first open the tool asks one question: **is this a GD SDK project?**

**GD SDK project** (has `Assets/Configurations/SDKConfiguration.asset`):
- Auto-detects which SDKs are active by reading your `NetworkSO` files directly
- No manual configuration needed — hit Scan immediately

**External / non-GD project:**
- Quick setup screen to tick which SDKs your project uses
- Only those SDKs are checked — no false positives for SDKs you don't have

### JSON Import

If your expected SDK config is tracked in a JSON file (e.g. from a config management system):

1. Click **Import from JSON** on the home screen
2. Paste your expected config JSON
3. Click **Import & Scan** — the tool compares live project values against your expected values

Useful for catching drift between what was agreed and what was actually implemented.

### Rescan and reset

- **↺ Rescan** — reruns the scan without leaving the results view
- **⚙ Change Setup** — resets SDK selection if your project's stack changes

---

## Hub Launcher

The hub (`Tools → GD CodeShield`) is a fixed 640×420 launcher window:

- Game icon wallpaper loaded async from `gamedistrict.co` (16 icons, parallel download, 8s timeout — silent fallback if offline)
- Animated cards — hover to see glow, expanding underline, and button fill
- Yellow card = SOLID Review, Green card = SDK Checklist

---

## Changelog

### [1.0.2]
- Card icons redrawn as pure IMGUI vector shapes — no Texture2D, no external assets
- SOLID Review card: shield outline with bold S letterform
- SDK Checklist card: 3-row checklist with tick marks and text lines

### [1.0.1]
- Added card icons for both tools

### [1.0.0]
- Initial unified release combining GD SOLID Review (v1.0.9) and GD Checklist
- New GD CodeShield hub launcher (`Tools → GD CodeShield`)
- Animated hover cards — glow, underline expansion, button fill on hover
- Game icon wallpaper (async, silent, timeout-safe)
- Single UPM package `com.gamedistrict.codeshield`, single asmdef `GD.CodeShield.Editor`
- Both individual tool MenuItems removed — Hub is the only entry point
