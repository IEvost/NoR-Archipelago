using System.IO;
using System.Text;
using UnityEngine;

namespace NoR.DataDumper
{
    /// <summary>
    /// Writes the raw GameDataEditor master data (weapons, use items, rings, magic, ...).
    /// The game loads it via GDEDataManager.Init("gde_data") from an unencrypted Resources TextAsset.
    /// </summary>
    internal static class GdeDumper
    {
        private static readonly string[] Assets = { "gde_data" };

        public static string Dump(string outDir)
        {
            var sb = new StringBuilder();
            foreach (var name in Assets)
            {
                var asset = Resources.Load(name) as TextAsset;
                if (asset == null)
                {
                    sb.Append(name).Append(": not found; ");
                    continue;
                }
                var path = Path.Combine(outDir, name + ".json");
                File.WriteAllText(path, asset.text, new UTF8Encoding(false));
                sb.Append(name).Append(": ").Append(asset.text.Length).Append(" chars; ");
            }
            var msg = "GDE dump: " + sb;
            Plugin.Log.LogInfo(msg);
            return msg;
        }
    }
}
