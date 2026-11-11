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

        /// <summary>
        /// Locations collected in the current game session. Some game events call their reward method many times
        /// (EvSuzuneTalk invokes Itemset on every frame while a dialogue line is shown) - each location must count once.
        /// Reset when a new state is activated (new game / load).
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> CollectedThisSession = new System.Collections.Generic.HashSet<string>();
        private static RandoState _sessionState;

        public static bool Collect(string locationKey, string item)
        {
            var current = Plugin.State;
            if (!ReferenceEquals(current, _sessionState)) { CollectedThisSession.Clear(); _sessionState = current; }
            if (!CollectedThisSession.Add(locationKey)) return false;   // repeated call for the same location
            if (current != null && current.Remote && current.ApSent.Contains(locationKey)) return false; // sent before (earlier session)

            if (item == RandoState.RemoteItem)
            {
                var st = Plugin.State;
                if (st != null) st.ApSent.Add(locationKey);
                if (RemoteHandler != null) RemoteHandler(locationKey);
                else Plugin.Log.LogWarning("Remote check " + locationKey + " but no Archipelago client loaded");
                return true;
            }
            ItemGranter.Grant(item, Plugin.Table);
            return true;
        }
    }
}
