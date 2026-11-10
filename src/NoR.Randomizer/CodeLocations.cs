using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoR.Randomizer
{
    /// <summary>
    /// Locations that are rewards granted by game code rather than pickups placed in scenes
    /// (docs/research-locations.md sections A, C, E). Defined in data/code_locations.tsv (shared with the apworld).
    /// Their "checked" state is the game's own one-time flag (_BossIdeaflag, _NPCEvFlag, _EvArraystage, _Tradeflag),
    /// which vanilla code still sets; only the reward changes.
    ///
    /// Reward calls all look like Class.treasureNumSet[2](int num, int kind) with kind 0 = weapon, 1 = ring, 2 = use item.
    /// Runtime key: "reward:Class.Method:actor:kind-num" (actor = the instance's talkactor field, or -1); trades "trade:N".
    /// </summary>
    public static class CodeLocations
    {
        public sealed class Entry
        {
            public string Category, Class, Method, Label;
            public int Actor, Kind, Num;
            public string Key { get { return Category == "trade" ? "trade:" + Method : "reward:" + CodeLocations.Key(Class, Method, Actor, Kind, Num); } }
        }

        public static readonly List<Entry> Entries = new List<Entry>();

        public static void Load()
        {
            if (Entries.Count > 0) return;
            using (var s = typeof(CodeLocations).Assembly.GetManifestResourceStream("NoR.Randomizer.code_locations.tsv"))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length < 7) continue;
                    Entries.Add(new Entry
                    {
                        Category = c[0], Class = c[1], Method = c[2], Actor = int.Parse(c[3]),
                        Kind = int.Parse(c[4]), Num = int.Parse(c[5]), Label = c[6],
                    });
                }
            }
        }

        /// <summary>All hooked reward methods (distinct Class.Method pairs, trades excluded).</summary>
        public static IEnumerable<KeyValuePair<string, string>> HookTargets()
        {
            var seen = new HashSet<string>();
            foreach (var e in Entries)
                if (e.Category != "trade" && seen.Add(e.Class + "." + e.Method)) yield return new KeyValuePair<string, string>(e.Class, e.Method);
            // Hooked for discovery only (logs unlisted calls).
            if (seen.Add("EvNpcTalkactor.treasureNumSet")) yield return new KeyValuePair<string, string>("EvNpcTalkactor", "treasureNumSet");
        }

        public static string Key(string cls, string method, int actor, int kind, int num)
        {
            return cls + "." + method + ":" + actor + ":" + kind + "-" + num;
        }

        public static string ItemCode(int kind, int num)
        {
            switch (kind)
            {
                case 0: return "weapon:" + num;
                case 1: return "crestring:" + num;
                case 2: return "UseItem:" + num;
                case 3: return "costume:" + num;
                default: return null;
            }
        }

        /// <summary>Adds these locations to the table (idempotent).</summary>
        public static void Register(LocationTable table)
        {
            Load();
            foreach (var e in Entries)
            {
                if (table.Get(e.Key) != null) continue;
                table.AddVirtual(new LocationInfo
                {
                    Key = e.Key, Category = e.Category, Scene = "(" + e.Label + ")", A = e.Actor, B = e.Num,
                    Vanilla = ItemCode(e.Kind, e.Num), School = string.Empty, MagicName = string.Empty, DisplayJp = string.Empty,
                });
            }
        }
    }
}
