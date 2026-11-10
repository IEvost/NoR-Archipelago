using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NoR.Core.Json
{
    /// <summary>
    /// Minimal streaming JSON writer. .NET 3.5 has no built-in JSON support and the
    /// in-game tools should not drag in third-party dependencies just for dumping.
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly TextWriter _out;
        private readonly bool _indent;

        // One entry per open container: number of items written so far.
        private readonly Stack<int> _counts = new Stack<int>();
        private bool _afterName;

        public JsonWriter(TextWriter output, bool indent = true)
        {
            _out = output;
            _indent = indent;
        }

        public static string Serialize(Action<JsonWriter> body, bool indent = true)
        {
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb, CultureInfo.InvariantCulture))
            {
                body(new JsonWriter(sw, indent));
            }
            return sb.ToString();
        }

        public JsonWriter BeginObject() { BeforeValue(); _out.Write('{'); _counts.Push(0); return this; }
        public JsonWriter EndObject() { Close('}'); return this; }
        public JsonWriter BeginArray() { BeforeValue(); _out.Write('['); _counts.Push(0); return this; }
        public JsonWriter EndArray() { Close(']'); return this; }

        public JsonWriter Name(string name)
        {
            NextItem();
            WriteString(name);
            _out.Write(_indent ? ": " : ":");
            _afterName = true;
            return this;
        }

        public JsonWriter Null() { BeforeValue(); _out.Write("null"); return this; }
        public JsonWriter Value(bool v) { BeforeValue(); _out.Write(v ? "true" : "false"); return this; }
        public JsonWriter Value(long v) { BeforeValue(); _out.Write(v.ToString(CultureInfo.InvariantCulture)); return this; }

        public JsonWriter Value(double v)
        {
            BeforeValue();
            if (double.IsNaN(v) || double.IsInfinity(v)) _out.Write("null");
            else _out.Write(v.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Value(string v)
        {
            BeforeValue();
            if (v == null) _out.Write("null");
            else WriteString(v);
            return this;
        }

        public JsonWriter Prop(string name, string v) { return Name(name).Value(v); }
        public JsonWriter Prop(string name, long v) { return Name(name).Value(v); }
        public JsonWriter Prop(string name, double v) { return Name(name).Value(v); }
        public JsonWriter Prop(string name, bool v) { return Name(name).Value(v); }

        private void BeforeValue()
        {
            if (_afterName) { _afterName = false; return; }
            if (_counts.Count > 0) NextItem();
        }

        private void NextItem()
        {
            int n = _counts.Pop();
            if (n > 0) _out.Write(',');
            _counts.Push(n + 1);
            NewLine(_counts.Count);
        }

        private void Close(char c)
        {
            int n = _counts.Pop();
            if (n > 0) NewLine(_counts.Count);
            _out.Write(c);
        }

        private void NewLine(int depth)
        {
            if (!_indent) return;
            _out.Write('\n');
            for (int i = 0; i < depth; i++) _out.Write("  ");
        }

        private void WriteString(string s)
        {
            _out.Write('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _out.Write("\\\""); break;
                    case '\\': _out.Write("\\\\"); break;
                    case '\n': _out.Write("\\n"); break;
                    case '\r': _out.Write("\\r"); break;
                    case '\t': _out.Write("\\t"); break;
                    default:
                        if (c < 0x20) _out.Write("\\u" + ((int)c).ToString("x4"));
                        else _out.Write(c);
                        break;
                }
            }
            _out.Write('"');
        }
    }
}
