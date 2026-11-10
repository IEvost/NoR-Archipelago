using DarkTonic.MasterAudio;
using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// Floor items and chest contents. The location keeps its vanilla flag
    /// (_TreasureItemArrayStage[Stage, Getflagnum]) as "checked" state; only the granted item changes.
    /// </summary>
    [HarmonyPatch(typeof(Pickup_item), "Update")]
    internal static class PickupItemPatch
    {
        private static bool Prefix(Pickup_item __instance, bool ___stayflag, playercon ___playcom,
            CanvasMapinfo ___mapinfo, game_fragmng ___Flagmng, int ___Stage, int ___Getflagnum)
        {
            if (Plugin.State == null || !___stayflag || ___playcom == null || !___playcom._key_submit) return true;

            var loc = Plugin.Table.FindPickup(__instance.gameObject.scene.name, ___Stage, ___Getflagnum);
            string item;
            if (loc == null || !Plugin.State.Placement.TryGetValue(loc.Key, out item)) return true; // not randomized

            MasterAudio.StopAllOfSound("snd_getitem1");
            MasterAudio.PlaySound("snd_getitem1");
            Checks.Collect(loc.Key, item);
            if (___mapinfo != null) ___mapinfo.ResetCanvas();
            ___Flagmng._TreasureItemArrayStage[___Stage, ___Getflagnum] = true;
            Plugin.Log.LogInfo("Checked " + loc.Key + " -> " + item);
            Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    /// <summary>
    /// Magic pickups. Vanilla uses the magic's own ownership flag as "picked up" state, which no longer works
    /// once the magic is elsewhere, so the checked state moves to the spare treasure flag row.
    /// </summary>
    [HarmonyPatch(typeof(Pickup_magic), "Start")]
    internal static class PickupMagicStartPatch
    {
        private static bool Prefix(Pickup_magic __instance, ref game_fragmng ___Flagmng, int ___Getflagnum,
            bool ___GetMagic_GUN, bool ___GetMagic_Fire, bool ___GetMagic_Lightning, bool ___GetMagic_Sacred, bool ___GetMagic_darkness)
        {
            if (Plugin.State == null) return true;
            var loc = MagicLocation(__instance, ___Getflagnum, ___GetMagic_GUN, ___GetMagic_Fire, ___GetMagic_Lightning, ___GetMagic_Sacred, ___GetMagic_darkness);
            if (loc == null || !Plugin.State.Placement.ContainsKey(loc.Key)) return true;

            ___Flagmng = GameRefs.Flags;
            if (___Flagmng._TreasureItemArrayStage[LocationTable.MagicCheckRow, loc.MagicSlot])
                __instance.gameObject.SetActive(false);
            return false;
        }

        internal static LocationInfo MagicLocation(Pickup_magic p, int num, bool gun, bool fire, bool lightning, bool sacred, bool dark)
        {
            string school = gun ? "GUN" : fire ? "Fire" : lightning ? "Lightning" : sacred ? "Sacred" : dark ? "darkness" : null;
            return school == null ? null : Plugin.Table.FindMagic(p.gameObject.scene.name, school, num);
        }
    }

    [HarmonyPatch(typeof(Pickup_magic), "Update")]
    internal static class PickupMagicUpdatePatch
    {
        private static bool Prefix(Pickup_magic __instance, bool ___stayflag, playercon ___playcom, CanvasMapinfo ___mapinfo,
            int ___Getflagnum, bool ___GetMagic_GUN, bool ___GetMagic_Fire, bool ___GetMagic_Lightning, bool ___GetMagic_Sacred, bool ___GetMagic_darkness)
        {
            if (Plugin.State == null || !___stayflag || ___playcom == null || !___playcom._key_submit) return true;

            var loc = PickupMagicStartPatch.MagicLocation(__instance, ___Getflagnum, ___GetMagic_GUN, ___GetMagic_Fire, ___GetMagic_Lightning, ___GetMagic_Sacred, ___GetMagic_darkness);
            string item;
            if (loc == null || !Plugin.State.Placement.TryGetValue(loc.Key, out item)) return true;

            MasterAudio.StopAllOfSound("snd_getitem1");
            MasterAudio.PlaySound("snd_getitem1");
            Checks.Collect(loc.Key, item);
            if (___mapinfo != null) ___mapinfo.ResetCanvas();
            GameRefs.Flags._TreasureItemArrayStage[LocationTable.MagicCheckRow, loc.MagicSlot] = true;
            Plugin.Log.LogInfo("Checked " + loc.Key + " -> " + item);
            Object.Destroy(__instance.gameObject);
            return false;
        }
    }
}
