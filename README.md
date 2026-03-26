# GD CodeShield

**Game District developer quality tools in one package.**

## Install via UPM (Git URL)

In Unity: `Window → Package Manager → + → Add package from git URL`

```
https://github.com/CoreTeamOrganization/GD-CodeShield.git
```

## Open

```
Tools → GD CodeShield
```

The launcher opens a small hub window. Pick your tool:

| Tool | What it does |
|---|---|
| **SOLID Review** | Scans C# scripts for SRP, OCP, LSP, ISP violations and generates AI-powered fixes via Claude API |
| **SDK Checklist** | Verifies all SDK keys, App IDs, and network configuration across your project |

## Requirements

- Unity 2021.3+
- Internet access for SOLID Review AI fixes (Claude API key required)
- Game icon wallpaper loads from gamedistrict.co — works offline without it

## Changelog

### [1.0.0]
- Initial unified release combining GD SOLID Review and GD Checklist
- New GD CodeShield Hub launcher (`Tools → GD CodeShield`)
- Animated hover cards with game icon wallpaper
- Single UPM package, single asmdef
