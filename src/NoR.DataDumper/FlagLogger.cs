using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NoR.Core;
using NoR.Core.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.DataDumper
{
    /// <summary>
    /// Records every change of progression-relevant state as JSON lines. Used in the research playthrough
    /// (Phase 0.5) to learn which pickups, doors, bosses and events set which flags, and where.
    ///
    /// Sources: all non-float fields of game_fragmng, selected PlayerStatus fields,
    /// ItemInventory lists, and StaticMng static fields (except per-frame stat totals).
    /// </summary>
    internal sealed class FlagLogger : IDisposable
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private readonly StreamWriter _log;
        private readonly float _interval;
        private readonly HashSet<string> _statusFields;
        private Dictionary<string, string> _prev;
        private UnityEngine.Object _prevController;
        private float _next;

        public int ChangeCount { get; private set; }

        public FlagLogger(string path, float interval, IEnumerable<string> statusFields)
        {
            _log = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
            _interval = Mathf.Max(0.05f, interval);
            _statusFields = new HashSet<string>();
            foreach (var f in statusFields) if (f.Trim().Length > 0) _statusFields.Add(f.Trim());
            Write(w => w.Prop("event", "session_start").Prop("time", DateTime.Now.ToString("s")));
        }

        public void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + _interval;
            if (!GameRefs.InGame) return;

            var cur = Snapshot();
            if (_prevController != GameRefs.Flags)
            {
                // New game controller (new game / load / title): log a full baseline instead of a diff.
                _prevController = GameRefs.Flags;
                _prev = cur;
                Write(w =>
                {
                    Header(w, "baseline");
                    w.Name("state").BeginObject();
                    foreach (var kv in cur) w.Prop(kv.Key, kv.Value);
                    w.EndObject();
                });
                return;
            }

            List<string> changed = null;
            foreach (var kv in cur)
            {
                string old;
                if (!_prev.TryGetValue(kv.Key, out old) || old != kv.Value)
                    (changed ?? (changed = new List<string>())).Add(kv.Key);
            }
            if (changed == null) { _prev = cur; return; }

            var prev = _prev;
            _prev = cur;
            ChangeCount += changed.Count;
            Write(w =>
            {
                Header(w, "change");
                w.Name("changes").BeginArray();
                foreach (var k in changed)
                {
                    string from;
                    prev.TryGetValue(k, out from);
                    w.BeginObject().Prop("k", k).Prop("from", from).Prop("to", cur[k]).EndObject();
                }
                w.EndArray();
            });
        }

        public void Marker(int n)
        {
            Write(w => { Header(w, "marker"); w.Prop("n", n); });
        }

        public void OnSceneEvent(string kind, string scene, string mode)
        {
            Write(w => { Header(w, "scene_" + kind); w.Prop("scene", scene); if (mode != null) w.Prop("mode", mode); });
        }

        private static void Header(JsonWriter w, string evt)
        {
            w.Prop("event", evt);
            w.Prop("t", Math.Round(Time.unscaledTime, 2));
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SceneManager.GetSceneAt(i).name);
            }
            w.Prop("scenes", sb.ToString());
            var player = GameRefs.Player;
            if (player != null)
            {
                var p = player.transform.position;
                w.Name("pos").BeginObject().Prop("x", Math.Round(p.x, 2)).Prop("y", Math.Round(p.y, 2)).EndObject();
            }
        }

        private void Write(Action<JsonWriter> body)
        {
            var line = JsonWriter.Serialize(w => { w.BeginObject(); body(w); w.EndObject(); }, indent: false);
            _log.WriteLine(line);
        }

        private Dictionary<string, string> Snapshot()
        {
            var d = new Dictionary<string, string>();
            AddFields(d, "flags", GameRefs.Flags, null);
            AddFields(d, "status", GameRefs.Status, _statusFields);
            AddFields(d, "inv", GameRefs.Inventory, null);
            AddStatics(d, "static", typeof(StaticMng));
            return d;
        }

        private static void AddFields(Dictionary<string, string> d, string prefix, object obj, HashSet<string> only)
        {
            if (obj == null) return;
            foreach (var f in obj.GetType().GetFields(Inst))
            {
                if (only != null && !only.Contains(f.Name)) continue;
                AddValue(d, prefix + "." + f.Name, f.FieldType, f.GetValue(obj));
            }
        }

        private static void AddStatics(Dictionary<string, string> d, string prefix, Type t)
        {
            foreach (var f in t.GetFields(Stat))
            {
                if (f.Name.StartsWith("total", StringComparison.Ordinal) || f.IsLiteral) continue;
                AddValue(d, prefix + "." + f.Name, f.FieldType, f.GetValue(null));
            }
        }

        private static void AddValue(Dictionary<string, string> d, string key, Type type, object v)
        {
            // Floats change every frame (play time, HP, timers) and are not progression state.
            if (type == typeof(float) || type == typeof(double)) return;
            if (type.IsArray && (type.GetElementType() == typeof(float) || type.GetElementType() == typeof(double))) return;
            if (type == typeof(List<float>)) return;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return;

            if (v == null) { d[key] = "null"; return; }
            if (type == typeof(bool) || type == typeof(int) || type == typeof(string) || type.IsEnum || type == typeof(long))
            {
                d[key] = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
            if (type == typeof(Vector2))
            {
                var p = (Vector2)v;
                d[key] = p.x.ToString("0.#") + "," + p.y.ToString("0.#");
                return;
            }

            var arr = v as Array;
            if (arr != null && arr.Rank == 2 && IsSimple(type.GetElementType()))
            {
                for (int i = 0; i < arr.GetLength(0); i++)
                    for (int j = 0; j < arr.GetLength(1); j++)
                        d[key + "[" + i + "," + j + "]"] = Convert.ToString(arr.GetValue(i, j), System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
            if (arr != null && arr.Rank == 1 && IsSimple(type.GetElementType()))
            {
                for (int i = 0; i < arr.Length; i++)
                    d[key + "[" + i + "]"] = Convert.ToString(arr.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
                return;
            }

            // Lists (inventory IDs, shop counts, nested NormalItemID) and any other arrays
            // (multi-dimensional, non-simple element types) are logged as one joined value.
            var seq = v as IEnumerable;
            if (seq != null)
            {
                d[key] = Join(seq);
            }
        }

        private static string Join(IEnumerable seq)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var x in seq) // foreach works for multi-dimensional arrays, the IList indexer does not
            {
                if (!first) sb.Append(',');
                first = false;
                var inner = x as IEnumerable;
                if (inner != null && !(x is string)) sb.Append(Join(inner));
                else if (x is float) sb.Append(((float)x).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                else sb.Append(Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        private static bool IsSimple(Type t)
        {
            return t == typeof(bool) || t == typeof(int) || t == typeof(string) || t == typeof(long);
        }

        public void Dispose()
        {
            Write(w => w.Prop("event", "session_end").Prop("time", DateTime.Now.ToString("s")));
            _log.Dispose();
        }
    }
}
