using System;

namespace NoR.Randomizer
{
    /// <summary>
    /// Single entry point for "the player just checked a location". Offline games grant the placed item;
    /// Archipelago games hand the location to <see cref="RemoteHandler"/> (set by the NoR.Archipelago plugin), and the
    /// item arrives later through the server's received-items list.
    /// </summary>
    public static class Checks
    {
        /// <summary>Called with the location key for remote (Archipelago) checks.</summary>
        public static Action<string> RemoteHandler;

        /// <summary>Display text for the item at a remote location (from scouting), e.g. "Hookshot (Link's world)".</summary>
        public static Func<string, string> RemoteDescribe;

        /// <summary>Shop price for a remote location (by item importance).</summary>
        public static Func<string, int> RemotePrice;

        public static void Collect(string locationKey, string item)
        {
            if (item == RandoState.RemoteItem)
            {
                var st = Plugin.State;
                if (st != null) st.ApSent.Add(locationKey);
                if (RemoteHandler != null) RemoteHandler(locationKey);
                else Plugin.Log.LogWarning("Remote check " + locationKey + " but no Archipelago client loaded");
                return;
            }
            ItemGranter.Grant(item, Plugin.Table);
        }
    }
}
