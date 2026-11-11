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
/// Logic data: one region per scene, edges from data/regions.draft.json (transitions + observed scripted moves),
/// key doors from data/key_doors.tsv (conservative: the whole scene and its exits require the key).
///
/// Usage (from the repo root): dotnet run --project tools/NoR.ApWorldGen
/// </summary>
internal static class Program
{
    private const long LocationBase = 7_700_000;
    private const long ItemBase = 7_700_000;
    private const int DataVersion = 3;
    private const string StartRegion = "FirstMap";
    private const string GoalRegion = "LastBoss";

    /// <summary>Use items that matter more than consumables: boss Ideas (trade currency), Witch's Elixir (potion cap).</summary>
    private static readonly HashSet<int> UsefulUseItems = new() { 15, 16, 17, 18, 19, 20, 21, 64 };

    /// <summary>Trap items (effects implemented in NoR.Randomizer/Traps.cs).</summary>
    private static readonly Dictionary<string, string> Traps = new()
    {
        ["trap:ambush"] = "Ambush Trap",
        ["trap:idea"] = "Idea Drain Trap",
        ["trap:knockdown"] = "Knockdown Trap",
        ["trap:aphrodisiac"] = "Aphrodisiac Trap", // lewd trap (option lewd_traps)
    };

    private static readonly HashSet<string> LewdTraps = new() { "trap:aphrodisiac" };

    private sealed record Loc(string Key, string Category, string Name, string VanillaCode, int Slot, string Region);

    private static int Main()
    {
        var en = LoadTranslations("data/translation/en");
        var items = JsonNode.Parse(File.ReadAllText("data/items.json"))!;
        var magicNames = new Dictionary<string, string>();   // magic code -> Japanese display name

        // ---------------- key doors
        var keyDoors = new Dictionary<string, string>();     // scene -> key item code
        foreach (var line in File.ReadLines("data/key_doors.tsv"))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var c = line.Split('\t');
            keyDoors[c[0]] = c[1];
        }
        var keyCodes = new HashSet<string>(keyDoors.Values);

        // ---------------- locations
        var locs = new List<Loc>();
        foreach (var line in File.ReadLines("data/runtime/locations.tsv"))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var c = line.Split('\t');
            string key = c[0], cat = c[1], scene = c[2], vanilla = c[5], displayJp = c[8];
            if (vanilla.Length == 0) continue;
            if (cat == "magic") magicNames[vanilla] = displayJp;
            locs.Add(new Loc(key, cat, scene, vanilla, -1, scene));
        }
        foreach (var line in File.ReadLines("data/code_locations.tsv"))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var c = line.Split('\t');
            string cat = c[0], cls = c[1], method = c[2], label = c[6], scene = c[7];
            int actor = int.Parse(c[3]), kind = int.Parse(c[4]), num = int.Parse(c[5]);
            string key = cat == "trade" ? "trade:" + method : $"reward:{cls}.{method}:{actor}:{kind}-{num}";
            // treasureNumSet(num, kind) -> DROPItemKind(ID, USE, RING, ...): 0 weapon, 1 use item, 2 ring; 3 costume (trades)
            string code = kind switch { 0 => "weapon:" + num, 1 => "UseItem:" + num, 2 => "crestring:" + num, _ => "costume:" + num };
            locs.Add(new Loc(key, cat, label, code, -1, scene));
        }
        var shopPool = File.ReadLines("data/shop_pool.txt").Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToList();
        for (int i = 0; i < shopPool.Count; i++) locs.Add(new Loc("shop:" + i, "shop", "Witch's Market", shopPool[i], i, StartRegion));

        // ---------------- regions and edges
        var regionsDoc = JsonNode.Parse(File.ReadAllText("data/regions.draft.json"))!;
        var edges = new SortedSet<(string, string)>();
        foreach (var e in regionsDoc["edges"]!.AsArray())
        {
            string from = e!["from"]!.GetValue<string>(), to = e["to"]!.GetValue<string>();
            if (from != to) edges.Add((from, to));
        }
        var regions = new SortedSet<string>(StringComparer.Ordinal) { StartRegion, GoalRegion };
        foreach (var (f, t) in edges) { regions.Add(f); regions.Add(t); }
        foreach (var l in locs) regions.Add(l.Region);

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

        foreach (var l in locs)
            if (!locIds.ContainsKey(l.Key)) locIds[l.Key] = NextId(locIds, LocationBase);
        foreach (var code in locs.Select(l => l.VanillaCode.Split('+')[0]).Distinct())
            if (!itemIds.ContainsKey(code)) itemIds[code] = NextId(itemIds, ItemBase);
        if (!itemIds.ContainsKey("UseItem:9")) itemIds["UseItem:9"] = NextId(itemIds, ItemBase); // filler
        foreach (var trap in Traps.Keys)
            if (!itemIds.ContainsKey(trap)) itemIds[trap] = NextId(itemIds, ItemBase);

        ids["locations"] = locIds;
        ids["items"] = itemIds;
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(idsPath, ids.ToJsonString(jsonOpts) + "\n");

        // Items the data module knows: every code used by a current location, the filler and the traps.
        var usedCodes = new HashSet<string>(locs.Select(l => l.VanillaCode.Split('+')[0])) { "UseItem:9" };
        foreach (var t in Traps.Keys) usedCodes.Add(t);

        // ---------------- data.py
        var py = new StringBuilder();
        py.AppendLine("# Generated by tools/NoR.ApWorldGen - do not edit by hand.");
        py.AppendLine("from typing import Any, Dict, List, Tuple");
        py.AppendLine();
        py.AppendLine($"DATA_VERSION = {DataVersion}");
        py.AppendLine($"FILLER_ITEM = {Py(ItemName("UseItem:9"))}");
        py.AppendLine($"TRAPS = [{string.Join(", ", Traps.Keys.Where(k => !LewdTraps.Contains(k)).Select(k => Py(ItemName(k))))}]");
        py.AppendLine($"LEWD_TRAPS = [{string.Join(", ", Traps.Keys.Where(k => LewdTraps.Contains(k)).Select(k => Py(ItemName(k))))}]");
        py.AppendLine($"START_REGION = {Py(StartRegion)}");
        py.AppendLine($"GOAL_REGION = {Py(GoalRegion)}");
        py.AppendLine();
        py.AppendLine("# scene -> key item name required to use the scene's locations and exits (when keys are shuffled)");
        py.AppendLine("KEY_DOORS: Dict[str, str] = {");
        foreach (var kv in keyDoors) py.AppendLine($"    {Py(kv.Key)}: {Py(ItemName(kv.Value))},");
        py.AppendLine("}");
        py.AppendLine();
        py.AppendLine($"REGIONS: List[str] = [{string.Join(", ", regions.Select(Py))}]");
        py.AppendLine();
        py.AppendLine("EDGES: List[Tuple[str, str]] = [");
        foreach (var (f, t) in edges) py.AppendLine($"    ({Py(f)}, {Py(t)}),");
        py.AppendLine("]");
        py.AppendLine();
        py.AppendLine("# name -> {id, code, classification}");
        py.AppendLine("ITEMS: Dict[str, Dict[str, Any]] = {");
        foreach (var kv in itemIds.Where(kv => usedCodes.Contains(kv.Key)).OrderBy(kv => (long)kv.Value!))
        {
            string code = kv.Key;
            py.AppendLine($"    {Py(ItemName(code))}: {{\"id\": {(long)kv.Value!}, \"code\": {Py(code)}, \"classification\": {Py(Classify(code, keyCodes))}}},");
        }
        py.AppendLine("}");
        py.AppendLine();
        py.AppendLine("# name -> {id, key, category, region, vanilla (item names), slot (shop only), key_location (holds a door key in vanilla)}");
        py.AppendLine("LOCATIONS: Dict[str, Dict[str, Any]] = {");
        foreach (var l in locs.OrderBy(l => (long)locIds[l.Key]!))
        {
            string first = l.VanillaCode.Split('+')[0];
            bool keyLoc = keyCodes.Contains(first) || l.Category == "key";
            py.AppendLine($"    {Py(locName[l.Key])}: {{\"id\": {(long)locIds[l.Key]!}, \"key\": {Py(l.Key)}, \"category\": {Py(l.Category)}, " +
                          $"\"region\": {Py(l.Region)}, \"vanilla\": [{Py(ItemName(first))}], \"slot\": {l.Slot}, \"key_location\": {(keyLoc ? "True" : "False")}}},");
        }
        py.AppendLine("}");
        Directory.CreateDirectory("apworld/nightofrevenge");
        File.WriteAllText("apworld/nightofrevenge/data.py", py.ToString(), new UTF8Encoding(false));

        // ---------------- client map
        var map = new StringBuilder("# Generated by tools/NoR.ApWorldGen. L = location (key, id, name); I = item (code, id, name)\n");
        foreach (var l in locs) map.Append($"L\t{l.Key}\t{(long)locIds[l.Key]!}\t{locName[l.Key]}\n");
        foreach (var kv in itemIds.Where(kv => usedCodes.Contains(kv.Key))) map.Append($"I\t{kv.Key}\t{(long)kv.Value!}\t{ItemName(kv.Key)}\n");
        File.WriteAllText("data/runtime/ap_map.tsv", map.ToString(), new UTF8Encoding(false));

        Console.WriteLine($"locations: {locs.Count} (key locations: {locs.Count(l => keyCodes.Contains(l.VanillaCode.Split('+')[0]) || l.Category == "key")}), " +
                          $"items: {usedCodes.Count}, regions: {regions.Count}, edges: {edges.Count}");
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

    private static string Classify(string code, HashSet<string> keyCodes)
    {
        if (keyCodes.Contains(code)) return "progression";
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
