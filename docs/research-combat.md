# Combat research: enemy stats, double hits, bad statuses

Research notes for Night of Revenge 1.0.7 (Unity 5.6, Assembly-CSharp decompiled with ILSpy).

- `acs/` means the decompiled sources (`scratchpad/decomp/acs`).
- Line numbers refer to those files.
- "(inferred)" marks conclusions that were not verified directly in code or data.
- Prefab and scene structure (tags, colliders, components) was read straight from the game's
  `NightofRevenge_Data` with UnityPy. The static JSON dumps contain only MonoBehaviours that have
  serialized fields, so they are missing colliders, tags and field-less scripts such as
  `ChildColliderTrigger`.

---

## 1. Enemy stats (for scaling a randomized replacement)

### 1.1 Common base class

Every regular enemy and every boss derives from **`EnemyDate : MonoBehaviour, ExecuteInterface, ExecuteInterfaceMagic`**
(`acs/EnemyDate.cs`). Sixty-one classes derive from it, including goblin, SinnerslaveCrossbow (prefab "Dorei"),
Inquisition (prefab "InquisitionBlack"), InquisitionRED, Undead, CrawlingCreatures, Pilgrim, Minotaurosu,
RequiemKnight, TouzokuNormal/TouzokuAxe, BlackMafia ("mafia_spine"), Vagrant and all the bosses.

`enemyHP.cs` and `enemyATK.cs` are empty or dead stubs; `enemyATK` only reads `Kakash.enmATK`. `enemyUI.cs`
only stores `Camera.main`. None of the three holds stats.

Stat fields, all public and declared on `EnemyDate`:

| Field | Line | Meaning |
|---|---|---|
| `public float MaxHp = 100f` | EnemyDate.cs:53 | max HP |
| `public float Hp = 100f` | :59 | current HP |
| `public int Exp` | :39 | Idea (EXP) awarded on death |
| `public float enmATK` | :73 | damage of the **current** attack. It is passed to `playercon.fun_damage_Improvement` at :814 and to projectiles |
| `public float enmMAXtough / enmTough = 2f` | :69, :71 | poise (stagger) |
| `protected float enmSpcut = 20f` | | SP cut on guard |
| `public int DropItem = 1000` | :65 | |
| `protected int GameDifficultyFlag = 1` | :170 | copied from `StaticMng.GameDifficulty` in `start_fun()` (:557-563) |

**Serialized prefab values are meaningless.** The "Dorei" prefab, for example, serializes `MaxHp=100, Hp=100, Exp=0, enmATK=0`
(dump `prefabs/sharedassets9.assets.json`, around line 60). Each subclass's private `Start()` assigns the real values in
code, always in this pattern (goblin.cs:91-142):

```csharp
start_fun();                         // GameDifficultyFlag = StaticMng.GameDifficulty
SetStatusGameDifficulty(170f, 2f);   // MaxHp, enmMAXtough with difficulty factor   (goblin.cs:118)
Hp = MaxHp;                          // :120
enmATK = 50f;                        // :121 (initial / contact value)
...
Exp = 100; ExpVal(Exp);              // :130-131, difficulty factor
```

How the values are used afterwards:

- **ATK is set per attack.** Each attack state calls `SetAtkVal(x)` just before the attack becomes active,
  for example goblin.cs:689/697/709/717/727/737/746/756. Projectiles copy `enmATK` when they are spawned
  (goblin.cs:1142 `Suraimu_bullet(..., enmATK, ...)`).
- **EXP is paid out in `OnDestroy`** when `state == DEATH`: `playerstatus.ExpALL += Exp; playerstatus.Exppoint += Exp;`
  (goblin.cs:1179-1187; same pattern in every class, e.g. Undead.cs:1255, SinnerslaveCrossbow.cs:1229).
  Changing `Exp` at any point before death therefore works.
- **The HP bar is not cached.** Every class does `hpbar.fillAmount = Hp / MaxHp;` in `Update()`
  (goblin.cs:258, Undead.cs:300, Inquisition.cs:269, Minotaurosu.cs:332, ...). `hpbar` is the Image
  `Canvas/Hp` under the enemy (goblin.cs:110-112). Rewriting `MaxHp` and `Hp` after `Start` updates the bar on the next
  frame, so no UI fix-up is needed.

### 1.2 Difficulty and NG+

Difficulty multipliers live only in `EnemyDate` and use `GameDifficultyFlag` (0 = Easy, 1 = Normal, 2 = Hard, 3 = Very Hard):

| Helper | Line | Easy | Normal | Hard | Very Hard |
|---|---|---|---|---|---|
| `SetStatusGameDifficulty(maxhp, maxtough)` → MaxHp | :226-247 | x0.6 | x1 | x1 | x1.1 |
| (same) → enmMAXtough | | ceil(x0.5) | x1 | x1 | ceil(x1.5) |
| `SetAtkVal(set)` → enmATK | :188-205 | x0.5 | x1 | x1.2 | x1.5 |
| `SetSPcutVal(set)` → enmSpcut | :207-224 | x0.8 | x1 | x1.2 | x1.5 |
| `ExpVal(exp)` → Exp | :249-265 | x1.1 | x1 | x1.1 | x1.5 |
| stab damage correction | :337-353 | 1 | 1 | 1.2 | 1.5 |

Other places that read `StaticMng.GameDifficulty` (PlayerStatus.cs:1007 and 1319, playercon.cs:4314 and 4335) handle the
status-screen label and potion heal amounts. They do not touch enemies.

**NG+ is not applied to enemies.** `StaticMng.PlaythroughCount` (StaticMng.cs:262) is only loaded and saved
(LoadFile.cs:245, SaveFile.cs:250, game_fragmng.cs:82/412/561). No enemy reads it.

### 1.3 Per-class base values (Normal difficulty, read from each class's `Start`)

"ATK" lists the distinct `SetAtkVal`/`enmATK` constants in the file (low to high). Variant flags are `[SerializeField]`
bools or ints on the prefab and can be read with reflection.

| Prefab name(s) at spawners (count) | Class | Base MaxHp | Poise | Exp | ATK values |
|---|---|---|---|---|---|
| InquisitionBlack (82) | Inquisition | 600 | 3 | 800 | 20,40,50,60,70 |
| Undead (76) | Undead | 300 | 1 | 300 | 40,50,60,70 |
| Dorei (72) | SinnerslaveCrossbow | 320 | 2 | 200 | 30,35,40,50 |
| CrawlingCreatures (65) | CrawlingCreatures | 220 | 2 | 500 | 50,60,70 |
| Pilgrim (43) | Pilgrim | 600 | 2 | 2000 | 40-100 |
| goblin (41) | goblin | 170 | 2 | 100 | 20,30,50 |
| Inquisition_RED (38) | InquisitionRED | 750 | 2 | 3000 | 40-120 |
| Vagrant_spine / _Throw / _Guard | Vagrant | 320 | 1.2 | 500 | 50-80 |
| BlackOoze_Monster | BlackOoze_Monster | 350 | 1.5 | 900 | 40-100 |
| CrawlingSisterKnight | CrawlingSisterKnight | 600 | 3 | 1500 | 40-70 |
| mafia_spine | BlackMafia | 600 | 3 | 2000 | 40-80 |
| mafia_muscle | Mafiamuscle | 1800 | 4 | 3000 | 20-100 |
| angel_Statue | AngelStatue | 10 | 1 | 300 | 70 |
| NormalSnailshell / Snailshell | Snailshell (`RockShell`) | 300 rock / 200 normal | 6 / 2.5 | 600 | 40-100 |
| requiemKnight | RequiemKnight | 1800 | 8 | 5000 | 30-120 |
| Minotaurosu / Minotaurosu_dif | Minotaurosu (`OnryOne`) | 3500 / 2500 | 8 / 6 | 8000 | 50-150 |
| Touzoku / Touzoku_Axe | TouzokuNormal / TouzokuAxe | 300 / 200 | 2 / 2 | 100 / 80 | 30-130 / 30-50 |
| sura_create, sraimu | suraimu | 120 | 2 | 120 | 20,30,50 |
| CocoonmanStart, Cocoonman | Cocoonman | 550 | 3 | 2000 | 50-100 |
| sisiruirui | Sisiruirui | 1400 | 10 | 3000 | 60-110 |
| skelton_ooze | SkeltonOoze | 650 | 1 | 3000 | 40-110 |
| kinoko_spine | Kinoko | 300 | 1.5 | 300 | 40-90 |
| kakasienemy | Kakash | 320 | 2 | 100 | 20-80 |
| Inquisition_white / _dif | InquisitionWhite (`OnryOne`) | 1800 / 900 | 6 / 4 | 4000 | 30-120 |
| arulaune_spine | Arulaune | 600 | 3 | 1800 | 40-90 |
| Sisterknight | Sisterknight | 400 | 2.2 | 1300 | 30-100 |
| Mutude | Mutude | 500 | 3 | 350 | 20-60 |
| HighInquisition_famale | HighInquisition_famale | 700 | 4 | 2500 | 60-120 |
| CrowInquisition / _hide | CrowInquisition (`OnryOne`) | 1800 / 800 | 6 / 4 | 3500 | 30-140 |
| coolmaiden_spine | Coolmaiden | 750 | 2 | 2300 | 40-120 |
| SlaveBigAxe / EV* | SlaveBigAxe (`NikuArmor`) | 1500 / 1000 | 8 | 2000 / 800 | 20-70 |
| (other) | OtherSlavebigAxe (`NikuArmor`) | 1000 / 800 | 8 | 2000 / 800 (no ExpVal) | 20-70 |
| Librarian | Librarian | 600 | 2 | 2500 | 40-100 |
| Tyoukyousi_spine / _Red | Tyoukyoushi / TyoukyoushiRed | 550 / 450 | 2 | 1100 / 800 | 40-100 |
| Prisonofficer(B) | PrisonOfficer | 700 | 4 | 900 | 60-120 |
| Gorotuki | Gorotuki | 800 | 5 | 1500 | 30-130 |
| MummyDog_spine / Mummyman_spine | MummyDog / MummyMan | 500 / 700 | 2 | 1000 / 1500 | 50-100 / 30-130 |
| BigMerman / DifferentBigMerman | BigMerman (`OnryOne`) | 3000 / 2500 | 6 | 5000 | 50-180 |
| Sheephead | Sheepheaddemon (`OnryOne`) | 3000 / 2500 | 6 | 6000 | 50-180 |
| Slaughterer | Slaughterer (`NikuArmor`) | 1500 / 2000 | 8 | 2000 / 3000 | 30-130 |
| CrawlingDead_spine | CrawlingDead | 3500 | 4 | 4000 | 30-130 |
| Bigoni_spine | Bigoni | 3500 | 4 | 2000 | 30-130 |
| (Mimick) | Mimick (`rank` 0/1/2) | 800/1200/1500 | 4/5/6 | 2000/4000/6000 | 50-140 |
| (Tentacle) | Tentacle | `MaxHp = 10` directly | | 100 | 10 |

Spawn counts come from `Spawnenemy.enemy` across `dump/static/scenes`. The prefab-to-class mapping for "Dorei" and
"InquisitionBlack" was confirmed in `prefabs/sharedassets9.assets.json`; the other prefab names map by obvious
naming (inferred).

Exceptions to the standard `Start` pattern:

- OtherSlavebigAxe sets `Exp` without calling `ExpVal`.
- Tentacle sets `MaxHp` directly.
- Snailshell calls `SetStatusGameDifficulty(200,2.5)` again when the shell breaks (Snailshell.cs:1328, 1457). This resets `MaxHp` to the unscaled value.
- Wrapper prefabs such as `sura_create` (an `Enemy_create`) spawn the real enemy later and **unparented**
  (Enemy_create.cs:36), so a "new child of the spawner" check will not see it.

### 1.4 Recommended scaling hook

Do not read stats from the prefab, because the serialized values are defaults. Use a **ratio built from a static
base table** (§1.3), keyed by `EnemyDate` subclass plus its variant flag, which can be read from the prefab by reflection.
The difficulty multipliers are the same for the original and the replacement, so they cancel in the ratio:

```
hpRatio  = base[orig].MaxHp / base[repl].MaxHp
expRatio = base[orig].Exp   / base[repl].Exp
atkRatio = base[orig].AtkRef / base[repl].AtkRef   // AtkRef = e.g. max or mean of the ATK list
```

Hook points:

1. **`Spawnenemy.Update` prefix (existing `SpawnEnemyPatch`)**. Before swapping `___enemy`, remember the original prefab and
   `__instance.transform.childCount`, and pass both through Harmony's `__state`.
2. **`Spawnenemy.Update` postfix**. If `childCount` grew, the new instance is `transform.GetChild(childCount-1)`, because
   `Object.Instantiate(enemy, pos, rot, gameObject.transform)` at Spawnenemy.cs:22 appends it as the last child. Take
   `GetComponentInChildren<EnemyDate>(true)` and register `pending[ed.GetInstanceID()] = ratios`. The instance's `Start`
   has **not** run yet at this point, so do not write stats here; `Start` would overwrite them.
3. **Postfix on every `EnemyDate` subclass's private `Start`**. Use `TargetMethods()` that enumerates
   `typeof(EnemyDate).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(EnemyDate)))` and
   `AccessTools.DeclaredMethod(t, "Start")`. In the postfix, if `pending` has the instance:
   `MaxHp *= hpRatio; Hp = MaxHp; Exp = Mathf.RoundToInt(Exp * expRatio); enmATK *= atkRatio;` (optionally also
   `enmMAXtough` and `enmTough`). This also handles the OtherSlavebigAxe, Tentacle and difficulty quirks.
4. **Postfix on `EnemyDate.SetAtkVal(float)`** (protected, non-virtual, EnemyDate.cs:188). If the instance has an
   `atkRatio`, apply `__instance.enmATK *= atkRatio`. This covers every per-attack ATK set, and projectiles because they copy
   `enmATK` afterwards.
5. Optional: a postfix on `EnemyDate.SetStatusGameDifficulty` for already-scaled instances, which re-applies `hpRatio`
   when Snailshell's shell breaks.

Use a `Dictionary<int, Scale>` and clear it on scene load (or remove entries in an `OnDestroy` postfix on the subclasses).
The HP bar needs no change (§1.1).

---

## 2. Double hit bug

### 2.1 How a player melee hit reaches an enemy

- **Player side** (level10 "Common" scene, object `Player/AttackCol`, read with UnityPy): tag **`ATKweapon`**, layer 10.
  Components: `BoneFollower`, `BoundingBoxFollower` (Spine), and **7 `PolygonCollider2D` (isTrigger, all disabled)**,
  one per bounding-box attachment of the weapon slot. The separate `Player/StabCol` (tag `ATKstab`, BoxCollider2D) is the
  finisher (stab) hitbox.
- `Spine.Unity/BoundingBoxFollower.cs`:
  - `HandleRebuild` (:77-111) creates one collider per `BoundingBoxAttachment`, all disabled.
  - `LateUpdate` (:140-146) calls `MatchAttachment` whenever the slot's attachment changes.
  - `MatchAttachment` (:148-167) **disables the old collider and enables the new one**.
- **Enemy side** (prefabs goblin in sharedassets2, Dorei in sharedassets9, Undead in sharedassets31, read with UnityPy): one child
  `DamageCol`, tag `EnemyCol`, with `BoxCollider2D` (trigger) and **`ChildColliderTrigger`**.
  The `AttackCol` children (tag `EnemyAtk`) carry `playerDamage`, which is the enemy-to-player path.
- `ChildColliderTrigger.OnTriggerEnter2D` (ChildColliderTrigger.cs:15-41): when the other collider's tag is `ATKweapon`, `ATKmagic` or `Trap`, it runs
  `ExecuteEvents.Execute(parent, ..., r => r.OndamageSend(tag))`.
- `EnemyDate.OndamageSend(string tag)` (EnemyDate.cs:759) → `case "ATKweapon"`: sets `damedir`, handles parry blank,
  then calls the virtual **`fun_enedamage()`** (:777, declared :881).
- The subclass `fun_enedamage()` either dodges (Choose / STEP) or calls its private **`getdame_fun()`**.
  `getdame_fun()` calls `EnemyDate.WeaponDamage()` (:317), which does `Hp -= ATK*resist*rand`.
  Example: goblin.cs:1190-1260.

### 2.2 ATKID

`playercon.ATKID` is a `public float` (playercon.cs:310). It is re-rolled with `Random.Range(2f, 100f)` at the start of every combo
step, charged attack and so on (playercon.cs:3171, 3241, 3323, 3394, 3459, 3625-3837). It is also re-rolled by the Spine
animation event named **`"ID"`** (playercon.cs:4500-4504, in `OnEvent` :3924), which multi-hit animations use to open a
new hit window. Because the values are random floats, a repeat across swings is practically impossible.

**Only 11 bosses use ATKID as a gate**: BOSS_Village, BossInsomniaUnder, BossScapegoatentrance, BossTouzoku, Boss_Ranch,
Candore, DemonRequiemKnight, LastIbaranoMajyo, OriginIbaranoMajyo, Praymaiden and SuccubusSpine. Each has
`private float[] damegeID = new float[6]`. `AtkIDcheck()` (Candore.cs:3230-3236) calls `getdame_fun()` only if
`com_player.ATKID` is not in the ring, and `getdame_fun()` pushes the ATKID into the ring (Candore.cs:3283-3288).
`EnemyDate.dameID` (:129) exists but no enemy uses it (only Trapdata.cs).
**No regular enemy checks ATKID at all.** Every `OnTriggerEnter2D` from an `ATKweapon` collider is a full hit
(goblin.cs:1190-1260).

### 2.3 Root cause

A single swing animation keys several different bounding-box attachments on the weapon slot, so the hitbox changes shape
during the swing. Each change makes `BoundingBoxFollower` switch to a **different** `PolygonCollider2D`. Box2D treats the
newly enabled collider as a new contact with the enemy's `DamageCol`, so **`OnTriggerEnter2D` fires again** in the same
swing. A regular enemy has no ATKID gate, so the second Enter is a second full hit.

- **Close range** makes it worse: the enemy overlaps both the early (close) and the later (extended) shape of the swing.
  At distance it only touches one of them.
- **About 20% of swings, not 100%** (inferred): the attachment swap happens in `LateUpdate` at frame rate, while trigger
  contacts are evaluated at the physics step. If the disable-old and enable-new sequence falls so that the enemy is
  never inside two consecutive active shapes during a physics step, or two swaps land between physics steps, only one
  Enter fires.
- It is not two enemy colliders (each enemy has one `DamageCol`), and it is not Enter plus Stay: `ChildColliderTrigger` has
  no Stay handler.
- Bosses are immune because their `damegeID[]` ring buffer is the developer's own fix for exactly this problem.
- The number of bounding-box keys per animation was not verified (inferred). The 7 colliders on AttackCol show that the weapon slot
  has several bbox attachments.

### 2.4 Recommended fix: one generic Harmony gate

Patch **`EnemyDate.OndamageSend(string tag)`** with a prefix. It is public and non-virtual, declared once in EnemyDate.cs:759,
and reached through the `ExecuteInterface` dispatch for every enemy and boss, so patching it covers all of them:

```csharp
[HarmonyPatch(typeof(EnemyDate), nameof(EnemyDate.OndamageSend))]
static class MeleeHitGate
{
    static readonly Dictionary<int, KeyValuePair<float, float>> Last = new Dictionary<int, KeyValuePair<float, float>>(); // id -> (ATKID, time)

    static bool Prefix(EnemyDate __instance, string tag)
    {
        if (tag != "ATKweapon") return true;               // leave Trap / ATKstab / playerDAMAGEcol untouched
        var pc = __instance.com_player;
        if (pc == null) return true;
        int id = __instance.GetInstanceID();
        KeyValuePair<float, float> prev;
        if (Last.TryGetValue(id, out prev) && prev.Key == pc.ATKID && Time.time - prev.Value < 1.0f)
            return false;                                  // same swing already registered on this enemy
        Last[id] = new KeyValuePair<float, float>(pc.ATKID, Time.time);
        return true;
    }
}
```

Notes on the gate:

- It is keyed by (enemy instance, ATKID), the same rule the bosses use. Multi-hit moves still work, because their `"ID"`
  Spine event re-rolls ATKID.
- The time window is a safety net in case some attack path never re-rolls ATKID.
- A dodged swing also consumes that ATKID for that enemy, which is the correct outcome.
- Clear `Last` on scene change.
- Bosses already gate internally, so the extra gate is harmless for them.
- Not covered: a few objects with their own `ATKweapon` handlers (AngelStatue.cs:341, IvyRoadStop.cs:502, Mob* helpers,
  breakables). Patch `ChildColliderTrigger.OnTriggerEnter2D` with the same key, using the parent GameObject, if those matter.

---

## 3. Bad statuses (aphrodisiac and others)

Storage: `PlayerStatus.BadstatusVal = new float[6]` (PlayerStatus.cs:108). It is exposed as `_BadstatusVal`, a getter that
returns the live array (:601-610). Only indices 0-3 are used. The flags live in the `Buff` component on the
`GameController` object (`PlayerStatus.Buff` is private, :240, assigned at :1799 `GetComponent<Buff>()`). The flags are
`_Pleasureparalysis`, `_orgasm`, `_Creampie` and `_Pregnancy` (Buff.cs:67-113).

The UI is `CanvasBadstatusinfo` on the object tagged **`Badinfo`**. `create_Badstatus(n)` shows bar n (CanvasBadstatusinfo.cs:35-43).
`Badstatus.Update` draws `_BadstatusVal[n]/100` and shows the labels (Badstatus.cs:83-106):

- 0 快感マヒ (pleasure paralysis)
- 1 快感絶頂 (pleasure climax)
- 2 着床進行 (implantation)
- 3 異種妊娠 (monster pregnancy)
- 4 異種出産 (monster birth; text only)

### 3.1 The aphrodisiac / lust effect is slot 0, "快感マヒ" (Pleasure Paralysis)

The string 媚薬 appears nowhere in the code. The pink-mist lust effect is this status (inferred from the visuals and from
it being the only gas-applied status).

**What applies it**: only **`MultipleAtkBadstatus`** calls `BadstatusValPlus`.
- **Mutude (六つ手)** uses its `BREATH` attack (`enemystate.ATKBREATH`). The Spine event `"CREATE"` instantiates the
  `CreateATK` cloud prefab and calls `MultipleAtkBadstatus.Set(...)` (Mutude.cs:1208-1214).
- `MultipleAtkBadstatus.OnTriggerStay2D` (MultipleAtkBadstatus.cs:14-30) adds `Pl.BadstatusValPlus(35f)` for every 1 s
  of contact with `playerDAMAGEcol`. The counter resets on Exit.

**`PlayerStatus.BadstatusValPlus(float val)`** (PlayerStatus.cs:1543-1556):

- If `Buff._Pleasureparalysis` is already true, it does nothing.
- Otherwise it does `BadstatusVal[0] += val` and shows bar 0.
- When the meter reaches ≥ 100 it clamps to 100 and sets `Buff._Pleasureparalysis = true`.

**Duration**: `Buff.PleasureParalysis()` runs from `Buff.Update` (Buff.cs:288-327). In every state, `BadstatusVal[0]` drops
by 10 every 2 s (5 per second).
- Below 100 the meter only builds up. It has no effect and decays to 0 (val/5 seconds).
- While paralysed, the status lasts until the meter drains to 0: **20 s from 100**. At that point `_Pleasureparalysis`,
  `_orgasm` and meter 1 are cleared.

**Effects while paralysed**:

- Every 10 s, `PlayerStatus.PleasureParalysisAction()` (:1575) runs:
  - `playercon.PleasureParalysis()` (playercon.cs:1098-1156) plays the "FEEL" stagger. It interrupts attack, magic and item use,
    then applies `Sp -= AllMaxSP/2`, `Hp -= Hp/10` and `Mp += 10`.
  - It then calls `ParalysisOrgasm(35f)`.
- Each attack start also calls `PleasureParalysisActionPercentage()` (:1581). This has a 25% chance to trigger the same action
  after 0.2 s (call sites playercon.cs:2318, 2323, 2630, 2646, 2718, 2767, 2798, 3154).
- When slot 1 (orgasm) reaches 100, `Buff._orgasm = true`, `Buff.ParalysisOrgasm()` and `ParalysisOrgasmAction()` run
  (PlayerStatus.cs:1558-1573):
  - `playercon.ParalysisOrgasm()` (playercon.cs:1158) plays "FEEL2".
  - When that animation ends, `erodown = 1; BadEro.Erostart(playerstatus)` (playercon.cs:4526-4531, PlayerconBadero.cs:41-58) starts
    the forced "ORGASTART" down scene.
  - Paralysis and meter 0 are cleared (Buff.cs:329-343).

**Applying it programmatically**:

```csharp
var ps   = GameObject.FindWithTag("GameController").GetComponent<PlayerStatus>();
var buff = ps.GetComponent<Buff>();
if (GameObject.FindWithTag("Badinfo") == null) return;   // BadstatusValPlus NREs without the HUD canvas

// a) Build-up only (amount < 100): ps.BadstatusValPlus(amount);   // decays at 5/s, no gameplay effect
// b) Full paralysis for 20 s:     ps.BadstatusValPlus(100f);
// c) Paralysis for N seconds (N <= 20): ps.BadstatusValPlus(100f); ps._BadstatusVal[0] = Mathf.Clamp(N * 5f, 1f, 100f);
// d) N > 20: (b), then a coroutine that keeps ps._BadstatusVal[0] = 100f until N-20 s have passed (BadstatusValPlus is
//    ignored while already paralysed, so write the array directly).
```

**Cure**: `PlayerStatus.BADstatusReset()` (:1621-1631) clears every flag and value. The game calls it from the save point
(Savepoint_on.cs:163), from PlayerStatus.cs:1318 and from playercon.cs:815.

### 3.2 Other bad statuses (one line each)

- **Slot 1 Orgasm 快感絶頂**: `ps.ParalysisOrgasm(val)` (PlayerStatus.cs:1558). Reaching 100 forces the FEEL2 orgasm-down
  scene. Normally it only grows by 35 per paralysis proc. It does nothing while `_orgasm` is already true.
- **Slot 2 Creampie / implantation 着床進行**: call `ps.CreampieVal_UI()` (:1600; sets value 10 and `_Creampie = true`) and
  `playercon._BirthNumber = 1` (slime/tentacle birth) or `2` (other monster), the way the ERO scenes do (EroAnimation_suraimu.cs:263-269).
  - It grows by 10 every 2 s outside H-scenes (Buff.cs:350-384), so it reaches 100 after about 18 s.
  - At 100 it runs `PleasureParalysis()` and the pregnant costume, then starts slot 3.
  - Cure: `ps.CreampieReset()` (:1616) or the player's scoop-out action `playercon.CreampieKakidasu()` (playercon.cs:1255).
  - The vanilla game only applies it when slots 2 and 3 are both 0.
- **Slot 3 Pregnancy 異種妊娠**: `buff.Pregnancystart()` (public, Buff.cs:410-416; shows UI, `_Pregnancy = true`, value +10).
  - It grows by 5 every 2 s (Buff.cs:386-408), so it reaches 100 after about 36 s.
  - At 100, `ps.BirthAction()` → `playercon.Birth()` "FEEL3" → `Pregnancyero.Birthstart`. Which birth plays depends on `_BirthNumber` (playercon.cs:4533-4545).
  - Cure: `buff.StopPregnancy()`.
- **Birth 異種出産** (no own slot): `ps.BirthAction()` (:1595) triggers the birth scene directly. Set `_BirthNumber` first.
- **Slots 4 and 5**: unused. Buff fields `buff_hatujyou` (発情, heat) and `buff_syokusyuhuku` (tentacle suit) are declared
  (Buff.cs:27-29) but never used.

There is a dev cheat left in the release build: **Esc+5** or **Esc+6** in `Buff.Update` calls `CreampieVal_UI()` with
`_BirthNumber` set to 1 or 2 (Buff.cs:151-167).
