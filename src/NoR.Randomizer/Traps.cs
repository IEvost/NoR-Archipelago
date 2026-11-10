using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer
{
    /// <summary>
    /// Trap items (Archipelago): "trap:ambush", "trap:idea", "trap:knockdown".
    /// Neutral effects built on existing systems; lewd status-effect traps belong to the H-content options (later).
    /// </summary>
    public static class Traps
    {
        public static string Apply(string kind)
        {
            switch (kind)
            {
                case "ambush": return Ambush();
                case "idea": return IdeaDrain();
                case "knockdown": return Knockdown();
                default:
                    Plugin.Log.LogWarning("Unknown trap " + kind);
                    return null;
            }
        }

        /// <summary>Spawns a random regular enemy from the global pool a few steps away from Aradia.</summary>
        private static string Ambush()
        {
            var pool = EnemyPool.Instance;
            var player = GameRefs.Player;
            if (pool == null || !pool.Ready || pool.Names.Count == 0 || player == null) return Knockdown(); // fallback
            var template = pool.Templates[pool.Names[Random.Range(0, pool.Names.Count)]];

            // Parent under the level's spawn manager so the enemy lives (and unloads) with the level scene.
            var parent = Object.FindObjectOfType<SpawnParent>();
            float side = Random.value < 0.5f ? -1f : 1f;
            var pos = player.transform.position + new Vector3(4f * side, 1.5f, 0f);
            var enemy = parent != null
                ? Object.Instantiate(template, pos, Quaternion.identity, parent.transform)
                : Object.Instantiate(template, pos, Quaternion.identity);
            enemy.name = template.name;
            Plugin.Log.LogInfo("Ambush trap: " + template.name);
            return "An enemy ambushes you!";
        }

        /// <summary>Loses 20% of the Idea Aradia carries (at least 100 if she has that much).</summary>
        private static string IdeaDrain()
        {
            var st = GameRefs.Status;
            if (st == null) return null;
            int loss = Mathf.Min(st.Exppoint, Mathf.Max(100, st.Exppoint / 5));
            st.Exppoint -= loss;
            return loss > 0 ? "Idea drained: -" + loss : "Idea drain (you had nothing to lose)";
        }

        /// <summary>Knocks Aradia down like a heavy hit (she can get up normally).</summary>
        private static string Knockdown()
        {
            var go = GameRefs.Player;
            var pc = go != null ? go.GetComponent<playercon>() : null;
            if (pc == null || pc._Death) return null;
            pc.mutekitime = false;
            pc.nowdamage = true;
            if (pc.erodown == 0) pc.erodown = 1;
            var t = Traverse.Create(pc);
            t.Field("nockbackspeed").SetValue(0f);
            t.Field("downup").SetValue(0);
            return "You are knocked down!";
        }
    }
}
