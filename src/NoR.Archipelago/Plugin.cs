using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.Archipelago
{
    /// <summary>
    /// Archipelago client.
    ///   Title screen: connection window (F1 toggles). Connect, then start a NEW GAME (new slot run) or LOAD the
    ///   save of that slot (it reconnects automatically to the stored server/slot).
    ///   F7: connection test with the [Test] settings (TextOnly login, no items).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("nor.archipelago.randomizer")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nor.archipelago.client";
        public const string Name = "NoR Archipelago Client";
        public const string Version = "0.2.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Plugin Instance;

        private static ConfigEntry<string> _server, _slot, _password, _testGame, _testServer, _testSlot;
        private static ConfigEntry<Connection.BridgeMode> _bridge;
        private static ConfigEntry<bool> _diagnostics;
        internal static Connection.BridgeMode BridgeMode { get { return _bridge.Value; } }
        internal static string Password { get { return _password.Value; } }
        private static ConfigEntry<bool> _serverMessages;
        internal static bool ShowServerMessages { get { return _serverMessages.Value; } }

        private Connection _conn;
        internal ApClient Client;
        private readonly Notifications _notes = new Notifications();
        private bool _showWindow = true;
        private Rect _window = new Rect(0, 0, 0, 0);
        private string _uiServer, _uiSlot, _uiPassword, _uiChat;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            _server = Config.Bind("Connection", "Server", "archipelago.gg:38281", "host:port of the Archipelago server (prefix ws:// or wss:// to force a protocol).");
            _slot = Config.Bind("Connection", "Slot", "", "Slot (player) name.");
            _password = Config.Bind("Connection", "Password", "", "Room password, if any.");
            _bridge = Config.Bind("Connection", "Bridge", Connection.BridgeMode.Auto,
                "Auto/Always: connect through NoR.ApBridge (TLS + WebSocket compression). Never: direct plain ws:// connection (no TLS, no compression; local servers only).");
            _testServer = Config.Bind("Test", "Server", "127.0.0.1:38281", "Server used by the F7 connection test.");
            _testSlot = Config.Bind("Test", "Slot", "Tester", "Slot used by the F7 connection test.");
            _testGame = Config.Bind("Test", "Game", "ChecksFinder",
                "Game name of the slot used by the F7 connection test (any existing slot works; the test logs in as TextOnly).");
            _serverMessages = Config.Bind("Display", "ShowServerMessages", true, "Show chat, hints and server notices on screen.");
            _diagnostics = Config.Bind("Test", "DiagnosticsOnStart", false,
                "At startup, check the runtime's own TLS support and the bridge against archipelago.gg (writes results to the log).");

            _uiServer = _server.Value;
            _uiSlot = _slot.Value;
            _uiPassword = _password.Value;

            _conn = new Connection(Path.GetDirectoryName(Info.Location));
            Client = new ApClient(_conn, ApMap.LoadEmbedded(), _notes, Path.GetDirectoryName(Info.Location));
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            if (_diagnostics.Value) RunDiagnostics();
        }

        private void Update()
        {
            _conn.Pump();
            Client.Update();
            if (Input.GetKeyDown(KeyCode.F7)) ConnectionTest();
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (AtTitle) _showWindow = !_showWindow;
                else SetInGameWindow(!_showInGame);
            }
            if (AtTitle && _showInGame) SetInGameWindow(false);
        }

        // In game the window is hidden by default (F1 opens it, e.g. to reconnect after the room changed its port).
        private bool _showInGame, _lockedControl;

        private void SetInGameWindow(bool open)
        {
            _showInGame = open;
            var st = NoR.Core.GameRefs.Status;
            if (open)
            {
                if (st != null && st._SOUSA) { st._SOUSA = false; st._SOUSAMNG = false; _lockedControl = true; }
            }
            else if (_lockedControl)
            {
                if (st != null) { st._SOUSA = true; st._SOUSAMNG = true; }
                _lockedControl = false;
            }
        }

        private static bool AtTitle { get { return SceneManager.GetActiveScene().name == "Gametitle"; } }

        // ------------------------------------------------------------------ UI

        private void OnGUI()
        {
            _notes.Draw();
            Client.DeathLink.Draw();
            if (AtTitle ? !_showWindow : !_showInGame) return;
            float w = Mathf.Min(520f, Screen.width * 0.4f);
            if (_window.width == 0) _window = new Rect(Screen.width - w - 20, 20, w, 10);
            _window = GUILayout.Window(0x4E6F52, _window, DrawWindow, AtTitle ? "Archipelago (F1 to hide)" : "Archipelago (F1 to close)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("Status: " + _conn.Status);
            GUI.enabled = !_conn.Connecting && !Client.Connected;
            GUILayout.Label("Server (host:port)");
            _uiServer = GUILayout.TextField(_uiServer ?? string.Empty);
            GUILayout.Label("Slot name");
            _uiSlot = GUILayout.TextField(_uiSlot ?? string.Empty);
            GUILayout.Label("Password");
            _uiPassword = GUILayout.PasswordField(_uiPassword ?? string.Empty, '*');
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            if (!Client.Connected)
            {
                GUI.enabled = !_conn.Connecting && _uiServer.Trim().Length > 0 && _uiSlot.Trim().Length > 0;
                if (GUILayout.Button("Connect"))
                {
                    _server.Value = _uiServer.Trim();
                    _slot.Value = _uiSlot.Trim();
                    _password.Value = _uiPassword;
                    Client.Connect(_server.Value, _slot.Value, _password.Value);
                }
                GUI.enabled = true;
            }
            else if (GUILayout.Button("Disconnect"))
            {
                Client.Disconnect();
            }
            GUILayout.EndHorizontal();

            if (Client.Connected)
            {
                GUILayout.Label("Chat / command (e.g. !hint Pilgrim's Path Key)");
                GUILayout.BeginHorizontal();
                _uiChat = GUILayout.TextField(_uiChat ?? string.Empty);
                bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                if ((GUILayout.Button("Send", GUILayout.Width(70)) || enter) && _uiChat.Trim().Length > 0)
                {
                    Client.Say(_uiChat.Trim());
                    _uiChat = string.Empty;
                }
                GUILayout.EndHorizontal();
            }
            if (AtTitle)
                GUILayout.Label(Client.Connected
                    ? "Connected. NEW GAME starts a run for this slot; LOAD continues a saved run of it."
                    : "Not connected: New Game uses the offline randomizer settings.");
            else
                GUILayout.Label("Reconnect here if the room moved to a new port. The save accepts any address of the same seed and slot.");
            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ test & diagnostics

        private void ConnectionTest()
        {
            string slot = _testSlot.Value;
            _notes.Add("Connection test: " + _testServer.Value + " as " + slot + " ...", Notifications.Kind.Info);
            var test = new Connection(Path.GetDirectoryName(Info.Location));
            test.ConnectAsync(_testServer.Value, _testGame.Value, slot, _password.Value, new[] { "TextOnly", "NoR-Test" },
                ItemsHandlingFlags.NoItems, _bridge.Value, result =>
                {
                    var ok = result as LoginSuccessful;
                    if (ok != null) _notes.Add("Connection test OK (slot " + ok.Slot + ", " + test.Session.Locations.AllLocations.Count + " locations)", Notifications.Kind.Received);
                    else _notes.Add("Connection test FAILED: " + string.Join("; ", ((LoginFailure)result).Errors), Notifications.Kind.Error);
                    test.Dispose();
                });
            // The test connection posts its callback to its own queue; pump it from here until done.
            StartCoroutine(PumpTest(test));
        }

        private System.Collections.IEnumerator PumpTest(Connection test)
        {
            float until = Time.unscaledTime + 60f;
            while (Time.unscaledTime < until)
            {
                test.Pump();
                if (!test.Connecting && test.Status != "Connecting to " + _testServer.Value + "...") yield break;
                yield return null;
            }
        }

        /// <summary>Background checks written to the log: native TLS of this runtime, bridge TLS.</summary>
        private void RunDiagnostics()
        {
            string dir = Path.GetDirectoryName(Info.Location);
            var t = new Thread(() =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://archipelago.gg/");
                    req.Method = "HEAD";
                    req.Timeout = 8000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                        Log.LogInfo("[diag] native TLS to archipelago.gg: OK (" + (int)resp.StatusCode + ")");
                }
                catch (Exception e)
                {
                    Log.LogInfo("[diag] native TLS to archipelago.gg: FAILED (" + e.GetType().Name + ": " + e.Message + ") -> bridge required");
                }
                try
                {
                    using (var c = new Connection(dir))
                    {
                        int port = c.StartBridgeForDiagnostics("archipelago.gg:443");
                        using (var tcp = new TcpClient("127.0.0.1", port))
                        {
                            var s = tcp.GetStream();
                            var req = Encoding.ASCII.GetBytes("HEAD / HTTP/1.1\r\nHost: archipelago.gg\r\nConnection: close\r\n\r\n");
                            s.Write(req, 0, req.Length);
                            s.ReadTimeout = 8000;
                            var buf = new byte[256];
                            int n = s.Read(buf, 0, buf.Length);
                            Log.LogInfo("[diag] bridge TLS to archipelago.gg: " + Encoding.ASCII.GetString(buf, 0, n).Split('\r')[0]);
                        }
                    }
                }
                catch (Exception e) { Log.LogError("[diag] bridge test FAILED: " + e); }
            }) { IsBackground = true };
            t.Start();
        }

        private void OnDestroy()
        {
            if (_conn != null) _conn.Dispose();
        }
    }

    /// <summary>Goal: the Witch in the Thorns' final form dies (OriginIbaranoMajyo.OnDestroy in state DEATH).</summary>
    [HarmonyPatch(typeof(OriginIbaranoMajyo), "OnDestroy")]
    internal static class GoalPatch
    {
        private static void Prefix(OriginIbaranoMajyo __instance)
        {
            try
            {
                var state = Traverse.Create(__instance).Field("state").GetValue();
                if (state != null && state.ToString() == "DEATH") Plugin.Instance.Client.ReportGoal();
            }
            catch (Exception e) { Plugin.Log.LogError("Goal detection failed: " + e); }
        }
    }
}
