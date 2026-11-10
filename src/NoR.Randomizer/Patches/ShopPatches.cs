using HarmonyLib;
using NoR.Randomizer.Shop;
using UnityEngine;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// Opens the Witch's Market while standing at a save point: keyboard key (config, default B) or the
    /// gamepad "item switch" action (Rewired "Itemcursor").
    /// </summary>
    [HarmonyPatch(typeof(Savepoint_on), "Update")]
    internal static class SavepointShopPatch
    {
        internal static bool PlayerAtSavepoint;
        private static int _lastFrame;

        private static void Postfix(bool ___stay, bool ___onof, playercon ___playcom, PlayerStatus ___playsta)
        {
            if (Plugin.State == null || !Plugin.ShopEnabled || Plugin.State.ShopSlots < 0) return;
            if (!___stay || ___onof || ___playcom == null || ___playsta == null) return;

            PlayerAtSavepoint = true;
            _lastFrame = Time.frameCount;

            if (ShopWindow.Current != null || Time.unscaledTime - ShopWindow.ClosedAt < 0.5f) return;
            if (!___playsta._SOUSA || (___playcom.state != "IDLE" && ___playcom.state != "WALK")) return;

            bool pressed = Input.GetKeyDown(Plugin.ShopKey);
            if (!pressed)
            {
                var rp = Traverse.Create(___playcom).Field("player").GetValue<Rewired.Player>();
                pressed = rp != null && rp.GetButtonDown("Itemcursor");
            }
            if (pressed) ShopWindow.Open();
        }

        /// <summary>True only while a save point reported the player standing at it this or last frame.</summary>
        internal static bool AtSavepointNow { get { return PlayerAtSavepoint && Time.frameCount - _lastFrame <= 1; } }
    }
}
