using System.Text;
using UnityEngine;

namespace NoR.Core
{
    public static class Hierarchy
    {
        /// <summary>Full transform path, e.g. "Stage/Items/Pickup_item (3)".</summary>
        public static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
            {
                sb.Insert(0, '/');
                sb.Insert(0, p.name);
            }
            return sb.ToString();
        }

        /// <summary>True if the string contains Japanese (kana or CJK ideographs).</summary>
        public static bool ContainsJapanese(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if ((c >= '぀' && c <= 'ヿ') || (c >= '一' && c <= '鿿') || (c >= '＀' && c <= '￯'))
                    return true;
            }
            return false;
        }
    }
}
