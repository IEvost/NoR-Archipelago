using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.DataDumper
{
    /// <summary>
    /// Developer-only research plugin (Milestone 0). Not part of the player release.
    ///
    ///   F8  toggle overlay
    ///   F9  write a numbered MARKER line into the flag log (note what you just did)
    ///   F10 re-dump all currently loaded scenes
    ///   F11 re-dump GameDataEditor data
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nor.archipelago.datadumper";
        public const string Name = "NoR Data Dumper";
        public const string Version = "0.2.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        private ConfigEntry<bool> _dumpScenes;
        private ConfigEntry<bool> _dumpAllComponents;
        private ConfigEntry<string> _typeRegex;
        private ConfigEntry<bool> _logFlags;
        private ConfigEntry<float> _pollInterval;
        private ConfigEntry<string> _statusFields;

        private string _outDir;
        private SceneDumper _sceneDumper;
        private FlagLogger _flagLogger;
        private bool _overlay = true;
        private int _markers;
        private string _lastMessage = string.Empty;

        private void Awake()
        {
            Log = Logger;

            _dumpScenes = Config.Bind("Dump", "DumpScenesOnLoad", true, "Dump component data of every scene when it is loaded.");
            _dumpAllComponents = Config.Bind("Dump", "DumpAllComponents", false, "Dump every Assembly-CSharp MonoBehaviour instead of only types matching TypeRegex (large output).");
            _typeRegex = Config.Bind("Dump", "TypeRegex",
                "Pickup|Treasure|Spawn|Boss|Door|Gimick|Gimmick|Elevator|Wall|Karma|Carma|Lake|Shop|Trade|Slave|help|^Ev|Talk|TextController|Dialog|Savepoint|Warp|LoadBoss|Load|Scene|Move|Ending|Choose|Drop|Enemy|Library|Title|Npc|NPC|Witch",
                "Regex (case-insensitive) on the component type name selecting what gets dumped.");
            _logFlags = Config.Bind("FlagLog", "Enabled", true, "Log every change of game flags, karma, level and inventory with scene and player position.");
            _pollInterval = Config.Bind("FlagLog", "PollIntervalSeconds", 0.25f, "How often flags are compared.");
            _statusFields = Config.Bind("FlagLog", "PlayerStatusFields",
                "KarmaCount,LV,ExpALL,Str,Int,Dex,LUCK,HP_Posion,MP_Posion,Costume,CostumeBreak,RapeCount,HaramiCount",
                "PlayerStatus fields included in the flag log.");

            _outDir = Path.Combine(Paths.BepInExRootPath, "NoR.Dump");
            Directory.CreateDirectory(_outDir);

            _sceneDumper = new SceneDumper(Path.Combine(_outDir, "scenes"), _typeRegex.Value, _dumpAllComponents.Value);
            if (_logFlags.Value)
                _flagLogger = new FlagLogger(Path.Combine(_outDir, "flaglog.jsonl"), _pollInterval.Value, _statusFields.Value.Split(','));

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            Log.LogInfo(Name + " " + Version + " writing to " + _outDir);
        }

        private void Start()
        {
            // GDE data is a Resources TextAsset, available from the first frame.
            Safe("GDE dump", () => _lastMessage = GdeDumper.Dump(_outDir));
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_flagLogger != null) _flagLogger.OnSceneEvent("loaded", scene.name, mode.ToString());
            if (_dumpScenes.Value) Safe("scene dump " + scene.name, () => _lastMessage = _sceneDumper.Dump(scene));
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_flagLogger != null) _flagLogger.OnSceneEvent("unloaded", scene.name, null);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) _overlay = !_overlay;
            if (Input.GetKeyDown(KeyCode.F9) && _flagLogger != null)
            {
                _markers++;
                _flagLogger.Marker(_markers);
                _lastMessage = "MARKER " + _markers;
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    Safe("scene dump " + s.name, () => _lastMessage = _sceneDumper.Dump(s));
                }
            }
            if (Input.GetKeyDown(KeyCode.F11)) Safe("GDE dump", () => _lastMessage = GdeDumper.Dump(_outDir));

            if (_flagLogger != null) Safe("flag log", _flagLogger.Tick);
        }

        private void OnGUI()
        {
            if (!_overlay) return;
            string text = Name + " | F9 marker (" + _markers + ") | F10 dump scenes | F11 dump GDE | F8 hide\n"
                + (_flagLogger != null ? "flag changes logged: " + _flagLogger.ChangeCount + "\n" : string.Empty)
                + _lastMessage;
            GUI.Label(new Rect(8, Screen.height - 64, Screen.width - 16, 60), text);
        }

        private void OnDestroy()
        {
            if (_flagLogger != null) _flagLogger.Dispose();
        }

        private static readonly System.Collections.Generic.HashSet<string> ReportedErrors = new System.Collections.Generic.HashSet<string>();

        private static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                // Report each distinct failure once; Update() would otherwise spam the log every tick.
                if (ReportedErrors.Add(what + "|" + e.GetType().Name + "|" + e.Message))
                    Log.LogError(what + " failed: " + e);
            }
        }
    }
}
