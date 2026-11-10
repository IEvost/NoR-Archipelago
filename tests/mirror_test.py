"""Mirror test: the in-game client (NoR.Randomizer Placement.GenerateRemote) and the apworld must include exactly the
same locations for every option set, otherwise locations exist that can never be checked (or checks get lost).

Usage (repo root): .tools/ap-venv/Scripts/python.exe tests/mirror_test.py
"""
import importlib.util
import itertools
import pathlib
import subprocess
import sys
import types

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = next((ROOT / "tests" / "MirrorTest" / "bin").rglob("MirrorTest.exe"))

spec = importlib.util.spec_from_file_location("nor_data", ROOT / "apworld" / "nightofrevenge" / "data.py")
data = importlib.util.module_from_spec(spec)
spec.loader.exec_module(data)


def apworld_keys(shop, boss, npc, trade, keys):
    """Same rules as NightOfRevengeWorld._included."""
    out = set()
    for info in data.LOCATIONS.values():
        if info["key_location"]:
            ok = keys
        elif info["category"] == "shop":
            ok = info["slot"] < shop
        elif info["category"] == "boss_drop":
            ok = boss
        elif info["category"] == "npc":
            ok = npc
        elif info["category"] == "trade":
            ok = trade
        else:
            ok = True
        if ok:
            out.add(info["key"])
    return out


failures = 0
cases = [(shop, *flags) for shop in (0, 10, 20) for flags in itertools.product((0, 1), repeat=4)]
for shop, boss, npc, trade, keys in cases:
    client = set(subprocess.run([str(EXE), str(shop), str(boss), str(npc), str(trade), str(keys)],
                                capture_output=True, text=True, check=True).stdout.split())
    world = apworld_keys(shop, boss, npc, trade, keys)
    if client != world:
        failures += 1
        print(f"MISMATCH shop={shop} boss={boss} npc={npc} trade={trade} keys={keys}")
        print("  only in client:", sorted(client - world)[:10])
        print("  only in apworld:", sorted(world - client)[:10])
print(f"{len(cases) - failures}/{len(cases)} option sets match")
sys.exit(1 if failures else 0)
