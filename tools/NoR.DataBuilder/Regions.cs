using System.Text;
using System.Text.Json.Nodes;

namespace NoR.DataBuilder;

/// <summary>
/// First-pass region graph: one node per scene, edges from scene transition components.
///   SceneMove        trigger zone, unconditional
///   SceneMoveSubmit  interact door ("KeyItem" field exists but is never read by the code) - unconditional
///   SceneMoveMainEv  goes to an event scene while _MainEVflag[before] && !_MainEVflag[ev], otherwise to SceneName
/// Gates inside scenes (key doors, karma barriers, sacred walls, one-way shortcuts) are attached as annotations;
/// splitting scenes into sub-regions comes from the playthrough flag log.
/// </summary>
internal sealed class Regions
{
    public sealed record Edge(string From, string To, string Kind, string Object, double X, double Y, string? Condition);

    public readonly List<Edge> Edges = new();
    public readonly SortedDictionary<string, int> SceneIndex = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<string>> Gates = new(StringComparer.Ordinal);

    private static readonly string[] GateTypes =
    {
        "DoorOpen", "MapGimickMoveDoor", "MapGimickMovedoorStart", "ElevatorON", "LakeUnlock", "EndingCarmaCheck",
        "MainLastWall", "LastAreaEvflag", "SacredWall", "SacredWallCreate", "IvyRoadStop", "EvflagActive", "EvflagActiveobject", "NomalflagActiveobject",
    };

    public static Regions Build(SceneData scenes)
    {
        var r = new Regions();
        foreach (var kv in scenes.Scenes) r.SceneIndex[kv.Value] = kv.Key;

        // Unity's SceneManager.LoadScene(name) is case-insensitive and the game's strings use inconsistent case
        // ("valley", "InUndergroundChurch"); map every target to the build's canonical scene name.
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in BuildSceneNames()) canonical[name] = name;
        foreach (var name in scenes.Scenes.Values) canonical[name] = name;
        string Canon(string s) => canonical.TryGetValue(s, out var c) ? c : s;

        foreach (var c in scenes.Components)
        {
            switch (c.Type)
            {
                case "SceneMove":
                case "SceneMoveSubmit":
                {
                    var to = Canon(c.Str("SceneName") ?? "");
                    if (!string.IsNullOrEmpty(to)) r.Edges.Add(new Edge(c.Scene, to, c.Type, c.Path, c.X, c.Y, null));
                    break;
                }
                case "SceneMoveMainEv":
                {
                    var to = Canon(c.Str("SceneName") ?? "");
                    var ev = Canon(c.Str("EVSceneName") ?? "");
                    if (!string.IsNullOrEmpty(to)) r.Edges.Add(new Edge(c.Scene, to, c.Type, c.Path, c.X, c.Y, null));
                    if (!string.IsNullOrEmpty(ev))
                        r.Edges.Add(new Edge(c.Scene, ev, c.Type + "(event)", c.Path, c.X, c.Y,
                            $"MainEVflag[{c.Int("beforeEvnum")}] && !MainEVflag[{c.Int("Evflagnum")}]"));
                    break;
                }
            }
            if (Array.IndexOf(GateTypes, c.Type) >= 0)
            {
                if (!r.Gates.TryGetValue(c.Scene, out var list)) r.Gates[c.Scene] = list = new List<string>();
                list.Add($"{c.Type} @ ({c.X:0},{c.Y:0}) {c.Path}");
            }
        }

        // Scripted transitions recorded from play logs.
        var observed = Path.Combine("data", "observed_edges.tsv");
        if (File.Exists(observed))
        {
            foreach (var line in File.ReadAllLines(observed))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('\t');
                if (p.Length >= 2) r.Edges.Add(new Edge(Canon(p[0]), Canon(p[1]), "observed", p.Length > 2 ? p[2] : "", 0, 0, null));
            }
        }
        return r;
    }

    /// <summary>All scene names in the build (dump/static/build_scenes.json), including scenes without dumped components.</summary>
    private static IEnumerable<string> BuildSceneNames()
    {
        var path = Path.Combine("dump", "static", "build_scenes.json");
        if (!File.Exists(path)) yield break;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        foreach (var e in doc.RootElement.EnumerateArray())
            yield return e.GetProperty("name").GetString()!;
    }

    /// <summary>Scenes reachable from the start scene using transitions only (gates ignored).</summary>
    public HashSet<string> Reachable(string start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            foreach (var e in Edges.Where(e => e.From == s))
                if (seen.Add(e.To)) queue.Enqueue(e.To);
        }
        return seen;
    }

    public JsonNode ToJson()
    {
        var edges = new JsonArray();
        foreach (var e in Edges.OrderBy(e => SceneIndex.GetValueOrDefault(e.From, 999)).ThenBy(e => e.To, StringComparer.Ordinal))
        {
            edges.Add(new JsonObject
            {
                ["from"] = e.From, ["to"] = e.To, ["kind"] = e.Kind, ["object"] = e.Object,
                ["x"] = Math.Round(e.X, 1), ["y"] = Math.Round(e.Y, 1), ["condition"] = e.Condition,
            });
        }
        var gates = new JsonObject();
        foreach (var kv in Gates.OrderBy(k => SceneIndex.GetValueOrDefault(k.Key, 999)))
            gates[kv.Key] = new JsonArray(kv.Value.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return new JsonObject
        {
            ["_comment"] = "DRAFT scene graph generated by NoR.DataBuilder. Gates are annotations only; sub-scene regions come from the playthrough log.",
            ["edges"] = edges,
            ["gates"] = gates,
        };
    }

    public string ToMarkdown(Locations locs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Scene graph (transitions only, gates ignored)");
        sb.AppendLine();
        const string start = "FirstMap";
        var reach = Reachable(start);
        var scenesWithLocs = locs.All.Select(l => l.Scene).Distinct().ToList();
        sb.AppendLine($"Start: `{start}`. Reachable by transitions: {reach.Count} scenes.");
        sb.AppendLine();
        var unreached = scenesWithLocs.Where(s => !reach.Contains(s) && s != "(shop)").ToList();
        sb.AppendLine("Scenes with locations NOT reachable by plain transitions (reached via warps, elevators, events or boss scenes - to verify):");
        sb.AppendLine();
        foreach (var s in unreached) sb.AppendLine($"- {s}");
        if (unreached.Count == 0) sb.AppendLine("- none");
        sb.AppendLine();
        sb.AppendLine("| Scene | Exits | Gates in scene |");
        sb.AppendLine("|---|---|---|");
        foreach (var s in Edges.Select(e => e.From).Concat(Gates.Keys).Distinct().OrderBy(s => SceneIndex.GetValueOrDefault(s, 999)))
        {
            var exits = Edges.Where(e => e.From == s).Select(e => e.To + (e.Condition != null ? " (event)" : "")).Distinct();
            var gates = Gates.TryGetValue(s, out var g) ? g.Select(x => x.Split(' ')[0]).GroupBy(x => x).Select(x => x.Key + (x.Count() > 1 ? " x" + x.Count() : "")) : Enumerable.Empty<string>();
            sb.AppendLine($"| {s} | {string.Join(", ", exits)} | {string.Join(", ", gates)} |");
        }
        return sb.ToString();
    }
}
