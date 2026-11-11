using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.Randomizer
{
    /// <summary>
    /// Area-tier scaling for randomized enemies (user decision: "match area tier").
    ///
    /// Each scene gets a tier = the average base HP / Idea / attack of its vanilla spawns (data/enemy_stats.tsv).
    /// A replacement enemy is scaled towards that tier: factor = (areaAverage / replacementBase) ^ Strength, so with
    /// Strength 1 every enemy in an area has the area's average stats, with lower values the enemies keep part of their
    /// own character. Difficulty multipliers apply to both sides and cancel out.
    ///
    /// Stats are written after the enemy's own Start() (which assigns them in code), and the attack factor is re-applied
    /// on every EnemyDate.SetAtkVal (attacks set their damage right before hitting).
    /// </summary>
    public static class EnemyScaling
    {
        public sealed class Stats { public float Hp, Exp, Atk; }
        public sealed class Scale { public float Hp = 1f, Exp = 1f, Atk = 1f; }

        public static bool Enabled = true;
        public static float Strength = 0.8f;

        private static readonly Dictionary<string, Stats> Base = new Dictionary<string, Stats>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Stats> AreaTier = new Dictionary<string, Stats>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Scale> Pending = new Dictionary<int, Scale>();  // EnemyDate instance id
        private static readonly FieldInfo EnemyField = AccessTools.Field(typeof(Spawnenemy), "enemy");

        public static void Load()
        {
            using (var s = typeof(EnemyScaling).Assembly.GetManifestResourceStream("NoR.Randomizer.enemy_stats.tsv"))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length < 4) continue;
                    Base[c[0]] = new Stats { Hp = float.Parse(c[1]), Exp = float.Parse(c[2]), Atk = float.Parse(c[3]) };
                }
            }
            SceneManager.sceneLoaded += (scene, mode) => ComputeAreaTier(scene);
        }

        /// <summary>Average base stats of the scene's vanilla spawns, read before any spawner is randomized.</summary>
        private static void ComputeAreaTier(Scene scene)
        {
            if (AreaTier.ContainsKey(scene.name)) return;
            float hp = 0, exp = 0, atk = 0;
            int n = 0;
            foreach (var sp in Resources.FindObjectsOfTypeAll<Spawnenemy>())
            {
                if (sp == null || sp.gameObject.scene != scene) continue;
                var prefab = EnemyField.GetValue(sp) as GameObject;
                Stats st;
                if (prefab == null || !Base.TryGetValue(prefab.name, out st)) continue;
                hp += st.Hp; exp += st.Exp; atk += st.Atk; n++;
            }
            if (n == 0) return;
            AreaTier[scene.name] = new Stats { Hp = hp / n, Exp = exp / n, Atk = atk / n };
            Plugin.Log.LogInfo("Area tier " + scene.name + ": HP " + (hp / n).ToString("0") + ", Idea " + (exp / n).ToString("0") +
                               ", ATK " + (atk / n).ToString("0") + " (" + n + " spawns)");
        }

        public static Scale ScaleFor(string scene, string replacementPrefab)
        {
            Stats area, repl;
            if (!Enabled || !AreaTier.TryGetValue(scene, out area) || !Base.TryGetValue(replacementPrefab, out repl)) return null;
            return new Scale
            {
                Hp = Factor(area.Hp, repl.Hp),
                Exp = Factor(area.Exp, repl.Exp),
                Atk = Factor(area.Atk, repl.Atk),
            };
        }

        private static float Factor(float area, float own)
        {
            if (own <= 0f || area <= 0f) return 1f;
            return Mathf.Clamp(Mathf.Pow(area / own, Strength), 0.05f, 20f);
        }

        /// <summary>Called right after a spawner instantiated a randomized enemy (its Start has not run yet).</summary>
        public static void Register(GameObject spawned, string scene, string replacementPrefab)
        {
            var scale = ScaleFor(scene, replacementPrefab);
            if (scale == null || spawned == null) return;
            foreach (var ed in spawned.GetComponentsInChildren<EnemyDate>(true))
                Pending[ed.GetInstanceID()] = scale;
        }

        internal static Scale Get(EnemyDate ed)
        {
            Scale s;
            return ed != null && Pending.TryGetValue(ed.GetInstanceID(), out s) ? s : null;
        }

        internal static void ApplyAfterStart(EnemyDate ed)
        {
            var s = Get(ed);
            if (s == null) return;
            ed.MaxHp = Mathf.Max(1f, ed.MaxHp * s.Hp);
            ed.Hp = ed.MaxHp;
            ed.Exp = Mathf.Max(1, Mathf.RoundToInt(ed.Exp * s.Exp));
            ed.enmATK *= s.Atk;
        }

        public static void ClearScene()
        {
            if (Pending.Count > 2000) Pending.Clear();
        }

        // ------------------------------------------------------------------ patches

        /// <summary>Postfix on every EnemyDate subclass's own Start(): their stats are assigned there in code.</summary>
        [HarmonyPatch]
        internal static class StartPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return typeof(EnemyDate).Assembly.GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(EnemyDate)) && !t.IsAbstract)
                    .Select(t => (MethodBase)AccessTools.DeclaredMethod(t, "Start"))
                    .Where(m => m != null);
            }

            private static void Postfix(EnemyDate __instance)
            {
                try { ApplyAfterStart(__instance); }
                catch (Exception e) { Plugin.Log.LogWarning("Enemy scaling failed: " + e.Message); }
            }
        }

        /// <summary>Attacks set their damage right before they hit.</summary>
        [HarmonyPatch(typeof(EnemyDate), "SetAtkVal")]
        internal static class AtkPatch
        {
            private static void Postfix(EnemyDate __instance)
            {
                var s = Get(__instance);
                if (s != null) __instance.enmATK *= s.Atk;
            }
        }

        /// <summary>Snailshell re-runs SetStatusGameDifficulty when its shell breaks (resets MaxHp unscaled).</summary>
        [HarmonyPatch(typeof(EnemyDate), "SetStatusGameDifficulty")]
        internal static class HpResetPatch
        {
            private static void Postfix(EnemyDate __instance)
            {
                var s = Get(__instance);
                if (s == null) return;
                __instance.MaxHp = Mathf.Max(1f, __instance.MaxHp * s.Hp);
                if (__instance.Hp > __instance.MaxHp) __instance.Hp = __instance.MaxHp;
            }
        }
    }

    /// <summary>
    /// Vanilla bug fix: one swing could hit an enemy twice. The player's attack hitbox swaps collider shapes mid-swing
    /// and each newly enabled shape fires OnTriggerEnter2D again; only bosses guard against it (damegeID ring buffer).
    /// Gate every enemy's melee damage by the swing id (playercon.ATKID, re-rolled per swing / multi-hit event).
    /// </summary>
    [HarmonyPatch(typeof(EnemyDate), "OndamageSend")]
    internal static class DoubleHitFix
    {
        public static bool Enabled = true;
        private static readonly Dictionary<int, KeyValuePair<float, float>> LastHit = new Dictionary<int, KeyValuePair<float, float>>();

        private static bool Prefix(EnemyDate __instance, string tag)
        {
            if (!Enabled || tag != "ATKweapon" || __instance.com_player == null) return true;
            int id = __instance.GetInstanceID();
            float atkId = __instance.com_player.ATKID;
            KeyValuePair<float, float> last;
            if (LastHit.TryGetValue(id, out last) && last.Key == atkId && Time.time - last.Value < 1f) return false;
            LastHit[id] = new KeyValuePair<float, float>(atkId, Time.time);
            if (LastHit.Count > 512) LastHit.Clear();
            return true;
        }
    }
}
