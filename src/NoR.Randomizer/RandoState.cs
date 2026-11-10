using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NoR.Randomizer
{
    /// <summary>
    /// Per-save randomizer settings, stored in the save slot (ES2 tag "NoR_Rando").
    ///
    /// Offline games: immutable after a new game starts; the placement is derived deterministically from it and the
    /// "checked" state of locations lives in the game's own flags.
    ///
    /// Archipelago games (Remote = true): placement values are <see cref="RemoteItem"/> (contents live on the server),
    /// and the save also records the AP session (seed name, slot, server), the index of the next received item to
    /// grant, the locations already sent and whether the goal was reported. Because all of that is written together
    /// with the game's own save, reloading a save never duplicates or loses items.
    /// </summary>
    public sealed class RandoState
    {
        public const int FormatVersion = 1;
        public const string RemoteItem = "remote";

        public ulong Seed;
        public bool ShuffleFloorItems = true;
        public bool ShuffleChests = true;
        public bool ShuffleMagic = true;
        public bool RandomizeEnemies = true;
        public int ShopSlots = 0;
        public bool ShuffleBossDrops, ShuffleNpcRewards, ShuffleTrades;

        // ---- Archipelago
        public bool Remote;
        public string ApSeedName = string.Empty, ApSlot = string.Empty, ApServer = string.Empty;
        public int ApItemIndex;                                  // received items [0, ApItemIndex) are already granted
        public readonly HashSet<string> ApSent = new HashSet<string>(StringComparer.Ordinal); // location keys
        public bool ApGoal;

        /// <summary>location key -> item code (or RemoteItem)</summary>
        public Dictionary<string, string> Placement;

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "v={0};seed={1};floor={2};chest={3};magic={4};enemy={5};shop={6};boss={7};npc={8};trade={9}",
                FormatVersion, Seed, B(ShuffleFloorItems), B(ShuffleChests), B(ShuffleMagic), B(RandomizeEnemies), ShopSlots,
                B(ShuffleBossDrops), B(ShuffleNpcRewards), B(ShuffleTrades));
            if (Remote)
            {
                sb.Append(";ap=1;apseed=").Append(Enc(ApSeedName)).Append(";apslot=").Append(Enc(ApSlot)).Append(";apserver=").Append(Enc(ApServer));
                sb.Append(";apidx=").Append(ApItemIndex.ToString(CultureInfo.InvariantCulture)).Append(";apgoal=").Append(B(ApGoal));
                var keys = new List<string>(ApSent);
                sb.Append(";apsent=").Append(Enc(string.Join("\n", keys.ToArray())));
            }
            return sb.ToString();
        }

        public static RandoState Parse(string s)
        {
            var st = new RandoState();
            var kv = new Dictionary<string, string>();
            foreach (var part in s.Split(';'))
            {
                int i = part.IndexOf('=');
                if (i > 0) kv[part.Substring(0, i)] = part.Substring(i + 1);
            }
            string v;
            if (!kv.TryGetValue("v", out v) || v != FormatVersion.ToString(CultureInfo.InvariantCulture))
                throw new FormatException("Unsupported randomizer save format: " + s);
            st.Seed = ulong.Parse(kv["seed"], CultureInfo.InvariantCulture);
            st.ShuffleFloorItems = kv["floor"] == "1";
            st.ShuffleChests = kv["chest"] == "1";
            st.ShuffleMagic = kv["magic"] == "1";
            st.RandomizeEnemies = Flag(kv, "enemy");
            string shop;
            int slots;
            st.ShopSlots = kv.TryGetValue("shop", out shop) && int.TryParse(shop, NumberStyles.Integer, CultureInfo.InvariantCulture, out slots) ? slots : 0;
            // Missing keys = saves created before the option existed: keep their placement unchanged.
            st.ShuffleBossDrops = Flag(kv, "boss");
            st.ShuffleNpcRewards = Flag(kv, "npc");
            st.ShuffleTrades = Flag(kv, "trade");

            st.Remote = Flag(kv, "ap");
            if (st.Remote)
            {
                st.ApSeedName = Dec(Get(kv, "apseed"));
                st.ApSlot = Dec(Get(kv, "apslot"));
                st.ApServer = Dec(Get(kv, "apserver"));
                int idx;
                st.ApItemIndex = int.TryParse(Get(kv, "apidx"), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) ? idx : 0;
                st.ApGoal = Flag(kv, "apgoal");
                foreach (var k in Dec(Get(kv, "apsent")).Split('\n'))
                    if (k.Length > 0) st.ApSent.Add(k);
            }
            return st;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Remote) sb.Append("Archipelago ").Append(ApSlot).Append(" @ ").Append(ApServer).Append(", ");
            sb.Append("seed ").Append(Seed);
            sb.Append(" (floor=").Append(ShuffleFloorItems).Append(", chests=").Append(ShuffleChests).Append(", magic=").Append(ShuffleMagic)
              .Append(", enemies=").Append(RandomizeEnemies).Append(", shop slots=").Append(ShopSlots)
              .Append(", bosses=").Append(ShuffleBossDrops).Append(", npcs=").Append(ShuffleNpcRewards).Append(", trades=").Append(ShuffleTrades).Append(')');
            return sb.ToString();
        }

        private static bool Flag(Dictionary<string, string> kv, string k) { string v; return kv.TryGetValue(k, out v) && v == "1"; }
        private static string Get(Dictionary<string, string> kv, string k) { string v; return kv.TryGetValue(k, out v) ? v : string.Empty; }
        private static string B(bool b) { return b ? "1" : "0"; }
        private static string Enc(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty)); }
        private static string Dec(string s) { return s.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(s)); }

        public static ulong NewSeed()
        {
            // 9 decimal digits: easy to read out and type in.
            return (ulong)(Math.Abs(Guid.NewGuid().GetHashCode()) % 1000000000);
        }
    }
}
