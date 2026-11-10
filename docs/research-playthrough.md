# Research playthrough guide (Milestone 0.5)

The goal of this playthrough is to record **which action sets which game flag, where**. The
`NoR.DataDumper` plugin does the recording; you only play normally and press a key at important moments.

## Setup

1. Build the solution (`dotnet build NoR.Archipelago.slnx`). The build copies the plugin into
   `<game>\BepInEx\plugins\NoR.DataDumper\` automatically.
2. Start the game. An overlay line appears at the bottom-left of the screen.
3. Output goes to `<game>\BepInEx\NoR.Dump\`:
   - `gde_data.json` – item database as loaded at runtime
   - `scenes\<scene>.json` – components of every scene you enter (runtime view, complements the offline scan)
   - `flaglog.jsonl` – every flag/karma/inventory change, with scene names and player position

## Keys

| Key | Action |
|---|---|
| F8 | Show/hide the overlay |
| F9 | Write a numbered **MARKER** into the flag log |
| F10 | Re-dump all loaded scenes |
| F11 | Re-dump the item database |

## How to play for the log

- Start a **new game** (a fresh baseline is logged automatically).
- Press **F9 before and after** anything that might gate progress, then write the marker number and a
  short note into a text file (e.g. `notes.txt`): "M12 – opened the iron door with the lever",
  "M13 – boss Requiem Knight defeated", "M14 – talked to Suzune", "M15 – could not pass this wall, needs X".
- Important moments: doors/levers/elevators, bosses, NPC conversations and quests, karma doors,
  sacred walls, shop purchases, trades, slave rescues, anything that blocks you.
- Normal pickups need no marker; they are recognized from the flags automatically.
- Play to the **Witch in the Thorns** and defeat her; if possible also look at both ending choices
  (keep a save before the final battle).

## What to send back

The whole `BepInEx\NoR.Dump\` folder plus your `notes.txt`. The flag log contains no personal data,
only game state.
