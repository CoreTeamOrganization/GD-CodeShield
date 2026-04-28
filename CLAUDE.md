# GD CodeShield

Unity editor toolkit by Game District. Two tools in one UPM package (`com.gamedistrict.codeshield`), opened via `Tools > GD CodeShield`.

## Project Structure

```
Editor/
  GD.CodeShield.Editor.asmdef      # Single asmdef, Editor-only platform
  Hub/
    GDCodeShieldHub.cs              # Launcher window (namespace: GDCodeShield)
    ContactSupportWindow.cs         # Webhook-based support form
  Checklist/
    ChecklistWindow.cs              # 9-tab scan results UI (namespace: GDChecklist)
    AssetScanner.cs                 # SDK field extraction + code reverse engineering
    ReleaseScanner.cs               # Pre-Release / Build / Manual checks
    SDKConfig.cs                    # GD vs non-GD SDK detection, EditorPrefs storage
    ScanProgress.cs                 # Progress bar wrapper
    KeyValidator.cs                 # Key format validation
  SolidReview/
    SolidAnalyzer.cs                # Regex-based SRP/OCP/LSP/ISP detector (namespace: SolidAgent)
    SolidAgentWindow.cs             # SOLID Review UI — sidebar + detail + AI fix
    AIFixGenerator.cs               # Claude API for AI fix suggestions
    SolidReportExporter.cs          # Word Doc (.docx) export via bundled Node.js
    ContractChecker.cs              # LSP contract validation
    RegressionHarness.cs            # Post-fix regression scaffolding
    SolidAgentSetup.cs              # Setup placeholder
```

## Requirements

- Unity 2021.3+
- `com.unity.nuget.newtonsoft-json` 3.0.2 (auto-installed)
- Claude API key for AI fixes in SOLID Review (optional)

## Namespaces

- **GDChecklist** — Checklist tool (ChecklistWindow, AssetScanner, ReleaseScanner, SDKConfig, ScanProgress, KeyValidator)
- **SolidAgent** — SOLID Review tool (SolidAnalyzer, SolidAgentWindow, AIFixGenerator, etc.)
- **GDCodeShield** — Hub launcher only

## Tool 1: GD Checklist

### Data Flow
1. `ChecklistWindow` → setup screen → `SDKConfig.BuildScanConfig()` → `SDKScanConfig`
2. `AssetScanner.Scan(dataPath, json, config)` → `ScanResult` with `List<FieldResult>`
3. `ReleaseScanner.AppendChecks(result, dataPath)` → appends Pre-Release, Build, Manual items

### Tab Indices
- 0=AppLovin, 1=Metica, 2=Adjust, 3=AppMetrica, 4=Firebase, 5=AdUnits (AssetScanner)
- 6=Pre-Release, 7=Build, 8=Manual (ReleaseScanner)

### FieldStatus Enum
Match (green), Mismatch (red), Missing (orange), NotConfigured, Ignored, Empty

### Two Scan Paths
- **GD SDK**: Assets at `Assets/GDMonetization/Runtime/Resources/Configurations/*.asset` — no filesystem scan
- **Non-GD**: Full `*.asset` + `*.cs` scan with file cache (`InitScanCache`), reverse engineers SDK init calls via `TraceVariable()`

### ReleaseScanner Sections
- **Pre-Release**: App version, bundle code, GraphicsAPI=OpenGLES3, ES3.1 unchecked, Unity Services, Firebase files, Adjust env=Production, AppLovin flags, AppMetrica auto-collection
- **Build**: symbols.zip=Public (API), Compression=LZ4HC (reflection — internal API)
- **Manual**: 20 device verification items (YamlKey="manual" sentinel)

## Tool 2: SOLID Review

### Data Flow
1. `SolidAnalyzer` scans `.cs` files → `FileAnalysisResult` with `List<Violation>`
2. Checks SRP, OCP, LSP, ISP (DIP excluded — tight coupling OK for casual mobile games)
3. Scoring: 1-5 per principle, based on GD Easy Rating Guide
4. AI fixes via `AIFixGenerator` → Claude API (key in EditorPrefs)
5. Export via `SolidReportExporter` → `.docx` using bundled Node.js + docx package

## Known API Constraints

- `EditorUserBuildSettings.GetCompressionType()` and `Compression` enum are **internal** in Unity 2021.3 — use reflection (`BindingFlags.NonPublic`) not direct calls
- `EditorUserBuildSettings.androidCreateSymbols` is public — use directly with `#if UNITY_2021_1_OR_NEWER`
- `Library/EditorUserBuildSettings.asset` is binary — cannot parse with string matching
- `PlayerSettings.GetGraphicsAPIs()` is public

## Config Storage

- SDK selections: `EditorPrefs` keys `GDChecklist_SDK_*`
- Claude API key: `EditorPrefs` (never in source control)
- Config cache: `SDKConfig._cachedConfig` (in-memory, invalidated on setup change)

## Conventions

- All UI uses IMGUI (no UIToolkit) with GD dark theme (yellow accent #FFD300)
- YAML reading: simple regex `key:\s*(.+)` on Unity serialized .asset files
- SDK auto-excluded from SOLID scans (Adjust, AppMetrica, MaxSdk, Firebase, etc.)
- File paths normalized with `Replace('\\', '/')`
