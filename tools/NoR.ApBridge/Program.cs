using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace NoR.ApBridge;

/// <summary>
/// TLS bridge for the in-game Archipelago client.
///
/// Night of Revenge runs on Unity 5.6's old Mono runtime, which cannot negotiate TLS 1.2+. archipelago.gg rooms only
/// accept TLS (wss://). The plugin starts this process and connects to it with plain ws://127.0.0.1:&lt;port&gt;;
/// every accepted TCP connection is forwarded byte-for-byte to the target host, wrapped in TLS when needed.
/// Being a TCP-level tunnel it never parses Archipelago or WebSocket data.
///
/// Usage: NoR.ApBridge --target host:port [--tls auto|on|off] [--listen 0] [--parent-pid N]
///   --listen 0     pick a free port; the chosen port is printed to stdout as "LISTENING &lt;port&gt;"
///   --tls auto     try TLS first, fall back to plain TCP (same behavior as the official AP clients)
///   --parent-pid   exit when the game process exits
/// </summary>
internal static class Program
{
    private static string _host = "";
    private static int _port;
    private static string _tlsMode = "auto";
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

        if (parentPid > 0) _ = WatchParent(parentPid);

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Handle(client);
        }
    }

    private static async Task Handle(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            Stream? upstream = null;
            TcpClient? server = null;
            try
            {
                (server, upstream) = await ConnectUpstream();
                Log($"connected to {_host}:{_port} ({(upstream is SslStream ? "TLS" : "plain")})");
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

    private static async Task<(TcpClient, Stream)> ConnectUpstream()
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
