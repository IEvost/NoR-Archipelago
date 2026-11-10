# Night of Revenge Setup Guide

## Requirements
- Night of Revenge **1.0.7** (legally owned copy)
- BepInEx 5.4.x (x64, Mono) installed into the game folder
- The NoR Archipelago mod release (BepInEx plugins + this apworld)

## Installation
1. Extract the mod release into the game folder so that `BepInEx/plugins/NoR.*` exist.
2. Put `nightofrevenge.apworld` into the `custom_worlds` folder of your Archipelago installation.
3. Start the game once to create the config files.

## Connecting
Enter the server address, slot name and password in the in-game connection screen on the title menu
(or in `BepInEx/config/nor.archipelago.client.cfg`). archipelago.gg rooms work out of the box: the mod
starts a small helper (`NoR.ApBridge.exe`) that provides the secure connection the old game engine lacks.
