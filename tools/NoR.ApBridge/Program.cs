using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace NoR.ApBridge;

/// <summary>
/// Connection bridge for the in-game Archipelago client.
///
/// Night of Revenge runs on Unity 5.6's old Mono runtime: no TLS 1.2, and its WebSocket library (websocket-sharp)
/// has no permessage-deflate compression. archipelago.gg rooms only accept TLS (wss://), and the Archipelago server
/// warns that uncompressed clients "may stop working in the future". The plugin starts this process and connects
/// to it with plain ws://127.0.0.1:&lt;port&gt;.
///
/// Mode "ws" (default): WebSocket proxy. The game's connection is terminated here; a modern ClientWebSocket with
/// permessage-deflate (and TLS when available) connects to the server; whole messages are forwarded both ways.
/// Mode "tcp": raw byte tunnel (TLS wrapping only) - fallback, no compression.
///
/// Usage: NoR.ApBridge --target host:port [--tls auto|on|off] [--mode ws|tcp] [--listen 0] [--parent-pid N]
///   --listen 0     pick a free port; the chosen port is printed to stdout as "LISTENING &lt;port&gt;"
///   --tls auto     try TLS first, fall back to plain (same behavior as the official AP clients)
///   --parent-pid   exit when the game process exits
/// </summary>
internal static class Program
{
    private static string _host = "";
    private static int _port;
    private static string _tlsMode = "auto";
    private static string _mode = "ws";
    private static bool? _useTls; // resolved by the first successful auto-detection

    private static async Task<int> Main(string[] args)
    {
        int listenPort = 0, parentPid = 0;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--target":
                    var t = args[i + 1];
                    int c = t.LastIndexOf(':');
                    if (c <= 0 || !int.TryParse(t[(c + 1)..], out _port)) return Fail("bad --target, expected host:port");
                    _host = t[..c];
                    break;
                case "--tls": _tlsMode = args[i + 1].ToLowerInvariant(); break;
                case "--mode": _mode = args[i + 1].ToLowerInvariant(); break;
                case "--listen": listenPort = int.Parse(args[i + 1]); break;
                case "--parent-pid": parentPid = int.Parse(args[i + 1]); break;
            }
        }
        if (_host.Length == 0) return Fail("missing --target");
        if (_tlsMode == "on") _useTls = true;
        if (_tlsMode == "off") _useTls = false;

        var listener = new TcpListener(IPAddress.Loopback, listenPort);
        listener.Start();
        int actual = ((IPEndPoint)listener.LocalEndpoint).Port;
        Console.WriteLine("LISTENING " + actual);
        Console.Out.Flush();
        Log($"mode {_mode}, target {_host}:{_port}, tls {_tlsMode}");

        if (parentPid > 0) _ = WatchParent(parentPid);

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = _mode == "tcp" ? HandleTcp(client) : HandleWebSocket(client);
        }
    }

    // ================================================================== WebSocket proxy (default)

    private static async Task HandleWebSocket(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            WebSocket? game = null;
            ClientWebSocket? server = null;
            try
            {
                var stream = client.GetStream();
                game = await AcceptGameWebSocket(stream);
                server = await ConnectServer();
                using var cts = new CancellationTokenSource();
                var up = Pump(game, server, "game->server", cts);
                var down = Pump(server, game, "server->game", cts);
                await Task.WhenAny(up, down);
                cts.Cancel();
            }
            catch (Exception e)
            {
                Log("connection failed: " + e.Message);
            }
            finally
            {
                await CloseQuietly(server);
                await CloseQuietly(game);
                server?.Dispose();
                game?.Dispose();
            }
        }
    }

    /// <summary>Minimal RFC 6455 server handshake for the local game connection (no compression offered).</summary>
    private static async Task<WebSocket> AcceptGameWebSocket(NetworkStream stream)
    {
        var header = new StringBuilder();
        var buf = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            int n = await stream.ReadAsync(buf.AsMemory(0, 1));
            if (n == 0) throw new IOException("game closed during handshake");
            header.Append((char)buf[0]);
            if (header.Length > 16384) throw new IOException("handshake too large");
        }
        string? key = null;
        foreach (var line in header.ToString().Split("\r\n"))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                key = line[(colon + 1)..].Trim();
        }
        if (key == null) throw new IOException("not a WebSocket upgrade request");

        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        string response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                          "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes);
        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) });
    }

    /// <summary>Connects to the Archipelago server with permessage-deflate; TLS first in auto mode.</summary>
    private static async Task<ClientWebSocket> ConnectServer()
    {
        if (_useTls != false)
        {
            var ws = NewServerSocket();
            try
            {
                await ws.ConnectAsync(new Uri($"wss://{_host}:{_port}/"), CancellationToken.None);
                _useTls = true;
                Log($"connected to wss://{_host}:{_port} (compression: {Compression(ws)})");
                return ws;
            }
            catch (Exception e) when (_tlsMode == "auto" && _useTls == null)
            {
                Log("TLS failed (" + e.Message + "), falling back to ws://");
                ws.Dispose();
            }
        }
        var plain = NewServerSocket();
        await plain.ConnectAsync(new Uri($"ws://{_host}:{_port}/"), CancellationToken.None);
        _useTls = false;
        Log($"connected to ws://{_host}:{_port} (compression: {Compression(plain)})");
        return plain;
    }

    private static ClientWebSocket NewServerSocket()
    {
        var ws = new ClientWebSocket();
        ws.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
        {
            ClientMaxWindowBits = 15,
            ServerMaxWindowBits = 15,
        };
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        ws.Options.CollectHttpResponseDetails = true; // to log the negotiated extension
        // Archipelago messages (data package, location scouts) can be large.
        ws.Options.SetBuffer(1 << 16, 1 << 16);
        return ws;
    }

    private static string Compression(ClientWebSocket ws)
    {
        // The negotiated extension is reported in the response headers when available.
        var ext = ws.HttpResponseHeaders?.TryGetValue("Sec-WebSocket-Extensions", out var v) == true ? string.Join(",", v) : null;
        return string.IsNullOrEmpty(ext) ? "requested" : ext;
    }

    /// <summary>Forwards whole messages (fragmented frames preserved) until either side closes.</summary>
    private static async Task Pump(WebSocket from, WebSocket to, string direction, CancellationTokenSource cts)
    {
        var buffer = new byte[1 << 16];
        try
        {
            while (!cts.IsCancellationRequested && from.State == WebSocketState.Open)
            {
                var result = await from.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log(direction + ": closed (" + result.CloseStatus + " " + result.CloseStatusDescription + ")");
                    if (to.State == WebSocketState.Open)
                        await to.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, CancellationToken.None);
                    return;
                }
                await to.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log(direction + ": " + e.Message); }
    }

    private static async Task CloseQuietly(WebSocket? ws)
    {
        if (ws == null) return;
        try
        {
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bridge closing", cts.Token);
            }
        }
        catch { /* best effort */ }
    }

    // ================================================================== raw TCP tunnel (fallback, --mode tcp)

    private static async Task HandleTcp(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            Stream? upstream = null;
            TcpClient? server = null;
            try
            {
                (server, upstream) = await ConnectUpstreamTcp();
                Log($"connected to {_host}:{_port} ({(upstream is SslStream ? "TLS" : "plain")}, raw tunnel)");
                var down = client.GetStream();
                var a = down.CopyToAsync(upstream);
                var b = upstream.CopyToAsync(down);
                await Task.WhenAny(a, b);
            }
            catch (Exception e)
            {
                Log("connection failed: " + e.Message);
            }
            finally
            {
                upstream?.Dispose();
                server?.Dispose();
            }
        }
    }

    private static async Task<(TcpClient, Stream)> ConnectUpstreamTcp()
    {
        if (_useTls != false)
        {
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                await tcp.ConnectAsync(_host, _port);
                var ssl = new SslStream(tcp.GetStream(), false);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                });
                _useTls = true;
                return (tcp, ssl);
            }
            catch (Exception e) when (_tlsMode == "auto" && _useTls == null)
            {
                Log("TLS failed (" + e.Message + "), falling back to plain TCP");
                tcp.Dispose();
            }
        }
        var plain = new TcpClient { NoDelay = true };
        await plain.ConnectAsync(_host, _port);
        _useTls = false;
        return (plain, plain.GetStream());
    }

    // ================================================================== misc

    private static async Task WatchParent(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            await p.WaitForExitAsync();
        }
        catch { /* parent already gone */ }
        Environment.Exit(0);
    }

    private static void Log(string s) => Console.Error.WriteLine("[bridge] " + s);

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 2;
    }
}
