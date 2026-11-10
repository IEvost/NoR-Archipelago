using System.Text.Json.Nodes;

namespace NoR.DataBuilder;

internal sealed record Location(
    string Key,          // stable identifier, independent of AP numeric IDs
    string Category,     // floor_item | chest | magic | slave_rescue
    string Scene,
    int SceneIndex,
    string ObjectPath,
    bool ActiveAtStart,
    double X, double Y,
    string Flag,         // game flag that marks the location as collected
    string? VanillaItem, // "schema:index" of the original item, or magic name
    string? VanillaItemJp,
    string? Requirement, // e.g. chest lockpick cost
    string? Note)
{
    // Runtime lookup indices: (stage, flag) for Pickup_item, (school, num) for Pickup_magic.
    public int A { get; init; } = -1;
    public int B { get; init; } = -1;
    public string? School { get; init; }
    public string? MagicName { get; init; }
}

internal sealed class Locations
{
    public readonly List<Location> All = new();
    public readonly List<string> Anomalies = new();

    private static readonly string[] MagicSchools = { "GUN", "Fire", "Lightning", "Sacred", "darkness" };
    private static readonly string[] ChestLocks = { "unlocked", "bronze_lock", "silver_lock", "gold_lock" };

    public static Locations Build(SceneData scenes, Items items)
    {
        var r = new Locations();

        // Chests: TreasureBox reveals a child Pickup_item that uses the same treasure flag.
        var chestByFlag = new Dictionary<(string scene, int stage, int flag), SceneComponent>();
        foreach (var c in scenes.OfType("TreasureBox"))
            chestByFlag[(c.Scene, c.Int("itemstagenum"), c.Int("itemflagnum"))] = c;

        // ---- floor items and chest contents (Pickup_item)
        var seenFlags = new Dictionary<string, Location>();
        foreach (var c in scenes.OfType("Pickup_item"))
        {
            int stage = c.Int("Stage"), num = c.Int("Getflagnum");
            string flag = $"TreasureItemArrayStage[{stage},{num}]";

            var (item, itemJp, note) = DescribePickup(c, items, r);
            chestByFlag.TryGetValue((c.Scene, stage, num), out var chest);
            string category = chest != null ? "chest" : "floor_item";
            string? req = null;
            if (chest != null)
            {
                // Locked chests consume N of a use item (always UseItem 4 "lockpick wire" in 1.0.7):
                // Keykind 1/2/3 = bronze/silver/gold lock -> 1/2/4 picks (itemneedval).
                int kind = chest.Int("Keykind");
                string lockName = kind >= 0 && kind < ChestLocks.Length ? ChestLocks[kind] : "lock_kind_" + kind;
                req = kind == 0 ? null : $"UseItem:{chest.Int("itemID")} x{chest.Int("itemneedval")} ({lockName})";
                chestByFlag.Remove((c.Scene, stage, num));
            }

            var loc = new Location($"{category}:{c.Scene}:{stage}-{num}", category, c.Scene, c.SceneIndex, c.Path, c.Active,
                c.X, c.Y, flag, item, itemJp, req, note) { A = stage, B = num };

            if (stage < 0 || num < 0)
                r.Anomalies.Add($"{loc.Key} ({c.Path}): negative flag index, pickup is never remembered");
            if (seenFlags.TryGetValue(flag, out var other))
                r.Anomalies.Add($"flag {flag} shared by {other.Key} ({other.ObjectPath}) and {loc.Key} ({c.Path})");
            else
                seenFlags[flag] = loc;

            r.All.Add(loc);
        }
        foreach (var kv in chestByFlag)
            r.Anomalies.Add($"TreasureBox in {kv.Key.scene} ({kv.Value.Path}) has no matching Pickup_item for flag [{kv.Key.stage},{kv.Key.flag}]");

        // ---- magic pickups
        foreach (var c in scenes.OfType("Pickup_magic"))
        {
            string? school = MagicSchools.FirstOrDefault(s => c.Int("GetMagic_" + s, 0) == 1);
            int num = c.Int("Getflagnum");
            string magic = c.Str("Magicname") ?? "?";
            if (school == null) r.Anomalies.Add($"Pickup_magic {c.Scene} {c.Path} has no school flag set");
            var gde = items.Magic(magic);
            r.All.Add(new Location($"magic:{school}:{num}", "magic", c.Scene, c.SceneIndex, c.Path, c.Active, c.X, c.Y,
                $"GetMagic_{school}[{num}]", $"magic:{school}:{num}", c.Str("Itemname"), null,
                gde == null ? "magic name not found in GDE" : null)
            { A = Array.IndexOf(MagicSchools, school), B = num, School = school, MagicName = magic });
        }

        // ---- slave rescues (flag set via SpawnSlave stage/number; resolved during playthrough)
        foreach (var c in scenes.OfType("Slavehelp"))
        {
            var spawn = scenes.Components.FirstOrDefault(s => s.Scene == c.Scene && s.Type == "SpawnSlave" && s.PathId == c.PtrPathId("spawnslave"));
            string flag = spawn != null ? $"helpslaveStage[{spawn.Int("stage")},{spawn.Int("SlaveNumber")}]" : "helpslaveStage[?]";
            r.All.Add(new Location($"slave:{c.Scene}:{c.PathId}", "slave_rescue", c.Scene, c.SceneIndex, c.Path, c.Active, c.X, c.Y,
                flag, "karma:10", null, null, $"createIdea={c.Int("createIdea", 0)}"));
        }

        var dupKeys = r.All.GroupBy(l => l.Key).Where(g => g.Count() > 1).ToList();
        foreach (var g in dupKeys) r.Anomalies.Add($"duplicate location key {g.Key} x{g.Count()} ({string.Join(", ", g.Select(l => l.ObjectPath))})");

        r.All.Sort((a, b) => a.SceneIndex != b.SceneIndex ? a.SceneIndex.CompareTo(b.SceneIndex) : string.CompareOrdinal(a.Key, b.Key));
        return r;
    }

    private static (string? item, string? jp, string? note) DescribePickup(SceneComponent c, Items items, Locations r)
    {
        var parts = new List<(string schema, int idx)>();
        if (c.Int("pickUP_ID") >= 0) parts.Add(("weapon", c.Int("pickUP_ID")));
        if (c.Int("pickUP_RINGID") >= 0) parts.Add(("crestring", c.Int("pickUP_RINGID")));
        if (c.Int("pickUP_USEID") >= 0) parts.Add(("UseItem", c.Int("pickUP_USEID")));
        string? note = null;
        if (c.Int("pickUP_COSID") >= 0) note = $"costume id {c.Int("pickUP_COSID")} (ItemGetCanvas.item_get ignores it)";
        if (c.Int("pickUP_MGID") >= 0) note = (note == null ? "" : note + "; ") + $"magic id {c.Int("pickUP_MGID")} (unused by Pickup_item)";

        if (parts.Count == 0) return (null, null, note ?? "empty pickup");
        if (parts.Count > 1) note = (note == null ? "" : note + "; ") + "grants multiple items";

        var (schema, idx) = parts[0];
        var gi = items.Get(schema, idx);
        if (gi == null) r.Anomalies.Add($"{c.Scene} {c.Path}: {schema} index {idx} not in GDE");
        string all = string.Join("+", parts.Select(p => $"{p.schema}:{p.idx}"));
        string? jp = string.Join(" + ", parts.Select(p => items.Get(p.schema, p.idx)?.NameJp ?? "?"));
        return (all, jp, note);
    }

    /// <summary>
    /// Compact tab-separated table embedded into the runtime plugin (net35 has no JSON parser).
    /// Columns: key, category, scene, a, b, vanillaItem, school, magicName, displayJp
    /// </summary>
    public string ToRuntimeTsv()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# generated by NoR.DataBuilder - do not edit\n");
        sb.Append("#key\tcategory\tscene\ta\tb\tvanilla\tschool\tmagicName\tdisplayJp\n");
        foreach (var l in All.Where(l => l.Category is "floor_item" or "chest" or "magic"))
        {
            sb.Append(string.Join("\t",
                l.Key, l.Category, l.Scene, l.A, l.B, l.VanillaItem ?? "", l.School ?? "", l.MagicName ?? "",
                (l.VanillaItemJp ?? "").Replace('\t', ' ').Replace('\n', ' '))).Append('\n');
        }
        return sb.ToString();
    }

    public JsonNode ToJson()
    {
        var arr = new JsonArray();
        foreach (var l in All)
        {
            arr.Add(new JsonObject
            {
                ["key"] = l.Key,
                ["category"] = l.Category,
                ["scene"] = l.Scene,
                ["sceneIndex"] = l.SceneIndex,
                ["object"] = l.ObjectPath,
                ["activeAtStart"] = l.ActiveAtStart,
                ["x"] = Math.Round(l.X, 2),
                ["y"] = Math.Round(l.Y, 2),
                ["flag"] = l.Flag,
                ["vanilla"] = l.VanillaItem,
                ["vanillaJp"] = l.VanillaItemJp,
                ["requires"] = l.Requirement,
                ["note"] = l.Note,
            });
        }
        return new JsonObject
        {
            ["_comment"] = "DRAFT generated by NoR.DataBuilder from the static scene scan. Region/logic assignment and verification come from the playthrough flag log.",
            ["locations"] = arr,
        };
    }
}
