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
                changed += TranslateFields(mb);
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
            return 1;
        }

        private static int TranslateFields(object obj)
        {
            int n = 0;
            foreach (var f in ObjectSerializer.GetSerializedFields(obj.GetType()))
            {
                var ft = f.FieldType;
                if (ft == typeof(string))
                {
                    var s = (string)f.GetValue(obj);
                    var en = Plugin.Table.Translate(s);
                    if (!ReferenceEquals(en, s)) { f.SetValue(obj, en); n++; }
                }
                else if (ft == typeof(string[]))
                {
                    var arr = (string[])f.GetValue(obj);
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var en = Plugin.Table.Translate(arr[i]);
                        if (!ReferenceEquals(en, arr[i])) { arr[i] = en; n++; }
                    }
                }
                else if (ft == typeof(List<string>))
                {
                    var list = (List<string>)f.GetValue(obj);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var en = Plugin.Table.Translate(list[i]);
                        if (!ReferenceEquals(en, list[i])) { list[i] = en; n++; }
                    }
                }
            }
            return n;
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
        private static void Prefix(ref string value) { if (Plugin.Enabled) value = Plugin.Table.Translate(value); }
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
