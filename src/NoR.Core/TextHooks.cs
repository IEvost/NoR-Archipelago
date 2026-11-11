using System;

namespace NoR.Core
{
    /// <summary>
    /// Cross-plugin text translation hook. NoR.Localization sets <see cref="Translate"/> (Japanese -> English);
    /// other plugins call it for names they build themselves (e.g. received spells, shop rows), so they do not
    /// depend on the localization plugin being installed.
    /// </summary>
    public static class TextHooks
    {
        public static Func<string, string> Translate = s => s;

        public static string T(string s)
        {
            try { return s == null ? null : Translate(s); }
            catch { return s; }
        }
    }
}
