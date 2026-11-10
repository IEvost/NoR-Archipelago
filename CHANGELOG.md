# Changelog

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
- The client library's WebSocket lacks compression; the server currently only warns about it.
