using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.Randomizer
{
    /// <summary>
    /// Global enemy pool. Enemy prefabs live in per-scene shared asset files, so they are only loadable
    /// together with a scene that uses them. At the title screen we load a minimal set of such scenes
    /// additively, immediately deactivate all their root objects (so Start/Update never run), keep one
    /// inactive copy of each enemy prefab under a DontDestroyOnLoad holder, and unload the scene again.
    /// </summary>
    public sealed class EnemyPool : MonoBehaviour
    {
        public static EnemyPool Instance { get; private set; }

        /// <summary>prefab name -> inactive template (usable with Object.Instantiate)</summary>
        public readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        public readonly List<string> Names = new List<string>();

        public bool Loading { get; private set; }
        public bool Ready { get; private set; }
        public string Status = string.Empty;

        private Transform _holder;
        private HashSet<string> _excluded;
        private static readonly FieldInfo EnemyField = typeof(Spawnenemy).GetField("enemy", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static EnemyPool Create(IEnumerable<string> excluded)
        {
            var go = new GameObject("NoR.EnemyPool");
            DontDestroyOnLoad(go);
            var pool = go.AddComponent<EnemyPool>();
            pool._excluded = new HashSet<string>(excluded, StringComparer.Ordinal);

            var holder = new GameObject("Templates");
            holder.SetActive(false); // children never Awake while parked here
            holder.transform.SetParent(go.transform, false);
            pool._holder = holder.transform;
            Instance = pool;
            return pool;
        }

        /// <summary>Adds the prefabs referenced by spawners of a loaded scene (also used during normal play).</summary>
        public int Harvest(Scene scene)
        {
            int added = 0;
            foreach (var sp in Resources.FindObjectsOfTypeAll<Spawnenemy>())
            {
                if (sp == null || sp.gameObject.scene != scene) continue;
                var prefab = EnemyField.GetValue(sp) as GameObject;
                if (prefab == null || Templates.ContainsKey(prefab.name) || _excluded.Contains(prefab.name)) continue;
                var copy = Instantiate(prefab, _holder, false);
                copy.name = prefab.name;
                Templates[prefab.name] = copy;
                Names.Add(prefab.name);
                added++;
            }
            if (added > 0) Names.Sort(StringComparer.Ordinal);
            return added;
        }

        public IEnumerator Preload(List<string> scenes)
        {
            Loading = true;
            int i = 0;
            foreach (var name in scenes)
            {
                i++;
                Status = "Loading enemy pool " + i + "/" + scenes.Count + " (" + name + ")";
                Scene loaded = default(Scene);
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) =>
                {
                    if (s.name != name) return;
                    loaded = s;
                    // Runs after Awake/OnEnable but before any Start: switch the whole scene off right away.
                    foreach (var root in s.GetRootGameObjects()) root.SetActive(false);
                };
                SceneManager.sceneLoaded += onLoaded;
                AsyncOperation op = null;
                try { op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive); }
                catch (Exception e) { Plugin.Log.LogWarning("Enemy pool: cannot load " + name + ": " + e.Message); }
                if (op != null) while (!op.isDone) yield return null;
                SceneManager.sceneLoaded -= onLoaded;

                if (loaded.IsValid())
                {
                    int added = 0;
                    try { added = Harvest(loaded); }
                    catch (Exception e) { Plugin.Log.LogWarning("Enemy pool: harvest failed in " + name + ": " + e.Message); }
                    Plugin.Log.LogInfo("Enemy pool: " + name + " +" + added + " (total " + Templates.Count + ")");
                    var unload = SceneManager.UnloadSceneAsync(loaded);
                    if (unload != null) while (!unload.isDone) yield return null;
                }
            }
            yield return Resources.UnloadUnusedAssets();
            Loading = false;
            Ready = true;
            Status = "Enemy pool ready: " + Templates.Count + " enemy types";
            Plugin.Log.LogInfo(Status + ": " + string.Join(", ", Names.ToArray()));
        }

        /// <summary>Greedy set cover: the fewest scenes that together contain every enemy prefab.</summary>
        public static List<string> PlanScenes(string catalogTsv, HashSet<string> excluded)
        {
            var sceneToPrefabs = new List<KeyValuePair<string, HashSet<string>>>();
            var needed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in catalogTsv.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c.Length < 3) continue;
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in c[2].Split(',')) if (p.Length > 0 && !excluded.Contains(p)) { set.Add(p); needed.Add(p); }
                sceneToPrefabs.Add(new KeyValuePair<string, HashSet<string>>(c[0], set));
            }

            var plan = new List<string>();
            while (needed.Count > 0)
            {
                KeyValuePair<string, HashSet<string>> best = default(KeyValuePair<string, HashSet<string>>);
                int bestGain = 0;
                foreach (var kv in sceneToPrefabs)
                {
                    int gain = 0;
                    foreach (var p in kv.Value) if (needed.Contains(p)) gain++;
                    if (gain > bestGain) { bestGain = gain; best = kv; }
                }
                if (bestGain == 0) break;
                plan.Add(best.Key);
                foreach (var p in best.Value) needed.Remove(p);
            }
            return plan;
        }

        public static string LoadCatalog()
        {
            using (var s = typeof(EnemyPool).Assembly.GetManifestResourceStream("NoR.Randomizer.enemies.tsv"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        /// <summary>Deterministic choice per spawn point: same seed + same spawner = same enemy on every visit.</summary>
        public GameObject Choose(ulong seed, string scene, string spawnerPath, int spawnNumber)
        {
            if (Names.Count == 0) return null;
            ulong h = seed ^ 0xA0761D6478BD642FUL;
            h = Mix(h, scene);
            h = Mix(h, spawnerPath);
            h = (h ^ (ulong)spawnNumber) * 0x100000001B3UL;
            var rng = new SplitMix64(h);
            return Templates[Names[rng.NextInt(Names.Count)]];
        }

        private static ulong Mix(ulong h, string s)
        {
            foreach (char ch in s) h = (h ^ ch) * 0x100000001B3UL; // FNV-1a step
            return h;
        }

        private void OnGUI()
        {
            if (!Loading) return;
            GUI.Label(new Rect(8, 32, 700, 24), "NoR Randomizer: " + Status + " - please wait before starting a game");
        }
    }
}
