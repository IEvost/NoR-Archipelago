using System;
using System.IO;
using System.Text;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using NoR.Randomizer;

namespace NoR.Archipelago
{
    /// <summary>
    /// Feeds the F12 "Server log" tab: every server message (item sends between all players, joins/leaves, chat,
    /// hints, goals, releases, command results) in the server's colors, plus client notices. Also appends plain text
    /// to BepInEx/NoR.Archipelago/server_log_&lt;seed&gt;.txt so the history survives restarts.
    /// </summary>
    public sealed class ServerLogFeed
    {
        private readonly string _dir;
        private string _file;

        public ServerLogFeed(string pluginDir)
        {
            _dir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(pluginDir)), "NoR.Archipelago");
        }

        /// <summary>Starts (or continues) the log file of a seed.</summary>
        public void UseSeed(string seedName)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                _file = Path.Combine(_dir, "server_log_" + seedName + ".txt");
                Write("---- connected " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ----");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Server log file unavailable: " + e.Message); _file = null; }
        }

        public void OnMessage(LogMessage msg)
        {
            var rich = new StringBuilder();
            foreach (MessagePart part in msg.Parts)
            {
                string text = Escape(part.Text);
                if (part.IsBackgroundColor) { rich.Append(text); continue; }
                var c = part.Color;
                rich.Append("<color=#").Append(c.R.ToString("x2")).Append(c.G.ToString("x2")).Append(c.B.ToString("x2")).Append('>')
                    .Append(text).Append("</color>");
            }
            DebugConsole.AppendLog(rich.ToString(), AboutMe(msg));
            Write(msg.ToString());
        }

        /// <summary>Client-side notices (connection state, traps, DeathLink) in grey.</summary>
        public void OnClientNote(string text, bool error)
        {
            DebugConsole.AppendLog("<color=" + (error ? "#ff8a8a" : "#b0b0b0") + ">[client] " + Escape(text) + "</color>", true);
            Write("[client] " + text);
        }

        private static bool AboutMe(LogMessage msg)
        {
            var send = msg as ItemSendLogMessage;
            if (send != null) return send.IsRelatedToActivePlayer;
            var specific = msg as PlayerSpecificLogMessage;
            if (specific != null) return specific.IsActivePlayer;
            return msg is ChatLogMessage || msg is ServerChatLogMessage || msg is CommandResultLogMessage;
        }

        // Unity rich text has no escape sequence; keep a visible lookalike instead of a tag-breaking '<'.
        private static string Escape(string s) { return (s ?? string.Empty).Replace("<", "‹").Replace(">", "›"); }

        private void Write(string line)
        {
            if (_file == null) return;
            try { File.AppendAllText(_file, DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine, Encoding.UTF8); }
            catch { /* disk issues must never break the game */ }
        }
    }
}
