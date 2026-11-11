using System;
using System.Collections.Generic;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer
{
    /// <summary>
    /// F12 / backquote panel with two tabs:
    ///   Server log - history of everything the Archipelago server reported this session (items sent between all
    ///                players, joins/leaves, chat, hints, goals, releases) plus the client's own notices. The input line
    ///                sends chat and server commands (!hint, !release, ...). Filled by NoR.Archipelago via AppendLog.
    ///   Console    - backup commands for when logic is wrong or a player is stuck:
    ///                help | give &lt;code&gt; [count] | give key lab|pilgrimage|rooftop | find &lt;text&gt; | karma &lt;n&gt; | idea &lt;n&gt; | state
    /// Player control is locked while the panel is open. Commands are logged to BepInEx/LogOutput.log.
    /// </summary>
    public sealed class DebugConsole : MonoBehaviour
    {
        public static KeyCode ToggleKey = KeyCode.BackQuote;

        // ------------------------------------------------------------------ server log API (used by NoR.Archipelago)

        private sealed class LogEntry
        {
            public string Rich;      // Unity rich text (color tags)
            public bool AboutMe;     // involves the local slot
        }

        private static readonly List<LogEntry> ServerLog = new List<LogEntry>();
        private const int MaxServerLog = 1000;
        private static bool _logChanged;

        /// <summary>Sends chat/commands typed in the Server log tab; set by NoR.Archipelago. Returns false when offline.</summary>
        public static Func<string, bool> ChatSender;

        /// <summary>Adds a line to the Server log tab. richText may contain &lt;color=#rrggbb&gt; tags.</summary>
        public static void AppendLog(string richText, bool aboutMe)
        {
            ServerLog.Add(new LogEntry { Rich = "<color=#888888>" + DateTime.Now.ToString("HH:mm:ss") + "</color>  " + richText, AboutMe = aboutMe });
            if (ServerLog.Count > MaxServerLog) ServerLog.RemoveAt(0);
            _logChanged = true;
        }

        // ------------------------------------------------------------------ UI state

        private enum Tab { ServerLog, Console }

        private bool _open;
        private Tab _tab = Tab.ServerLog;
        private string _input = string.Empty;
        private string _chatInput = string.Empty;
        private readonly List<string> _lines = new List<string>();
        private Vector2 _scroll, _logScroll;
        private bool _focus, _onlyMe, _autoScroll = true;
        private GUIStyle _logStyle;

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

        /// <summary>Typing must not move Aradia: player control is locked while the panel is open.</summary>
        private void SetOpen(bool open)
        {
            _open = open;
            _focus = open;
            if (open) _autoScroll = true;
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
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (enter)
            {
                if (_tab == Tab.Console) { Run(_input.Trim()); _input = string.Empty; }
                else SendChat();
                e.Use();
            }
            // Toggling is handled in Update(); just keep the toggle character out of the input fields.
            if (e.type == EventType.KeyDown && (e.keyCode == ToggleKey || e.character == '`')) { e.Use(); }

            if (_logStyle == null) _logStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

            float w = Mathf.Min(1100f, Screen.width - 40f), h = Mathf.Min(560f, Screen.height * 0.6f);
            GUILayout.BeginArea(new Rect(20, 20, w, h), GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == Tab.ServerLog, "Server log", GUI.skin.button, GUILayout.Width(140))) _tab = Tab.ServerLog;
            if (GUILayout.Toggle(_tab == Tab.Console, "Console", GUI.skin.button, GUILayout.Width(140))) _tab = Tab.Console;
            GUILayout.FlexibleSpace();
            if (_tab == Tab.ServerLog)
            {
                _onlyMe = GUILayout.Toggle(_onlyMe, " only about me", GUILayout.Width(130));
                _autoScroll = GUILayout.Toggle(_autoScroll, " follow", GUILayout.Width(80));
            }
            GUILayout.Label("F12 / ` to close", GUILayout.Width(110));
            GUILayout.EndHorizontal();

            if (_tab == Tab.ServerLog) DrawServerLog(); else DrawConsole();
            GUILayout.EndArea();
        }

        private void DrawServerLog()
        {
            if (_logChanged && _autoScroll) _logScroll.y = float.MaxValue;
            _logChanged = false;
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            if (ServerLog.Count == 0) GUILayout.Label("No server messages yet. Connect to an Archipelago room (F1).");
            // Only the newest 400 entries are drawn (IMGUI cost); the file log keeps everything.
            int from = Mathf.Max(0, ServerLog.Count - 400);
            for (int i = from; i < ServerLog.Count; i++)
            {
                if (_onlyMe && !ServerLog[i].AboutMe) continue;
                GUILayout.Label(ServerLog[i].Rich, _logStyle);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("NoRChatInput");
            _chatInput = GUILayout.TextField(_chatInput ?? string.Empty);
            if (GUILayout.Button("Send", GUILayout.Width(70))) SendChat();
            GUILayout.EndHorizontal();
            if (_focus) { GUI.FocusControl("NoRChatInput"); _focus = false; }
        }

        private void SendChat()
        {
            string text = (_chatInput ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (ChatSender == null || !ChatSender(text)) AppendLog("<color=#ff8080>Not connected - message not sent.</color>", true);
            _chatInput = string.Empty;
            _autoScroll = true;
        }

        private void DrawConsole()
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var l in _lines) GUILayout.Label(l);
            GUILayout.EndScrollView();
            GUI.SetNextControlName("NoRConsoleInput");
            _input = GUILayout.TextField(_input ?? string.Empty);
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
                if (l.Category == "magic" && n < 30 && Match(l.MagicName + " " + TextHooks.T(l.DisplayJp), text)) { Print(l.Vanilla + "  " + TextHooks.T(l.DisplayJp)); n++; }
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
