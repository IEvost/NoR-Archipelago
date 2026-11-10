using System.Collections.Generic;
using UnityEngine;

namespace NoR.Archipelago
{
    /// <summary>Short on-screen messages (sent/received items, connection state), newest at the bottom.</summary>
    public sealed class Notifications
    {
        public enum Kind { Info, Sent, Received, Error }

        private sealed class Entry
        {
            public string Text;
            public Kind Kind;
            public float Until;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private GUIStyle _style;
        public float Duration = 6f;

        public void Add(string text, Kind kind)
        {
            Plugin.Log.LogInfo("[note] " + text);
            _entries.Add(new Entry { Text = text, Kind = kind, Until = Time.unscaledTime + Duration });
            if (_entries.Count > 8) _entries.RemoveAt(0);
        }

        public void Draw()
        {
            _entries.RemoveAll(e => e.Until < Time.unscaledTime);
            if (_entries.Count == 0) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { richText = false };
            _style.fontSize = Mathf.Max(14, Screen.height / 48);
            float line = _style.fontSize * 1.5f;
            float y = Screen.height * 0.70f - _entries.Count * line;
            foreach (var e in _entries)
            {
                var color = e.Kind == Kind.Sent ? new Color(0.75f, 0.85f, 1f) : e.Kind == Kind.Received ? new Color(0.85f, 1f, 0.75f)
                    : e.Kind == Kind.Error ? new Color(1f, 0.6f, 0.6f) : Color.white;
                var r = new Rect(20, y, Screen.width * 0.6f, line);
                _style.normal.textColor = new Color(0, 0, 0, 0.85f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), e.Text, _style);
                _style.normal.textColor = color;
                GUI.Label(r, e.Text, _style);
                y += line;
            }
        }
    }
}
