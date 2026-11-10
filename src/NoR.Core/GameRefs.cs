using UnityEngine;

namespace NoR.Core
{
    /// <summary>
    /// Cached access to the game's central singletons. They all live on the object tagged
    /// "GameController" (game_fragmng, PlayerStatus, ItemInventory) and the "Player" object.
    /// The cache re-resolves automatically after scene changes destroy the old instances.
    /// </summary>
    public static class GameRefs
    {
        private static GameObject _controller;
        private static game_fragmng _flags;
        private static PlayerStatus _status;
        private static ItemInventory _inventory;
        private static GameObject _player;

        public static GameObject Controller
        {
            get
            {
                if (_controller == null) _controller = GameObject.FindWithTag("GameController");
                return _controller;
            }
        }

        public static game_fragmng Flags
        {
            get
            {
                if (_flags == null && Controller != null) _flags = Controller.GetComponent<game_fragmng>();
                return _flags;
            }
        }

        public static PlayerStatus Status
        {
            get
            {
                if (_status == null && Controller != null) _status = Controller.GetComponent<PlayerStatus>();
                return _status;
            }
        }

        public static ItemInventory Inventory
        {
            get
            {
                if (_inventory == null && Controller != null) _inventory = Controller.GetComponent<ItemInventory>();
                return _inventory;
            }
        }

        public static GameObject Player
        {
            get
            {
                if (_player == null) _player = GameObject.FindWithTag("Player");
                return _player;
            }
        }

        public static bool InGame { get { return Flags != null && Player != null; } }
    }
}
