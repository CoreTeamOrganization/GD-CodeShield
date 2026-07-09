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
    SolidAnalyzer.cs                # Regex-based SRP/OCP/LSP/ISP detector + RatingEngine (namespace: SolidAgent)
    SolidAgentWindow.cs             # SOLID Review UI — sidebar + detail + AI fix
    AIFixGenerator.cs               # Claude API for AI fix suggestions
    SolidReportExporter.cs          # Word Doc (.docx) export via bundled Node.js
    SolidTelemetry.cs               # Posts scan results to Apps Script webhook → Google Sheet dashboard
    ContractChecker.cs              # Fix-safety check: AI fix must preserve public API (not an LSP scanner)
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
3. Scoring: 1-5 per principle — density-based (SonarQube-style): severity-weighted findings per scanned file (High=3, Med=2, Low=1); 0 findings=5, ≤0.10=4, ≤0.40=3, ≤1.00=2, else 1, plus small-count floors so a few non-High findings never tank a small project. All-Low findings floor at 4 — notes inform, never punish. `PrincipleRating.Density` is the continuous number shown next to the stars in the sidebar. Comments are stripped (offset-preserving, strings kept) before all detection — never regex-match raw source
4. AI fixes via `AIFixGenerator` → Claude API (key in EditorPrefs)
5. Export via `SolidReportExporter` → `.docx` using bundled Node.js + docx package
6. `SolidTelemetry.ReportScanCompleted()` fires after every scan — fire-and-forget POST, swallows all failures silently (a dead webhook produces no error anywhere)

### Detection Rules (v1.5.0 — cohesion signal added 2026-07-09; base rework 2026-07-07)
- **SRP** three-signal ladder: **High** = ≥2 method-name concern groups (each ≥2 methods) + ≥2 API families (`DependencySignals`: Audio, UI, Persistence, Animation, Physics, Network, SceneFlow) + ≥2 disjoint LCOM4 cohesion clusters. **Medium** = names + APIs agree, cohesion not computable. **Low** = single signal, cohesion veto (1 cluster = naming coincidence, capped at Low), or size note (>15 non-lifecycle methods). Cohesion (`ComputeCohesion`) clusters non-lifecycle methods connected by shared instance fields or direct calls; only trusted when ≥3 analyzable methods cover ≥60% of the class (Unity code works through transform/statics, so coverage is often too low — that's the Medium path). Lifecycle methods excluded from clustering (Awake/Start wire everything and would glue clusters). **Bare method count must never become Medium+ again** — that false positive is what broke team trust in v1.3. Delegation/orchestrator classes (≥70% one-line delegating methods) are exempt entirely.
- **OCP**: `switch` on type-like var (≥3 cases, counted inside the switch's own block only — not to end of class) + string `if/else` chains (≥3 branches within 3 lines of each other — scattered comparisons don't count).
- **LSP**: throw-only bodies (block or `=>` expression, NotImplemented/NotSupported) High; empty overrides (`override ... { }`) Medium. Skips methods already covered by an ISP fat-interface finding (one root cause = one finding, never two).
- **ISP**: interface >5 methods Medium; implementor with ≥3 throwing methods and ≥50% ratio High. `CheckISP_Implementor` runs BEFORE `CheckLSP` and returns the covered method-name set.
- Analyzer emits all three severities — Low exists since v1.4.0 and every UI/exporter path handles it.

### Testing SolidAnalyzer
`SolidAnalyzer.cs` is pure C# (no Unity refs) — compile it standalone with `dotnet` in a scratch classlib + fixture `.cs` files to regression-test detector changes without opening Unity. Score changes should always be verified this way before shipping.

### Score History
Scores from ≤v1.3.0 are NOT comparable to v1.4.0+ (old scale: 4 was unreachable, any Medium capped at 3, no size normalization). Telemetry sheet rows before 2026-07-07 are old-scale; remapped estimates in `GD_CodeShield_Telemetry_Remapped.xlsx` (Downloads).

### Telemetry / Support Webhook
One Apps Script URL is duplicated in THREE files: `SolidTelemetry.cs`, `ChecklistTelemetry.cs`, `ContactSupportWindow.cs` — update all three together (should be extracted to a shared constant). As of 2026-07-07 the deployed webhook returns 405 "unable to open the file" (dead deployment — likely redeployed under a new URL); no scans reach the sheet until it's fixed.

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
