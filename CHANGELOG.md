# Changelog
All notable changes to GD CodeShield are documented here.

## [Unreleased]
### Added
- **Preview report** button on the SOLID Review results screen, next to Download Word report. Opens an in-editor visual summary (modelled on Unity's Memory Profiler Summary tab): overall score bar, per-principle scores, violation distribution by principle, severity breakdown, and top files by violation count — each with a stacked bar and a colour-coded legend. The preview also has a one-click Download Word report shortcut.

### Fixed
- SOLID Review folder picker now scrolls through expanded subfolders. The scroll view height was computed from the top-level folder count only, so folders revealed by expanding a parent fell below the scrollable area and could not be reached.

## [1.3.0] - 2026-05-20
### Changed
- **Full visual redesign** across all CodeShield windows — Hub, SOLID Review, and SDK Checklist now share a unified editorial design language: cream surface, navy text, gold accents, Fraunces serif display, Inter UI font. Quieter, more readable, and consistent across the whole package.
- **Hub launcher** redesigned with two-column editorial layout: workspace overview on the left (with quick stats), tool entries on the right with hover affordances. Game District logo anchored bottom-left.
- **SOLID Review** redesigned with three editorial screens — principle picker / scanning progress / results — and a tabbed detail pane (Violation / Proposed fix / Claude Code) with proper code-block scrolling.
- **SDK Checklist** redesigned with three editorial screens — Welcome (GD SDK vs raw SDKs choice) / first-time setup (auto-detected SDK toggles) / scan report with consolidated tab strip. The 10-tab report and all field rows preserve their existing behaviour, restyled to the new palette.
- All CodeShield windows now resizable with a 900×600 minimum so they adapt to studio screen layouts.

### Added
- Fraunces (display) and Inter (UI) bundled as TTF assets — consistent typography across operating systems with graceful fallback to the system font if the assets are missing.
- Game District logo asset bundled in the Hub.
- Soft hover states (gold tint + slide-arrow affordances) on tool entries and principle cells.

### Fixed
- "Download Word report" and per-violation "File doc" buttons now open the generated Word document directly instead of revealing the folder.
- "Run in Claude Code" reliably launches Terminal with the prompt pre-loaded; the prompt is also placed on the clipboard as a fallback.
- Detail pane in SOLID Review now scrolls correctly when the affected code block is long — no more cut-off at the bottom.

## [1.2.0] - 2026-05-18
### Added
- **SDK Versions tab** — new first tab in Checklist showing all installed SDK versions auto-detected from your project:
  - **GD Monetization SDK** (when present) — version read from `MonetizationInitializeOnLoad.cs`
  - **AppLovin MAX** — Android SDK, iOS SDK, plus every mediation network adapter with their Android and iOS versions separately
  - **Metica** — version read from `MeticaSdk.cs` (asset install) or `.tgz` reference in `Packages/manifest.json`
  - **Adjust** — Unity Plugin, Android Native, iOS Native versions
  - **AppMetrica** — version detection from UPM manifest or asset files
  - **Firebase** — every installed module (Analytics, RemoteConfig, Messaging, etc.) with Unity, Android, and iOS versions per module
- SDK detection uses `Packages/manifest.json` (UPM, file:, .tgz) as primary source, then SDK folder constants as fallback — works for both UPM and `.unitypackage` installs

### Changed
- Per-SDK version headers removed from individual SDK tabs — the dedicated SDK Versions tab now consolidates all version info in one readable view, leaving SDK tabs focused on configuration validation

## [1.1.0] - 2026-05-18
### Added
- **Adjust Integration sub-tabs** — Adjust Checklist tab now has 6 sub-tabs (Config + Init Path / Manifest / Dependencies / iOS / Ad Revenue) for deeper integration validation alongside existing config checks
- Init Path scanner uses `EditorSceneManager` + reflection to read the live Adjust MonoBehaviour fields (including `startManually`) — accurate regardless of scene serialization format or prefab override structure
- Manifest scanner checks Android permissions (`INTERNET`, `ACCESS_NETWORK_STATE`, `AD_ID`) including `tools:node="remove"` detection
- Dependencies scanner checks EDM4U `*Dependencies.xml` for `play-services-ads-identifier`, `play-services-appset`, `installreferrer`
- iOS scanner validates `AdjustSettings.asset` framework flags (AdServices, AppTrackingTransparency, StoreKit) and `iOSUserTrackingUsageDescription`
- Ad Revenue scanner detects `Adjust.TrackAdRevenue` / `new AdjustAdRevenue` call sites and flags double-counting risk when paired with dashboard-side S2S
- Game name (`PlayerSettings.productName`) and Bundle ID (`Application.identifier`) now appear in the Word report cover

### Changed
- **Score labels are now target-aware** across both Editor UI and exported reports: On Target / Meets Target / Below Target / Needs Work / Critical (replaces Excellent / Very Good / Acceptable / Weak / Poor). Communicates required action instead of subjective quality
- **SOLID Word report inverted to light theme** — white page, dark text, light card surfaces. GD yellow retained as brand accent. Print-readable, no more dark-on-dark unreadable cells
- **Violation grouping** in Word reports — same-pattern violations across multiple classes collapse into a single grouped entry with class list underneath (e.g. 6 files with "X has multiple responsibilities" now show as one entry with all 6 class names, instead of 6 repeated lines)
- File / line references in reports are now bigger and clearly formatted (bold class name + file:line)

### Fixed
- Code scanner now filters out matches inside string literals and `//` `/* */` comments (eliminates false positives from regex patterns matching their own pattern strings)
- Adjust.cs path detection no longer hardcoded — uses `AssetDatabase.FindAssets` with `AdjustConfig.cs` sibling check so the SDK can live in any folder

## [1.0.9] - 2026-04-02
### Added
- Contact Support button in Hub footer — opens standalone window, sends message directly via webhook (no email client needed)
- AI disclaimer banner on Hub: "AI-Powered — Results may contain errors. Always review before applying."
- Non-GD SDK scan warning on Checklist home screen — alerts developer about extensive .cs file scanning
- Metica reverse engineering: finds `MeticaSdk.InitializeAsync`, traces `MeticaInitConfig` and `MeticaMediationInfo` arguments to actual values
- AdUnits code scan: traces `adUnitId` variables back to string values
- Adjust reverse engineering: finds `new AdjustConfig(...)`, traces each argument with file:line attribution
- File cache in AssetScanner — all .cs files read once into memory, O(1) lookups, no repeated filesystem traversals
- `EditorUtility.DisplayProgressBar` shown during non-GD scans

### Fixed
- Removed email address from Contact Support UI subtitle
- Package description corrected: removed incorrect PDF export mention
- README corrected: removed JSON Import section (not implemented) and internal Hub implementation details
- Git URL corrected to CoreTeamOrganization/GD-CodeShield

## [1.0.8] - 2026-03-01
### Changed
- Internal release

## [1.0.7] - 2026-02-01
### Changed
- Word Doc export replaces PDF export — reports now generate .docx files matching GD SOLID Review format
- Per-file report: Scores at a Glance table, per-principle breakdown, What to Fix table, priority page
- Project summary report: stats row, overall score, principle cards, violation breakdown
- docx npm package bundled inside DocxGen/node_modules/ — no global install required
- Node.js path resolved via explicit search (nvm, Homebrew, /usr/local/bin)
- Folder picker scroll fixed

## [2.0.0] - 2025-12-01
### Added
- GD Checklist completely overhauled — merged SDK key validation with full release checklist
- Pre-Release tab: app version, bundle code, GraphicsAPI, Unity Services, Firebase files, Adjust environment, AppLovin flags, AppMetrica auto-collection
- Build tab: symbols public, LZ4HC compression
- Manual tab: 20 device verification items with Confirm / Undo per item
- Setup screen for both GD SDK and non-GD SDK users
- Back button on setup screen

### Fixed
- Ad Units tab only scans networks selected during setup
- AssetScanner falls back to broad project search if asset not at expected GD path
- System.Linq missing from SDKConfig.cs

## [1.0.4] - 2025-10-01
### Changed
- Hub background updated to GD logo charcoal (#3A3A3A)

## [1.0.3] - 2025-09-01
### Changed
- Version badge reads live from PackageManager
- Hub title: "GD" in yellow, "CODESHIELD" in white

## [1.0.2] - 2025-08-01
### Changed
- Card icons redrawn as pure IMGUI vector shapes
- SOLID Review card: shield outline with S letterform
- GD Checklist card: checklist with tick marks

## [1.0.1] - 2025-07-01
### Added
- Card icons for both tools

## [1.0.0] - 2025-06-01
### Added
- Initial release — GD SOLID Review and GD Checklist
- GD CodeShield hub launcher (Tools → GD CodeShield)
- Single UPM package com.gamedistrict.codeshield
