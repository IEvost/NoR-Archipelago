using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace NoR.Randomizer
{
    /// <summary>
    /// Offline item randomizer (pre-logic prototype). Settings apply when a NEW GAME is started;
    /// existing saves keep the settings they were created with (or stay vanilla).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nor.archipelago.randomizer";
        public const string Name = "NoR Randomizer";
        public const string Version = "0.2.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        public static LocationTable Table;

        /// <summary>Active randomizer state for the running game, or null for a vanilla game.</summary>
        public static RandoState State;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<string> _seed;
        private static ConfigEntry<bool> _floor, _chests, _magic, _spoiler;
        private static string _outDir;

        private float _bannerUntil;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("NewGame", "Enabled", true, "Randomize new games. Existing saves are not affected.");
            _seed = Config.Bind("NewGame", "Seed", "", "Seed for the next new game (number). Empty = random.");
            _floor = Config.Bind("NewGame", "ShuffleFloorItems", true, "Include items lying in the world.");
            _chests = Config.Bind("NewGame", "ShuffleChests", true, "Include chest contents (lockpicks are never placed in chests).");
            _magic = Config.Bind("NewGame", "ShuffleMagic", true, "Include magic/spell pickups.");
            _spoiler = Config.Bind("Debug", "WriteSpoilerLog", true, "Write a spoiler log for every new seed to BepInEx/NoR.Randomizer/.");
            _enemies = Config.Bind("Enemies", "RandomizeEnemies", true,
                "Randomize regular enemies (global pool, all areas). Requires the pool to load at the title screen. Bosses are not affected.");
            _enemyExclude = Config.Bind("Enemies", "ExcludePrefabs", "",
                "Comma-separated enemy prefab names never used by the randomizer (see BepInEx/LogOutput.log for the pool list).");
            _verboseEnemies = Config.Bind("Debug", "LogEnemySwaps", false, "Log every randomized spawn point.");
            _shopEnabled = Config.Bind("Shop", "Enabled", true, "Witch's Market at save points (randomized games only).");
            _shopSlots = Config.Bind("Shop", "Slots", 10, "One-time shop slots that act as randomizer locations (0-20). Applies to new games.");
            _shopPrice = Config.Bind("Shop", "PriceMultiplier", 1.0f, "Multiplier for all shop prices.");
            _shopKey = Config.Bind("Shop", "OpenKey", KeyCode.B, "Keyboard key that opens the shop at a save point (gamepad: item-switch button).");

            _outDir = Path.Combine(Paths.BepInExRootPath, "NoR.Randomizer");

            _bossDrops = Config.Bind("NewGame", "ShuffleBossDrops", true, "Include boss first-kill drops (boss Idea items, Candore's weapon).");
            _npcRewards = Config.Bind("NewGame", "ShuffleNpcRewards", true, "Include NPC/event item rewards (Suzune, town NPCs, Ranch event, witch slave).");
            _trades = Config.Bind("NewGame", "ShuffleTrades", true, "Include Vendetta's trades (costs stay vanilla).");

            Table = LocationTable.LoadEmbedded();
            CodeLocations.Register(Table);
            Log.LogInfo("Location table: " + Table.All.Count + " locations");

            DeathSkip.Enabled = Config.Bind("Gameplay", "DeathSkip", true,
                "After a real death (HP 0) show 'Press G / hold Jump to skip and respawn' (skips the defeat animation and game-over panel).").Value;
            DeathSkip.Create();
            if (Config.Bind("Gameplay", "Console", true, "Backup console (` or F12): give items/keys, karma, Idea. Use if logic is wrong or you are stuck.").Value)
                DebugConsole.Create();

            EnemyScaling.Enabled = Config.Bind("Enemies", "ScaleToAreaTier", true,
                "Scale randomized enemies (HP, damage, Idea reward) towards the average of the area's original enemies.").Value;
            EnemyScaling.Strength = Mathf.Clamp01(Config.Bind("Enemies", "ScalingStrength", 0.8f,
                "1 = every enemy in an area gets exactly the area's average stats; lower keeps more of each enemy's own strength (0 = no scaling).").Value);
            EnemyScaling.Load();
            foreach (var s in Config.Bind("Enemies", "ExcludeScenes", "",
                "Comma-separated scenes whose spawners keep vanilla enemies (event scenes EV*/Ev* are always excluded).").Value.Split(','))
                if (s.Trim().Length > 0) EnemyPool.ExcludedScenes.Add(s.Trim());
            DoubleHitFix.Enabled = Config.Bind("Gameplay", "FixDoubleHit", true,
                "Fix the vanilla bug where one swing can hit an enemy twice.").Value;

            Gameplay.DialogueSkip.Enabled = Config.Bind("Gameplay", "DialogueSkip", true,
                "Hold Left Ctrl (or the gamepad Step button) to fast-forward dialogue. Stops at choices.").Value;
            Gameplay.DialogueSkip.Key = Config.Bind("Gameplay", "DialogueSkipKey", KeyCode.LeftControl, "Keyboard key for dialogue fast-forward.").Value;
            Gameplay.GrabQte.QteEnabled = Config.Bind("Gameplay", "GrabQte", true,
                "When grabbed, enter 4-6 prompted inputs to break free (replaces vanilla mashing). A wrong input costs HP.").Value;
            Gameplay.GrabQte.Keys = Config.Bind("Gameplay", "GrabQteKeys", Gameplay.GrabQte.KeySet.FullKeyboard,
                "FullKeyboard: letters A-Z. MovementAttack: Up/Down/Left/Right/Attack (also works on a gamepad).").Value;
            Gameplay.GrabQte.WrongInputDamage = Mathf.Clamp01(Config.Bind("Gameplay", "GrabQteWrongDamage", 0.10f,
                "Share of max HP lost on a wrong QTE input.").Value);
            Gameplay.GrabQte.LockSeconds = Mathf.Max(0f, Config.Bind("Gameplay", "KnockdownLockSeconds", 3f,
                "After a non-lethal knockdown Aradia cannot get up for this many seconds (gives enemies a chance to grab). 0 = vanilla.").Value);
            Gameplay.GrabQte.RushMultiplier = Mathf.Max(1f, Config.Bind("Gameplay", "DownedRushSpeed", 1.8f,
                "Movement speed multiplier of enemies walking to a downed Aradia to grab her. 1 = vanilla.").Value);
            Gameplay.GrabQte.Create();

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Patches.RewardPatches.Apply(harmony);
            Gameplay.DialogueSkip.Apply(harmony);
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static ConfigEntry<bool> _bossDrops, _npcRewards, _trades;
        private static ConfigEntry<bool> _shopEnabled;
        private static ConfigEntry<int> _shopSlots;
        private static ConfigEntry<float> _shopPrice;
        private static ConfigEntry<KeyCode> _shopKey;
        internal static bool ShopEnabled { get { return _shopEnabled.Value; } }
        internal static float ShopPriceMultiplier { get { return Mathf.Max(0.01f, _shopPrice.Value); } }
        internal static KeyCode ShopKey { get { return _shopKey.Value; } }

        private static ConfigEntry<bool> _enemies, _verboseEnemies;
        private static ConfigEntry<string> _enemyExclude;
        internal static bool VerboseEnemyLog { get { return _verboseEnemies.Value; } }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Build the enemy pool once, at the first title screen.
            if (scene.name != "Gametitle" || EnemyPool.Instance != null || !_enemies.Value) return;
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in _enemyExclude.Value.Split(',')) if (n.Trim().Length > 0) excluded.Add(n.Trim());
            foreach (var n in EnemyPool.DefaultExcludedPrefabs) excluded.Add(n);
            foreach (var n in EnemyPool.KeepVanillaAtSpawn) excluded.Add(n);
            var pool = EnemyPool.Create(excluded);
            var plan = EnemyPool.PlanScenes(EnemyPool.LoadCatalog(), excluded);
            Log.LogInfo("Enemy pool: loading " + plan.Count + " scenes: " + string.Join(", ", plan.ToArray()));
            pool.StartCoroutine(pool.Preload(plan));
        }

        internal static bool PoolBusy { get { return EnemyPool.Instance != null && EnemyPool.Instance.Loading; } }

        /// <summary>
        /// Set by NoR.Archipelago: returns an Archipelago state while connected at the title screen, otherwise null
        /// (then the offline randomizer settings apply).
        /// </summary>
        public static Func<RandoState> NewGameStateProvider;

        /// <summary>Raised after a state is activated (new game or loaded save).</summary>
        public static event Action<RandoState> StateActivated;

        /// <summary>Called when the player starts a new game from the title menu.</summary>
        internal static void OnNewGame()
        {
            var remote = NewGameStateProvider != null ? NewGameStateProvider() : null;
            if (remote != null) { Activate(remote); return; }
            if (!_enabled.Value) { State = null; Log.LogInfo("New game: randomizer disabled in config"); return; }

            var st = new RandoState
            {
                ShuffleFloorItems = _floor.Value,
                ShuffleChests = _chests.Value,
                ShuffleMagic = _magic.Value,
                RandomizeEnemies = _enemies.Value,
                ShopSlots = _shopEnabled.Value ? Mathf.Clamp(_shopSlots.Value, 0, LocationTable.MaxShopSlots) : 0,
                ShuffleBossDrops = _bossDrops.Value,
                ShuffleNpcRewards = _npcRewards.Value,
                ShuffleTrades = _trades.Value,
            };
            ulong seed;
            st.Seed = ulong.TryParse(_seed.Value.Trim(), out seed) ? seed : RandoState.NewSeed();
            Activate(st);
            if (_spoiler.Value) WriteSpoiler(st);
        }

        /// <summary>Called before a save slot is loaded. data == null means a vanilla save.</summary>
        internal static void OnLoadGame(string data)
        {
            if (string.IsNullOrEmpty(data)) { State = null; Log.LogInfo("Loaded vanilla save: randomizer inactive"); return; }
            try
            {
                Activate(RandoState.Parse(data));
            }
            catch (Exception e)
            {
                State = null;
                Log.LogError("Could not read randomizer data from save (" + data + "): " + e.Message);
            }
        }

        private static void Activate(RandoState st)
        {
            st.Placement = st.Remote ? Placement.GenerateRemote(st, Table) : Placement.Generate(st, Table);
            State = st;
            Log.LogInfo("Randomizer active: " + st + ", " + st.Placement.Count + " shuffled locations");
            Instance()._bannerUntil = Time.unscaledTime + 8f;
            if (StateActivated != null)
            {
                try { StateActivated(st); }
                catch (Exception e) { Log.LogError("StateActivated handler failed: " + e); }
            }
        }

        private static void WriteSpoiler(RandoState st)
        {
            try
            {
                Directory.CreateDirectory(_outDir);
                var sb = new StringBuilder();
                sb.AppendLine("NoR Randomizer spoiler log - " + st);
                sb.AppendLine();
                string scene = null;
                foreach (var l in Table.All)
                {
                    string item;
                    if (!st.Placement.TryGetValue(l.Key, out item)) continue;
                    if (l.Scene != scene) { scene = l.Scene; sb.AppendLine().AppendLine("[" + scene + "]"); }
                    sb.AppendLine("  " + l.Key + "  ->  " + item + "  (" + Describe(item) + ")   [vanilla: " + NoR.Core.TextHooks.T(l.DisplayJp) + "]");
                }
                File.WriteAllText(Path.Combine(_outDir, "spoiler_" + st.Seed + ".txt"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Log.LogWarning("Spoiler log failed: " + e.Message);
            }
        }

        /// <summary>Human-readable name for an item code (Japanese until the translation milestone).</summary>
        internal static string Describe(string code)
        {
            var parts = new List<string>();
            foreach (var part in code.Split('+'))
            {
                if (part.StartsWith("magic:", StringComparison.Ordinal))
                {
                    var src = Table.MagicSource(part);
                    parts.Add(src != null ? NoR.Core.TextHooks.T(src.DisplayJp) : part);
                    continue;
                }
                string name = null;
                foreach (var l in Table.All)
                {
                    if (l.Vanilla == part) { name = NoR.Core.TextHooks.T(l.DisplayJp); break; }
                }
                parts.Add(name ?? part);
            }
            return string.Join(" + ", parts.ToArray());
        }

        private static Plugin _instance;
        private static Plugin Instance() { return _instance; }
        private void OnEnable() { _instance = this; }

        private void OnGUI()
        {
            if (State != null && ShopEnabled && Shop.ShopWindow.Current == null && Patches.SavepointShopPatch.AtSavepointNow)
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 50), alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, Screen.height * 0.78f, Screen.width, 40), "[" + ShopKey + " / Item-switch]  Witch's Market", style);
            }
            if (State == null || Time.unscaledTime > _bannerUntil) return;
            GUI.Label(new Rect(8, 8, 600, 24), "NoR Randomizer active - " + State);
        }
    }
}
