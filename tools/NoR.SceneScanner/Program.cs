using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NoR.SceneScanner;

/// <summary>
/// Offline scanner for Night of Revenge asset files (Unity 5.6.7, Mono).
/// Reads every scene (levelN) and asset file without running the game and writes:
///   build_scenes.json            scene index -> scene name
///   scenes/NNN_name.json         selected game components per scene (pickups, chests, spawners, gates, ...)
///   prefabs/file.json            selected game components inside prefabs (enemy pool research)
///   text/jp_strings.jsonl        every serialized string containing Japanese (translation source)
///   textassets/*                 TextAssets (GameDataEditor data etc.)
///   summary.json                 component counts per type
///
/// Usage: NoR.SceneScanner --game "D:\Games\...\NightofRevenge_1.07" --out dump\static [--tpk .tools\classdata.tpk] [--types regex] [--only level12]
/// </summary>
internal static class Program
{
    public const string DefaultTypeRegex =
        "Pickup|Treasure|Spawn|Boss|Door|Gimick|Gimmick|Elevator|Wall|Karma|Carma|Lake|Shop|Trade|Slave|help|^Ev|Talk|TextController|Dialog|Savepoint|Warp|Load|Scene|Move|Ending|Choose|Drop|Library|Title|Npc|NPC|Witch|Enemy|enemy";

    private static int Main(string[] args)
    {
        var opts = Options.Parse(args);
        if (opts == null)
        {
            Console.Error.WriteLine("Usage: NoR.SceneScanner --game <gameDir> --out <outDir> [--tpk <classdata.tpk>] [--types <regex>] [--only <fileNameRegex>]");
            return 2;
        }

        var sw = Stopwatch.StartNew();
        using var scanner = new Scanner(opts);
        scanner.Run();
        Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0.0}s -> {Path.GetFullPath(opts.OutDir)}");
        return 0;
    }
}

internal sealed class Options
{
    public string GameDir = "";
    public string OutDir = "";
    public string TpkPath = "";
    public Regex TypeFilter = new(Program.DefaultTypeRegex, RegexOptions.IgnoreCase);
    public Regex? OnlyFiles;

    public string DataDir => Path.Combine(GameDir, "NightofRevenge_Data");
    public string ManagedDir => Path.Combine(DataDir, "Managed");

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--game": o.GameDir = args[i + 1]; break;
                case "--out": o.OutDir = args[i + 1]; break;
                case "--tpk": o.TpkPath = args[i + 1]; break;
                case "--types": o.TypeFilter = new Regex(args[i + 1], RegexOptions.IgnoreCase); break;
                case "--only": o.OnlyFiles = new Regex(args[i + 1], RegexOptions.IgnoreCase); break;
                default: return null;
            }
        }
        if (o.GameDir.Length == 0 || o.OutDir.Length == 0) return null;
        if (o.TpkPath.Length == 0)
        {
            // Default: <repo>/.tools/classdata.tpk, searched upwards from the working directory.
            for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, ".tools", "classdata.tpk");
                if (File.Exists(p)) { o.TpkPath = p; break; }
            }
        }
        if (!File.Exists(o.TpkPath)) { Console.Error.WriteLine("classdata.tpk not found; pass --tpk"); return null; }
        return o;
    }
}
