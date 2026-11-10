using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoR.Randomizer
{
    public sealed class LocationInfo
    {
        public string Key;
        public string Category;   // floor_item | chest | magic
        public string Scene;
        public int A;             // Pickup_item: stage    | Pickup_magic: school index
        public int B;             // Pickup_item: flag num | Pickup_magic: flag num
        public string Vanilla;    // item code, see ItemCode
        public string School;     // magic only
        public string MagicName;  // magic only
        public string DisplayJp;
        public int MagicSlot = -1; // magic only: column in the spare flag row (see LocationTable.MagicCheckRow)
    }

    /// <summary>Location table embedded from data/runtime/locations.tsv.</summary>
    public sealed class LocationTable
    {
        /// <summary>
        /// Unused row of game_fragmng._TreasureItemArrayStage (rows 1..18 are used by the game in 1.0.7).
        /// Magic pickup locations store their "checked" state here, so it is saved by the vanilla save code.
        /// </summary>
        public const int MagicCheckRow = 19;

        public static readonly string[] MagicSchools = { "GUN", "Fire", "Lightning", "Sacred", "darkness" };

        public readonly List<LocationInfo> All = new List<LocationInfo>();
        private readonly Dictionary<string, LocationInfo> _pickups = new Dictionary<string, LocationInfo>();
        private readonly Dictionary<string, LocationInfo> _magic = new Dictionary<string, LocationInfo>();
        private readonly Dictionary<string, LocationInfo> _byKey = new Dictionary<string, LocationInfo>();
        private readonly Dictionary<string, LocationInfo> _magicByItem = new Dictionary<string, LocationInfo>();

        public static LocationTable LoadEmbedded()
        {
            using (var s = typeof(LocationTable).Assembly.GetManifestResourceStream("NoR.Randomizer.locations.tsv"))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                return Parse(r.ReadToEnd());
            }
        }

        public static LocationTable Parse(string tsv)
        {
            var t = new LocationTable();
            int magicSlot = 0;
            foreach (var raw in tsv.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c.Length < 9) continue;
                var l = new LocationInfo
                {
                    Key = c[0], Category = c[1], Scene = c[2],
                    A = int.Parse(c[3]), B = int.Parse(c[4]),
                    Vanilla = c[5], School = c[6], MagicName = c[7], DisplayJp = c[8],
                };
                t.All.Add(l);
                t._byKey[l.Key] = l;
                if (l.Category == "magic")
                {
                    l.MagicSlot = magicSlot++;
                    t._magic[l.Scene + "|" + l.School + "|" + l.B] = l;
                    t._magicByItem[l.Vanilla] = l;
                }
                else
                {
                    t._pickups[l.Scene + "|" + l.A + "|" + l.B] = l;
                }
            }
            if (magicSlot > 20) throw new InvalidOperationException("Too many magic locations for the spare flag row: " + magicSlot);
            return t;
        }

        /// <summary>Unused row of _TreasureItemArrayStage holding the "bought" state of shop slots.</summary>
        public const int ShopCheckRow = 0;
        public const int MaxShopSlots = 20;

        /// <summary>
        /// Items the shop slots contribute to the pool (vanilla shop stock: shards, lockpicks, sewing kit, talismans).
        /// Two lockpicks bring the world total to 15, exactly what all locked chests need.
        /// </summary>
        public static readonly string[] ShopPoolItems = LoadShopPool(); // data/shop_pool.txt (shared with the apworld)

        private static string[] LoadShopPool()
        {
            var list = new List<string>();
            using (var s = typeof(LocationTable).Assembly.GetManifestResourceStream("NoR.Randomizer.shop_pool.txt"))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length > 0 && line[0] != '#') list.Add(line);
                }
            }
            return list.ToArray();
        }

        public readonly List<LocationInfo> ShopSlots = new List<LocationInfo>();

        /// <summary>Adds a location that is not part of the scene table (code rewards, trades).</summary>
        public void AddVirtual(LocationInfo l)
        {
            All.Add(l);
            _byKey[l.Key] = l;
        }

        /// <summary>Adds the virtual shop-slot locations (not part of the scene table).</summary>
        public void EnsureShopSlots(int count)
        {
            count = Math.Min(count, MaxShopSlots);
            while (ShopSlots.Count < count)
            {
                int i = ShopSlots.Count;
                var l = new LocationInfo
                {
                    Key = "shop:" + i, Category = "shop", Scene = "(shop)", A = ShopCheckRow, B = i,
                    Vanilla = ShopPoolItems[i], School = string.Empty, MagicName = string.Empty, DisplayJp = string.Empty,
                };
                ShopSlots.Add(l);
                All.Add(l);
                _byKey[l.Key] = l;
            }
        }

        public LocationInfo FindPickup(string scene, int stage, int num)
        {
            LocationInfo l;
            return _pickups.TryGetValue(scene + "|" + stage + "|" + num, out l) ? l : null;
        }

        public LocationInfo FindMagic(string scene, string school, int num)
        {
            LocationInfo l;
            if (_magic.TryGetValue(scene + "|" + school + "|" + num, out l)) return l;
            // Copies of a magic pickup spawned elsewhere (e.g. Candore's fallback drop of Sacred[1] in her boss scene)
            // share the same flag, so they are the same location.
            return _magicByItem.TryGetValue("magic:" + school + ":" + num, out l) ? l : null;
        }

        public LocationInfo Get(string key)
        {
            LocationInfo l;
            return _byKey.TryGetValue(key, out l) ? l : null;
        }

        /// <summary>Metadata (magic name, display name) for a magic item code "magic:School:num".</summary>
        public LocationInfo MagicSource(string itemCode)
        {
            LocationInfo l;
            return _magicByItem.TryGetValue(itemCode, out l) ? l : null;
        }
    }
}
