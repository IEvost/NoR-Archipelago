# Night of Revenge 1.0.7 — Archipelago + Randomizer + English Translation

Development plan (draft v0.4, 2026-11-10). Status: **decisions D1–D13 taken (section 10); no blocking questions**.

---

## 1. Verified facts about the target (from the local install)

| Fact | Value | Source |
|---|---|---|
| Install path | `D:\Games\H-Games\NightofRevenge_1.07` | user |
| Engine | Unity **5.6.7f1**, **Mono** scripting backend (not IL2CPP) | `BepInEx\LogOutput.log` |
| Runtime | CLR 2.0 → mods must target **.NET Framework 3.5** | `LogOutput.log` |
| Mod loader | BepInEx **5.4.18** already installed (HarmonyX, MonoMod) | `BepInEx\core` |
| Existing text mod | XUnity.AutoTranslator 5.1.0 + ResourceRedirector 2.0.0, ~3,750-line `en` cache that looks partially hand-edited | `BepInEx\Translation\en\Text` |
| Game code | `Assembly-CSharp.dll` (~6 MB, ~2,000 classes once decompiled), no obfuscation | ILSpy |
| Data system | **GameDataEditor (GDE)** JSON (`gde_data`), item types: `weapon` (271 keys incl. upgrade tiers), `UseItem` (68), `crestring` (25), `magic` (13) | `GDEItemKeys.cs` |
| Save system | **Easy Save 2** (`ES2.dll`), tag-based keys in `SaveData\SaveDataNN.txt` | `SaveFile.cs` |
| Built-in localization | **None.** Japanese only, many hard-coded literals | grep of decompiled code |
| Dialogue | `TextController.scenarios : string[]` serialized in scene objects (Pixel Crushers Dialogue System DLL ships but is not used by game code) | `TextController.cs` |
| Text rendering | TextMeshPro (old Unity 5.6-era TMP; some `SetText` overloads missing) | XUnity warnings |

### Key game classes found (hook targets)

- **Locations / pickups:** `Pickup_item` (weapon / ring / use-item / costume IDs, guarded by `game_fragmng._TreasureItemArrayStage[stage, flag]`), `TreasureBox` (+ `Treasureboxdialog`, key kinds 0–3: none/bronze/silver/gold), `Pickup_magic` (`_GetMagic_GUN/Fire/Lightning/Sacred/darkness[5]`), `Pickup_Idea` (EXP), `Pickup_Dropitem`/`EnemyColDropsystem` (enemy drops), `Shopmng`/`ShopInventory` (shop), `TradeMng` (`_Tradeflag[20]`), boss flags `_BOSS[20]`, `_BossIdeaflag[20]`, slave rescues `_helpslave[50]`, titles `_GetTitleList[50]`.
- **Item granting:** `ItemGetCanvas.item_get(...)`, `ItemInventory.UseItemAddorCount`, `weaponItemID`, `CrestringID`, `game_fragmng` magic arrays.
- **Enemies:** `Spawnenemy` (one serialized `GameObject enemy` prefab per spawn point, gated by `SpawnParent._SpawnPoint[]`), `Enemy_create`, `GAenemyCreate`, bosses via `BossSpawn`, `LoadBossScene*`.
- **Flags:** `game_fragmng` (central flag manager — everything is fixed-size bool arrays).
- **Movement:** `playercon.jumpcount = 1` is the default, so the air jump does **not** look item-gated. Progression gating is probably keys / bosses / events. **Needs a playthrough check.**

---

## 2. Goals and scope

1. **Archipelago (AP) multiworld client**: NoR sends location checks and receives items from other games' worlds.
2. **Standalone offline randomizer**: the same item shuffle without a server, driven by a seed.
3. **Enemy randomizer**: shuffle regular enemies (and optionally bosses).
4. **Full English localization**: every Japanese string (UI, items, descriptions, dialogue, system messages, textures that contain text) has a curated English version.
5. All mod UI, configs, logs, docs and code identifiers in **English only**.

Out of scope unless requested: other game versions (1.0.6 and earlier), other languages, gameplay rebalancing, new content.

---

## 3. Architecture

```
NoR-Archipelago/
├─ src/
│  ├─ NoR.Core/              # shared: game-state access, flag/ID tables, Harmony utils, logging
│  ├─ NoR.Randomizer/        # seed → placement; offline mode; enemy shuffle
│  ├─ NoR.Archipelago/       # AP client (Archipelago.MultiClient.Net), connect UI, item queue, DeathLink
│  ├─ NoR.Localization/      # English text engine (own) — or XUnity data pack, see 6.3
│  └─ NoR.Tools/             # .NET 8+ console tools: data dumper, ID-table generator, translation extractor/packer
├─ apworld/nightofrevenge/   # Python Archipelago world (REQUIRED by Archipelago — see Q1)
├─ data/
│  ├─ locations.json         # generated + hand-verified location table
│  ├─ items.json             # generated from GDE dump
│  ├─ logic.json             # region graph + requirements (shared by C# offline mode and Python apworld)
│  └─ translation/en/*.json  # string tables
├─ lib/                      # references copied from the game (NOT committed to git)
└─ docs/
```

- Runtime plugins: **C#, `net35`, BepInEx 5 plugins**, Harmony patches. One DLL per module or one merged DLL (recommendation: separate DLLs, one BepInEx plugin GUID each, `NoR.Core` as a dependency).
- Tools: modern .NET (SDK 10 is installed) — they run outside the game.
- Single source of truth for IDs and logic: `data/*.json`; both the C# side and the Python apworld are generated / read from it so they can never drift.

---

## 4. Phase 0 — Research & tooling (prerequisite for everything)

| # | Task | Output |
|---|---|---|
| 0.1 | Set up repo, `.csproj` targeting net35, reference `Assembly-CSharp.dll`, `UnityEngine.dll`, `BepInEx.dll`, `0Harmony.dll` from the game folder via a `GamePath` MSBuild property; post-build copy into `BepInEx\plugins` | buildable empty plugin |
| 0.2 | **Data dumper plugin** (runs in-game): dump GDE `gde_data` (all weapons/use items/rings/magic with names, descriptions, IDs), and on each scene load dump every `Pickup_item`, `TreasureBox`, `Pickup_magic`, `Spawnenemy`, `BossSpawn`, shop, trade, NPC with scene name, object path, serialized fields, position | `dump/*.json` |
| 0.3 | **Scene scanner (offline)**: use AssetsTools.NET to read `level0..level106` + `sharedassets*.assets` without running the game, to find every component instance above (catches objects that never get loaded in a normal run) | cross-checked location list |
| 0.4 | Map scene index → area name (scene list from `globalgamemanagers`) | `areas.json` |
| 0.5 | **Progression research**: full playthrough with a logging plugin recording which flags, keys, bosses and events open which doors/elevators/areas (`_GimmickArraystage`, `_ElevatorArrayStage`, `_EvArraystage`, `_MainEVflag`) | first draft of `logic.json` |
| 0.6 | Confirm Archipelago.MultiClient.Net works on net35 / Unity 5.6 Mono (websocket + TLS for `wss://archipelago.gg`). Old Mono TLS is a known risk — see section 9 | spike result |

---

## 5. Phase 1 — Randomizer core

### 5.1 Location model
Each location gets a stable `LocationKey` = `scene:component:flagStage:flagIndex` (e.g. `level12:Pickup_item:3:7`) and a numeric AP ID (`base + n`).

Candidate location categories (each can be toggled via options):

| Category | Detection hook | Notes |
|---|---|---|
| Floor items | `Pickup_item.Update` (prefix, when `stayflag && _key_submit`) | skip original grant, report check, show custom "Sent X to Y" panel |
| Chests | `TreasureBox` item reveal | keyed chests need key items in logic |
| Magic pickups | `Pickup_magic.Update` | |
| Boss defeats | `game_fragmng._BOSS[i]` write / `BossDeathEvent` | |
| Boss "Idea" rewards | `_BossIdeaflag` | |
| Shop slots | `Shopmng` buy | optional; shop prices/stock handling |
| Trades | `TradeMng` / `_Tradeflag` | optional |
| Slave rescues | `_helpslave` | optional, requires review of what is rewarded |
| Titles / achievements | `_GetTitleList` | optional, could be too grindy |
| Enemy drops / Idea orbs | — | **not** recommended as locations (random, repeatable) |

### 5.2 Item model
- Progression: keys (bronze/silver/gold if they are items), any event key items, magic schools (if they gate anything), whatever 0.5 reveals.
- Useful: weapons (base tier only; upgrade tiers `wp_x1..x5` stay vanilla upgrades), rings, costumes.
- Filler: consumables, Idea (EXP) bundles.
- Traps (optional): e.g. status effects the game already has (needs selection — see Q7).

### 5.3 Granting items
A central `ItemGranter` in `NoR.Core` that calls the game's own APIs (`ItemInventory.UseItemAddorCount`, weapon/ring list insert, `game_fragmng` magic flags) and reuses `ItemGetCanvas` to show an English popup. Items are queued until the player is in a safe state (not in menus, cutscenes, H-scenes, loading).

### 5.4 Offline seed generator
C# implementation of assumed-fill over `logic.json`, seed + options → placement written into the save. Optional; if Q4 says "AP only", the offline mode is just "generate a single-player AP seed" and skipped in C#.

### 5.5 Enemy randomizer — options (pick in Q5)

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Intra-scene shuffle** | On scene load, collect the `enemy` prefabs of all `Spawnenemy` in the scene and permute them | trivial, safe, no asset loading | low variety |
| **B. Global pool via additive preload** | At boot, load each level additively, steal enemy prefabs into a `DontDestroyOnLoad` cache, unload | real cross-game variety | memory, long boot, hidden scene side-effects (scripts in `Start`) |
| **C. Global pool via extracted AssetBundles** | Use Unity 5.6.7 editor + AssetRipper to rebuild enemy prefabs as bundles | clean runtime | heavy, fragile, distributes game assets (license issue) |

Constraints to handle in all options: placement categories (ground / flying / wall / ceiling / water), size classes (big enemies in narrow corridors), enemies with scripted arenas or event triggers (exclude list), grab/H-scene enemies requiring specific camera setups, difficulty curve (option: scale HP/damage to the original slot's enemy). Boss shuffle is a separate, later toggle because bosses live in dedicated scenes (`LoadBossScene*`).
**Recommendation: A first, then B with a category/size whitelist.**

---

## 6. Phase 2 — Archipelago integration

### 6.1 Client (C#)
- Library: **Archipelago.MultiClient.Net** (net35 build) + Newtonsoft.Json net35.
- Connect screen on the title menu (IMGUI is fine on Unity 5.6): host, slot, password; stored in `BepInEx\config`.
- AP session ↔ save slot binding: store `seed name + slot + received item index` in the ES2 save under our own tags (e.g. `?tag=AP_ItemIndex`), so reloading never duplicates or loses items.
- Location checks: send immediately; offline buffer persisted to disk and resent on reconnect.
- Received items: `ItemReceived` → thread-safe queue → main-thread `ItemGranter`.
- Hints, chat and item log: small English overlay (toggle key).
- Goal: send `ClientStatus.Goal` on the chosen ending (Q3).
- **DeathLink** (optional): outgoing on game over; incoming triggers game over. NoR's defeat flow has H-scene / game-over animations — define exact behaviour (Q6).
- Optional later: **EnergyLink / RingLink**-style links are not planned.

### 6.2 APWorld (Python — unavoidable)
Archipelago generates seeds on the server side in Python; a world for NoR **must** be a Python package (`nightofrevenge.apworld`). It contains items, locations, regions, rules, options, slot data. It will be generated from `data/*.json` by a C# tool so the C# and Python sides share IDs and logic.

### 6.3 Options (YAML) — proposal
`goal`, `shuffle_chests`, `shuffle_floor_items`, `shuffle_magic`, `shuffle_shops`, `shuffle_trades`, `shuffle_boss_rewards`, `enemy_randomizer: off | intra_area | global`, `boss_randomizer`, `enemy_scaling`, `exp_multiplier`, `start_inventory`, `death_link`, `trap_percentage`, `h_content` (see Q8).

---

## 7. Phase 3 — English localization

### 7.1 Text sources to cover
1. GDE data (item / weapon / ring / magic names and descriptions).
2. Hard-coded C# literals (~700+ occurrences, e.g. `GetItemPanel`, `Savepoint_menu`, `PlayerStatus`, `Magicinfo`, `"拾う"`, `"を習得した"`, concatenations like `Itemname + "を習得した"` that need grammar-aware patching, not string replacement).
3. Scene-serialized text: `TextController.scenarios[]`, other `string[]` dialogue holders, static TMP/UI labels in scenes.
4. Textures/sprites with baked Japanese text (title, menus, gallery) — need redrawn English images.
5. Fonts: verify the TMP font assets contain full Latin + punctuation; add an English TMP font asset if needed.

### 7.2 Delivery options (Q2)

| Option | Description | Pros | Cons |
|---|---|---|---|
| **L1. XUnity data pack** | Produce curated `Translation/en/Text/*.txt`, texture replacements via ResourceRedirector, plus a few Harmony fixes for concatenated strings | fastest, compatible with existing setup | key = Japanese string (ambiguity), relies on 3rd-party plugin, text flicker, partial TMP hooking issues already logged |
| **L2. Own `NoR.Localization` plugin** | Extract all strings by ID (scene/object/field/index or GDE key) to JSON tables; Harmony patches inject English at source (GDE load, `TextController.SetNextLine`, literal sites via transpilers) | exact, context-aware, no runtime guessing, word-wrap/format control | more work |
| **L3. Hybrid** | L2 for GDE + dialogue + concatenations, XUnity as fallback for leftovers | best coverage with less effort | two systems |

**Recommendation: L3 now, migrate to L2 over time.**

### 7.3 Translation workflow
Extract → JSON (`id`, `jp`, `en`, `context`, `speaker`, `max_len`, `status`) → translate → review → pack → in-game QA screenshots. Who translates and from what base is open (Q2).

---

## 8. Phase 4 — QA, packaging, release

- Debug overlay: current scene, nearby location keys, flag viewer, item give/take console.
- Automated checks: ID-table consistency C# ↔ Python, every location reachable in logic (`apworld` unit tests), translation coverage report (% of strings with `en`).
- Full playthrough per goal option with randomization on.
- Package: zip containing `BepInEx/plugins/NoR.*`, configs, `nightofrevenge.apworld`, README. **No game files or game assets** in the release.

## Milestones (proposal)

| M | Deliverable |
|---|---|
| M0 | Build pipeline + data dumper + complete location/item tables |
| M1 | Logic graph from playthrough research |
| M2 | Offline item randomizer (vanilla-compatible save handling) |
| M3 | APWorld + C# client, local AP server test with another game |
| M4 | Enemy randomizer (option A), then B |
| M5 | English localization: GDE + UI + system strings |
| M6 | English localization: all dialogue + textures |
| M7 | Polish, DeathLink, release |

---

## 9. Risks

- **TLS on Unity 5.6 Mono / .NET 3.5**: old Mono may fail TLS 1.2 to `wss://archipelago.gg`. Mitigations: plain `ws://` for self-hosted servers, bundle a managed TLS stack, or a tiny local proxy process (modern .NET) that the game connects to via `ws://localhost`.
- Fixed-size flag arrays (`bool[20,20]` etc.) — AP state must live in our own save tags, not in new array slots.
- Pickups destroy themselves on `Start` if their flag is set; randomized locations must keep the original flag semantics so the world state stays consistent.
- Events/cutscenes may grant items directly (not via `Pickup_item`) — found only by playthrough.
- Enemy shuffle can soft-lock arenas (doors waiting for a specific enemy death) → exclusion list.
- Adult content: Archipelago community servers and the AP GitHub org have rules on NSFW worlds; the apworld will likely have to be distributed independently (not upstreamed).

---

## 10. Decisions taken (2026-11-10)

| # | Decision | Plan impact |
|---|---|---|
| D1 | APWorld in Python, **generated by a C# tool** from `data/*.json` | `NoR.Tools` gets an `apworld` generator; Python is a thin generated layer |
| D2 | English = **LLM-assisted translation + human review**; existing XUnity cache is reference only | `NoR.Tools` extractor exports context-rich JSON batches; review status per string; glossary file for names/terms |
| D3 | Enemy randomizer = **global pool from the start** | Option B (additive preload + prefab cache) is the M4 target; needs size/placement category tables and an exclusion list built in Phase 0 |
| D4 | Goal = **final boss**; **standalone offline C# seed generator** is required | Section 5.4 is in scope; the C# fill algorithm must produce the same logic results as the apworld (shared `logic.json`, shared test fixtures) |

| D5 | Goal boss = **The Witch in the Thorns** | Hook `OriginIbaranoMajyo.OnDestroy` (death state): it sets `_MainEVflag[9]` and `[11]` (ending flags, also read by `LastAreaEvflag`, `MainLastWall`). Scene `BOSS_Last` also holds `LastIbaranoMajyo` (shows "Victory" only) — which form is the real final kill is **to verify in playthrough** |
| D6 | DeathLink incoming = **defeat state**: player is downed/stunned and cannot get up until a mob catches her (defeat/H scene) or the player chooses "give up" → respawn at last save point | Needs the game's existing down/struggle state (`playercon` bad-status/down logic) to be triggerable without an attacker, plus a "Give up" prompt. Outgoing DeathLink = game over / respawn event. Option `death_link_mode: defeat_state | instant_respawn` |
| D7 | Shops, trades, slave rescues, titles **are locations**; **traps are wanted** | Trap candidates come from existing status effects (needs list from code); slave rescues give karma → see 12.2 |
| D8 | **Add H-content options** | e.g. `skip_defeat_scenes`, `skip_ero_events`, `defeat_scene_speed`, `gore` (game already has a Gore setting). Exact list after cataloguing `*ERO*` classes |
| D9 | **Public release** | No game files/assets in releases (also rules out enemy-rando option C). Needs a license and a public repo; the apworld will be distributed by us, not upstreamed (AP's main repo/Discord don't accept NSFW worlds — to be re-checked at release time) |
| D10 | Progression unknown to the user; `maps\` folder is trusted as current | Logic derived from **code analysis + logged playthrough**; web research results in 12.1 |
| D11 | Sacred walls: INT check **replaced by randomized items** | Harmony patch on `SacredWall`: the INT >= 20 wall needs 1x progressive "Sacred Seal", the INT >= 30 wall needs 2x. The vanilla INT check is disabled while the mod is active, so logic never depends on player level |
| D12 | Goal = **any kill** of the Witch in the Thorns (no ending check) | YAML option `goal_ending: any_kill (default) / good_ending`; `good_ending` also hooks the karma ending choice (`LastChooseSelect`, `EndingCarmaCheck`) and sends Goal only on the credits ending |
| D13 | License (developer's choice) | **Code: MIT. English translation data: CC BY-NC 4.0.** The README states that the original Japanese text, story and all game assets are (c) D-lis, a legally owned copy is required, and no game files are distributed. Third-party licenses (BepInEx LGPL-2.1, HarmonyX MIT, Archipelago.MultiClient.Net MIT) are listed in `THIRD_PARTY_NOTICES.md` |

## 12. Progression research (first pass)

### 12.1 Web research
No complete progression guide exists online; only fragments:
- Area names (NamuWiki / Baidu): Hidden Forest, Parish Church, Small Abandoned Village, Ancient Castle, Leaf Village, Bridge City, Underground Parish, Submerged Canyon, Research Facility, Cathedral of White Robes (order not given).
- "Unknown Lake" requires **100 Karma**; the Ranch opens via Suzune's sidequest (TV Tropes).
- Rescuing trapped characters yields karma, keys and items (Baidu).
- Ending depends on a karma-tied choice before/after the final battle; only the good ending plays credits (TV Tropes).

Sources: https://en.namu.wiki/w/Night%20of%20Revenge · https://baike.baidu.com/en/item/Witch's%20Night%20of%20Revenge/3261604 · https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/TheWitchsNightOfVengeance

### 12.2 Gates found in code
| Gate | Class | Condition | AP design consequence |
|---|---|---|---|
| Karma doors | `LakeUnlock`, `EndingCarmaCheck` | `PlayerStatus._karma >= needkarma` | Karma becomes a **progression item** ("Karma +N") when slave rescues are shuffled; logic counts karma items |
| Sacred walls | `SacredWall` | `AllINT() >= 20` / `>= 30` (stat check!) | Replaced by progressive item **"Sacred Seal"** (D11) |
| Doors / levers | `DoorOpen`, `MapGimickMoveDoor`, `ElevatorON` | `_GimmickArraystage` / `_ElevatorArrayStage` flags, mostly one-sided shortcuts | region graph edges |
| Locked chests | `TreasureBox` + `Treasureboxopenkeyuse` | consumes N **lockpick wires** (UseItem 4): bronze 1, silver 2, gold 4 — 15 total for all 8 chests (verified in scan) | lockpicks are a consumable progression item: logic must count picks against chests opened (no separate keys exist) |
| Last area wall | `MainLastWall`, `LastAreaEvflag` | `_MainEVflag[9]`, `[11]` | final region access |
| Ending choice | `LastChooseSelect`, `DendCheck` | karma | goal option (any ending vs good ending) |

### 12.3 Code research results (details: `docs/research-locations.md`)
| Category | Locations | Detection hook | Reward suppression | Notes |
|---|---|---|---|---|
| Bosses | 9 via `game_fragmng.fun_BOSS(i)` (i = 0,1,2,4,5,6,7,8,10) + final boss | postfix `fun_BOSS(int)`, dedupe with a mod-saved set | prefix `treasureNumSet` on 8 boss classes | `_BOSS[n]` resets at the rematch altar → never use it as "sent" state. 8 first-kill drops gated by `_BossIdeaflag[1..8]` (7 boss ideas USE 15–21, Candore weapon 115); Ranch boss reward = Suzune weapon 67 via event `_MainEVflag[29]` |
| Goal | `OriginIbaranoMajyo.OnDestroy` (state DEATH) | prefix | — | fallback `_MainEVflag[11] && [9]` |
| Shop | **0** — shop is non-functional in 1.0.7 (buy only decrements a counter, nothing saved, shop object inactive) | — | — | D7 "shops as locations" is moot unless we implement a shop ourselves (option for later) |
| Trades | 9 (`_Tradeflag[1..9]`) | prefix `TradeSelectItem.getitemSet()` keyed by `tradeflagnum` | same (return false) | cost = boss idea items + 5000/10000 Idea → boss ideas must stay obtainable (logic) |
| Titles | **0 useful** (only title 0 can be set; no rewards) | — | — | dropped from D7 |
| NPC/event grants | ~16 (7 `EvNpcTalkactor`, 2 Suzune, `EvMainSlave` karma, `EnemydeathCheck` rooftop key USE 62, `witchslaveslime` ring 30, Bigoni/Slaughterer/Mimick treasures, slave-rescue magic) | prefix the respective `treasureNumSet*` | same | enemy randomizer can move treasure carriers (Bigoni holds USE 58, unused) |
| Gates | key doors USE 60 (lab), 61 (pilgrimage ×2), 62 (rooftop) — possession, not consumed; karma gates lake 100, Ranch 60, endings 70/120/130; `_MainEVflag` story; elevators unlock globally per number; fire ivy | — | — | prototype keeps USE 60/61/62 vanilla until logic exists |

Other risks: `Magicselectget.ListCheck` force-sets `_GetMagic_Lightning[1]` when the magic menu opens; `ShopWpEv` hides Shop ITEM15 when weapons 265–270 are owned; starting items are granted in `PlayerStatus.Start` (AP start inventory must be granted after it).

Remaining progression mapping happens in Phase 0.5 (flag-logging playthrough) and by statically reading every scene's gate components (Phase 0.3).

## 14. Progress log

### M0 — research tooling (2026-11-10)
Done:
- Repo, `NoR.Archipelago.slnx`, shared `Directory.Build.props` (`GamePath`), net35 plugin build via `Microsoft.NETFramework.ReferenceAssemblies.net35`, auto-deploy into `BepInEx\plugins`.
- `NoR.Core` (net35): JSON writer, Unity-style reflection serializer, `GameRefs`, hierarchy helpers.
- `NoR.DataDumper` (in-game, dev-only): GDE dump, runtime scene dumps (incl. inactive objects), Japanese UI text capture, **flag change logger** (`flaglog.jsonl`) with F9 markers. Built and deployed; **not yet run in-game**.
- `NoR.SceneScanner` (offline, AssetsTools.NET + Cecil): scans all 107 scenes + asset files in ~8 s → `dump/static` (git-ignored).
- `NoR.DataBuilder`: `data/items.json` (392 GDE items with runtime indices), `data/locations.draft.json` (210 candidates), `data/report.md`.

Findings:
- Locations found statically: 176 floor items, 8 chests, 16 magic pickups, 10 slave rescues (+ shops, trades, bosses, titles still to map).
- 837 enemy spawn points, ~60 distinct enemy prefabs (see `data/report.md`).
- 7,862 serialized Japanese strings in scenes/prefabs (translation source, excl. GDE and code literals).
- Pickup IDs = per-schema file order in `gde_data` (verified: weapon/ring indices equal GDE `ID`).
- Chest "keys" are lockpick wires (see 12.2).

Update (same day, while the user tests):
- DataDumper verified loading in-game (overlay visible). Fixed a flag-logger crash on multi-dim static arrays (`StaticMng`), active after the next game restart.
- **`NoR.Randomizer` v0.1 prototype** (offline, no logic yet): seeded vanilla-pool shuffle of floor items, chests and magic pickups (lockpicks never in chests); magic locations keep their checked state in the unused flag row `_TreasureItemArrayStage[19, *]`; settings stored per save slot in ES2 tag `NoR_Rando`; spoiler log. **Global enemy randomizer**: pool built at the title screen by additively loading a greedy set-cover of spawner scenes (roots deactivated before `Start`), templates kept under an inactive `DontDestroyOnLoad` holder; deterministic choice per spawner. Bosses untouched. Testing guide: `docs/testing-randomizer.md`.
- Only 13 lockpicks exist as world pickups vs 15 needed for all chests (rest presumably shop/drops) — chests are optional, so no softlock.
- **`NoR.TextExtractor`**: code literals (Cecil `ldstr`), GDE strings, scene strings → `dump/text/` (local only). 9,509 occurrences, **3,803 unique strings, ~131k Japanese characters** (code 448, GDE 378, scenes 3,151 unique). Strings include TMP rich-text tags that must be preserved.
- Translation storage decision: the public repo stores only `id + source hash + English`; the Japanese source is extracted locally from the user's install. Code literals will be replaced at runtime by a generic Harmony transpiler keyed by `method + ldstr index`.

Update 2 (user playtest OK: randomized items + enemies, no crashes/lag; pool = 56 enemy types from 14 scenes):
- Code research for bosses/shop/trades/titles/NPC grants/gates done (12.3).
- **Softlock fix**: door keys USE 60/61/62 excluded from the logic-free shuffle (lab key and pilgrimage key were floor items in the pool). Changes the layout of not-yet-collected items in existing randomized saves.
- **`NoR.Localization` v0.1**: 4 layers (code-literal transpiler on 244 listed methods, GDE post-Init, serialized fields on scene load incl. loaded prefabs, TMP/UI/TextMesh setters + TMP OnEnable). Hash-keyed tables in `data/translation/en/*.tsv`; 11 logic-key literals excluded (`code_unsafe.txt`). Deployed with an empty table (no-op) for stability testing.
- Translation pipeline: `NoR.TextExtractor` writes batches (`dump/text/batches/*.jsonl`) with reference hints from the user's XUnity cache (3,222 of 3,964 strings have a hint); glossary `docs/translation/glossary.md`. GDE + code batches being translated.

Update 3:
- **Full English draft deployed**: 3,792 of 3,803 unique strings (GDE 378, code 437, scenes 3,149 in four parts); remaining = 11 logic keys + 2 credits supporter lists. Cross-file conflicts resolved (names, place names); glossary extended by each batch. Open review points: `docs/translation/review-notes.md`.
- **Witch's Market** (custom shop at save points, B / gamepad item-switch): up to 20 one-time slots as randomizer locations (`shop:N`, bought state in spare flag row `_TreasureItemArrayStage[0, *]`), unlimited vanilla supplies, Idea currency; shop slots add vanilla shop stock to the pool (incl. 2 lockpicks → 15 total = chest demand). Untested in-game.
- **NoR.Widescreen**: game player settings disable "Others" aspect ratios (launcher hides 21:9). Plugin sets native resolution after startup; Expand mode (CanvasScaler match height + stretch full-screen overlays) or Pillarbox fallback. Untested in-game.

Update 4 (Archipelago groundwork):
- Dev environment: Python 3.13 (user scope), Archipelago source `.tools/Archipelago` (Nov 2026), venv `.tools/ap-venv`; local test seed (ChecksFinder) + `MultiServer` on 127.0.0.1:38281.
- Client library: `Archipelago.MultiClient.Net` 6.7.1 has a **net35** build (bundles websocket-sharp + Newtonsoft). Verified login / check / item receive with the net35 DLLs (on .NET 4.8).
- archipelago.gg rooms accept **wss only** (`WebHostLib/customserver.py` uses the cert's SSL context). Unity 5.6 Mono can't do TLS 1.2 → **`NoR.ApBridge`** (net10 single-file exe, 13 MB): TCP-level tunnel, plain `ws://127.0.0.1:<port>` from the game, TLS (or plain fallback) to the server; exits with the game. Verified: TLS 1.2+ to archipelago.gg, AP login through the bridge.
- **`NoR.Archipelago` plugin skeleton**: connection layer with automatic bridge, startup diagnostics (native TLS vs bridge), F7 connection test. Pending: first in-game run on Mono 2.0.
- **Scene graph** (`data/regions.draft.json`, report section in `data/report.md`): 84 transition edges from `SceneMove`/`SceneMoveSubmit`/`SceneMoveMainEv` (scene names canonicalized case-insensitively; `SceneMoveSubmit.KeyItem` is never read) + scripted edges observed in play logs (`data/observed_edges.tsv`). With the prologue chain from the first log, all 46 scenes with locations are reachable. Remaining logic work = gates inside scenes (sub-regions), from the flag log.
- Risk: the AP server warns that websocket-sharp lacks permessage-deflate ("may stop working in the future"). Mitigation if it becomes mandatory: let the bridge speak WebSocket itself (terminate + re-frame with compression) instead of a raw TCP tunnel.

Update 5:
- In-game verification: native Mono TLS fails (as expected), bridge TLS from inside the game OK, F7 login to the local test server OK → **AP connection path verified on Unity 5.6 Mono**.
- Shop text: game TMP font `NormalKozMinProMSDF` is an MSDF atlas that renders Latin glyphs broken with the stock shader → shop uses a dynamic OS font (Georgia) via `UnityEngine.UI.Text`; MSDF fonts excluded from the localization fallback-font picker. TMP enable hook moved to `TextMeshProUGUI`/`TextMeshPro` (TMP_Text has no OnEnable in this version).
- **Reward locations implemented** (offline randomizer): 8 boss first-kill drops, 10 NPC/event rewards, 9 trades (costume skins as new item type `costume:N`). One shared prefix on `treasureNumSet[2](num, kind)`, keyed by class/method/talkactor/kind/num; unlisted calls keep vanilla and are logged. Definitions in `data/code_locations.tsv`, shop pool in `data/shop_pool.txt` (both shared with the apworld). Magic lookup falls back to school/num (Candore's duplicate magic drop).
- **Decision — apworld v1 = vanilla progression**: door keys (USE 60/61/62), karma sources, story events and the INT-based Sacred Walls stay vanilla; lockpicks are always buyable → no shuffled item gates anything, all locations are in logic from the start. Gate shuffling (keys, karma items, Sacred Seals D11) follows once the flag log maps the gates (door direction cannot be derived from positions).
- **apworld** (`apworld/nightofrevenge`): static template (`__init__.py`, `options.py`, setup doc) + generated `data.py` from **`tools/NoR.ApWorldGen`** (persistent ids in `data/ap_ids.json`, client map `data/runtime/ap_map.tsv`). 245 locations, 88 item types, goal event "Defeat the Witch in the Thorns". Options: shop_slots, boss_drops, npc_rewards, trades, enemy_randomizer, death_link, death_link_mode. `tools/package-apworld.ps1` builds the `.apworld`. **Generation verified**: 2-game multiworld, 25 random-option seeds (25/25), minimal solo seed.

Update 6 — full client v0.2:
- Randomizer: all location hooks go through `Checks.Collect(key, item)`; Archipelago games use placement value `remote` and hand checks to the client. `RandoState` stores the AP session (seed name, slot, server, received-item index, sent location keys, goal) in the save → reload-safe (no duplicate / lost items). `Placement.GenerateRemote` mirrors the apworld's location filter.
- `NoR.Archipelago`: title-screen connect window (IMGUI, F1), slot data → new-game state, scouting of all locations (names for "Sent X to Y" and shop rows, shop price by item importance), item delivery one at a time only while the player is in control, auto-reconnect + resync of sent checks and goal, save/seed/slot mismatch protection, goal patch on `OriginIbaranoMajyo.OnDestroy` (DEATH), on-screen notifications.
- Local test server: NoR slot "Aradia" + ChecksFinder slot "Tester" (Tester finished, 22 items queued for Aradia). Server-side item names verified in English.
- Not yet: DeathLink (needs research into the downed/struggle state), traps, in-game connect UI styling.

Update 7:
- In-game verified by the user: AP connect, item receive with messages, no duplicates across save/reload; DeathLink works.
- DeathLink refined per user: a DeathLink always ends in death (downed → can't get up; grabbed → if released alive she dies); player chooses watch or skip. **DeathSkip** (NoR.Randomizer, also offline): after a real death (HP 0, never on stun) "G / hold Jump 2 s = skip and respawn" → `PlayerStatus.REstrat()`; cancels the vanilla death timers (`fun_invoke_DIED`, `GOeny_set`), refuses the defeat banner / any-key arming while alive, closes leftover game-over panels opened by enemy defeat sequences for 15 s.
- Game-over buttons shortened ("Retry", "Nightmare").
- **Release packaging**: `tools/package-release.ps1` → `dist/NoR-Archipelago-<ver>.zip` (plugins without the data dumper, translations, bridge, apworld, docs; 6.5 MB, no game files).
- **Traps**: Ambush (random pool enemy spawns near Aradia, parented to the level's SpawnParent), Idea Drain (−20% carried Idea, min 100), Knockdown (vanilla down, can get up). apworld option `trap_percentage` (0–50, default 10) replaces that share of filler. 15/15 random seeds generate. Lewd status-effect traps deferred to the H-content options (D8).

Next:
1. ~~Run `NoR.DataDumper` in-game once~~ (done).
2. Research playthrough with flag log → region graph.
3. Map remaining location types: bosses (`_BOSS[]`), shops, trades, titles.
4. Translation extractor (GDE + scene strings + code literals) → M5.

## 13. Open questions

None blocking. Next step: **Milestone M0** (repo + build pipeline + in-game data dumper + offline scene scanner).

Environment note: Python 3.11+ is not installed on the dev machine; it is needed later (M3) to run Archipelago generation and apworld tests.
