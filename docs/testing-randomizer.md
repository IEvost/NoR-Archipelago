# Testing the offline randomizer prototype (v0.1)

**Status: experimental, no logic yet.** It shuffles items and enemies; it does not yet guarantee that
everything is reachable (logic comes after the research playthrough). Use a separate save slot.

## What it does
- **Items**: the items of all floor pickups, chests and magic pickups (200 locations) are shuffled among
  those locations. Lockpick wires are never placed inside chests.
- **Enemies**: every regular spawn point gets a random enemy from a global pool of all regular enemies
  in the game (bosses are untouched). The same spawn point always gets the same enemy for a given seed.
- Settings apply to **new games only**. Old saves stay vanilla; a randomized save remembers its seed.

## How to test
1. Start the game. At the title screen a line shows "Loading enemy pool x/N" — wait until it disappears
   (New Game / Load Game are blocked meanwhile). Expect some seconds to a minute.
2. Start a **New Game**. Top-left shows "NoR Randomizer active - seed ..." for a few seconds.
3. Spoiler log: `BepInEx\NoR.Randomizer\spoiler_<seed>.txt` (item names still Japanese).

## Config (`BepInEx\config\nor.archipelago.randomizer.cfg`, created after the first start)
| Setting | Default | Meaning |
|---|---|---|
| NewGame.Enabled | true | Randomize new games |
| NewGame.Seed | (empty) | Fixed seed for the next new game; empty = random |
| NewGame.ShuffleFloorItems / ShuffleChests / ShuffleMagic | true | Item categories |
| Enemies.RandomizeEnemies | true | Enemy randomizer (also controls the pool loading at the title) |
| Enemies.ExcludePrefabs | (empty) | Enemy prefab names to keep out of the pool (comma-separated) |
| Debug.LogEnemySwaps | false | Log every enemy replacement |

## Witch's Market (shop)
- Stand at any save point and press **B** (keyboard, configurable) or the **item-switch** button (gamepad).
  A hint line appears at the bottom while you stand at a save point.
- "Rare goods": one-time slots (default 10) — these are randomizer locations, their contents come from the
  shuffle (spoiler log lists them as `shop:N`). "Supplies": consumables from the original shop list, unlimited.
- Paid with Idea. Close with Cancel (jump button) or Escape.
- Only in games started after this update (older randomized saves get the supplies only).
- Config section `[Shop]`: Enabled, Slots, PriceMultiplier, OpenKey.

## Widescreen (`BepInEx\config\nor.archipelago.widescreen.cfg`)
- The game's launcher hides 21:9 resolutions; the plugin switches to your monitor's native resolution after startup.
- `AspectMode = Expand` (default): full width, HUD scaled by height, full-screen overlays stretched.
  If you see glitches (empty areas beyond level edges, misplaced UI), try `Pillarbox` (centered 16:9 with black bars).
- `Resolution = native | 3440x1440 | game`, `Fullscreen = true/false`, `Debug.LogCanvases` for troubleshooting.

## Archipelago (first multiworld test)
A local server runs on this PC with a test multiworld: slot **Aradia** (Night of Revenge) and slot
**Tester** (ChecksFinder, already finished - it sent 22 items to Aradia).
1. Title screen: the **Archipelago** window (top right, F1 hides it). Server `127.0.0.1:38281`, slot `Aradia`,
   no password. Click **Connect**.
2. Start a **New Game**. Within a few seconds after you have control, the waiting items arrive one by one
   ("Received ... from Tester").
3. Pick up items / open chests / buy rare goods: messages show what was found. Items for other players show
   "Sent X to Y" (in this seed all items are your own, so they come back to you as "Received").
4. Save, quit to title, load the save: it reconnects automatically and must not give items twice.
5. Optional: the shop's rare goods show the scouted item names.

### Current test seed: keys shuffled, 20% traps, DeathLink on
Start a **New Game** (new seed - older saves will not connect).
- Pilgrim's Path Key is in the starting forest (Bandits' Hidden Forest, Decayed Crest Ring spot);
  the Blackened Key is behind the pilgrimage doors; the Rooftop Chamber Key is Vendetta's trade 1.
- Boss drops and NPC rewards are now real locations (they were silently vanilla before - kind mapping bug fixed):
  killing the bandit leader should show a check instead of dropping his Idea.

### Backup console (` or F12)
`help`, `give key lab|pilgrimage|rooftop`, `give UseItem:4 5`, `find lockpick`, `karma 100`, `idea 5000`, `state`.
Player control is locked while the console is open.

### Traps
Ambush Trap (an enemy appears next to you), Idea Drain Trap (lose 20% of carried Idea), Knockdown Trap.

### DeathLink (mode defeat_state)
Old saves of slot Aradia belong to the previous seed and will refuse to connect - start a **New Game**.
- Incoming: run `.tools\aptest\send-deathlink.cmd` while playing. Aradia should be knocked down and stay down
  ("DeathLink from Tester" prompt). An enemy grabbing her ends it; otherwise press **G** or hold **Jump** 2 s to give up
  (normal game over, respawn at the save point).
- Outgoing: die normally - the server log shows the DeathLink from Aradia (and "DeathLink sent" appears in game).
  A death caused by an incoming DeathLink is not sent back.

## Please report
- Enemies that break (fall through floors, never attack, stuck, error spam, grab scenes failing): the
  prefab name is in `BepInEx\LogOutput.log` when `LogEnemySwaps = true`.
- Anything odd at the title screen while the pool loads (sound, flashes, freezes, errors).
- Items that were not what the spoiler log says, or pickups that reappear.
- Copy of `BepInEx\LogOutput.log` after a session.
