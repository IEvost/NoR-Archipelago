using System.Collections.Generic;

namespace NoR.Randomizer
{
    /// <summary>
    /// Offline placement, "vanilla pool shuffle": the items originally found at the enabled locations
    /// are permuted among those locations. No logic graph yet (comes with Milestone 1); safety rules:
    ///   - lockpick wires (UseItem:4) never go into chests, so all picks stay obtainable without picks.
    /// Uses its own PRNG so results do not depend on the runtime's System.Random implementation.
    /// </summary>
    public static class Placement
    {
        public const string Lockpick = "UseItem:4";

        /// <summary>
        /// Door keys checked by possession (docs/research-locations.md, category F): lab key 60, pilgrimage key 61,
        /// rooftop key 62. Without a logic graph they stay at their vanilla locations to prevent softlocks.
        /// </summary>
        public static readonly string[] ProgressionItems = { "UseItem:60", "UseItem:61", "UseItem:62" };

        public static Dictionary<string, string> Generate(RandoState st, LocationTable table)
        {
            table.EnsureShopSlots(st.ShopSlots);
            var locs = new List<LocationInfo>();
            foreach (var l in table.All)
            {
                if (string.IsNullOrEmpty(l.Vanilla)) continue;
                if (IsProgression(l.Vanilla) || l.Category == "key") continue; // offline: no logic, keys stay vanilla
                if (l.Category == "shop" && l.B >= st.ShopSlots) continue;
                if (l.Category == "boss_drop" && !st.ShuffleBossDrops) continue;
                if (l.Category == "npc" && !st.ShuffleNpcRewards) continue;
                if (l.Category == "trade" && !st.ShuffleTrades) continue;
                if (l.Category == "floor_item" && !st.ShuffleFloorItems) continue;
                if (l.Category == "chest" && !st.ShuffleChests) continue;
                if (l.Category == "magic" && !st.ShuffleMagic) continue;
                locs.Add(l);
            }

            var items = new List<string>(locs.Count);
            foreach (var l in locs) items.Add(l.Vanilla);

            var rng = new SplitMix64(st.Seed);
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                var tmp = items[i]; items[i] = items[j]; items[j] = tmp;
            }

            // Repair: move lockpicks out of chests by swapping with a random non-chest, non-lockpick slot.
            for (int i = 0; i < locs.Count; i++)
            {
                if (locs[i].Category != "chest" || !ContainsLockpick(items[i])) continue;
                var candidates = new List<int>();
                for (int k = 0; k < locs.Count; k++)
                    if (locs[k].Category != "chest" && !ContainsLockpick(items[k])) candidates.Add(k);
                if (candidates.Count == 0) break;
                int pick = candidates[rng.NextInt(candidates.Count)];
                var tmp = items[i]; items[i] = items[pick]; items[pick] = tmp;
            }

            var result = new Dictionary<string, string>(locs.Count);
            for (int i = 0; i < locs.Count; i++) result[locs[i].Key] = items[i];
            return result;
        }

        /// <summary>
        /// Archipelago placement: every location the apworld includes maps to <see cref="RandoState.RemoteItem"/>.
        /// Must mirror apworld/nightofrevenge/__init__.py (_included) and NoR.ApWorldGen (vanilla-only keys).
        /// </summary>
        public static Dictionary<string, string> GenerateRemote(RandoState st, LocationTable table)
        {
            table.EnsureShopSlots(st.ShopSlots);
            var result = new Dictionary<string, string>();
            foreach (var l in table.All)
            {
                if (string.IsNullOrEmpty(l.Vanilla)) continue;
                bool keyLocation = IsProgression(l.Vanilla) || l.Category == "key";
                if (keyLocation && !st.ShuffleKeys) continue; // keys stay vanilla
                if (l.Category == "shop" && l.B >= st.ShopSlots) continue;
                if (l.Category == "boss_drop" && !st.ShuffleBossDrops) continue;
                if (l.Category == "npc" && !st.ShuffleNpcRewards) continue;
                if (l.Category == "trade" && !st.ShuffleTrades) continue;
                result[l.Key] = RandoState.RemoteItem;
            }
            return result;
        }

        private static bool IsProgression(string code)
        {
            foreach (var part in code.Split('+'))
                foreach (var p in ProgressionItems)
                    if (part == p) return true;
            return false;
        }

        private static bool ContainsLockpick(string code)
        {
            foreach (var part in code.Split('+')) if (part == Lockpick) return true;
            return false;
        }
    }

    /// <summary>SplitMix64 PRNG (deterministic across runtimes).</summary>
    public sealed class SplitMix64
    {
        private ulong _s;
        public SplitMix64(ulong seed) { _s = seed; }

        public ulong Next()
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextInt(int exclusiveMax)
        {
            return (int)(Next() % (ulong)exclusiveMax);
        }
    }
}
