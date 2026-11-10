# Night of Revenge Archipelago

Archipelago multiworld support, an item/enemy randomizer and a full English translation for
**Night of Revenge 1.0.7** (D-lis). BepInEx 5 mod written in C#.

> Status: **Milestone 0 – research tooling.** Nothing playable yet. See [PLAN.md](PLAN.md).

## Requirements

- A legally owned copy of Night of Revenge **1.0.7**
- BepInEx 5.4.x (x64, Mono)
- For development: .NET SDK 8+ (10 recommended). Python 3.11+ later for the apworld.

## Repository layout

| Path | Contents |
|---|---|
| `src/NoR.Core` | Shared runtime helpers (net35): game object access, JSON writer, reflection serializer |
| `src/NoR.DataDumper` | Developer-only in-game research plugin (scene dumps, flag change log) |
| `tools/NoR.SceneScanner` | Offline scanner for the game's asset files (AssetsTools.NET) |
| `tools/NoR.DataBuilder` | Builds `data/*.json` tables from the scanner output |
| `data/` | Generated item/location tables and report |
| `docs/` | Research notes and guides |
| `apworld/` | Archipelago world (generated, later milestone) |

## Building

```powershell
# Game path defaults to D:\Games\H-Games\NightofRevenge_1.07; override per machine:
#   dotnet build -p:GamePath="E:\Games\NightofRevenge_1.07"
# or create Directory.Build.user.props with a <GamePath> property.
dotnet build NoR.Archipelago.slnx
```

In-game plugins are copied into `<game>\BepInEx\plugins\<Project>\` after each build
(disable with `-p:DeployToGame=false`).

## Regenerating data

```powershell
# 1. Offline scan of all scenes and asset files (needs .tools\classdata.tpk from UABEA)
dotnet run --project tools\NoR.SceneScanner -c Release -- --game "<game dir>" --out dump\static
# 2. Build data tables
dotnet run --project tools\NoR.DataBuilder -- --dump dump\static --out data
```

`dump/` contains extracted game data and is git-ignored. Never commit game files.

## Legal

- Code: MIT (see [LICENSE](LICENSE)).
- English translation data: CC BY-NC 4.0 (when added).
- Night of Revenge, its story, original Japanese text and all game assets are (c) D-lis.
  This project distributes no game files; a legally owned copy of the game is required.
- Third-party components: see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
