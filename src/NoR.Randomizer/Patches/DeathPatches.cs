using HarmonyLib;
using NoR.Core;

namespace NoR.Randomizer.Patches
{
    /// <summary>
    /// The defeat banner and the press-any-key arming are only valid while Aradia is dead. Vanilla only calls them in
    /// that state; after a skip-respawn, delayed calls could still arrive, so they are refused while she is alive.
    /// </summary>
    [HarmonyPatch(typeof(map_title), "text_DEATH")]
    internal static class DefeatBannerGuard
    {
        private static bool Prefix() { return PlayerIsDead(); }

        internal static bool PlayerIsDead()
        {
            var go = GameRefs.Player;
            var pc = go != null ? go.GetComponent<playercon>() : null;
            return pc == null || pc._Death; // no player: keep vanilla behavior
        }
    }

    [HarmonyPatch(typeof(map_title), "GOeny_set")]
    internal static class AnyKeyGuard
    {
        private static bool Prefix() { return DefeatBannerGuard.PlayerIsDead(); }
    }
}
