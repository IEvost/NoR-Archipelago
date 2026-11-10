using System;
using System.Reflection;
using DarkTonic.MasterAudio;
using HarmonyLib;
using UnityEngine;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// Boss first-kill drops, NPC/event rewards (treasureNumSet / treasureNumSet2) and trades.
    /// Patched manually because the same prefix applies to many classes.
    /// </summary>
    internal static class RewardPatches
    {
        public static void Apply(Harmony harmony)
        {
            var asm = typeof(game_fragmng).Assembly;
            var prefix = new HarmonyMethod(typeof(RewardPatches).GetMethod("RewardPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            foreach (var t in CodeLocations.HookTargets())
            {
                var type = asm.GetType(t.Key, false);
                var m = type != null ? AccessTools.Method(type, t.Value, new[] { typeof(int), typeof(int) }) : null;
                if (m == null) { Plugin.Log.LogWarning("Reward hook target not found: " + t.Key + "." + t.Value); continue; }
                harmony.Patch(m, prefix: prefix);
            }
            var trade = AccessTools.Method(typeof(TradeSelectItem), "getitemSet");
            harmony.Patch(trade, prefix: new HarmonyMethod(typeof(RewardPatches).GetMethod("TradePrefix", BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static bool RewardPrefix(MonoBehaviour __instance, MethodBase __originalMethod, int num, int kind)
        {
            try
            {
                string cls = __instance.GetType().Name;
                int actor = -1;
                var f = AccessTools.Field(__instance.GetType(), "talkactor");
                if (f != null && f.FieldType == typeof(int)) actor = (int)f.GetValue(__instance);
                string key = "reward:" + CodeLocations.Key(cls, __originalMethod.Name, actor, kind, num);

                if (Plugin.Table.Get(key) == null)
                {
                    // Not in the research table: keep vanilla, but record the call so the table can be corrected.
                    Plugin.Log.LogInfo("Unlisted reward call (vanilla kept): " + key);
                    return true;
                }
                string item;
                if (Plugin.State == null || !Plugin.State.Placement.TryGetValue(key, out item)) return true;

                MasterAudio.StopAllOfSound("snd_getitem1");
                MasterAudio.PlaySound("snd_getitem1");
                Checks.Collect(key, item);
                Plugin.Log.LogInfo("Checked " + key + " -> " + item);
                return false; // skip spawning the vanilla drop
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Reward hook failed, vanilla kept: " + e);
                return true;
            }
        }

        private static bool TradePrefix(int ___tradeflagnum)
        {
            string key = "trade:" + ___tradeflagnum;
            string item;
            if (Plugin.State == null || !Plugin.State.Placement.TryGetValue(key, out item)) return true;
            Checks.Collect(key, item);
            Plugin.Log.LogInfo("Checked " + key + " -> " + item);
            return false; // BuyOK still consumes the cost and sets _Tradeflag
        }
    }
}
