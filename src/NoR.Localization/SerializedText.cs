using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameDataEditor;
using HarmonyLib;
using NoR.Core.Json;
using UnityEngine;

namespace NoR.Localization
{
    /// <summary>
    /// Layer 3: string fields of game components (scene objects and loaded prefab assets), e.g. dialogue arrays
    /// (TextController.scenarios) and TMP texts. Runs after every scene load; each object is processed once.
    /// </summary>
    internal static class SerializedText
    {
        private static readonly HashSet<int> Done = new HashSet<int>();
        private static readonly Assembly GameAsm = typeof(game_fragmng).Assembly;

        public static void ProcessLoaded()
        {
            if (!Plugin.Enabled) return;
            int changed = 0;
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                int id = mb.GetInstanceID();
                if (Done.Contains(id)) continue;
                Done.Add(id);

                var tmp = mb as TMPro.TMP_Text;
                if (tmp != null) { changed += Apply(tmp); continue; }
                var ui = mb as UnityEngine.UI.Text;
                if (ui != null) { var en = Plugin.Table.Translate(ui.text); if (!ReferenceEquals(en, ui.text)) { ui.text = en; changed++; } continue; }

                var t = mb.GetType();
                if (t.Assembly != GameAsm || t.Namespace != null) continue;
                changed += TranslateFields(mb, 0);
            }
            // Dialogue and library data are ScriptableObject "Excel assets" (MainEV, SubEventDialogue, NpcTalk, Chapter,
            // E_Library, WpLibrary) holding lists of serializable rows (Jptext / Jpname ...).
            foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (so == null) continue;
                var t = so.GetType();
                if (t.Assembly != GameAsm || t.Namespace != null) continue;
                if (!Done.Add(so.GetInstanceID())) continue;
                changed += TranslateFields(so, 0);
            }
            foreach (var tm in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (tm == null || !Done.Add(tm.GetInstanceID())) continue;
                var en = Plugin.Table.Translate(tm.text);
                if (!ReferenceEquals(en, tm.text)) { tm.text = en; changed++; }
            }
            if (Done.Count > 200000) Done.Clear();
            if (changed > 0) Plugin.Log.LogDebug("Serialized text replaced: " + changed);
        }

        internal static int Apply(TMPro.TMP_Text tmp)
        {
            var src = tmp.text;
            var en = Plugin.Table.Translate(src);
            if (ReferenceEquals(en, src)) return 0;
            tmp.text = en;
            FitToBox(tmp);
            return 1;
        }

        private static readonly HashSet<int> Fitted = new HashSet<int>();

        /// <summary>
        /// English is longer than the Japanese the UI was laid out for; 342 of 353 TMP texts in the common scene have
        /// auto-size off, so labels wrap and overlap (altar menu, headers). For translated texts in small boxes, enable
        /// auto-sizing down to 60% of the original size.
        /// </summary>
        internal static void FitToBox(TMPro.TMP_Text tmp)
        {
            if (tmp == null || tmp.enableAutoSizing || !Fitted.Add(tmp.GetInstanceID())) return;
            var rt = tmp.rectTransform;
            if (rt == null || rt.rect.height <= 0f || rt.rect.height > 80f) return; // dialogue boxes are taller - leave them
            float size = tmp.fontSize;
            tmp.fontSizeMax = size;
            tmp.fontSizeMin = Mathf.Max(10f, size * 0.6f);
            tmp.enableAutoSizing = true;
            if (Fitted.Count > 50000) Fitted.Clear();
        }

        private const int MaxDepth = 6;

        /// <summary>
        /// Translates all serialized string fields of obj, recursing into nested [Serializable] classes and into
        /// arrays/lists of them (e.g. MainEV.MainEV1[i].Jptext). Unity object references are not followed.
        /// </summary>
        private static int TranslateFields(object obj, int depth)
        {
            if (obj == null || depth > MaxDepth) return 0;
            int n = 0;
            foreach (var f in SerializedFields(obj.GetType()))
            {
                var ft = f.FieldType;
                if (ft == typeof(string))
                {
                    var s = (string)f.GetValue(obj);
                    var en = Plugin.Table.Translate(s);
                    if (!ReferenceEquals(en, s)) { f.SetValue(obj, en); n++; }
                    continue;
                }
                if (typeof(UnityEngine.Object).IsAssignableFrom(ft) || ft.IsPrimitive || ft.IsEnum) continue;

                var value = f.GetValue(obj);
                if (value == null) continue;
                var list = value as IList;
                if (list != null)
                {
                    n += TranslateList(list, depth);
                    continue;
                }
                if (IsSerializableClass(ft)) n += TranslateFields(value, depth + 1);
            }
            return n;
        }

        private static int TranslateList(IList list, int depth)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var s = item as string;
                if (s != null)
                {
                    var en = Plugin.Table.Translate(s);
                    if (!ReferenceEquals(en, s)) { list[i] = en; n++; }
                }
                else if (item != null && !(item is UnityEngine.Object) && IsSerializableClass(item.GetType()))
                {
                    n += TranslateFields(item, depth + 1);
                }
            }
            return n;
        }

        private static bool IsSerializableClass(Type t)
        {
            return t.IsClass && t != typeof(string) && t.Assembly == GameAsm && t.IsSerializable;
        }

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        /// <summary>Unity serialization rules: public or [SerializeField], not [NonSerialized], up to the Unity base types.</summary>
        private static FieldInfo[] SerializedFields(Type type)
        {
            FieldInfo[] result;
            if (FieldCache.TryGetValue(type, out result)) return result;
            var list = new List<FieldInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject)
                                && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(UnityEngine.Object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (f.IsNotSerialized || f.IsInitOnly || f.IsLiteral) continue;
                    if (f.IsPublic || f.GetCustomAttributes(typeof(SerializeField), true).Length > 0) list.Add(f);
                }
            }
            result = list.ToArray();
            FieldCache[type] = result;
            return result;
        }
    }

    /// <summary>Layer 2: GameDataEditor master data (item names, descriptions). Re-applied on every Init.</summary>
    [HarmonyPatch(typeof(GDEDataManager), "Init", new[] { typeof(TextAsset), typeof(bool) })]
    internal static class GdePatch
    {
        private static void Postfix(bool __result)
        {
            if (!__result || !Plugin.Enabled) return;
            int n = 0;
            foreach (var entry in GDEDataManager.DataDictionary)
            {
                var fields = entry.Value as Dictionary<string, object>;
                if (fields == null) continue;
                var keys = new List<string>(fields.Keys);
                foreach (var k in keys)
                {
                    var s = fields[k] as string;
                    if (s != null)
                    {
                        var en = Plugin.Table.Translate(s);
                        if (!ReferenceEquals(en, s)) { fields[k] = en; n++; }
                        continue;
                    }
                    var list = fields[k] as IList;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var ls = list[i] as string;
                        if (ls == null) continue;
                        var en = Plugin.Table.Translate(ls);
                        if (!ReferenceEquals(en, ls)) { list[i] = en; n++; }
                    }
                }
            }
            Plugin.Log.LogInfo("GDE strings translated: " + n);
        }
    }

    /// <summary>Layer 4: anything assigned to visible text at runtime.</summary>
    [HarmonyPatch(typeof(TMPro.TMP_Text), "text", MethodType.Setter)]
    internal static class TmpTextSetter
    {
        private static void Prefix(TMPro.TMP_Text __instance, ref string value)
        {
            if (!Plugin.Enabled) return;
            var en = Plugin.Table.Translate(value);
            if (!ReferenceEquals(en, value)) { value = en; SerializedText.FitToBox(__instance); }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "text", MethodType.Setter)]
    internal static class UiTextSetter
    {
        private static void Prefix(ref string value) { if (Plugin.Enabled) value = Plugin.Table.Translate(value); }
    }

    [HarmonyPatch(typeof(TextMesh), "text", MethodType.Setter)]
    internal static class TextMeshSetter
    {
        private static void Prefix(ref string value) { if (Plugin.Enabled) value = Plugin.Table.Translate(value); }
    }

    /// <summary>
    /// Prefab TMP objects instantiated after the scene load still carry Japanese m_text.
    /// OnEnable is declared on the concrete classes in this TMP version, not on TMP_Text.
    /// </summary>
    [HarmonyPatch(typeof(TMPro.TextMeshProUGUI), "OnEnable")]
    internal static class TmpUguiEnable
    {
        private static void Postfix(TMPro.TMP_Text __instance)
        {
            if (Plugin.Enabled) SerializedText.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(TMPro.TextMeshPro), "OnEnable")]
    internal static class TmpWorldEnable
    {
        private static void Postfix(TMPro.TMP_Text __instance)
        {
            if (Plugin.Enabled) SerializedText.Apply(__instance);
        }
    }
}
