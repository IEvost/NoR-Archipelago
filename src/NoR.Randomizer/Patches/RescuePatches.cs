using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// "Attack to rescue" events: hitting the abused slave spawns the monster (SlaveSideLook.createinvoke ->
    /// Instantiate(CreateMonster), witchslaveslime -> createMob). These bypass Spawnenemy, so they are randomized here.
    /// The rooftop crow events are kept: their monster carries EnemydeathCheck, which releases the slave and grants the
    /// rooftop key (docs/research-feedback.md section 4).
    /// </summary>
    internal static class RescueRandomizer
    {
        internal static GameObject Choose(Component source, GameObject original)
        {
            var st = Plugin.State;
            var pool = EnemyPool.Instance;
            if (st == null || !st.RandomizeEnemies || pool == null || !pool.Ready || original == null) return null;
            if (original.GetComponentInChildren<EnemydeathCheck>(true) != null) return null;
            string scene = source.gameObject.scene.name;
            if (EnemyPool.IsExcludedScene(scene)) return null;
            var choice = pool.Choose(st.Seed, scene, "rescue:" + Hierarchy.GetPath(source.transform), 0);
            if (choice != null && Plugin.VerboseEnemyLog)
                Plugin.Log.LogInfo("Rescue monster " + scene + "/" + Hierarchy.GetPath(source.transform) + ": " + original.name + " -> " + choice.name);
            return choice;
        }
    }

    [HarmonyPatch(typeof(SlaveSideLook), "createinvoke")]
    internal static class SlaveSideLookPatch
    {
        private static void Prefix(SlaveSideLook __instance, ref GameObject ___CreateMonster)
        {
            var choice = RescueRandomizer.Choose(__instance, ___CreateMonster);
            if (choice != null) ___CreateMonster = choice;
        }
    }

    [HarmonyPatch(typeof(witchslaveslime), "Start")]
    internal static class WitchSlaveSlimePatch
    {
        private static void Postfix(witchslaveslime __instance, ref GameObject ___createMob)
        {
            var choice = RescueRandomizer.Choose(__instance, ___createMob);
            if (choice != null) ___createMob = choice;
        }
    }
}
