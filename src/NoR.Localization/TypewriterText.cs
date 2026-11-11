using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NoR.Localization
{
    /// <summary>
    /// The game's typewriter controllers (DialogControllerMainEVunder, DialogControllerEroEv, TextController, ...) copy a
    /// line into the private field currentText in SetNextLine and then show growing substrings of it. Substrings are
    /// not in the table, so the line rolled out in Japanese and only the final full string was translated by the TMP
    /// setter. Translating currentText right after SetNextLine fixes every controller regardless of where its data
    /// comes from; the display time is recomputed for the new length.
    /// </summary>
    internal static class TypewriterText
    {
        public static int PatchAll(Harmony harmony)
        {
            var postfix = new HarmonyMethod(typeof(TypewriterText).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            int n = 0;
            foreach (var t in AccessTools.GetTypesFromAssembly(typeof(game_fragmng).Assembly))
            {
                if (t.Namespace != null || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                var field = t.GetField("currentText", flags);
                if (field == null || field.FieldType != typeof(string)) continue;
                var method = t.GetMethod("SetNextLine", flags, null, Type.EmptyTypes, null);
                if (method == null) continue;
                try { harmony.Patch(method, postfix: postfix); n++; }
                catch (Exception e) { Plugin.Log.LogWarning("Typewriter patch failed for " + t.Name + ": " + e.Message); }
            }
            return n;
        }

        private static void Postfix(object __instance)
        {
            if (!Plugin.Enabled || __instance == null) return;
            var tr = Traverse.Create(__instance);
            var src = tr.Field("currentText").GetValue<string>();
            var en = Plugin.Table.Translate(src);
            if (ReferenceEquals(en, src)) return;
            tr.Field("currentText").SetValue(en);
            var interval = tr.Field("intervalForCharacterDisplay");
            var until = tr.Field("timeUntilDisplay");
            if (interval.FieldExists() && until.FieldExists())
                until.SetValue(en.Length * interval.GetValue<float>());
        }
    }
}
