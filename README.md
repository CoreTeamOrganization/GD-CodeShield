# ⚡ GD CodeShield

**One package. Two tools. Ship cleaner games.**

GD CodeShield is Game District's internal Unity editor toolkit that catches code quality issues and SDK misconfigurations before they reach production. Open it from `Tools → GD CodeShield` and pick your tool from the hub launcher.

---

## Install via UPM (Git URL)

In Unity: `Window → Package Manager → + → Add package from git URL`

```
https://github.com/GameDistrict/gd-codeshield.git#2.0.0
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
- **Principle badges** — SRP · OCP · LSP · ISP pills in the top bar

### AI Fix Generation

With a Claude API key set, each violation gets a **Generate Fix** button:

1. Claude reads the violating code in context
2. Proposes a refactored version with an explanation
3. You review the diff before applying anything
4. A **Behavioural Contract Check** runs automatically — verifies all public methods are preserved before applying
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

## Tool 2 — GD Checklist

> *Full release checklist — SDK keys, Player Settings, Build config, and manual device verification. Single scan, everything in one place.*

### What it checks

GD Checklist runs two types of checks in a single scan:

**Auto-checked** — reads your project files and settings directly, no manual work:

| Tab | What it validates |
|---|---|
| **AppLovin / AdMob** | SDK key, AdMob App IDs (Android + iOS) |
| **Metica** | API keys and App IDs (Android + iOS) |
| **Adjust** | App tokens, environment (must be Production for release), log level |
| **AppMetrica** | API keys, crash reporting, session tracking, log settings |
| **Firebase** | `google-services.json` + `GoogleService-Info.plist` present, network settings |
| **Ad Units** | All ad unit IDs per network (Interstitial, Rewarded, Banner, MRec, AppOpen) |
| **Pre-Release** | App version, bundle code, Graphics API = OpenGLES3, Require ES3.1 unchecked, Unity Services connected, Firebase files present, Adjust environment = Production, AppLovin Max Terms + Ad Review unchecked, AppMetrica auto-collection off |
| **Build** | Create symbols.zip = Public, Compression = LZ4HC |
| **Manual** | 20 device verification items — Debugging, Dashboards, Post-Release |

Each field shows:
- ✅ **Pass** — correct value confirmed
- ❌ **Fail** — wrong value, with exact fix instruction
- ⚠️ **Warn** — value set but needs attention
- ☐ **Manual** — requires device verification, click **✓ Confirm** after checking

### How to use

1. Open `Tools → GD CodeShield` → click **SDK CHECKLIST**
2. Answer the setup question and select your SDKs (once only)
3. Click **SCAN PROJECT**
4. Work through each tab — fix auto-detected issues, confirm manual items on device

### First-time setup

On first open, the tool asks: **are you using the GD SDK?**

Both paths go to the same SDK selection screen — the difference is how it pre-fills:

**GD SDK project** (`Assets/Configurations/SDKConfiguration.asset` present):
- SDKs are auto-detected from your project and pre-ticked
- You can uncheck any SDK before confirming
- Falls back to broad project-wide search if asset isn't at the expected GD path

**External / non-GD project:**
- All SDKs unchecked by default — tick only what your project uses
- Only selected SDKs are scanned — no false positives

You can always return to this screen via **⚙ Change Setup** in the top bar. The setup screen also has a **← Back** button to return to the previous screen.

### Ad Units tab — respects your selection

The Ad Units tab only scans networks you selected during setup. If you unchecked Metica, no Metica ad unit rows appear. If you unchecked AppLovin, AppLovin and AdMob ad unit rows are skipped.

### Manual tab

20 items that can only be verified on a real device, grouped by category:

| Category | Items |
|---|---|
| **Monetization** | Firebase remote config updated, test + real ads verified, AppOpen from 2nd launch only, IAP working, consent + privacy policy current |
| **Debugging** | Adjust production + sandbox verified, internet panel shows correctly |
| **Permissions** | APK permissions checked via analyzer tool |
| **Dashboards** | Adjust testing console, Firebase DebugView, AppMetrica events |
| **Submission** | Symbol files provided with build |
| **Post-Release** | AppMetrica users + revenue normal, Firebase revenue + installs normal, Adjust installs + revenue normal, Play Console Android vitals, AppLovin ad units active + view rate consistent |

Each item has a **✓ Confirm** button — tap it after verifying on device. Confirmed items turn green. Use **↺ Undo** to unconfirm if needed.

### JSON Import

Paste your expected SDK config JSON on the home screen to compare live project values against an expected config. Useful for catching drift between what was agreed and what was actually implemented.

### Rescan and reset

- **↺ Rescan** — reruns the full scan without leaving the results view
- **⚙ Change Setup** — resets SDK selection if your project's stack changes

---

## Hub Launcher

The hub (`Tools → GD CodeShield`) is a fixed 640×420 launcher window:

- Game icon wallpaper loaded async from `gamedistrict.co` (16 icons, parallel, 8s timeout — silent fallback if offline)
- Animated cards — hover to see glow, underline expansion, and button fill
- Dynamic version badge reads live from PackageManager — always shows the installed version
- Yellow card = SOLID Review · Green card = GD Checklist

---

## Changelog

### [2.0.0]
- **GD Checklist completely overhauled** — merged SDK key validation with full release checklist
- Added Pre-Release tab: app version, bundle code, GraphicsAPI, Unity Services, Firebase files, Adjust environment, AppLovin flags, AppMetrica auto-collection
- Added Build tab: symbols public, LZ4HC compression
- Added Manual tab: 20 device verification items with ✓ Confirm / ↺ Undo per item
- Setup screen now shown for both GD SDK and non-GD SDK users
- GD SDK path: SDKs auto-detected and pre-ticked, developer can adjust before confirming
- Added ← Back button on setup screen
- Fixed "GAME DISTRICT" label overlapping buttons in top bar
- Ad Units tab now only scans networks selected during setup
- AssetScanner falls back to broad project search if asset not at expected GD path
- Fixed System.Linq missing from SDKConfig.cs

### [1.0.4]
- Hub background updated to GD logo charcoal (#3A3A3A)

### [1.0.3]
- Version badge reads live from PackageManager — no longer hardcoded
- Hub title: "GD" in yellow, "CODESHIELD" in white

### [1.0.2]
- Card icons redrawn as pure IMGUI vector shapes — no Texture2D, no external assets
- SOLID Review card: shield outline with bold S letterform
- GD Checklist card: 3-row checklist with tick marks

### [1.0.1]
- Added card icons for both tools

### [1.0.0]
- Initial release combining GD SOLID Review and GD Checklist
- GD CodeShield hub launcher (`Tools → GD CodeShield`)
- Animated hover cards, game icon wallpaper (async, silent, timeout-safe)
- Single UPM package `com.gamedistrict.codeshield`, single asmdef `GD.CodeShield.Editor`
