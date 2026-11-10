using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NoR.Core;
using NoR.Core.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.DataDumper
{
    /// <summary>
    /// Dumps game components of one loaded scene, including inactive objects, to scenes/&lt;scene&gt;.json.
    /// Also collects every on-screen text element containing Japanese for the translation pipeline.
    /// </summary>
    internal sealed class SceneDumper
    {
        private readonly string _dir;
        private readonly Regex _typeFilter;
        private readonly bool _all;
        private readonly System.Reflection.Assembly _gameAssembly = typeof(game_fragmng).Assembly;

        public SceneDumper(string dir, string typeRegex, bool all)
        {
            _dir = dir;
            _all = all;
            _typeFilter = new Regex(typeRegex, RegexOptions.IgnoreCase);
            Directory.CreateDirectory(dir);
        }

        public string Dump(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "scene not loaded: " + scene.name;

            var components = new List<MonoBehaviour>();
            var texts = new List<KeyValuePair<Component, string>>();

            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null || mb.gameObject.scene != scene) continue;
                var t = mb.GetType();

                string text = GetText(mb);
                if (text != null && Hierarchy.ContainsJapanese(text)) texts.Add(new KeyValuePair<Component, string>(mb, text));

                if (t.Assembly != _gameAssembly) continue;
                if (t.Namespace != null) continue; // skip third-party namespaces (TMPro, Spine, PixelCrushers, ...)
                if (!_all && !_typeFilter.IsMatch(t.Name)) continue;
                components.Add(mb);
            }
            foreach (var tm in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (tm != null && tm.gameObject.scene == scene && Hierarchy.ContainsJapanese(tm.text))
                    texts.Add(new KeyValuePair<Component, string>(tm, tm.text));
            }

            components.Sort((a, b) => string.CompareOrdinal(a.GetType().Name + Hierarchy.GetPath(a.transform), b.GetType().Name + Hierarchy.GetPath(b.transform)));

            var path = Path.Combine(_dir, Sanitize(scene.name) + ".json");
            using (var sw = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                var w = new JsonWriter(sw);
                w.BeginObject();
                w.Prop("scene", scene.name);
                w.Prop("buildIndex", scene.buildIndex);
                w.Prop("dumpedAt", DateTime.Now.ToString("s"));

                w.Name("components").BeginArray();
                foreach (var mb in components) WriteComponent(w, mb);
                w.EndArray();

                w.Name("texts").BeginArray();
                foreach (var kv in texts)
                {
                    w.BeginObject();
                    w.Prop("type", kv.Key.GetType().Name);
                    w.Prop("path", Hierarchy.GetPath(kv.Key.transform));
                    w.Prop("text", kv.Value);
                    w.EndObject();
                }
                w.EndArray();
                w.EndObject();
            }

            var msg = "Dumped " + scene.name + ": " + components.Count + " components, " + texts.Count + " JP texts";
            Plugin.Log.LogInfo(msg);
            return msg;
        }

        private static void WriteComponent(JsonWriter w, MonoBehaviour mb)
        {
            var go = mb.gameObject;
            var pos = go.transform.position;
            w.BeginObject();
            w.Prop("type", mb.GetType().Name);
            w.Prop("path", Hierarchy.GetPath(go.transform));
            w.Prop("activeInHierarchy", go.activeInHierarchy);
            w.Prop("enabled", mb.enabled);
            w.Prop("tag", go.tag);
            w.Name("position").BeginObject().Prop("x", pos.x).Prop("y", pos.y).EndObject();
            w.Name("fields");
            try { ObjectSerializer.WriteFields(w, mb); }
            catch (Exception e) { w.Value("<error: " + e.Message + ">"); }
            w.EndObject();
        }

        private static string GetText(MonoBehaviour mb)
        {
            var tmp = mb as TMPro.TMP_Text;
            if (tmp != null) return tmp.text;
            var ui = mb as UnityEngine.UI.Text;
            if (ui != null) return ui.text;
            return null;
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
