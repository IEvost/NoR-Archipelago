using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;

namespace NoR.Archipelago
{
    /// <summary>
    /// Connection to an Archipelago server. Unity 5.6's Mono cannot do TLS 1.2+, so remote servers are reached through
    /// NoR.ApBridge.exe (plain ws:// to 127.0.0.1, TLS from the bridge to the server). Local servers are used directly.
    /// All network work runs on a background thread; results are delivered through a main-thread queue.
    /// </summary>
    public sealed class Connection : IDisposable
    {
        public enum BridgeMode { Auto, Always, Never }

        public ArchipelagoSession Session { get; private set; }
        public bool Connecting { get; private set; }
        public string Status = "Not connected";

        private Process _bridge;
        private readonly Queue<Action> _mainThread = new Queue<Action>();
        private readonly string _pluginDir;

        public Connection(string pluginDir)
        {
            _pluginDir = pluginDir;
        }

        /// <summary>Runs queued callbacks; call from Update().</summary>
        public void Pump()
        {
            while (true)
            {
                Action a;
                lock (_mainThread)
                {
                    if (_mainThread.Count == 0) return;
                    a = _mainThread.Dequeue();
                }
                try { a(); }
                catch (Exception e) { Plugin.Log.LogError("Callback failed: " + e); }
            }
        }

        public void Post(Action a)
        {
            lock (_mainThread) _mainThread.Enqueue(a);
        }

        /// <param name="server">host:port, optionally with ws:// or wss:// prefix</param>
        public void ConnectAsync(string server, string game, string slot, string password, string[] tags,
            ItemsHandlingFlags flags, BridgeMode bridgeMode, Action<LoginResult> done)
        {
            if (Connecting) return;
            Connecting = true;
            Status = "Connecting to " + server + "...";
            var t = new Thread(() =>
            {
                LoginResult result;
                try
                {
                    string url = ResolveUrl(server, bridgeMode);
                    Plugin.Log.LogInfo("Connecting: " + url + " (server " + server + ")");
                    var session = ArchipelagoSessionFactory.CreateSession(new Uri(url));
                    result = session.TryConnectAndLogin(game, slot, flags, null, tags, null,
                        string.IsNullOrEmpty(password) ? null : password, true);
                    if (result.Successful) Session = session;
                }
                catch (Exception e)
                {
                    result = new LoginFailure(e.GetType().Name + ": " + e.Message);
                }
                Post(() =>
                {
                    Connecting = false;
                    Status = result.Successful ? "Connected to " + server + " as " + slot : "Connection failed";
                    done(result);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private string ResolveUrl(string server, BridgeMode mode)
        {
            string s = server.Trim();
            bool explicitWs = s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase);
            bool explicitWss = s.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
            if (explicitWs) s = s.Substring(5);
            if (explicitWss) s = s.Substring(6);
            s = s.TrimEnd('/');
            if (s.IndexOf(':') < 0) s += ":38281";
            string host = s.Substring(0, s.LastIndexOf(':'));

            // The bridge also adds WebSocket compression (permessage-deflate), which the game's websocket-sharp lacks
            // and Archipelago servers may require in the future - so every connection goes through it unless disabled.
            bool useBridge = mode != BridgeMode.Never || explicitWss;
            if (!useBridge) return "ws://" + s;

            int port = StartBridge(s, explicitWss ? "on" : (explicitWs ? "off" : "auto"));
            return "ws://127.0.0.1:" + port;
        }

        /// <summary>Starts NoR.ApBridge.exe for the target and returns its local port.</summary>
        private int StartBridge(string target, string tls, string mode = "ws")
        {
            StopBridge();
            string exe = Path.Combine(_pluginDir, "NoR.ApBridge.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("NoR.ApBridge.exe missing next to the plugin", exe);

            var psi = new ProcessStartInfo(exe,
                "--target " + target + " --tls " + tls + " --mode " + mode + " --listen 0 --parent-pid " + Process.GetCurrentProcess().Id)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _bridge = Process.Start(psi);
            string line = _bridge.StandardOutput.ReadLine();
            if (line == null || !line.StartsWith("LISTENING ", StringComparison.Ordinal))
                throw new IOException("Bridge did not start: " + (line ?? _bridge.StandardError.ReadToEnd()));
            var bridge = _bridge;
            var errThread = new Thread(() =>
            {
                try
                {
                    string l;
                    while ((l = bridge.StandardError.ReadLine()) != null) Plugin.Log.LogInfo(l);
                }
                catch { /* bridge exited */ }
            }) { IsBackground = true };
            errThread.Start();
            return int.Parse(line.Substring(10).Trim());
        }

        /// <summary>Starts the bridge with TLS forced on (diagnostics only); returns the local port.</summary>
        public int StartBridgeForDiagnostics(string target)
        {
            return StartBridge(target, "on", "tcp"); // raw HTTP test needs the byte tunnel, not the WebSocket proxy
        }

        private void StopBridge()
        {
            if (_bridge == null) return;
            try { if (!_bridge.HasExited) _bridge.Kill(); } catch { }
            _bridge = null;
        }

        public void Disconnect()
        {
            // net35 build of the library has only the synchronous Disconnect().
            try { if (Session != null && Session.Socket.Connected) Session.Socket.Disconnect(); } catch { }
            Session = null;
            Status = "Not connected";
            StopBridge();
        }

        public void Dispose() { Disconnect(); }
    }
}
