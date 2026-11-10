using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NoR.ApWorldGen;

/// <summary>
/// Generates the data module of the Archipelago world (apworld/nightofrevenge/data.py) and the client-side id map
/// (data/runtime/ap_map.tsv) from the project's data tables. Archipelago ids are persistent: they live in
/// data/ap_ids.json and new keys are only ever appended, so ids never change between versions.
///
/// Usage (from the repo root): dotnet run --project tools/NoR.ApWorldGen
/// </summary>
internal static class Program
{
    private const long LocationBase = 7_700_000;
    private const long ItemBase = 7_700_000;
    private const int DataVersion = 1;

    /// <summary>Door keys stay at their vanilla locations in v1 (logic for gates comes later).</summary>
    private static readonly HashSet<string> VanillaOnly = new() { "UseItem:60", "UseItem:61", "UseItem:62" };

    /// <summary>Use items that matter more than consumables: boss Ideas (trade currency), Witch's Elixir (potion cap).</summary>
    private static readonly HashSet<int> UsefulUseItems = new() { 15, 16, 17, 18, 19, 20, 21, 64 };

    /// <summary>Trap items (effects implemented in NoR.Randomizer/Traps.cs).</summary>
    private static readonly Dictionary<string, string> Traps = new()
    {
        ["trap:ambush"] = "Ambush Trap",
        ["trap:idea"] = "Idea Drain Trap",
        ["trap:knockdown"] = "Knockdown Trap",
    };

    private sealed record Loc(string Key, string Category, string Name, string VanillaCode, int Slot);

    private static int Main()
    {
        var en = LoadTranslations("data/translation/en");
        var items = JsonNode.Parse(File.ReadAllText("data/items.json"))!;
        var magicNames = new Dictionary<string, string>();   // magic code -> Japanese display name

        // ---------------- locations
        var locs = new List<Loc>();
        var areaCounts = new Dictionary<string, int>();
        foreach (var line in File.ReadLines("data/runtime/locations.tsv"))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var c = line.Split('\t');
            string key = c[0], cat = c[1], scene = c[2], vanilla = c[5], displayJp = c[8];
            if (vanilla.Length == 0) continue;
            if (cat == "magic") magicNames[vanilla] = displayJp;
            locs.Add(new Loc(key, cat, scene, vanilla, -1));
        }
        foreach (var line in File.ReadLines("data/code_locations.tsv"))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var c = line.Split('\t');
            string cat = c[0], cls = c[1], method = c[2], label = c[6];
            int actor = int.Parse(c[3]), kind = int.Parse(c[4]), num = int.Parse(c[5]);
            string key = cat == "trade" ? "trade:" + method : $"reward:{cls}.{method}:{actor}:{kind}-{num}";
            string code = kind switch { 0 => "weapon:" + num, 1 => "crestring:" + num, 2 => "UseItem:" + num, _ => "costume:" + num };
            locs.Add(new Loc(key, cat, label, code, -1));
        }
        var shopPool = File.ReadLines("data/shop_pool.txt").Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToList();
        for (int i = 0; i < shopPool.Count; i++) locs.Add(new Loc("shop:" + i, "shop", "Witch's Market", shopPool[i], i));

        // ---------------- item names
        var itemNames = new Dictionary<string, string>(); // code -> unique English name
        var usedNames = new Dictionary<string, string>();  // name -> code
        string ItemName(string code)
        {
            if (itemNames.TryGetValue(code, out var n)) return n;
            n = EnglishItemName(code, items, en, magicNames);
            if (usedNames.TryGetValue(n, out var other) && other != code)
            {
                string suffix = code.Split(':')[0] switch { "crestring" => " (Ring)", "magic" => " (Spell)", "weapon" => " (Weapon)", "costume" => " (Costume)", _ => " (Item)" };
                n += suffix;
                if (usedNames.ContainsKey(n)) n += " #" + code;
            }
            usedNames[n] = code;
            itemNames[code] = n;
            return n;
        }

        // ---------------- location names (readable, unique)
        var locName = new Dictionary<string, string>();
        var usedLoc = new HashSet<string>();
        foreach (var l in locs)
        {
            string firstCode = l.VanillaCode.Split('+')[0];
            string baseName = l.Category switch
            {
                "floor_item" => $"{Area(l.Name)} - {ItemName(firstCode)}",
                "chest" => $"{Area(l.Name)} - Chest ({ItemName(firstCode)})",
                "magic" => $"{Area(l.Name)} - {ItemName(firstCode)}",
                "shop" => $"Witch's Market - Rare Goods {l.Slot + 1}",
                "boss_drop" => $"{l.Name} - Boss Drop",
                _ => l.Name,
            };
            string name = baseName;
            for (int k = 2; !usedLoc.Add(name); k++) name = baseName + " " + k;
            locName[l.Key] = name;
        }

        // ---------------- persistent ids
        var idsPath = "data/ap_ids.json";
        var ids = File.Exists(idsPath) ? JsonNode.Parse(File.ReadAllText(idsPath))!.AsObject() : new JsonObject();
        var locIds = ids["locations"]?.AsObject() ?? new JsonObject();
        var itemIds = ids["items"]?.AsObject() ?? new JsonObject();
        long NextId(JsonObject o, long @base) => o.Count == 0 ? @base : o.Select(kv => (long)kv.Value!).Max() + 1;

        var included = locs.Where(l => !l.VanillaCode.Split('+').Any(VanillaOnly.Contains)).ToList();
        foreach (var l in included)
            if (!locIds.ContainsKey(l.Key)) locIds[l.Key] = NextId(locIds, LocationBase);
        foreach (var code in included.Select(l => l.VanillaCode.Split('+')[0]).Distinct())
            if (!itemIds.ContainsKey(code)) itemIds[code] = NextId(itemIds, ItemBase);
        if (!itemIds.ContainsKey("UseItem:9")) itemIds["UseItem:9"] = NextId(itemIds, ItemBase); // filler
        foreach (var trap in Traps.Keys)
            if (!itemIds.ContainsKey(trap)) itemIds[trap] = NextId(itemIds, ItemBase);

        ids["locations"] = locIds;
        ids["items"] = itemIds;
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(idsPath, ids.ToJsonString(jsonOpts) + "\n");

        // ---------------- data.py
        var py = new StringBuilder();
        py.AppendLine("# Generated by tools/NoR.ApWorldGen - do not edit by hand.");
        py.AppendLine("from typing import Any, Dict");
        py.AppendLine();
        py.AppendLine($"DATA_VERSION = {DataVersion}");
        py.AppendLine($"FILLER_ITEM = {Py(ItemName("UseItem:9"))}");
        py.AppendLine($"TRAPS = [{string.Join(", ", Traps.Keys.Select(k => Py(ItemName(k))))}]");
        py.AppendLine();
        py.AppendLine("# name -> {id, code, classification}");
        py.AppendLine("ITEMS: Dict[str, Dict[str, Any]] = {");
        foreach (var kv in itemIds.OrderBy(kv => (long)kv.Value!))
        {
            string code = kv.Key;
            py.AppendLine($"    {Py(ItemName(code))}: {{\"id\": {(long)kv.Value!}, \"code\": {Py(code)}, \"classification\": {Py(Classify(code))}}},");
        }
        py.AppendLine("}");
        py.AppendLine();
        py.AppendLine("# name -> {id, key, category, vanilla (item name), slot (shop only)}");
        py.AppendLine("LOCATIONS: Dict[str, Dict[str, Any]] = {");
        foreach (var l in included.OrderBy(l => (long)locIds[l.Key]!))
        {
            string vanilla = ItemName(l.VanillaCode.Split('+')[0]);
            py.AppendLine($"    {Py(locName[l.Key])}: {{\"id\": {(long)locIds[l.Key]!}, \"key\": {Py(l.Key)}, \"category\": {Py(l.Category)}, \"vanilla\": [{Py(vanilla)}], \"slot\": {l.Slot}}},");
        }
        py.AppendLine("}");
        Directory.CreateDirectory("apworld/nightofrevenge");
        File.WriteAllText("apworld/nightofrevenge/data.py", py.ToString(), new UTF8Encoding(false));

        // ---------------- client map
        var map = new StringBuilder("# Generated by tools/NoR.ApWorldGen. L = location (key, id, name); I = item (code, id, name)\n");
        foreach (var l in included) map.Append($"L\t{l.Key}\t{(long)locIds[l.Key]!}\t{locName[l.Key]}\n");
        foreach (var kv in itemIds) map.Append($"I\t{kv.Key}\t{(long)kv.Value!}\t{ItemName(kv.Key)}\n");
        File.WriteAllText("data/runtime/ap_map.tsv", map.ToString(), new UTF8Encoding(false));

        Console.WriteLine($"locations: {included.Count} (excluded vanilla-only: {locs.Count - included.Count}), items: {itemIds.Count}");
        Console.WriteLine("wrote apworld/nightofrevenge/data.py, data/runtime/ap_map.tsv, data/ap_ids.json");
        return 0;
    }

    // ------------------------------------------------------------------ naming

    private static string EnglishItemName(string code, JsonNode items, Dictionary<string, string> en, Dictionary<string, string> magicNames)
    {
        var p = code.Split(':');
        string? jp = null;
        switch (p[0])
        {
            case "weapon": jp = items["weapon"]![int.Parse(p[1])]!["nameJp"]?.GetValue<string>(); break;
            case "crestring": jp = items["crestring"]![int.Parse(p[1])]!["nameJp"]?.GetValue<string>(); break;
            case "UseItem": jp = items["UseItem"]![int.Parse(p[1])]!["nameJp"]?.GetValue<string>(); break;
            case "magic": magicNames.TryGetValue(code, out jp); break;
            case "costume": return "Phantom Garb Skin " + p[1];
            case "trap": return Traps[code];
        }
        if (jp != null && en.TryGetValue(Hash(jp), out var e)) return e.Replace("\\n", " ").Trim();
        return code;
    }

    private static string Classify(string code)
    {
        var p = code.Split(':');
        return p[0] switch
        {
            "weapon" or "crestring" or "magic" or "costume" => "useful",
            "UseItem" when UsefulUseItems.Contains(int.Parse(p[1])) => "useful",
            "trap" => "trap",
            _ => "filler",
        };
    }

    /// <summary>"UndergroundChurch" -> "Underground Church"; a few scene ids get proper names.</summary>
    private static string Area(string scene)
    {
        var fixedNames = new Dictionary<string, string>
        {
            ["village_main"] = "Witch Village Outskirts", ["village"] = "Small Abandoned Village", ["FirstMap"] = "Bandits' Hidden Forest",
            ["scapegoatEntrance"] = "Pillory Entrance", ["InundergroundChurch"] = "Inner Underground Church", ["Parishchurch"] = "Parish Church",
            ["Lake"] = "Nameless Lake", ["Shop"] = "Vendetta's Shop",
        };
        if (fixedNames.TryGetValue(scene, out var n)) return n;
        return Regex.Replace(scene, "(?<=[a-z])(?=[A-Z])", " ");
    }

    // ------------------------------------------------------------------ helpers

    private static Dictionary<string, string> LoadTranslations(string dir)
    {
        var d = new Dictionary<string, string>();
        foreach (var f in Directory.GetFiles(dir, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            foreach (var line in File.ReadLines(f))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int t = line.IndexOf('\t');
                if (t > 0 && t < line.Length - 1) d[line[..t]] = line[(t + 1)..];
            }
        return d;
    }

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..12].ToLowerInvariant();

    private static string Py(string s) => JsonSerializer.Serialize(s, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
