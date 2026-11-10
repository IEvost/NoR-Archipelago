# Location research: bosses, shop, trades, titles, NPC grants, progression gates

Source: ILSpy decompilation of `Assembly-CSharp.dll` (NoR 1.0.7). File references are to
`decomp/acs/<Class>.cs:<line>`. Instance lists come from `dump/static/scenes|prefabs/*.json`.
Anything not read directly from the code is marked **(inferred)**.

**Limitation of the static dump:** the scene dumper did not export every component type. Types such as
`MainEVNextLevel`, `Mggetkind`, `MagicGetCreate`, `MimickStart`, `Bigoni`/`Slaughterer`/`Mimick`
(enemy prefabs), `IvyRoadStop` and `MapBreakObjectName` appear nowhere in the dump, even though code
references them. Instances of these types have to be confirmed from the runtime dump or flag log.

Item index notation: `USE n` = `ItemInventory._USE[n]` (UseItem), `WP n` = `_WP[n]` (weapon),
`RING n` = `_RING[n]` (crestring), all from `data/items.json`.

## Shared reward plumbing (relevant for every category)

| Call | Effect | Ref |
|---|---|---|
| `ItemGetCanvas.item_get(inv, wp, ring, use, cos)` | Shows the pickup popup, then `inv._pick_ID = wp` (adds the weapon), `inv._pick_ID_ring = ring`, `inv.UseItemAddorCount(use)`. `cos` is ignored. | ItemGetCanvas.cs:35-61 |
| `ItemInventory._pick_ID` setter | `weaponItemID.Add(_WP[value].ID)` | ItemInventory.cs:53-63 |
| `ItemInventory._pick_ID_ring` setter | `CrestringID.Add(_RING[value].ID)` | ItemInventory.cs:65-75 |
| `ItemInventory.UseItemAddorCount(n)` | Adds 1 to the stack of `_USE[n]` | ItemInventory.cs:146-168 |
| `Auto_PickUp_Dropitem.DROPItemKind(ID, USE, RING, COS, MG)` | Stores the values. On player contact it calls `item_get(inven, ID, RING, USE, COS)` and destroys itself. | Auto_PickUp_Dropitem.cs:34-67 |
| `EvAuto_PickUpItem.DROPItemKind` | Same pattern, used by events | EvAuto_PickUpItem.cs:35,52 |
| `<X>.treasureNumSet(num, kind)` | Common helper in bosses, NPCs and events: spawns a drop object, with kind 0 = weapon, 1 = UseItem, 2 = ring | e.g. BOSS_Village.cs:349-367 |
| `PlayerStatus._karma` setter | **Adds** the value (`KarmaCount += value`). It does not assign. | PlayerStatus.cs:560-570 |
| `KarmaSetClass.KarmaSet5/10/15/20(a)` | `pl._karma = a` (adds a) and spawns a karma effect | KarmaSetClass.cs:24-46 |

A generic hook on `ItemGetCanvas.item_get` (prefix, return false) catches every drop-based grant
(boss drops, NPC drops, event drops, floor items). Trades do not go through it: they write to the
inventory directly.

---

## A. Boss defeats

### Detection and flags

Every standard boss follows the same pattern. In `Update`, when `Hp <= 0` and `!Dropflag`, it calls
`treasureNumSet(...)`, but only if `!_BossIdeaflag[Bossidenum]` (first kill only). In `OnDestroy`, when
`state == DEATH`, it sets `_BossIdeaflag[Bossidenum] = true` (Exp is halved if the flag was already
set), sets `StaticMng.Gaflag[n]` (gallery), adds Exp and calls `game_fragmng.fun_BOSS(i)`
(`BOSS[i] = true`, game_fragmng.cs:518-521). It then unloads the additive boss scene.
`Bossidenum` is **not serialized**: it is hard-coded in `Start` of each class.

| Boss class | Boss scene (loaded from) | `_BOSS` idx | `_BossIdeaflag` idx | First-kill drop | Extra | Refs |
|---|---|---|---|---|---|---|
| BossTouzoku | BOSS_Touzoku (092 FirstMap, LoadBossSceneAdd BossNum 10) | 10 | 1 | USE 15 デグレイドのイデア | Gaflag[3] | BossTouzoku.cs:286, 400-402, 2604-2618 |
| BOSS_Village | BOSS_Village (003 village, LoadBossScene → `_BOSS[0]`) | 0 | 2 | USE 16 八ツ手のイデア | Gaflag[10]; `fun_BOSS(0)` sits in a Spine "SE" event during DEATH, not in OnDestroy | BOSS_Village.cs:214, 399-401, 1461, 1619-1640 |
| BossScapegoatentrance | BOSS_scapegoatEntrance (009) | 1 | 3 | USE 17 教会の獣のイデア | Gaflag[18] | BossScapegoatentrance.cs:270, 375-377, 2841-2858 |
| Candore | BOSS_UnderChurch (013 InundergroundChurch, 3 triggers) | 2 | 4 | **WP 115 純白の処女** | Gaflag[24]; also instantiates `Mgdrop` (prefab Pickup_magic Sacred[1] `mg_crossexplosion`) if `!_GetMagic_Sacred[1]` | Candore.cs:296, 545-547, 3183-3212 |
| BossInsomniaUnder | BOSS_InsomniaUnder (025) | 4 | 5 | USE 18 繭背負いの悪魔のイデア | Gaflag[44] | BossInsomniaUnder.cs:265, 389-391, 1569-1593 |
| DemonRequiemKnight | BOSS_Requiem (032 requiemboss, LoadBossSceneAddStart) | 5 | 7 | USE 20 堕ちた鎮魂騎士のイデア | Gaflag[56] | DemonRequiemKnight.cs:265, 358-360, 2311-2335 |
| SuccubusSpine | BOSS_UndergroundLab (033) | 6 | 8 | USE 21 情欲の悪魔のイデア | Gaflag[61] | SuccubusSpine.cs:335, 442-444, 4148-4172 |
| Praymaiden | BOSS_Lake (097 Lake) | 7 | 6 | USE 19 祈りの乙女のイデア | Gaflag[50] | Praymaiden.cs:327, 421-423, 3355-3379 |
| Boss_Ranch (Kijinmaru) | BOSS_Ranch (096 Ranch) | 8 | — (no Ideaflag, no drop) | none directly | Gaflag[39]. If `!_MainEVflag[29]` it activates `BossdethEv` (BossDeathEvent → `MainEVNextLevel.SceneMOVE`), which leads to scene 079 RanchEv. There `EvMainTalkactor` talkactor 29 sets `_MainEVflag[29]` and drops **WP 67 終代 鈴音** (EvMainTalkactor.cs:1482-1544) | Boss_Ranch.cs:2585-2612 |
| LastIbaranoMajyo → OriginIbaranoMajyo | BOSS_Last (071) | — | — | none | Last phase: at HP ≤ 50 % it sets Gaflag[70] and BossTransformEvent swaps in Origin (LastIbaranoMajyo.cs:629-642). Origin `OnDestroy` (DEATH): Gaflag[115], [71], `_MainEVflag[11] = true`, `_MainEVflag[9] = true`, activates `BossdethEv` (OriginIbaranoMajyo.cs:4315-4335) | see left |

`_BOSS` indices 3 and 9 are never set. The `Bossidenum` field also exists in the two witch classes
but is never used there.

**Rematches:** each region has a `BossReturn` altar (instances: village 0, scapegoatEntrance 1,
InundergroundChurch 2, InsomniaTownUnder 4, requiemboss 5, UndergroundLaboratory 6, Lake 7, Ranch 8,
FirstMap 10). It appears once `_BOSS[n]` is set, and `BossflagReset` / `BossReturnMenu.FlagReset`
**sets `_BOSS[n] = false`** to allow a refight (BossReturn.cs:101-104, BossReturnMenu.cs:40-43).
`LoadBossSceneAdd` reloads the arena only while `!_BOSS[n]` (LoadBossSceneAdd.cs:25).

### Hooks

- **Detect (recommended):** postfix on `game_fragmng.fun_BOSS(int a)`. It covers all 9 non-final bosses
  including Ranch. It fires again on every rematch, so keep a set of checks already sent. Alternative:
  prefix/postfix on each boss's `OnDestroy` and compare `_BossIdeaflag[idx]` before and after (first
  kill only, but Ranch has no Ideaflag).
- **Suppress the drop:** prefix returning false on `treasureNumSet` of the 8 boss classes
  (BOSS_Village, BossTouzoku, BossScapegoatentrance, Candore, BossInsomniaUnder, DemonRequiemKnight,
  SuccubusSpine, Praymaiden). Each declares its own `public void treasureNumSet(int,int)`, so these are
  8 separate patches. They are called only on the first kill.
- **Candore magic:** the extra drop is a normal `Pickup_magic` prefab (Sacred[1]), so it is handled by
  the existing Pickup_magic patch (see the risks).
- **Final boss / goal:** prefix on `OriginIbaranoMajyo.OnDestroy` where `state == DEATH` (the field is
  private: use a Traverse or AccessTools field ref). As a fallback, check
  `_MainEVflag[11] && _MainEVflag[9]` on load. `_MainEVflag[19]` (EvMainTalkactor.cs:1006, ending
  sequence in 080 LastAreaEv) marks the ending reached.

### Risks
- `_BOSS[n]` is **not monotonic**: the rematch resets it. Use `_BossIdeaflag` or your own sent-set for
  the "already checked" state.
- The boss idea items (USE 15-21) are the **trade currency** (section C). If they are replaced by AP
  items, trades become impossible unless the ideas are shuffled into the pool or the trades become
  their own checks.
- The drop happens in `Update` at HP 0, before `fun_BOSS`/`OnDestroy`. Report the check from
  `fun_BOSS` and suppress the drop separately.
- Candore's magic duplicates the floor pickup `013_InundergroundChurch MG_GET` (Sacred[1]), which uses
  the same flag. It is a fallback, not a separate location.
- The Ranch boss's reward (WP 67) arrives through a separate event scene (RanchEv, `_MainEVflag[29]`).
  `Boss_Ranch.Start` also checks `_MainEVflag[29]` (Boss_Ranch.cs:282).

---

## B. Shop

**Result: the shop is non-functional in 1.0.7. It provides no locations.**

- `ShopInventory.shopList` seeds the stock: USE 3 ×100 (∞), USE 5/6/7/8 ×30, USE 29 ×5, USE 30 ×5,
  WP 265 (ShopInventory.cs:198-214). If `_ShopItemsave` is set, it loads `ShopUseitem/ShopCount`
  instead. The `Shopitemdata` rows in prefab sharedassets11 `BUY_menu` are USE 3, 5, 6, 7, 8, 29
  (RowID 0-5). The price shown is `_USE[id].buy`.
- `Buymenu.BUY()` only calls `ShopUseItemUSE(row)`, which decrements the shop counter
  (Buymenu.cs:29-33, ShopInventory.cs:105-116). **No Idea is deducted and no item is added.**
  `ShopInfoButton`/`GetShopPanel` only display data.
- `game_fragmng.ShopCount/ShopUseitem/_ShopItemsave` are never written and **never saved**
  (absent from SaveFile.cs:179-264).
- The only scene entry point is `018_Shop / SHOP` (WitchShop + TradeMenuStart). That GameObject is
  **inactive** in the scene, and no code activates it (inferred: unreachable).
- `ShopTalk` (the `Shopdialogue` prefab spawned by WitchShop) would give WP 265 骸布の塊 once, guarded
  by `_GimmickArraystage[5,11]` (ShopTalk.cs:72-99). This is unreachable while SHOP is inactive.
  WP 265 is also the floor item `018_Shop GameObject/ITEM15` (`TreasureItemArrayStage[5,15]`).
- `ShopWpEv` (018_Shop "GameObject", the parent of ITEM15) hides that pickup if the player **owns** any
  of WP 265-270 (ShopWpEv.cs:10-21). The check uses inventory, not a flag.

Hooks: none needed. **Risk:** if AP gives WP 265-270 before the player visits the Shop, `ShopWpEv`
hides the ITEM15 location. Patch `ShopWpEv.Setval` (prefix, return false), or the existing
`Pickup_item` logic must account for it.

---

## C. Trades (Vendetta's trade menu, 018_Shop `TradeMenu`)

- `TradeMenuStart.Start` disables the trader unless `_Tradeflag[0]` (TradeMenuStart.cs:34-37).
  `_Tradeflag[0]` is set in the BigAxe event `EvBigAxeTalkMng` (050 EVuChurch / 051 EVuChurchAfter,
  EvBigAxeTalkMng.cs:198, 209, 231).
- Purchase: `TradeOk.buy()` → `TradeSelectItem.BuyOK()` (TradeSelectItem.cs:230-243). If `allcheck`
  (enough items and enough Idea), it consumes `need_count` of `USE needitem_ID`, subtracts `need_idea`
  from `Exppoint` and `ExpALL`, calls `getitemSet()` and sets `_Tradeflag[tradeflagnum] = true`.
  `getitemSet` (TradeSelectItem.cs:245-261): kind 1 → `inventory._pick_ID = Getitem_ID` (weapon),
  kind 2 → `_pick_ID_ring`, kind 3 → `_CosSkinflag[Getitem_ID] = true`.
- Each trade can be done once (`clickSet` requires `!_Tradeflag[n]`).

Instances (prefab sharedassets18 `Trade_menu`, 9 entries):

| Tradeflag | Gives | Costs | kind |
|---|---|---|---|
| 1 | Costume skin 1 (`_CosSkinflag[1]`) | USE 3 復元裁縫セット ×3 + 5000 Idea | 3 |
| 2 | RING 26 非道の紋章指輪 | USE 15 デグレイドのイデア ×1 + 5000 | 2 |
| 3 | WP 181 狂い大剣 | USE 16 八ツ手のイデア + 10000 | 1 |
| 4 | WP 19 白眉の紋章剣 | USE 17 教会の獣のイデア + 10000 | 1 |
| 5 | WP 241 繭の衝撃杭 | USE 18 繭背負いの悪魔のイデア + 10000 | 1 |
| 6 | WP 43 祈りの乙女 | USE 19 祈りの乙女のイデア + 10000 | 1 |
| 7 | WP 187 黒喰む大剣 | USE 20 堕ちた鎮魂騎士のイデア + 10000 | 1 |
| 8 | WP 235 ブラッディランス | USE 21 情欲の悪魔のイデア + 10000 | 1 |
| 9 | Costume skin 2 (`_CosSkinflag[2]`) | USE 3 ×10 + 5000 | 3 |

**Hook:** prefix on `TradeSelectItem.getitemSet()`. Read the private `tradeflagnum`, send the check
and return false (vanilla consumption and the flag write in `BuyOK` still happen). Alternative: prefix
on `BuyOK`. **Risks:** the costs depend on boss idea items (section A). Trade 0 is the unlock flag,
not a location. Costume trades only toggle cosmetic flags (`CostumeChange.cs:49-119`).

---

## D. Titles (`_GetTitleList`)

Only `Gettitleinstantiate.title()` (Update) writes the flags (Gettitleinstantiate.cs:28-45):

| Index | Condition | Note |
|---|---|---|
| 0 | `pl._LostVirgin != "処女"` (set by an enemy H-attack, EnemyDate.cs:705) | works |
| 5 | `_nakadasi >= 500 && _GetTitleList[5]` | inverted check: can never become true |
| 40 | `LV == 2 && _GetTitleList[40]` | inverted check: never |

Titles give **no reward** and nothing reads them. There are no Steam achievements in the assembly.
`StaticMng.Gaflag[120]` holds gallery unlocks only. Recommendation: no locations.

---

## E. Other item / magic / karma grants

| Source (scene / object) | Condition and one-time flag | Reward | Ref |
|---|---|---|---|
| EvNpcTalkactor talkactor 0 (014 InsomniaTown NPCTalk0) | dialog step, `_NPCEvFlag[1]` | +10 karma (only if flag unset) | EvNpcTalkactor.cs:268-282 |
| talkactor 1 (013 InundergroundChurch NPCTalk1) | step 2, `_NPCEvFlag[4]` | +10 karma; `MGget()` activates `mgEvget`, which contains Pickup_magic `MG_GET_S2` Sacred[2] `mg_praisesword` (also gated by `MGgetNPCEv NPCflagnum=4`) | :286-307, 504-507, MGgetNPCEv.cs |
| talkactor 2 (013 NPCTalk5) | `_NPCEvFlag[5]` | +10 karma | :310-322 |
| talkactor 3 (017 InsomniaTownC NPCTalk8) | `_NPCEvFlag[8]` (requires `_DialyFlag[9]` dialog state) | +10 karma | :324-345 |
| talkactor 5 (016 InsomniaTownB NPCTalk1 (6)) | `_NPCEvFlag[10]` | +5 or +10 karma (branch) | :361-394 |
| talkactor 6 (014 InsomniaTown NPCTalk11) | `_NPCEvFlag[11]` | USE 5, 6, 7, 8, 66 (one each) + 10 karma | :395-413 |
| talkactor 7 (031 ForestOfRequiem NPCTalk1 (5)) | `_NPCEvFlag[12]` | **USE 64 魔女の秘薬** (raises max potion count, PlayerStatus.cs:2086-2098) + 10 karma | :415-433 |
| talkactor 4 (016 NPCTalk1 (4)) | `_NPCEvFlag[9]` | none | :347-358 |
| EvSuzuneTalk talkactor 0 (009 scapegoatEntrance) | `_EvArraystage[2,0]` | USE 29 潜在石の欠片 | EvSuzuneTalk.cs:216-222, 286-310 |
| EvSuzuneTalk talkactor 1 (012 UndergroundChurch) | `_EvArraystage[2,1]` | USE 30 潜在石の塊 | :240-246 |
| EvSuzuneTalk talkactor 2 (014 InsomniaTown) | `_EvArraystage[2,2]` | none (enables Ranch route, section F) | :261-266 |
| EvMainSlave (092 FirstMap Evmain3slave) | interact, `_MainEVflag[2]` | +5 karma | EvMainSlave.cs:47-56 |
| EvMainTalkactor talkactor 29 (079 RanchEv) | after the Ranch boss, `_MainEVflag[29]` | WP 67 終代 鈴音 | EvMainTalkactor.cs:1482-1544 |
| EnemydeathCheck (047 WhiteCathedralRooftop CrowInquisition_MOBERO, number 1) | `DeathCheck(1)`, `_GimmickArraystage[13,8]` | **USE 62 屋上小部屋の鍵** (key, section F) | EnemydeathCheck.cs:85-97 |
| witchslaveslime (002 village_main SlaveWitch/witch1) | `_GimmickArraystage[1,7]` set + interact | RING 30 ヴィオレッタの指輪 | witchslaveslime.cs:88-98 |
| Bigoni (prefab `Bigoni_spine`, spawned in 002 village_main, SpawnNumber 28) | first kill, `_GimmickArraystage[StageNum, GimmickflagNum]` (serialized in the prefab, not dumped) | serialized `treasureNum/KIND` + **USE 58 六角形の鍵** | Bigoni.cs:424-433 |
| Slaughterer / Mimick (via MimickStart) | first kill, `_GimmickArraystage[...]` (serialized, not dumped) | serialized treasure | Slaughterer.cs:400-407, Mimick.cs:402-409, MimickStart.cs:110-116 |
| MagicGetCreate (not dumped) | after `_helpslaveStage[stage, n]` (1 or 2 slaves) | spawns a Pickup_magic prefab (inferred: the prefab magics Fire[0] firecircle, Lightning[0] Lightningstrike, Lightning[4] buffthunder, Darkness[2] yaminabe) | MagicGetCreate.cs:80-105 |
| Mggetkind (not dumped) | `_BOSS[BossNum]` | keeps or destroys a magic pickup after a boss | Mggetkind.cs:31-45 |
| `Magicselectget.ListCheck` | every time the magic menu opens | **forces `_GetMagic_Lightning[1] = true`** (free spell) | Magicselectget.cs:38-40 |
| New game (`PlayerStatus.Start`) | `StaticMng.UseLoadStatic` | WP 0, RING 1, USE 67, magic `gun_bus` | PlayerStatus.cs:908-924 |

Not locations: random enemy drops (`Pickup_Dropitem`, e.g. Gorotuki.cs:396-410), Idea pickups
(`Pickup_Idea`, `FallIdeaMovePlayer`), the upgrade refund (`ReWpUpgradeenable.cs:206-234`) and
`Lvreset`.

**Hooks:**
- NPC drops: prefix on `EvNpcTalkactor.treasureNumSet2`. Send a check keyed by the private
  `talkactor` (+ item index) and return false. Use `NPCEvFlagSET` postfix or `KarmaSetClass` for the
  karma part.
- Suzune: prefix on `EvSuzuneTalk.treasureNumSet`, key `talkactor`.
- Main event: prefix on `EvMainTalkactor.treasureNumSet`.
- Bigoni / Slaughterer / Mimick / EnemydeathCheck / witchslaveslime: prefix on their `treasureNumSet`.
- The generic fallback is `ItemGetCanvas.item_get`.

Note that the talkactor 1 magic is a regular Pickup_magic.

---

## F. Progression gates

| Gate | Condition | Opens | Ref / instances |
|---|---|---|---|
| Key door (MapGimickMovedoorStart + MapKeyOpenGimick) | **owns** `USE itemnum` (not consumed) | hidden door, sets `_GimmickArraystage[s,g]` | MapGimickMovedoorStart.cs:44-65. 033 UndergroundLaboratory DoorKey: USE 60 黒く変色した鍵 → [9,4]. 036 PilgrimageEntrance DoorKey / DoorKey2: USE 61 巡礼道の鍵 → [10,4], [10,5]. 047 WhiteCathedralRooftop DoorKey[6]: USE 62 屋上小部屋の鍵 → [13,6] |
| USE 58 六角形の鍵 | no consumer found in code or dump | — (inferred: unused) | — |
| Lockpick chests | USE 4 (known) | — | Treasureboxopenkeyuse |
| LakeUnlock (027 Valley `Lakestop`) | `karma >= 100` | barrier toward the Lake, `_GimmickArraystage[17,9]` | LakeUnlock.cs:97-111 |
| EndingCarmaCheck | `karma >= needkarma` | event/scene move: 017 MoveRanchEv **60** (route to Ranch, inferred), 025 MoveCandoreEroEv 20, 036 MoveBendingEv 130 (B ending), 096 MoveCendingEv 120 (C ending) | EndingCarmaCheck.cs:86-97 |
| EvSuzuneGoRanch (017 MoveRanchEv) | `_EvArraystage[2,2]` and not `_SavePointArray[22/23]` | shows the Ranch trigger | EvSuzuneGoRanch.cs:10-17 |
| LastChooseSelect (071 BOSS_Last) | `karma >= 130` | 2nd choice button before the final battle | LastChooseSelect.cs:70-74 |
| DendCheck | `_MainEVflag[5] && karma >= needkarma` (default 70) | D ending | DendCheck.cs:39-56 |
| MainLastWall (047 Rooftop `MainEvwall`) | `_MainEVflag[11]` | removes the wall | MainLastWall.cs:13 |
| LastAreaEvflag (080) | `_MainEVflag[9]` / `[11]` | selects event 9 → 11 → end (19) | LastAreaEvflag.cs:14-25 |
| SceneMoveMainEv | `_MainEVflag[before] && !_MainEVflag[ev]` → event scene, otherwise the normal destination | story scenes (004→WitchVillage ev5; 009/012→WitchVillage ev6; 016→LastAreaEv ev9; 026→MemoryEV ev8; 033→NightmareEV ev10; 036→LastAreaEv ev11) | SceneMoveMainEv.cs:50 |
| WitchVillageEvflag (070) | `_MainEVflag[5]`, `[6]` | memory / witch village events | WitchVillageEvflag.cs:14-21 |
| EvflagActive / EvflagActiveobject | `_EvArraystage[...]` | Suzune talk triggers (009, 012, 014); ForestOfRequiem house entrance (031 Evhouse: `[8,0]&&[8,1]&&![8,2]`) | EvflagActive.cs, EvflagActiveobject.cs:28-48 |
| NPCtalkActiveSet / MGgetNPCEv | `_NPCEvFlag[n]` | hides NPC / shows magic | NPCtalkActiveSet.cs:14 |
| TradeMenuStart | `_Tradeflag[0]` | trader | above |
| Boss arenas | `!_BOSS[n]` loads the boss scene | arena walls exist only while the boss scene is loaded (inferred) | LoadBossSceneAdd.cs:25, UIfadeEv.cs:49 |
| ElevatorON | `_ElevatorPointList[number] \|\| FristUSE` | elevator; usable from the `FristUSE` side, which unlocks the other side | ElevatorON.cs:54-112 |
| DoorOpen (levers), GimmickLadder_on, BridgeOpen, StopWaterFall, MapBreakObject* | no item condition; they only set `_GimmickArraystage` | shortcuts | DoorOpen.cs:49-61 |
| IvyRoadStop (not dumped) | HP regenerates unless hit by element[0] (inferred: fire) from a weapon or magic | ivy barrier, `_GimmickArraystage` | IvyRoadStop.cs:151-157, 366-371, 408-413 |
| MapBreakObjectName / ObjectCollisionBreak (not dumped) | collision with an object of a specific serialized `Name` (inferred: a spell projectile or thrown object) | breakable wall | MapBreakObjectName.cs:57-84 |

Elevator numbers are global, not per scene: `number` 1 and 2 are reused in several scenes
(2 appears in 009, 012, 013, 032, 047, 061). Unlocking one unlocks all elevators with that number.

---

## Interaction with the prototype's existing patches

- **Pickup_magic.Start/Update**: these also cover Candore's `Mgdrop`, the `MagicGetCreate` prefabs and
  the `mgEvget` magic (NPC talkactor 1), because all of them are Pickup_magic. No extra patch is
  needed, but location keys must include prefab instances that have no scene path (key by magic kind +
  `Getflagnum`). `Magicselectget.ListCheck` writes Lightning[1] behind the patch's back. Patch it
  (transpiler, or a postfix that restores the previous value) if Lightning[1] is a location.
- **Spawnenemy.Update** (enemy randomizer): Bigoni_spine (village_main), Slaughterer (UndergroundChurch)
  and Mimicks carry one-time treasures. Bigoni also carries the Hexkey. Replacing or removing those
  spawns loses or moves those drops. Exclude them or move the treasure logic. Boss scenes are loaded
  through LoadBossScene*/UIfadeEv, not Spawnenemy.
- **Pickup_item.Update**: unaffected by the hooks above. Account for `ShopWpEv` hiding Shop ITEM15
  when WP 265-270 is owned.
- **SaveFile.SaveFlag / LoadFile.SetYesButtonClicked**: every flag used here
  (`_BOSS, _BossIdeaflag, _Tradeflag, _NPCEvFlag, _EvArraystage, _GimmickArraystage, _MainEVflag,
  _CosSkinflag`) is saved by vanilla (SaveFile.cs:223-253). `ShopCount/ShopUseitem` are not. A sent-set
  for boss checks has to be saved by the mod because `_BOSS` can be reset.
- **GameStart_menu.fun_Newgame**: the new-game starting items come from `PlayerStatus.Start`
  (PlayerStatus.cs:908-916), which runs later. Grant AP starting items after that point.
