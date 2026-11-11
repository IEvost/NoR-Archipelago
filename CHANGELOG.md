# Changelog

## Unreleased
- Typewriter text (opening narration, event dialogue, game-over texts) now rolls out in English instead of Japanese
  that switched to English only when the line was complete.
- The potion-switch button no longer works while grabbed (QTE) or during the knockdown lockout.
- Grab QTE escape now runs the game's own stand-up code (voice, gravity, damage timer, HUD face reset) instead of a
  partial manual reset; a fallback forces the release if it does not run. The log names the grabbing enemy.
- **Grab drain**: during a grab QTE the enemy drains 3% max HP per second (`GrabDrainPerSecond`); every drain and
  every wrong QTE input heals the grabbing enemy by the HP taken + 25 (`GrabEnemyHealBonus`), up to its max HP.
  The drain is not lethal: it stops at 10% HP (one wrong input), so only a failed QTE input can kill.
- Camera: after changing the zoom level the camera no longer pumps in and out while standing still (the game's
  speed-based zoom kept the vanilla baseline and fought the stuck-zoom watchdog).
- Censor quads (Ist/MosaicField) are drawn after all characters. The shader copies the screen once per frame through
  a named grab pass; a quad in a lower sorting layer on screen made every other quad paste background over the scene
  (dark squares, also with uncensor patches). Config `FixMosaicOrder`.
- Widescreen: level sky backdrops (attached to the camera by SceneStartEffect, sized for 16:9) are scaled uniformly
  to cover wider screens - no more blank strips at the left/right edges.
- UI: translated one-line labels (teleport menu area names and similar) stay on one line and shrink to fit instead of
  wrapping over the next entry; labels in menus that were hidden at load are fitted when the menu opens.
- Altar teleport menu: area name labels are 40% wider and stay on one line.
- Camera: fixed the camera jittering every frame (ghosting) in rooms narrower than the view - ultrawide screens or a
  zoomed-out camera. The collider boundaries pushed it +-0.1 between both walls; it is now centred between them. For
  numeric boundaries the zoom baseline is capped to the bounded size and the speed-based zoom pauses at the bounds.
- **Downed rush**: enemies walking to a downed Aradia move faster (`DownedRushSpeed`, default 1.8, 1 = vanilla).

## 0.2.0 - feedback from the first real multiworld
- **English dialogue**: event/NPC dialogue stored in data assets (MainEV, SubEventDialogue, NpcTalk, ...) is now
  translated (about 2,600 lines that still appeared in Japanese, letter by letter).
- Received spells show English names ("Thunderbolt acquired." instead of Japanese).
- **Enemy area-tier scaling**: randomized enemies are scaled towards the average HP / damage / Idea of the area's
  original enemies (`ScaleToAreaTier`, `ScalingStrength` 0.8).
- **Double-hit fix** (vanilla bug): one swing can no longer hit an enemy twice.
- **Dialogue fast-forward**: hold Left Ctrl (or gamepad Step); stops at choices.
- **Grab QTE**: 4-6 prompted inputs to break free (letters A-Z, or Up/Down/Left/Right/Attack via `GrabQteKeys`);
  a wrong input costs 10% max HP and restarts. Knockdowns keep Aradia down for 3 s so enemies can grab.
  DeathLink grabs cannot be escaped.
- **Aphrodisiac Trap** (Pleasure Paralysis) behind the new apworld option `lewd_traps` (default on).
- **Camera**: PageUp/PageDown/Home zoom in/out/reset (saved), automatic fix for the camera staying zoomed in after
  rescue events.
- Enemy randomizer: event battles (merchant rescue in EVuChurch, Forest house) and event-only enemy variants are kept
  vanilla - fixes the merchant not appearing / not being able to leave. "Attack to rescue" monsters are randomized.
  Treasure carriers (Bigoni: hexagonal key flag for the village witch, Slaughterer) keep their spawn points.
- UI: translated labels in small boxes shrink to fit (altar/teleport menu and others); the red "X" / "not enough
  stats" markers are positioned correctly on ultrawide screens.
- Fix: Suzune's rewards were checked dozens of times (event calls its reward every frame) - locations now count once.
- Fix: startup connection diagnostic.
- apworld data version 3 (new trap item). Seeds from 0.1.x keep working with this mod.

## 0.1.2
- **WebSocket compression**: `NoR.ApBridge` is now a WebSocket proxy. The game's connection is terminated in the
  bridge and a modern client with permessage-deflate (plus TLS) talks to the server, so the "client does not
  support compressed websocket connections" warning is gone and a future server requirement cannot break
  connections. Verified on archipelago.gg (`permessage-deflate` negotiated). The raw TCP tunnel remains as
  `--mode tcp` fallback.
- All connections (also to local servers) go through the bridge unless `Bridge = Never` is configured.
- Build: the game folder is configured per machine (`Directory.Build.user.props`), no default path.
- Repository is public.

## 0.1.1
- F12 / ` panel now has a **Server log** tab: the full session history from the server in the server's colors
  (items sent between all players, joins/leaves, chat, hints, goals, releases, command results) plus client notices;
  filter "only about me", follow mode, and an input line for chat and server commands.
- The history is also written to `BepInEx/NoR.Archipelago/server_log_<seed>.txt`.
- No apworld changes (seeds generated with 0.1.0 stay compatible).

## 0.1.0 - first playable version

### Archipelago
- apworld `Night of Revenge` (data version 2): 245 locations + 3 key locations, goal "Defeat the Witch in the Thorns".
- Options: `shop_slots`, `boss_drops`, `npc_rewards`, `trades`, `shuffle_keys` (experimental, conservative key-door
  logic), `enemy_randomizer`, `trap_percentage`, `death_link`, `death_link_mode`.
- Client: connection window (title screen, F1 in game to reconnect), automatic reconnect and resync, save-bound
  sessions (no duplicated or lost items), scouted item names, chat/commands and server messages, goal reporting,
  data-version check against the apworld.
- DeathLink: outgoing on death; incoming in `defeat_state` mode knocks Aradia down - the DeathLink always ends in
  death, the player chooses to watch or skip; `instant_respawn` mode.
- Traps: Ambush, Idea Drain, Knockdown.
- `NoR.ApBridge.exe`: TLS helper so the game (Unity 5.6, no TLS 1.2) can join archipelago.gg rooms.

### Randomizer (offline and shared)
- Item shuffle of floor items, chests, magic pickups, shop slots, boss drops, NPC rewards and trades; spoiler log.
- Global enemy randomizer (56 enemy types).
- Witch's Market shop at save points.
- Death skip after a real death (HP 0) or DeathLink.
- Backup console (` / F12): give items and keys, karma, Idea.

### English translation
- 3,792 of 3,803 strings (items, UI, code text, scene dialogue); LLM draft, glossary-based, under review.

### Widescreen
- Native resolution including 21:9 / 32:9, Expand or Pillarbox mode.

### Known limitations
- Key-door logic is conservative and unverified in a full playthrough (use the console if stuck).
- Karma, Sacred Walls and story events are not shuffled yet.
- The client library's WebSocket lacks compression; the server currently only warns about it (fixed in 0.1.2).
