using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.Localization
{
    /// <summary>
    /// English localization. Four layers:
    ///   1. code literals   - transpiler on every method listed in code_methods.txt (replaces ldstr operands)
    ///   2. GDE data        - item names/descriptions replaced right after GDEDataManager.Init
    ///   3. serialized text - string fields of game components and prefabs, on every scene load
    ///   4. display         - TMP / UI.Text / TextMesh setters (catches anything built at runtime)
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nor.archipelago.localization";
        public const string Name = "NoR English";
        public const string Version = "0.2.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static TranslationTable Table;
        internal static bool Enabled;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _dumpMissing;
        private string _dir;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true, "Replace Japanese text with English.");
            _dumpMissing = Config.Bind("Debug", "WriteMissingReport", true,
                "On exit, write hashes of Japanese strings that were displayed without translation to translation/missing_seen.txt.");
            Enabled = _enabled.Value;
            if (!Enabled) { Log.LogInfo("Localization disabled in config"); return; }

            _dir = Path.Combine(Path.GetDirectoryName(Info.Location), "translation");
            Table = TranslationTable.Load(_dir, "en");
            NoR.Core.TextHooks.Translate = s => Table.Translate(s);
            Log.LogInfo("Translation table: " + Table.Count + " strings");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            int patched = CodeLiteralPatcher.PatchAll(harmony, Path.Combine(_dir, "code_methods.txt"));
            Log.LogInfo("Code literal methods patched: " + patched);

            SceneManager.sceneLoaded += (s, m) =>
            {
                SerializedText.ProcessLoaded();
                int added = NoR.Core.Fonts.EnsureLatinFallbacks();
                if (added > 0) Log.LogInfo("Latin fallback font " + NoR.Core.Fonts.BestName + " added to " + added + " font(s)");
            };
        }

        private void OnApplicationQuit()
        {
            if (Table == null || !_dumpMissing.Value) return;
            try
            {
                var sb = new StringBuilder("# Japanese strings shown in-game without translation (hash). hits=" + Table.Hits + " misses=" + Table.Misses + "\n");
                foreach (var h in Table.MissingHashes) sb.Append(h).Append('\n');
                File.WriteAllText(Path.Combine(_dir, "missing_seen.txt"), sb.ToString());
            }
            catch (Exception e) { Log.LogWarning("Missing report failed: " + e.Message); }
        }
    }
}
