# Night of Revenge Setup Guide

## Requirements
- Night of Revenge **1.0.7** (legally owned copy)
- [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) (x64, Mono) installed into the game folder
- The NoR Archipelago mod release (`NoR-Archipelago-<version>.zip`)
- For generating: [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) 0.6.6 or newer

## Installing the mod
1. Install BepInEx 5 into the game folder (the folder containing `NightofRevenge.exe`) and start the game once.
2. Extract the mod release into the game folder, so `BepInEx/plugins/NoR.Archipelago`, `NoR.Randomizer`,
   `NoR.Localization` and `NoR.Widescreen` exist.
3. Start the game. At the title screen a short line shows the enemy pool loading; wait until it disappears.

## Generating a multiworld (done by whoever hosts)
Night of Revenge is not part of the official Archipelago release, so the host generates locally:
1. Put `nightofrevenge.apworld` into `custom_worlds` of the Archipelago installation (or double-click it).
2. Every player puts their YAML into `Players`. Get a Night of Revenge template from the Archipelago Launcher
   ("Generate Template Options") after installing the apworld. Minimal example:
   ```yaml
   name: Aradia
   game: Night of Revenge
   Night of Revenge:
     shop_slots: 10        # 0-20 one-time Witch's Market slots
     boss_drops: true
     npc_rewards: true
     trades: true
     shuffle_keys: false   # experimental key logic
     enemy_randomizer: true
     trap_percentage: 10   # 0-50
     death_link: false
     death_link_mode: defeat_state   # or instant_respawn
   ```
3. Run `ArchipelagoGenerate`. The result is `output/AP_<number>.zip`.
4. Host it: upload the zip at <https://archipelago.gg/uploads>, click **Create New Room**, share the room link.
   The room page shows the address, e.g. `archipelago.gg:38281` (rooms that went idle restart with a new port when
   the page is opened again - always check the room page).

## Playing
1. Start the game. The **Archipelago** window is at the top right of the title screen (F1 shows/hides it).
2. Enter the server (`archipelago.gg:<port>`), your slot name and the password (if any), click **Connect**.
   archipelago.gg works directly: the mod starts a small helper (`NoR.ApBridge.exe`) for the secure connection
   the game's old engine cannot make itself.
3. Start a **New Game** for a new run, or **Load** the save of this slot. Saves remember their server and slot and
   reconnect automatically; a save of another seed refuses to connect.

| Key | Function |
|---|---|
| F1 | Archipelago window (title screen; in game it opens the window to reconnect) |
| B / gamepad item-switch | Witch's Market at a save point |
| G / hold Jump 2 s | Skip and respawn after a death or DeathLink |
| ` or F12 | Backup console (give items/keys if you are stuck) |

## Troubleshooting
- Connection fails: check the room page for the current port. If the room moved to a new port while you play,
  press **F1** in game, enter the new address and connect - the save accepts any address of the same seed and
  slot and remembers it. Details are in `BepInEx/LogOutput.log`.
- Chat and commands (`!hint <item>`, `!release`, ...) can be typed into the Archipelago window (F1).
  Hints cost points earned by checking locations (default: 10% of your locations per hint, about 24 for a full
  Night of Revenge world). Hosts can lower `hint_cost` in `host.yaml` (`server_options`) before generating.
- Chat, hints and server notices appear on screen (disable with `ShowServerMessages = false` in
  `BepInEx/config/nor.archipelago.client.cfg`).
- Items arrive only while you are in control (not during menus, events or defeat scenes) - this is intended.
- Stuck because of a logic error: open the console (`) and use `give key lab|pilgrimage|rooftop` or `give <code>`.
