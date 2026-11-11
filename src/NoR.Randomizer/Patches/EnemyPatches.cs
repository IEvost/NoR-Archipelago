using System.Collections.Generic;
using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// Replaces the prefab of every regular spawn point (Spawnenemy) with an enemy from the global pool
    /// before the spawner first fires, and registers spawned replacements for area-tier scaling.
    /// Bosses (BossSpawn / boss scenes) are not touched.
    /// </summary>
    [HarmonyPatch(typeof(Spawnenemy), "Update")]
    internal static class SpawnEnemyPatch
    {
        private static readonly HashSet<int> Processed = new HashSet<int>();
        private static readonly Dictionary<int, string> Replaced = new Dictionary<int, string>(); // spawner id -> replacement prefab

        internal struct State
        {
            public int ChildCount;
            public string Replacement;
        }

        private static void Prefix(Spawnenemy __instance, ref GameObject ___enemy, int ___SpawnNumber, out State __state)
        {
            __state = new State { ChildCount = __instance.transform.childCount };
            var st = Plugin.State;
            var pool = EnemyPool.Instance;
            if (st == null || !st.RandomizeEnemies || pool == null || !pool.Ready) return;

            int id = __instance.GetInstanceID();
            string replaced;
            if (Replaced.TryGetValue(id, out replaced)) { __state.Replacement = replaced; return; }
            if (Processed.Contains(id)) return;
            Processed.Add(id);
            if (Processed.Count > 4096) { Processed.Clear(); Replaced.Clear(); }

            string scene = __instance.gameObject.scene.name;
            if (EnemyPool.IsExcludedScene(scene)) return;
            // Event battles count kills through DeathCount components that only the original prefabs carry
            // (EvBattleDeathCount on the spawner root: EVuChurch merchant rescue, Forest house) - keep them vanilla.
            if (__instance.transform.root.GetComponentInChildren<EvBattleDeathCount>(true) != null) return;
            // Treasure carriers set one-time flags on death (Bigoni: hexagonal key flag the village witch rescue needs).
            if (___enemy != null && EnemyPool.KeepVanillaAtSpawn.Contains(___enemy.name)) return;
            var choice = pool.Choose(st.Seed, scene, Hierarchy.GetPath(__instance.transform), ___SpawnNumber);
            if (choice == null) return;
            if (Plugin.VerboseEnemyLog)
                Plugin.Log.LogInfo("Spawn " + scene + "/" + Hierarchy.GetPath(__instance.transform) + ": " + (___enemy != null ? ___enemy.name : "null") + " -> " + choice.name);
            bool changed = ___enemy == null || ___enemy.name != choice.name;
            ___enemy = choice;
            if (changed)
            {
                Replaced[id] = choice.name;
                __state.Replacement = choice.name;
            }
        }

        private static void Postfix(Spawnenemy __instance, State __state)
        {
            if (__state.Replacement == null) return;
            var t = __instance.transform;
            if (t.childCount <= __state.ChildCount) return; // nothing spawned this frame
            EnemyScaling.Register(t.GetChild(t.childCount - 1).gameObject, __instance.gameObject.scene.name, __state.Replacement);
        }
    }
}
