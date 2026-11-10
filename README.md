# Night of Revenge Archipelago

Archipelago multiworld support, an item/enemy randomizer and a full English translation for
**Night of Revenge 1.0.7** (D-lis). BepInEx 5 mod written in C#.

> Status: **0.1.0 - first playable version.** Archipelago multiworld verified on archipelago.gg.
> Key-door logic is experimental (option `shuffle_keys`, off by default). See [CHANGELOG.md](CHANGELOG.md).

## Features

- **Archipelago multiworld**: 245+ locations (floor items, chests, magic pickups, Witch's Market slots, boss drops,
  NPC rewards, Vendetta's trades), goal = defeat the Witch in the Thorns, DeathLink (with "defeat state" mode),
  traps, chat/hints in game, automatic reconnect. Works with archipelago.gg (built-in TLS helper).
- **Offline randomizer** (no server needed): seeded item shuffle and spoiler log.
- **Enemy randomizer**: every regular enemy can be any enemy of the game (bosses unchanged).
- **Witch's Market**: a working shop at every save point.
- **Full English translation** of items, UI and dialogue (LLM draft, under review).
- **Widescreen / ultrawide** support (21:9, 32:9), **death skip** (skip the defeat animation after a real death),
  backup console for stuck players.

## Playing

See the **[setup guide](apworld/nightofrevenge/docs/setup_en.md)**: install BepInEx 5 and the release zip, generate
with `nightofrevenge.apworld`, host on archipelago.gg, connect from the title screen.

## Requirements

- A legally owned copy of Night of Revenge **1.0.7**
- BepInEx 5.4.x (x64, Mono)
- For development: .NET SDK 8+ (10 recommended), Python 3.12.10+/3.13 and the Archipelago source for the apworld.

## Repository layout

| Path | Contents |
|---|---|
| `src/NoR.Core` | Shared runtime helpers (net35): game access, fonts, JSON writer |
| `src/NoR.Randomizer` | Offline randomizer, enemy pool, shop, rewards, traps, death skip, console |
| `src/NoR.Archipelago` | Archipelago client (connection, items, checks, DeathLink, UI) |
| `src/NoR.Localization` | English translation runtime |
| `src/NoR.Widescreen` | Resolution / aspect ratio support |
| `src/NoR.DataDumper` | Developer-only research plugin (scene dumps, flag change log) - not in releases |
| `tools/NoR.SceneScanner` | Offline scanner for the game's asset files (AssetsTools.NET) |
| `tools/NoR.DataBuilder` | Builds data tables from the scanner output |
| `tools/NoR.TextExtractor` | Extracts translatable text and translation batches |
| `tools/NoR.ApWorldGen` | Generates the apworld data module and the client id map |
| `tools/NoR.ApBridge` | TLS bridge used by the client (game engine lacks TLS 1.2) |
| `apworld/nightofrevenge` | Archipelago world |
| `data/` | Hand-made tables, translations, persistent Archipelago ids |
| `tests/` | Client/apworld mirror test |
| `docs/` | Research notes, translation glossary, test guides |

## Building

```powershell
# Game path defaults to D:\Games\H-Games\NightofRevenge_1.07; override per machine:
#   dotnet build -p:GamePath="E:\Games\NightofRevenge_1.07"
# or create Directory.Build.user.props with a <GamePath> property.
dotnet build NoR.Archipelago.slnx
```

In-game plugins are copied into `<game>\BepInEx\plugins\<Project>\` after each build
(disable with `-p:DeployToGame=false`).

## Regenerating data (required after a fresh clone)

Some tables are generated from the game's own files and contain Japanese game text, so they are not in the
repository (`data/items.json`, `data/runtime/locations.tsv`, `data/runtime/enemies.tsv`, ...). Generate them from
your installed game before building:

```powershell
# 1. Offline scan of all scenes and asset files (needs .tools\classdata.tpk from UABEA)
dotnet run --project tools\NoR.SceneScanner -c Release -- --game "<game dir>" --out dump\static
# 2. Build data tables
dotnet run --project tools\NoR.DataBuilder -- --dump dump\static --out data
# 3. Archipelago world data + client id map (ids are persistent in data/ap_ids.json)
dotnet run --project tools\NoR.ApWorldGen
# 4. Package the apworld / a release
pwsh tools\package-apworld.ps1
pwsh tools\package-release.ps1 -Version 0.1.0
```

`dump/` contains extracted game data and is git-ignored. Never commit game files.

## Tests

```powershell
# Client vs apworld location sets must match for every option combination
dotnet build tests\MirrorTest
.tools\ap-venv\Scripts\python.exe tests\mirror_test.py
```

## In-game keys

| Key | Function |
|---|---|
| F1 | Archipelago connection window (title screen) |
| B / gamepad item-switch | Witch's Market at a save point |
| ` or F12 | Backup console: `give <code>`, `give key lab\|pilgrimage\|rooftop`, `find <name>`, `karma <n>`, `idea <n>` |
| G / hold Jump 2 s | Skip and respawn after a real death or DeathLink |

## Legal

- Code: MIT (see [LICENSE](LICENSE)).
- English translation data: CC BY-NC 4.0 (when added).
- Night of Revenge, its story, original Japanese text and all game assets are (c) D-lis.
  This project distributes no game files; a legally owned copy of the game is required.
- Third-party components: see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
