# Changelog
All notable changes to GD CodeShield are documented here.

## [Unreleased]

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

## [1.0.6] - 2025-12-01
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

## [1.0.5] - 2025-10-01
### Changed
- Hub background updated to GD logo charcoal (#3A3A3A)

## [1.0.2] - 2025-09-01
### Changed
- Version badge reads live from PackageManager
- Hub title: "GD" in yellow, "CODESHIELD" in white

## [1.0.0] - 2025-06-01
### Added
- Initial release — GD SOLID Review and GD Checklist
- GD CodeShield hub launcher (Tools → GD CodeShield)
- Single UPM package com.gamedistrict.codeshield
- Card icons for both tools
- Card icons redrawn as pure IMGUI vector shapes
- SOLID Review card: shield outline with S letterform
- GD Checklist card: checklist with tick marks

