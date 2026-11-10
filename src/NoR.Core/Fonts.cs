using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace NoR.Core
{
    /// <summary>
    /// Old TextMeshPro font assets are static atlases: many of the game's fonts only contain the glyphs of the
    /// Japanese text they were built for, so English text renders with missing letters. This helper finds the
    /// loaded font with the best Latin coverage and registers it as a fallback on fonts that lack Latin glyphs.
    /// </summary>
    public static class Fonts
    {
        public const string LatinSet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?'\"-+*/()[]<>%&=#_~";

        private static TMP_FontAsset _best;
        private static int _bestMissing = int.MaxValue;
        private static readonly HashSet<int> Patched = new HashSet<int>();

        /// <summary>Loaded font asset with the fewest missing Latin characters (prefers fonts used by many texts).</summary>
        public static TMP_FontAsset BestLatin()
        {
            if (_best != null && _bestMissing == 0) return _best;

            var usage = new Dictionary<TMP_FontAsset, int>();
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || t.font == null) continue;
                int n;
                usage.TryGetValue(t.font, out n);
                usage[t.font] = n + 1;
            }

            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null) continue;
                // MSDF atlases render Latin glyphs broken with the stock Distance Field shader (seen in-game).
                if (f.name.IndexOf("MSDF", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                int missing = Missing(f);
                int used;
                usage.TryGetValue(f, out used);
                int bestUsed = 0;
                if (_best != null) usage.TryGetValue(_best, out bestUsed);
                if (missing < _bestMissing || (missing == _bestMissing && used > bestUsed))
                {
                    _best = f;
                    _bestMissing = missing;
                }
            }
            return _best;
        }

        /// <summary>
        /// The material the game's own texts use with this font. Some fonts are MSDF atlases that only render correctly
        /// with the developer's material/shader; the font asset's default material shows broken glyphs.
        /// </summary>
        public static Material MaterialFor(TMP_FontAsset font)
        {
            if (font == null) return null;
            var counts = new Dictionary<Material, int>();
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || t.font != font || !t.gameObject.scene.IsValid()) continue;
                var m = t.fontSharedMaterial;
                if (m == null || m.GetTexture("_MainTex") != font.atlas) continue; // must sample this font's atlas
                int n;
                counts.TryGetValue(m, out n);
                counts[m] = n + 1;
            }
            Material best = null;
            int bestN = 0;
            foreach (var kv in counts)
                if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
            return best ?? font.material;
        }

        /// <summary>
        /// Makes the font's default material the one the game actually renders it with, so texts that get this font
        /// assigned at runtime (our UI, TMP fallback glyphs) render like the game's own texts.
        /// </summary>
        public static string FixDefaultMaterial(TMP_FontAsset font)
        {
            var m = MaterialFor(font);
            if (m == null) return "no material";
            string before = font.material != null ? font.material.name + " / " + font.material.shader.name : "null";
            if (m != font.material) font.material = m;
            return "material " + before + " -> " + m.name + " / " + m.shader.name;
        }

        public static int Missing(TMP_FontAsset f)
        {
            try
            {
                List<char> missing;
                return f.HasCharacters(LatinSet, out missing) ? 0 : (missing != null ? missing.Count : LatinSet.Length);
            }
            catch
            {
                return LatinSet.Length; // font not initialized
            }
        }

        /// <summary>Adds the best Latin font as fallback to every loaded font asset that lacks Latin glyphs.</summary>
        public static int EnsureLatinFallbacks()
        {
            var best = BestLatin();
            if (best == null || _bestMissing > 10) return 0;
            FixDefaultMaterial(best); // fallback glyphs are drawn with the fallback font's default material
            int added = 0;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null || f == best || Patched.Contains(f.GetInstanceID())) continue;
                Patched.Add(f.GetInstanceID());
                if (Missing(f) == 0) continue;
                if (f.fallbackFontAssets == null) f.fallbackFontAssets = new List<TMP_FontAsset>();
                if (!f.fallbackFontAssets.Contains(best))
                {
                    f.fallbackFontAssets.Add(best);
                    added++;
                }
            }
            return added;
        }

        public static string BestName { get { return _best != null ? _best.name + " (missing " + _bestMissing + ")" : "none"; } }
    }
}
