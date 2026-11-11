using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NoR.Core;

namespace NoR.Localization
{
    /// <summary>
    /// Japanese -> English lookup keyed by a hash of the Japanese source string, so the translation files
    /// never contain the game's original text.
    ///
    /// Files (in &lt;plugin&gt;/translation/):
    ///   en/*.tsv           "hash \t english" ('\n' escaped as \n, '\t' as \t); later files override earlier ones
    ///   code_unsafe.txt    hashes of strings used as logic keys - never replaced
    /// </summary>
    public sealed class TranslationTable
    {
        private readonly Dictionary<string, string> _byHash = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _unsafe = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _english = new HashSet<string>(StringComparer.Ordinal);

        public int Count { get { return _byHash.Count; } }
        public int Hits;
        public int Misses;

        /// <summary>Japanese strings seen at runtime without a translation (for coverage reports).</summary>
        public readonly HashSet<string> MissingHashes = new HashSet<string>(StringComparer.Ordinal);

        public static TranslationTable Load(string dir, string language)
        {
            var t = new TranslationTable();
            string unsafePath = Path.Combine(dir, "code_unsafe.txt");
            if (File.Exists(unsafePath))
            {
                foreach (var line in File.ReadAllLines(unsafePath))
                    if (line.Length > 0 && line[0] != '#') t._unsafe.Add(line.Trim());
            }

            string langDir = Path.Combine(dir, language);
            if (Directory.Exists(langDir))
            {
                var files = Directory.GetFiles(langDir, "*.tsv");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var f in files) t.LoadTsv(f);
            }
            return t;
        }

        private void LoadTsv(string path)
        {
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                string hash = raw.Substring(0, tab).Trim();
                string en = Unescape(raw.Substring(tab + 1));
                if (en.Length == 0) continue; // untranslated placeholder
                _byHash[hash] = en;
                _english.Add(en);
            }
        }

        /// <summary>True if s is one of the English strings of this table (text translated earlier).</summary>
        public bool IsTranslation(string s) { return s != null && _english.Contains(s); }

        public bool IsUnsafe(string jp) { return _unsafe.Contains(Hash(jp)); }

        /// <summary>Returns the English text for a Japanese string, or the input unchanged.</summary>
        public string Translate(string s)
        {
            if (s == null || !Hierarchy.ContainsJapanese(s)) return s;
            string cached;
            if (_cache.TryGetValue(s, out cached)) return cached;

            string h = Hash(s);
            string result = s;
            if (!_unsafe.Contains(h))
            {
                string en;
                if (_byHash.TryGetValue(h, out en)) { result = en; Hits++; }
                else { Misses++; MissingHashes.Add(h); }
            }
            if (_cache.Count > 20000) _cache.Clear();
            _cache[s] = result;
            return result;
        }

        /// <summary>True if s is a string this table produced (used to avoid re-processing).</summary>
        public static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(12);
                for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 't') sb.Append('\t');
                    else if (n == '\\') sb.Append('\\');
                    else { sb.Append('\\').Append(n); }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
