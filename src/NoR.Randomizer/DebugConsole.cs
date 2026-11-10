using System;
using System.Collections.Generic;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer
{
    /// <summary>
    /// Backup console for when logic is wrong or a player is stuck (toggle with the backquote key ` / F12).
    /// Commands:
    ///   help
    ///   give &lt;code&gt; [count]      item codes: weapon:N, crestring:N, UseItem:N, magic:School:N, costume:N
    ///   give key lab|pilgrimage|rooftop
    ///   find &lt;text&gt;               search weapons/rings/items/magic by (English) name, prints their codes
    ///   karma &lt;n&gt;                add karma (can be negative)
    ///   idea &lt;n&gt;                 add Idea
    ///   state                     randomizer state of this save
    /// Every command is logged to BepInEx/LogOutput.log.
    /// </summary>
    public sealed class DebugConsole : MonoBehaviour
    {
        public static KeyCode ToggleKey = KeyCode.BackQuote;

        private bool _open;
        private string _input = string.Empty;
        private readonly List<string> _lines = new List<string>();
        private Vector2 _scroll;
        private bool _focus;

        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "lab", "UseItem:60" }, { "pilgrimage", "UseItem:61" }, { "rooftop", "UseItem:62" },
        };

        public static void Create()
        {
            var go = new GameObject("NoR.DebugConsole");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugConsole>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey) || Input.GetKeyDown(KeyCode.F12)) SetOpen(!_open);
        }

        private bool _lockedControl;

        /// <summary>Typing must not move Aradia: player control is locked while the console is open.</summary>
        private void SetOpen(bool open)
        {
            _open = open;
            _focus = open;
            var st = GameRefs.Status;
            if (open)
            {
                if (_lines.Count == 0) Print("NoR console - type 'help'. Close with ` or F12.");
                if (st != null && st._SOUSA) { st._SOUSA = false; st._SOUSAMNG = false; _lockedControl = true; }
            }
            else if (_lockedControl)
            {
                if (st != null) { st._SOUSA = true; st._SOUSAMNG = true; }
                _lockedControl = false;
            }
        }

        private void OnGUI()
        {
            if (!_open) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                Run(_input.Trim());
                _input = string.Empty;
                e.Use();
            }
            // Toggling is handled in Update(); just keep the toggle character out of the input field.
            if (e.type == EventType.KeyDown && (e.keyCode == ToggleKey || e.character == '`')) { e.Use(); }

            float w = Mathf.Min(900f, Screen.width - 40f), h = Mathf.Min(420f, Screen.height * 0.45f);
            GUILayout.BeginArea(new Rect(20, 20, w, h), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var l in _lines) GUILayout.Label(l);
            GUILayout.EndScrollView();
            GUI.SetNextControlName("NoRConsoleInput");
            _input = GUILayout.TextField(_input ?? string.Empty);
            GUILayout.EndArea();
            if (_focus) { GUI.FocusControl("NoRConsoleInput"); _focus = false; }
        }

        private void Print(string s)
        {
            _lines.Add(s);
            if (_lines.Count > 200) _lines.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Run(string cmd)
        {
            if (cmd.Length == 0) return;
            Print("> " + cmd);
            Plugin.Log.LogInfo("[console] " + cmd);
            var a = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                switch (a[0].ToLowerInvariant())
                {
                    case "help":
                        Print("give <code> [count] | give key lab|pilgrimage|rooftop | find <text> | karma <n> | idea <n> | state");
                        Print("codes: weapon:N  crestring:N  UseItem:N  magic:School:N (GUN/Fire/Lightning/Sacred/darkness)  costume:N");
                        break;
                    case "give": Give(a); break;
                    case "find": Find(cmd.Substring(4).Trim()); break;
                    case "karma":
                        if (!InGame()) return;
                        GameRefs.Status._karma = int.Parse(a[1]);
                        Print("Karma now " + GameRefs.Status.KarmaCount);
                        break;
                    case "idea":
                        if (!InGame()) return;
                        GameRefs.Status.Exppoint += int.Parse(a[1]);
                        Print("Idea now " + GameRefs.Status.Exppoint);
                        break;
                    case "state":
                        Print(Plugin.State != null ? Plugin.State.ToString() : "vanilla game (no randomizer state)");
                        break;
                    default:
                        Print("Unknown command. Type 'help'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Print("Error: " + ex.Message);
            }
        }

        private void Give(string[] a)
        {
            if (!InGame()) return;
            if (a.Length < 2) { Print("give <code> [count] | give key <lab|pilgrimage|rooftop>"); return; }
            string code = a[1];
            int count = 1;
            if (code.Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                if (a.Length < 3 || !Keys.TryGetValue(a[2], out code)) { Print("keys: lab, pilgrimage, rooftop"); return; }
            }
            else if (a.Length > 2) count = Mathf.Clamp(int.Parse(a[2]), 1, 99);
            for (int i = 0; i < count; i++) ItemGranter.Grant(code, Plugin.Table);
            Print("Given " + code + (count > 1 ? " x" + count : string.Empty));
        }

        private void Find(string text)
        {
            if (!InGame()) return;
            var inv = GameRefs.Inventory;
            int n = 0;
            for (int i = 0; i < inv._WP.Count && n < 30; i++) if (Match(inv._WP[i].Itemname, text)) { Print("weapon:" + i + "  " + inv._WP[i].Itemname); n++; }
            for (int i = 0; i < inv._RING.Count && n < 30; i++) if (Match(inv._RING[i].Itemname, text)) { Print("crestring:" + i + "  " + inv._RING[i].Itemname); n++; }
            for (int i = 0; i < inv._USE.Count && n < 30; i++) if (Match(inv._USE[i].Itemname, text)) { Print("UseItem:" + i + "  " + inv._USE[i].Itemname); n++; }
            foreach (var l in Plugin.Table.All)
                if (l.Category == "magic" && n < 30 && Match(l.MagicName + " " + l.DisplayJp, text)) { Print(l.Vanilla + "  " + l.MagicName); n++; }
            if (n == 0) Print("No match.");
        }

        private static bool Match(string name, string text)
        {
            return name != null && name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool InGame()
        {
            if (GameRefs.InGame) return true;
            Print("Only available in game.");
            return false;
        }
    }
}
