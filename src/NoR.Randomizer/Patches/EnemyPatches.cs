using System.Collections.Generic;
using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// Replaces the prefab of every regular spawn point (Spawnenemy) with an enemy from the global pool
    /// before the spawner first fires. Bosses (BossSpawn / boss scenes) are not touched.
    /// </summary>
    [HarmonyPatch(typeof(Spawnenemy), "Update")]
    internal static class SpawnEnemyPatch
    {
        private static readonly HashSet<int> Processed = new HashSet<int>();

        private static void Prefix(Spawnenemy __instance, ref GameObject ___enemy, int ___SpawnNumber)
        {
            var st = Plugin.State;
            var pool = EnemyPool.Instance;
            if (st == null || !st.RandomizeEnemies || pool == null || !pool.Ready) return;

            int id = __instance.GetInstanceID();
            if (Processed.Contains(id)) return;
            Processed.Add(id);
            if (Processed.Count > 4096) Processed.Clear(); // scenes reload often; IDs of destroyed objects are never reused soon

            string scene = __instance.gameObject.scene.name;
            var choice = pool.Choose(st.Seed, scene, Hierarchy.GetPath(__instance.transform), ___SpawnNumber);
            if (choice == null) return;
            if (Plugin.VerboseEnemyLog)
                Plugin.Log.LogInfo("Spawn " + scene + "/" + Hierarchy.GetPath(__instance.transform) + ": " + (___enemy != null ? ___enemy.name : "null") + " -> " + choice.name);
            ___enemy = choice;
        }
    }
}
