# Research: player feedback items (grabs, dialogue skip, church event, slave events, camera, UI)

Night of Revenge 1.0.7. Sources:
- Decompiled game code (ILSpy, `Assembly-CSharp`, global namespace). File names below refer to that decompile, and line
  numbers are ILSpy line numbers.
- Static scene dumps in `dump/static`.
- Extra scans made for this note:
  - `NoR.SceneScanner --types` with narrower regexes, which also found field-less components such as `DeathCount`.
  - UnityPy for RectTransforms, CanvasScaler and Camera data.
  - A small AssetsTools.NET dump of TMP settings.

Anything marked **(inferred)** was not verified at runtime.

---

## 1. Grab / struggle

### 1.1 How a grab starts

All regular enemies use the same two-step pattern. `goblin.cs` is the reference; `Undead`, `Inquisition`, `Pilgrim`,
`suraimu` and `Minotaurosu` are identical apart from extra distance checks.

1. **Approach.** In the enemy's `Update`: if `com_player.erodown != 0 && com_player.m_Grounded && m_Grounded &&
   |distance| < 15 && ...`, and the enemy is not attacking, damaged or dead, then `state = EROWALK` (goblin.cs:374-377).
2. **Grab.** In `OnTriggerStay2D`: if `!com_player.eroflag && !eroflag && state == EROWALK && com_player.state == "DOWN"`
   and the collider tag is `playerDAMAGEcol` (goblin.cs:1403-1418), the enemy sets:
   - `com_player._eroflag2 = true`, `com_player.eroflag = true`, and its own `eroflag = true`.
   - Enables its ERO child (`ero`, `erospine`, `erodata`).
   - Calls `camera_GetComponent(); ero_camera_1();`, which zooms in.

So the universal gate for "can grab a downed player" is the string `playercon.state == "DOWN"` together with
`!playercon.eroflag`. `state` is set to `"DOWN"` in `anime_fun` when the player is grounded with `nowdamage &&
erodown == 1|2` and the knockback has slowed down (`nockbackspeed <= 10` or `<= 9`) (playercon.cs:4899-4932).

Variants:
- Minotaurosu also requires `|distance| <= 1` (Minotaurosu.cs:1624).
- Tentacle and TrapSpider only need `state == "DOWN"` or `erodown != 0` (Tentacle.cs:312, TrapSpider.cs:32).

### 1.2 Struggle input: handled in playercon, not in the ERO classes

The ERO classes (for example `goblinERO.cs`) only drive Spine animations and voice lines (`OnEvent` counters). They
never read input.

The only escape logic is `playercon.fun_nowdamage()` (playercon.cs:2016-2112), called every frame from
`playercon.Update` (playercon.cs:919):

```csharp
if (erodown == 0 || _easyESC || !playerstatus._SOUSA || (!key_submit && !key_atk)) return;   // 2050
if (m_Grounded) { if (downup >= 2) return; downup++; if (downup >= 2) { /* stand up */ } }
else if (rigi2d.velocity.y <= 0 && downup < 2) { downup++; ... same stand-up block ... }
```

The stand-up block (playercon.cs:2062-2081 and 2091-2110) does the following:
- `MasterAudio.StopBus("EroVoice"); MasterAudio.PlaySound("act_downup");`
- Resets `erodown = 0; damecount = 0; tough = maxtough; nowdamage = false; downup = 0;`
- Sets `rigi2d.velocity = (movespeed*key_move, vspeed)`, `rigi2d.gravityScale = gravity`, `walldown = 0`.
- Sets the HUD face to `IDLE` or `IDLE2`.

Inputs: `key_submit = player.GetButtonDown("Submit")` (playercon.cs:700) and `key_atk = GetButtonDown("Attack")`
(playercon.cs:690). While the player is grabbed, `rigi2d.simulated = false` (playercon.cs:951-954), so `m_Grounded`
stays true and **two presses** escape.

The same code serves both "get up from a knockdown" and "escape a grab". The only difference is whether an enemy has
already set `eroflag`.

### 1.3 What releases the grab

Each grabbing enemy polls the player in its own `Update`, in `eroanime()` (goblin.cs:1450-1477):

```csharp
if (!eroflag) return;
...
if (com_player.erodown == 0) {
    MasterAudio.StopBus("EroVoice");
    ero_camerareset(); Invoke("fun_DisableWhenOneTarget_reset", 2f);
    eroflag = false; ...; enmTough -= 999f; enmMAXfaltertime = 2.2f; enmfaltertime = 1f;   // enemy staggers
    erodata.SetActive(false); erodata2.SetActive(false);
    com_player.eroflag = false; com_player._eroflag2 = false;
}
```

So **the release trigger is `playercon.erodown == 0`**. The enemy clears the player's `eroflag`/`eroflag2` itself.

Coverage: every one of the 80 classes that set `eroflag = true` tests `erodown == 0`, except the scripted event and
cutscene classes. Those exceptions are `AutoMainEVStart`, `ColMainEVStart`, `EVslaveBigAxwMng`, `EvBarEROMng`,
`EvBarEV1_*TalkMng`, `EvBigAxeTalkMng`, `EvMainTalkactor`, `EvforestOfrequiem1`, `PunishmentTentacleIronmaiden` and
`RosewarmPunishment`, which use their own end logic.

`_easyESC` (backing field `easyESC`, property at playercon.cs:473) **blocks** escaping (playercon.cs:2050):
- Fatality and "fade to next scene" phases set it to `true`. Examples: InquisitionWhiteERO.cs:92-118,
  LibrarianERO.cs:103-129, `*Fatality.cs`, and playercon.cs:4544 for the birth animation.
- It is reset to `false` on the next normal damage (playercon.cs:1713) and on death (playercon.cs:998).

### 1.4 Recommended: force an immediate escape (QTE success)

Run the vanilla stand-up block from outside and let each enemy's `eroanime()` release on its next `Update`:

```csharp
// guard: only for real grabs, not cutscenes
if (pc.eroflag && pc.erodown != 0 && !pc._Death) {
    pc._easyESC = false;
    pc.erodown = 0;
    Tr.Field("downup").SetValue(0);  Tr.Field("damecount").SetValue(0f);
    Tr.Field("tough").SetValue(Tr.Field("maxtough").GetValue());
    pc.nowdamage = false;            // public? if private use Traverse
    pc.rigi2d.gravityScale = Tr.Field("gravity").GetValue<float>();
    Tr.Field("walldown").SetValue(0f);
    MasterAudio.StopBus("EroVoice"); MasterAudio.PlaySound("act_downup");
}
```

An equivalent shortcut is a Harmony prefix on `playercon.fun_nowdamage` that sets
`_easyESC = false; key_submit = true; downup = 1;` for one frame. The vanilla code then performs the stand-up, which
is the most faithful option.

- **Do not** clear `eroflag` yourself. If you do, the enemy's `eroanime()` never runs its cleanup: it hides its
  renderer, the ERO object stays active and the camera stays zoomed.
- Scripted event grabs (the list in §1.3) are not released by this. Skip the QTE when no `EnemyDate` in the scene has
  its own `eroflag` set. `EnemyDate.eroflag` is a serialized field, as seen in the dumps.

### 1.5 Recommended: damage during a grab

No vanilla ERO class damages HP. The only HP drain is `LastIbaranoMajyoERO.damageAradia`
(LastIbaranoMajyoERO.cs:75-84), which does `pl.Hp -= 10` and spawns a red TMP popup.

`fun_damage` / `fun_damage_Improvement` should not be used because they also change knockback, `erodown`, `state`
and guard state. Instead apply the damage directly:

```csharp
var ps = PlayerStatus instance;              // GameController
ps.Hp -= dmg;  ps._HpRecoveryTime? / pc._HpRecoveryTime = 2f;   // pause regen like vanilla (playercon.cs:1461)
if (ps.Hp <= 0f && !pc._Death) pc.SpDeath(); // same path DeathLinkHandler uses
```

`SpDeath` sets `Death`, calls `REstart_menu()` and `fun_death(4,...)`. That it is safe while `eroflag` is set is
(inferred) from DeathLink usage.

### 1.6 Downed (DOWN) duration and a ~3 s get-up lockout

**Vanilla has no minimum down time.** `downup` counts the presses:
- On the ground, presses count immediately, including during the knockback slide.
- In the air, presses count once the player is falling (`velocity.y <= 0`).

So mashing gets the player up on the first grounded frame. Without input the player stays down forever
(`nowdamage` stays true and `erodown != 0`); enemies with `erodown != 0` switch to `EROWALK` and come to grab.

Recommended lockout:

1. **Detect a knockdown.** Poll `pc.erodown` in a MonoBehaviour, or postfix `fun_damage` / `fun_damage_Improvement`.
   On the `0 -> !=0` transition with `!pc.eroflag && !pc._Death`, record `downStart = Time.time`.
   Optionally start the timer only when `pc.m_Grounded` first becomes true.
2. **Block the input.** Add a Harmony prefix on the private `playercon.fun_nowdamage()`. It runs after `Getinput()` in
   the same `Update`. While `Time.time - downStart < 3f && !pc.eroflag`, set the private fields `key_submit` and
   `key_atk` to `false` (via `AccessTools.FieldRefAccess<playercon,bool>`).
   - Keep the `!eroflag` condition, otherwise the lockout also blocks grab escapes.
   - A longer down window means more grabs, because every nearby enemy enters `EROWALK` while `erodown != 0`.
3. Alternative: set `_easyESC = true` for 3 s and restore it. This collides with fatalities that use the same flag, so
   it is not recommended.

---

## 2. Dialogue skip

### 2.1 Systems

PixelCrushers Dialogue System is **not used** by game classes. No game class references `DialogueManager`.
All game dialogue uses one of the custom typewriter controllers below. They share the same template:
- fields `currentText`, `timeUntilDisplay`, `timeElapsed`, `lastUpdateCharacter`, `intervalForCharacterDisplay`;
- `IsCompleteDisplayText => Time.time > timeElapsed + timeUntilDisplay`;
- `SetNextLine()`.

Text appears **character by character**:
`num = Clamp01((Time.time - timeElapsed) / timeUntilDisplay) * currentText.Length; uiText.text = currentText.Substring(0, num)`.

Advance input is `player._key_submit` or `Input.GetMouseButtonDown(0)`. Note that `playercon._key_submit` is
`key_submit = player.GetButtonUp("Submit")` (playercon.cs:559), so it triggers on button **up**.

| Controller | Used by / data | Interval | Advance / end logic |
|---|---|---|---|
| `DialogControllerMainEVunder` | main story events. Data: `MainEV.MainEV1` rows (`MainEvStory`: `Jptext`, `endOfTalk`, `branch`, `Wait`/`waittime`, `AnimationChange`/`ChangeNum`, `doConnect`/`skipId`). Actor: `EvMainTalkactor` | 0.05 | Update 448-536. Waits 2 s on start (`count`). If not complete, submit sets `timeUntilDisplay = 0` (521-524). If complete and not end-of-talk: `SetTalk(); SetNextLine();` (489-496). If end-of-talk: one of `talkendEvend` / `talkendGo` / `talkendBattle` / `talkendEV` / `WindowEnd` (497-519). Choices: `choisebuttonflag` stops Update and buttons call `ChooseYes`/`ChooseNo`. Timed waits: `Invoke("WaitEnd", waittime)` (443) |
| `DialogControllerNpcEVunder` | NPC events (`NpcTalk` asset). Actor: `EvNpcTalkactor` | 0.05 | same template (Update 428-490, `timeUntilDisplay = 0` at 488) |
| `DialogControllerEroEVunderWindow` | sub-events (`SubEventDialogue`). Actors: `EvSuzuneTalk`, `EvBigAxeTalkMng`, `EvBarEV1_*TalkMng`, `InsomniaEvTalkactor`, `EvforestOfrequiem1` | 0.05 | same (Update 428-490) |
| `DialogControllerEroEv` | sub-events (single window) | 0.05 | Update 287-375. End: `talkendEvEND` / `talkendGo` / `talkendBattle` / `talkendEV` / `WindowEnd` |
| `TextController` | game-over narration (`scenarios[]`) | 0.05 | Update 55-96. Last line runs `narration()`, `on_slow()`, `Invoke(REstrat, 7)` |
| `TextControllerGO`, `TextControllerGAGO` | bad-end texts (game / gallery). GAGO has its own Rewired `_key_submit` (TextControllerGAGO.cs:137) and loads `Gallery` at the end | 0.05 | GO Update 118-200, GAGO 164-246 |
| `Dialogtextcontrol`, `ShopDialogtextcontrol` | shop greeting (2 hard-coded lines) | 0.02 | End: hides the box, or `ShopDialogtextcontrol` instantiates the shop menu |
| `ShopTalk` | Witch shop talk | 0.02 | End (90-105): **grants ring/item 265 once** (`_GimmickArraystage[5,11]`), restores `_SOUSA`, `WitchShop.flagChange()`, destroys the root |
| `VendettaDialogtextcontrol` | Vendetta greeting | 0.02 | End: destroys itself and opens the menu |
| `VendettaDialogtextcontrol_second` | Vendetta (level reset) | 0.02 | At `currentLine == menucount` instantiates the `Lvreset` menu. End or `Allend()`: `_SOUSA = true`, `VendettaTalk.flagChange()`, destroys the root |
| `Treasureboxdialog` | chest text | 0.02 | same template |

Not typewriter based; these only open or host the controllers:
- `TalkStartMng`: press Submit in a trigger, then `Talkset` and after 1.2 s activates `TalkactorObj`.
- `VendettaTalk`, `VersatilityDialogStart`, `BOSSEvMainDialog`: trigger, then instantiate or activate a dialog object.

Not skippable and harmless: the event "actors" (`EvMainTalkactor`, `EvNpcTalkactor`, `EvSuzuneTalk`,
`EvBigAxeTalkMng`, ...) and the speech bubbles (`SlaveDialogOnly`, `SisterMobero`, `witchslave*`) are `Invoke`-timed
canvases.

**End actions live in two places:**
1. **In the controller.** The `talkend*` methods (fade, `REstrat`, battle start) and the shop or Vendetta end branches
   (item grant, menus).
2. **In the event "actor".** The actor watches the controller each frame:
   `if (Dialogmng._animeflagnum == 1 && Dialogmng._AnimationChange && Changenum[k] == Dialogmng._Changenum) ...`.
   This sets `FlagMng._MainEVflag[n]`, `_Tradeflag[0]`, boss flags and camera targets, and calls `Invoke("talkENDSceneMove", 1)`.
   Examples: EvMainTalkactor.cs:223-1570 and EvBigAxeTalkMng.cs:119-237.
   `_animeflagnum = 1` is set in `SetTalk()` when a line is advanced, and reset to 0 in `Update` once the next line is
   fully shown (DialogControllerMainEVunder.cs:445, 488).

So **jumping `currentLine` to the end would skip flags, rewards and scene moves.** Each line with `AnimationChange` must
be "seen" for at least one frame.

### 2.2 Recommended hooks

**(a) Instantly complete the current line.** Postfix `SetNextLine` on each controller, or prefix `Update`, and set the
private field `timeUntilDisplay = 0f`. This is exactly what vanilla does when Submit is pressed mid-line. All 12 classes
above have the field with this name: patch them through a list of types and `AccessTools.Field(t, "timeUntilDisplay")`.

**(b) Skip to the end while running all end actions (fast-forward).** Simulate Submit *inside the dialog controllers only*:

- Prefix and postfix on each controller's `Update` set and clear a static `InDialogUpdate` flag. Also set
  `timeUntilDisplay = 0` in the prefix.
- Postfix on the getter `playercon.get__key_submit`:
  `if (InDialogUpdate && SkipHeld && FrameGate()) __result = true;`.
  - Dialog *starters* (`TalkStartMng`, `VendettaTalk`, `Slavehelp.help`) also read `_key_submit`, but they are not
    inside a controller `Update`, so they are not affected.
- Each line advances once per frame, so every `_Changenum` is visible to the actor for at least one frame and all flag,
  reward and scene-move branches run. Use a 2-3 frame gate per line for safety. **(inferred)**: script execution order
  between controller and actor is not fixed.
- Stops by itself at:
  - choices (`choisebuttonflag` returns early, so the player must choose);
  - timed waits (`_wait`). To shorten one: `CancelInvoke("WaitEnd"); WaitEnd();` on the controller.
- At the end-of-talk line, press only **once** per controller instance. Repeated presses re-run `talkendEvend` /
  `talkendEV` (`fadeimg.on_slow()`) every frame.
- Exclude the shop and Vendetta controllers from auto-advance (instant text only). Auto-presses would open or close their
  menus.
- `TextControllerGAGO` uses its own input property, so patch its `_key_submit` getter the same way if gallery bad-ends
  should be skippable.

---

## 3. Witch merchant rescue event (EVuChurch): why it hangs

### 3.1 What the event waits for

Scene `EVuChurch` (level50):

1. `EvBigAxeTalkMng` (`TalkEvNo = 0`) reaches `Changenum[5]` and calls `Invoke("BattleSet", 2)`, which runs
   `EvBigAxeBattleMng.BattleStart()` (EvBigAxeTalkMng.cs:139-142, 249-252).
2. `BattleStart` activates `spwnMng = Spawn_UnderEV` (EvBigAxeBattleMng.cs:39-50).
3. Every frame, `EvBigAxeBattleMng.Update` checks
   `if (deathcount == EvDeathcount.EnemyDeathcount) { fade; Invoke("BattleEnd", 2f); }` (EvBigAxeBattleMng.cs:29-37).
   In the scene, `deathcount = 2` and `EvDeathcount` is the `EvBattleDeathCount` component on `Spawn_UnderEV`
   (scene dump 050_EVuChurch.json:186-280).
4. `EvBattleDeathCount.EnemyDeathcount` is only incremented by **`DeathCount.OnDestroy()`** (DeathCount.cs:12-15).
   `DeathCount` is a field-less component **on the enemy prefab**. In `Start` it finds
   `transform.root.GetComponent<EvBattleDeathCount>()` (DeathCount.cs:7-10). This works because
   `Spawnenemy.Update` instantiates the enemy **as a child of the spawn point** (Spawnenemy.cs:23), and the spawn point
   is a child of `Spawn_UnderEV`.
5. `BattleEnd` (EvBigAxeBattleMng.cs:52-69) re-enables the talk with `TalkEvNo = 1` and `TalkID(1)`.
   - The later lines set `Flagmng._Tradeflag[0] = true` (the witch merchant is rescued and shop trading unlocks), then
     `SceneMove`, which activates `MoveSceneObj` (EvBigAxeTalkMng.cs:196-211).
   - The player is locked (`playerActionStop`) until then, which explains "cannot leave".

Prefab scan results:
- `DeathCount` is present only on **`EVSlaveBigAxe`, `EVNomalSlaveBigAxe`** (sharedassets50) and **`Sheephead`**
  (Foresthouse).
- No other prefab has it.

With the randomizer, `Spawn_UnderEV/Spawnpoint (1)` (EVSlaveBigAxe) and `(2)` (EVNomalSlaveBigAxe) become ordinary
enemies without `DeathCount`, so the counter stays at 0 forever.

The reverse case also breaks things: an EV BigAxe or `Sheephead` placed in a normal scene has
`deathcount == null` and throws a NullReferenceException in `OnDestroy`. That is harmless. But if one lands in one of
these battle groups, it is counted (`Sheephead` is in the pool).

### 3.2 All event-watched spawners (exclude these from randomization)

| Scene (index) | Watcher | Spawner root | Required kills | Watched spawners (original prefab) | Other spawners under the same root |
|---|---|---|---|---|---|
| `EVuChurch` (50) | `EvBigAxeBattleMng` (BattleMng) | `Spawn_UnderEV` | 2 | `Spawnpoint (1)` EVSlaveBigAxe, `Spawnpoint (2)` EVNomalSlaveBigAxe (both at ~353,-162) | `Spawnpoint` goblin (-177,-50), `(3)` Dorei (355,-91), `(4)` InquisitionBlack (386,-106): copied leftovers, normally off-screen |
| `EVuChurchAfter` (51) | `EvBigAxeBattleMng` | `Spawn_UnderEV` | 2 | `(1)`, `(2)` Dorei (same position). `EvBigAxeTalkMng.TalkEvNo = 2` never calls `BattleSet`, so this battle is probably unused **(inferred)** | `Spawnpoint` goblin, `(3)` Dorei, `(4)` InquisitionBlack |
| `Foresthouse` (55) | `EvBattleforestOfrequiem` (EvBattleforestOfrequiem.cs:29-66, same logic) | `Spawn_UnderEV` | 1 | `Spawnpoint (1)` **Sheephead** (17.6,-284; battle at 16.3,-283.7) | `Spawnpoint` goblin, `(2)` EVNomalSlaveBigAxe (354,-162, off-screen leftover) |

**Simplest rule:** do not randomize any `Spawnenemy` whose `transform.root` has an `EvBattleDeathCount` component,
which in practice is any path starting with `Spawn_UnderEV/`.

If those spawners should still be randomized: postfix `Spawnenemy.Update`, detect the new last child (see
research-combat.md §1.4), and `AddComponent<DeathCount>()` to it when the original prefab had one. `DeathCount.Start`
runs after `AddComponent` and finds the root.

The randomizer currently excludes `EVSlaveBigAxe`, `EVNomalSlaveBigAxe`, `CrowInquisition_hide` and `CocoonmanStart`
from the *pool* (EnemyPool.cs:38). That stops them being placed elsewhere, but it does **not** protect the watched
spawners. `Sheephead` should also be excluded from the pool, or the `DeathCount` should be stripped from the spawned copy.

Other enemy-watching mechanisms (none use `Spawnenemy`):
- `EnemydeathCheck` in `WhiteCathedralRooftop` (47). See §4.
- `OnryOne` uniques: `DifferentBigMerman` (stage 7, gimmick 11), `Minotaurosu_dif` (10/11), `Inquisition_white_dif`
  (12/11).
  - They set `_GimmickArraystage[stage,11]` on death and drop a fixed weapon, for example `Minotaurosu_dif` drops 157
    (Minotaurosu.cs:343-358) and `DifferentBigMerman` drops 133 (BigMerman.cs:312-316, 1297-1300).
  - They deactivate themselves when the flag is set or when the player owns the weapon set (Minotaurosu.cs:186-215,
    BigMerman.cs:173-179).
  - They are in the pool. Placed elsewhere they duplicate that unique drop, and their original spawn point loses it.
    Exclude them from the pool and from swapping. This needs checking against `research-locations.md` if these drops
    are location checks.

### 3.3 Event-only and special pool prefabs

| Prefab | Component | Behaviour on spawn | Safe in pool? |
|---|---|---|---|
| `EVSlaveBigAxe`, `EVNomalSlaveBigAxe` | `OtherSlavebigAxe` + `DeathCount` + `SlaveBigAxeEro` | Normal BigAxe AI without `ExpVal`. `DeathCount` NREs outside the event | No (already excluded) |
| `Sheephead` | `Sheepheaddemon` + `DeathCount` + `SheepheaddemonFatality` | only placed in Foresthouse. Counts toward the Foresthouse battle | No, exclude |
| `CrowInquisition_hide` | `CrowInquisition` with `hide = 1` | Ambush: invisible (`HideSpine` shown, rigidbody off) until the player is within 4 units, then `ATKHIDEATTACK` (CrowInquisition.cs:571-600) | Works, but it is an ambush statue (already excluded) |
| `CrowInquisition_MOBERO`, `_MOBERO2` | `CrowInquisition` with `mobero = 1` + `EnemydeathCheck` | Created by the cathedral rooftop slave scenes. On death calls `EnemydeathCheck.DeathCheck(n)` (CrowInquisition.cs:1905-1909) | Not in the pool (no spawner) |
| `CocoonmanStart` | `Cocooncreat` | Static cocoon. On player contact it instantiates `Cocoonman` **unparented** in state `AIR` and destroys itself (Cocooncreat.cs:17-27) | Ambush wrapper (already excluded) |
| `sura_create` | `Enemy_create` (+ `HideDamage`) | Hidden slime puddle. On contact instantiates `sraimu` **unparented** in `ATK2` and destroys itself (Enemy_create.cs:32-41). It can also be killed directly (`damege`, 50 exp) | OK as a replacement. Because the real enemy is unparented, child-based post-processing misses it |

---

## 4. "Attack to rescue" slave events

### 4.1 How the enemy is created

These events do not use `Spawnenemy`:

- `SpawnSlave` (SpawnSlave.cs:28-46) only toggles a **scene-placed** MOB object (`slave`) by camera visibility until
  `_helpslaveStage[stage, SlaveNumber]` is set.
- The MOB object (`MobSlumSlave`, `MobCrawlingSlave`, `MobSpiderSlave`, `witchslaveViolin`, ...) handles input:
  - **Attack hit** (`ATKweapon` / `ATKmagic` trigger) sets `helpflag` and calls `LookMonster.create()`
    (MobSlumSlave.cs:221-237).
  - Afterwards, standing in the trigger with `helpflag` makes `Update` call `slavehelp.help()`, which on Submit
    "rescues" the slave (MobSlumSlave.cs:82-85, Slavehelp.cs:57-73).
- **`SlaveSideLook.create()`** runs `Invoke(createinvoke, 0.3)`, which does
  `Object.Instantiate(CreateMonster, pos, rot)` **unparented** and destroys the ERO monster sprite (SlaveSideLook.cs).
- Variants:
  - `EnemyMobCrowSlaveBack` / `EnemyMobCrowSlaveStandup` `createinvoke` instantiate `CreateMonster` and then
    **require** `GetComponent<EnemydeathCheck>()._flag = true` (EnemyMobCrowSlaveBack.cs:96-101,
    EnemyMobCrowSlaveStandup.cs:111-116).
  - `witchslaveslime` instantiates `createMob` on the Spine event `CREATE` (witchslaveslime.cs:139).

Monsters per scene (dump):

| Scene | Creator (prefab) |
|---|---|
| village_main | `SlaveSideLook` x2 (goblin), `witchslaveslime` (sraimu) |
| scapegoatEntrance | `SlaveSideLook` (Mutude, goblin) |
| UndergroundChurch | `SlaveSideLook` x6 (Dorei) |
| InundergroundChurch | `SlaveSideLook` (CrawlingCreatures) |
| InsomniaTownC | `SlaveSideLook` (Vagrant_spine) |
| WhiteCathedralRooftop | `EnemyMobCrowSlaveBack` / `Standup` + `SlaveSideLook` (CrowInquisition_MOBERO / _MOBERO2) |

### 4.2 Safe randomization

- Use a Harmony prefix on `SlaveSideLook.createinvoke` that replaces the private `CreateMonster` with
  `pool.Choose(seed, scene, path, 0)`. Do the same for `witchslaveslime` `createMob` (prefix on `OnEvent` or `Start`).
- **Skip** when the original `CreateMonster` has an `EnemydeathCheck` component (the `CrowInquisition_MOBERO*` ones).
  Also never touch `EnemyMobCrowSlaveBack` / `Standup`, which would NRE.
  - `EnemydeathCheck.DeathCheck(1)` releases the slave animation and grants a one-time item drop (item 62, kind 1;
    EnemydeathCheck.cs:85-98).
- The created monsters are unparented and not tracked by `SpawnParent`. They are destroyed by tag on death or respawn
  (`playercon.Death_flag`, playercon.cs:838-843). Stat scaling must hook `EnemyDate` `Start` generically rather than
  spawner children.

### 4.3 Camera stays zoomed in: cause

`ProCamera2DZoomToFitTargets.OverrideSize` (ProCamera2DZoomToFitTargets.cs:76-99):

```csharp
if (!enabled) return originalSize;          // originalSize == current size -> zoom FREEZES where it is
if (DisableWhenOneTarget && CameraTargets.Count <= 1) _targetCamSize = _initialCamSize;   // zoom back out
else UpdateTargetCamSize();                 // 1 target + DisableWhenOneTarget=false -> zoom in to _initialCamSize / MaxZoomInAmount
```

So zooming in means `enabled = true; DisableWhenOneTarget = false` with only the player as target. Zooming back out
requires the component to stay **enabled** with `DisableWhenOneTarget = true` until the size has smoothly returned.

The well-behaved paths keep it enabled and disable it 2 s later:
- `EnemyDate.ero_camerareset()` + `Invoke("fun_DisableWhenOneTarget_reset", 2f)` (EnemyDate.cs:637-671,
  goblin.cs:1460-1461).
- playercon stab end (playercon.cs:4390-4391, 2435-2438).
- `Savepoint_on._off` (Savepoint_on.cs:120-136).

The slave events zoom in when the player enters the MOB trigger:
- `Slavehelp.ero_camera_1` / `EnemyMobhelp.ero_camera_1` (Slavehelp.cs:84-88, EnemyMobhelp.cs:23-27, 50-57);
- MobSlumSlave.cs:243-246 and witchslaveViolin.cs:514-518.

They then **disable immediately**:
- `Slavehelp.ero_camerareset` / `EnemyMobhelp.ero_camerareset` / `witchslaveViolin.ero_camerareset` /
  `SisterMobero.ero_camerareset` do `prozoom.DisableWhenOneTarget = true; prozoom.enabled = false;`
  (Slavehelp.cs:90-94, EnemyMobhelp.cs:29-33, witchslaveViolin.cs:520-524, SisterMobero.cs:312-316).
- They are called on trigger exit, and in `Slavehelp.help()` on rescue (Slavehelp.cs:65).

Disabling freezes the camera at the zoomed-in size. Nothing restores it until a later grab or stab re-enables the
component with `DisableWhenOneTarget = true`.

Two related cases freeze the same way:
- `playercon.fun_cameraReset` (playercon.cs:849-856). It is used after save-point warps and respawns, via
  `fun_cameramove` (playercon.cs:858-865, game_fragmng.cs:591+, Savepoint_menu.cs:547-591).
- `Ero_camera.fun_DisableWhenOneTarget_reset` and `Trapdata.fun_DisableWhenOneTarget_reset` if they fire mid-zoom.

During grabs the player can also cycle `MaxZoomInAmount` 1.5 -> 2 -> 2.5 with Jump (`fun_cameraEROZOOM`,
playercon.cs:888-904). It is reset to 1.5 by `ero_camerareset`.

Camera facts (level10 "Common", also present in level2):
- `Main Camera` is a **perspective** camera (FOV 65, z = -8) driven by ProCamera2D.
- "Zoom" is `ProCamera2D.SetScreenSize()`. For a perspective camera this changes the camera **distance** (or FOV if
  `ZoomWithFOV`) (ProCamera2D.cs:719-735). `orthographicSize` has no effect.
- The baseline sizes are captured once:
  - `ProCamera2D._startScreenSizeInWorldCoordinates` in Awake (ProCamera2D.cs:220-221);
  - `ZoomToFitTargets._initialCamSize` in `Start` after `WaitForEndOfFrame` (ProCamera2DZoomToFitTargets.cs:68-74).

### 4.4 Recommended generic reset and zoom setting

- **Fix the cause.** Prefix the five `ero_camerareset` variants (`Slavehelp`, `EnemyMobhelp`, `witchslaveViolin`,
  `witchslaveslime`, `SisterMobero`) and `playercon.fun_cameraReset`.
  - Replace the immediate disable with `DisableWhenOneTarget = true; enabled = true;`, then disable it 2 s later
    (a coroutine on the plugin), exactly like `EnemyDate`.
- **Watchdog (covers any other path).** Every ~0.5 s, if all of the following hold:
  - `!pc.eroflag && !pc._eroflag2 && pc.erodown == 0`;
  - no dialog controller is active;
  - `!zoom.enabled`;
  - `|ProCamera2D.ScreenSizeInWorldCoordinates.y * 0.5 - desired| > 0.05`;

  then call `ProCamera2D.Instance.UpdateScreenSize(desired, 0.5f)`. Its public coroutine handles both ortho and
  perspective. Alternatively, enable the zoom with `DisableWhenOneTarget = true` for 2 s.
- **Zoom in/out setting.** Set `desired = baseHalfHeight * factor`. To make the vanilla code return to the new default
  instead of the original one:
  1. After `ZoomToFitTargets.Start` has run (one frame after scene load), write both baselines via reflection:
     - `ProCamera2D._startScreenSizeInWorldCoordinates = new Vector2(desired*2*aspect, desired*2)`;
     - `ZoomToFitTargets._initialCamSize = desired`.
  2. Then call `UpdateScreenSize(desired)`.

  Every vanilla "zoom back" (`DisableWhenOneTarget = true`, `ResetSize`, `OnReset`) then targets the user's level.
  Grab zoom-ins stay relative because `_minCameraSize = _initialCamSize / MaxZoomInAmount`.
  The **Reset** option uses the original baseline captured at the first scene load.

---

## 5. UI

### 5.1 Red "X" for insufficient weapon or spell stats

Code: `PlayerStatus.LowattributeIconSet` / `LowattributeIconMGSet` / `LowMGIcon` (PlayerStatus.cs:801-856) call
`UImng.LowattributeIconenabled(n)` / `...Unenabled(n)`, which only toggle `Lowattributeicon[n].enabled`
(UImng.cs:136-144). The "not enough stats" text is `UImng.Lowattribute` (TMP, shown for 1.3 s, UImng.cs:156-176).

**No code positions them; the layout comes from the scene.** HUD canvas `Canvas` in level10 (Common):
`CanvasScaler` ScaleWithScreenSize, reference 1280x720, MatchWidthOrHeight, match = 0 (width). That this is the HUD
scaler, out of the two named "Canvas", is (inferred).

| Object | Anchor | anchoredPosition | Belongs to (anchor (0,0)) |
|---|---|---|---|
| `wp_notact` (X, `Lowattributeicon[0]`) | **(0.5,0.5)** | (-575.5, -251) | `actnow0` / `Mg0` at (54,119/120) |
| `mg_notact1` | (0.5,0.5) | (-514.2, -251) | `Mg1` / `actnow1` (115,120) |
| `mg_notact2` | (0.5,0.5) | (-514.2, -189.6) | `Mg2` (115,181) |
| `mg_notact3` | (0.5,0.5) | (-453.9, -251) | `Mg3` (175,120) |
| `mg_notact4` | (0.5,0.5) | (-514.2, -311.1) | `Mg4` (115,59) |
| `Lowattribute`, `Can'tAction` (TMP) | (0.5,0.5) | (-525, -115) | above the spell wheel (`MagicUI` at 115,119) |
| `Zoomkey` (grab zoom hint) | (0.5,0.5) | (498, -240) | bottom-right column (`money` at (-142,82), anchor (1,0)) |

In the 1280x720 reference space every X sits at its icon position plus (+10.5, -10) (`wp_notact` -> (64.5,109)). That
lines up on 16:9 only.

On 21:9, for example 2560x1080:
- **Vanilla (match = 0):** the canvas is 1280 x 540. The X moves to y = 270 - 251 = 19 instead of 109, about 90 units
  below its icon.
- **With NoR.Widescreen (match = 1):** the canvas is 1706.7 x 720. The X moves to x = 853 - 575.5 = 278 instead of
  64.5, about 213 units to the right.

`Lowattribute`, `Can'tAction` and `Zoomkey` drift the same way.

**Fix** (fits `NoR.Widescreen.UiFixer.Fix`, for the HUD canvas, the one with child `UI` that has `UImng`): re-anchor
these children to the corner of their icon group and keep the 1280x720 layout:
- `newAnchor = (0,0)` for `wp_notact`, `mg_notact1-4`, `Lowattribute`, `Can'tAction`:
  `anchoredPosition += (640, 360)`.
- `Zoomkey`: anchor `(1,0)`, `anchoredPosition = (498+640-1280, -240+360) = (-142, 120)`.

The other center-anchored HUD items (`Lowtxt`, `Badstatusinfo`, `MAPinfo`, `GOenykey`, `maptitle`) are horizontally
centred, so they are only affected vertically, and only with match = 0.

### 5.2 Fast-travel (altar) menu and other overflowing labels

Altar menu is prefab `savepoint_menu/move` (sharedassets2).

**Destination buttons.** `savepoint_menu/move/Altarname/Altar<Area>/<btn>/TextMeshPro Text`, for example
`F_entrancebtn`, `villagebtn`, `TownB`, `UnderRoad`, `Inentrancebtn`, `OverPilgrimageEntrancebtn`, `returnbtn`. The
lists are instantiated by `AltarNameCreate.ListCreate` and selected by `Altarbuttonselect`.
- `TextMeshProUGUI`, fontSize 24, **enableAutoSizing = false** (min 18 / max 72 are defaults, unused).
- enableWordWrapping = true, overflowMode = Overflow (0), alignment Center (5).
- RectTransform 200x60 (`underbtn` 230x60), anchors centre.
- Long English names wrap to 3 lines or a long single word overflows the button.

**Area list** `savepoint_menu/move/Areaname/<Area>`. Only the numeric `help` TMP children are text (size 18, rect
35.3x30). The area names themselves are probably images **(inferred)**.

**Headers.**
- `savepoint_menu/move/NowArea`: 24 pt, 413.5x54.8, no autosize.
- `move/help`, `move/rank`: 24 pt, 208x30, align Left (4), no autosize.
- `move/help (1)`: 134x30. `move/help (2)`: 50x30.
- `move/back (1)/help (4)`: 800x60.
- `savepoint_menu/Button*/TextMeshPro Text` (save menu): 24 pt, 200x60.

**Already auto-sizing.** `savepoint_menu/Image/...` buttons (20 pt, min 12) and `Newdifficulty/info` (18, min 12).

**Other menus.** In level10 (pause menu `PlayerStatus/menu/*`, HUD) **342 of 353 TMP components have autosizing off**.
The biggest groups are `Magic_list` (189), `Weapon_list` (37), `canStatus` (19), `can_Sexstatus` (11), `canSpell` (10)
and `Ring_list` (8). Shop and trade menus (`BUY_menu`, `Trade_menu` in sharedassets11/18) follow the same pattern
**(inferred)**.

**Generic fix.** Hook into the existing translation path: `NoR.Localization.SerializedText.Apply(TMP_Text)` and the
`TMP_Text.text` setter prefix (SerializedText.cs:192-227). When a translation actually replaced the text:

```csharp
if (!tmp.enableAutoSizing && tmp.rectTransform.rect.height <= 80f)   // labels/buttons only, not dialogue boxes
{
    tmp.fontSizeMax = tmp.fontSize;
    tmp.fontSizeMin = Mathf.Max(10f, tmp.fontSize * 0.6f);
    tmp.enableAutoSizing = true;          // keep enableWordWrapping as authored
}
```

- TMP auto-size shrinks only when the text does not fit, so labels that already fit are unchanged.
- For single-line headers (`help`, `rank`, `NowArea`, `help (1)`), also set `enableWordWrapping = false` so they shrink
  instead of wrapping into the next row.
- Alternative for specific prefabs: patch `AltarNameCreate.ListCreate` (postfix) and walk the instantiated children.
- Keep dialogue windows (`dialoguetext`, height > 80) out of this. They are sized for multi-line text.

---

## Recommended hook summary

| Need | Hook |
|---|---|
| QTE escape | Write `erodown = 0` + the vanilla stand-up fields (or force `key_submit`/`downup` in a `fun_nowdamage` prefix). The enemy's `eroanime()` releases next frame |
| Grab damage | `PlayerStatus.Hp -= x`, then `SpDeath()` if `<= 0` |
| 3 s get-up lockout | `fun_nowdamage` prefix clears `key_submit`/`key_atk` while `!eroflag` and less than 3 s since the knockdown |
| Instant line | Set `timeUntilDisplay = 0` in the 12 typewriter controllers |
| Fast-forward | Return `true` from `playercon._key_submit` only inside the controllers' `Update`. Stop at choices, press the end line once, exclude shop and Vendetta |
| Church / Foresthouse hang | Skip `Spawnenemy` under a root with `EvBattleDeathCount` (`Spawn_UnderEV/*`). Exclude `Sheephead` and the `_dif` uniques from the pool |
| Slave events | Prefix `SlaveSideLook.createinvoke` (and `witchslaveslime.createMob`). Skip creators whose monster has `EnemydeathCheck` |
| Stuck zoom | Prefix the `ero_camerareset` variants and `fun_cameraReset` to use "enable + DisableWhenOneTarget, disable after 2 s", plus a watchdog using `ProCamera2D.UpdateScreenSize` |
| Zoom level | Set `_startScreenSizeInWorldCoordinates` and `_initialCamSize` via reflection, then `UpdateScreenSize` |
| HUD X on 21:9 | Re-anchor `wp_notact`, `mg_notact1-4`, `Lowattribute`, `Can'tAction` to (0,0) with +(640,360), and `Zoomkey` to (1,0) |
| Long English labels | Enable TMP auto-size (min about 60%) on translated labels with height <= 80 |
