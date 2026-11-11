using System;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer
{
    /// <summary>
    /// Grants an item code through the game's own systems so inventory, UI popup and save all behave as vanilla.
    /// Item codes: "weapon:N", "crestring:N", "UseItem:N" (N = runtime list index), "magic:School:num";
    /// bundles are joined with '+'.
    /// </summary>
    public static class ItemGranter
    {
        /// <summary>Message of the last trap applied (shown by the Archipelago client).</summary>
        public static string LastTrapMessage;

        public static void Grant(string code, LocationTable table)
        {
            var inv = GameRefs.Inventory;
            var canvasObj = GameObject.Find("ItemGet_info");
            var canvas = canvasObj != null ? canvasObj.GetComponent<ItemGetCanvas>() : null;
            if (inv == null || canvas == null)
            {
                Plugin.Log.LogError("Cannot grant " + code + ": inventory or ItemGet_info not found");
                return;
            }

            int wp = -1, ring = -1, use = -1;
            foreach (var part in code.Split('+'))
            {
                var p = part.Split(':');
                switch (p[0])
                {
                    case "weapon": wp = int.Parse(p[1]); break;
                    case "crestring": ring = int.Parse(p[1]); break;
                    case "UseItem": use = int.Parse(p[1]); break;
                    case "magic": GrantMagic(part, p, table, canvas); break;
                    case "trap":
                        LastTrapMessage = Traps.Apply(p[1]);
                        break;
                    case "costume":
                        // Cosmetic skin unlock (trades 1 and 9 in vanilla); no popup exists for it in the game.
                        GameRefs.Flags._CosSkinflag[int.Parse(p[1])] = true;
                        Plugin.Log.LogInfo("Costume skin " + p[1] + " unlocked");
                        break;
                    default: Plugin.Log.LogError("Unknown item code " + part); break;
                }
            }
            if (wp >= 0 || ring >= 0 || use >= 0)
                canvas.item_get(inv, wp, ring, use, -1);
        }

        private static void GrantMagic(string code, string[] p, LocationTable table, ItemGetCanvas canvas)
        {
            var flags = GameRefs.Flags;
            int num = int.Parse(p[2]);
            bool[] arr = MagicArray(flags, p[1]);
            if (arr == null) { Plugin.Log.LogError("Unknown magic school in " + code); return; }
            arr[num] = true;

            var src = table.MagicSource(code);
            canvas.magic_get(src != null ? src.MagicName : string.Empty, src != null ? NoR.Core.TextHooks.T(src.DisplayJp) : code);
        }

        public static bool[] MagicArray(game_fragmng flags, string school)
        {
            switch (school)
            {
                case "GUN": return flags._GetMagic_GUN;
                case "Fire": return flags._GetMagic_Fire;
                case "Lightning": return flags._GetMagic_Lightning;
                case "Sacred": return flags._GetMagic_Sacred;
                case "darkness": return flags._GetMagic_darkness;
                default: return null;
            }
        }
    }
}
