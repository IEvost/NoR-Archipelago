using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using NoR.Core;
using NoR.Randomizer;
using UnityEngine;

namespace NoR.Archipelago
{
    /// <summary>
    /// Game-side Archipelago logic: slot setup, location checks, item delivery, resync and goal.
    /// All methods run on the Unity main thread (network callbacks are marshalled through Connection.Post).
    /// </summary>
    public sealed class ApClient
    {
        public const string GameName = "Night of Revenge";

        /// <summary>Must equal DATA_VERSION in the generated apworld data (tools/NoR.ApWorldGen DataVersion).</summary>
        public const int ExpectedDataVersion = 3;

        private readonly Connection _conn;
        private readonly ApMap _map;
        private readonly Notifications _notes;
        private readonly Dictionary<long, ScoutedItemInfo> _scouted = new Dictionary<long, ScoutedItemInfo>();
        private RandoState _template;     // slot settings from the server, used for new games
        private string _server, _slot, _password;
        private float _nextGrant, _nextReconnect;
        private bool _wantConnected;

        public bool Connected { get { return _conn.Session != null && _conn.Session.Socket.Connected; } }
        public readonly DeathLinkHandler DeathLink;
        public string SlotName { get { return _slot; } }

        private readonly ServerLogFeed _log;

        public ApClient(Connection conn, ApMap map, Notifications notes, string pluginDir)
        {
            _conn = conn;
            _map = map;
            _notes = notes;
            _log = new ServerLogFeed(pluginDir);
            notes.ClientNote = (text, error) => _log.OnClientNote(text, error);
            DebugConsole.ChatSender = text =>
            {
                if (!Connected) return false;
                Say(text);
                return true;
            };
            DeathLink = new DeathLinkHandler(notes);
            Checks.RemoteHandler = OnLocationChecked;
            Checks.RemoteDescribe = DescribeLocation;
            Checks.RemotePrice = PriceOf;
            NoR.Randomizer.Plugin.NewGameStateProvider = NewGameState;
            NoR.Randomizer.Plugin.StateActivated += OnStateActivated;
        }

        // ------------------------------------------------------------------ connection

        public void Connect(string server, string slot, string password)
        {
            _server = server.Trim();
            _slot = slot.Trim();
            _password = password;
            _wantConnected = true;
            _conn.ConnectAsync(_server, GameName, _slot, _password, new string[0], ItemsHandlingFlags.AllItems,
                Plugin.BridgeMode, OnLoginResult);
        }

        public void Disconnect()
        {
            _wantConnected = false;
            DeathLink.Detach();
            _conn.Disconnect();
            _template = null;
            _scouted.Clear();
        }

        private void OnLoginResult(LoginResult result)
        {
            var ok = result as LoginSuccessful;
            if (ok == null)
            {
                string err = string.Join("; ", ((LoginFailure)result).Errors);
                _notes.Add("Archipelago: connection failed: " + err, Notifications.Kind.Error);
                Plugin.Log.LogError("Login failed: " + err);
                _nextReconnect = Time.unscaledTime + 15f;
                return;
            }

            var session = _conn.Session;
            _log.UseSeed(session.RoomState.Seed);
            session.MessageLog.OnMessageReceived += msg => _conn.Post(() =>
            {
                _log.OnMessage(msg);
                OnServerMessage(msg);
            });
            session.Socket.SocketClosed += reason => _conn.Post(() =>
            {
                _notes.Add("Archipelago: disconnected (" + reason + ")", Notifications.Kind.Error);
                _nextReconnect = Time.unscaledTime + 5f;
            });

            long worldVersion = Long(ok.SlotData, "version", 0);
            if (worldVersion > ExpectedDataVersion) // older worlds stay compatible; a newer world needs a newer mod
            {
                _notes.Add("Archipelago: the seed was generated with a newer apworld (data version " + worldVersion + ") than this mod supports (" +
                           ExpectedDataVersion + "). Update the mod.", Notifications.Kind.Error);
                Plugin.Log.LogWarning("Data version mismatch: world " + worldVersion + ", client " + ExpectedDataVersion);
            }
            _template = BuildTemplate(ok.SlotData, session.RoomState.Seed);
            DeathLink.Attach(session, _slot, Long(ok.SlotData, "death_link", 0) != 0,
                (DeathLinkHandler.Mode)Long(ok.SlotData, "death_link_mode", 0), _conn);
            _notes.Add("Archipelago: connected as " + _slot + " (" + session.Locations.AllLocations.Count + " locations)", Notifications.Kind.Info);
            Plugin.Log.LogInfo("Connected. Seed " + session.RoomState.Seed + ", slot data: " + _template);

            ScoutAll();

            var st = NoR.Randomizer.Plugin.State;
            if (st != null && st.Remote)
            {
                if (st.ApSeedName != session.RoomState.Seed || !string.Equals(st.ApSlot, _slot, StringComparison.OrdinalIgnoreCase))
                {
                    _notes.Add("Archipelago: this save belongs to slot " + st.ApSlot + " of another seed - disconnected.", Notifications.Kind.Error);
                    Disconnect();
                    return;
                }
                // Same seed and slot on a new address (archipelago.gg rooms change port after idling): remember it.
                if (!string.Equals(st.ApServer, _server, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo("Server address updated: " + st.ApServer + " -> " + _server);
                    st.ApServer = _server;
                }
                Resync(st);
            }
        }

        /// <summary>Reconnect automatically while a remote save is being played.</summary>
        public void Update()
        {
            var st = NoR.Randomizer.Plugin.State;
            if (_wantConnected && !Connected && !_conn.Connecting && Time.unscaledTime > _nextReconnect && st != null && st.Remote)
            {
                _nextReconnect = Time.unscaledTime + 15f;
                Plugin.Log.LogInfo("Reconnecting...");
                _conn.ConnectAsync(_server, GameName, _slot, _password, new string[0], ItemsHandlingFlags.AllItems, Plugin.BridgeMode, OnLoginResult);
            }
            DeliverItems(st);
            if (st != null && st.Remote && Connected) DeathLink.Update();
        }

        private RandoState BuildTemplate(Dictionary<string, object> slotData, string seedName)
        {
            return new RandoState
            {
                Remote = true,
                ApSeedName = seedName,
                ApSlot = _slot,
                ApServer = _server,
                Seed = (ulong)Math.Abs(Long(slotData, "seed", 1)),
                ShuffleFloorItems = true,
                ShuffleChests = true,
                ShuffleMagic = true,
                RandomizeEnemies = Long(slotData, "enemy_randomizer", 1) != 0,
                ShopSlots = (int)Long(slotData, "shop_slots", 10),
                ShuffleBossDrops = Long(slotData, "boss_drops", 1) != 0,
                ShuffleNpcRewards = Long(slotData, "npc_rewards", 1) != 0,
                ShuffleTrades = Long(slotData, "trades", 1) != 0,
                ShuffleKeys = Long(slotData, "shuffle_keys", 0) != 0,
            };
        }

        private static long Long(Dictionary<string, object> d, string key, long fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v ? 1 : 0;
            try { return Convert.ToInt64(v); }
            catch { return fallback; }
        }

        /// <summary>Randomizer asks for a new-game state: an Archipelago state while connected at the title screen.</summary>
        private RandoState NewGameState()
        {
            if (!Connected || _template == null) return null;
            var t = _template;
            _notes.Add("Archipelago: new game for slot " + _slot, Notifications.Kind.Info);
            return new RandoState
            {
                Remote = true, ApSeedName = t.ApSeedName, ApSlot = t.ApSlot, ApServer = t.ApServer, Seed = t.Seed,
                RandomizeEnemies = t.RandomizeEnemies, ShopSlots = t.ShopSlots,
                ShuffleBossDrops = t.ShuffleBossDrops, ShuffleNpcRewards = t.ShuffleNpcRewards, ShuffleTrades = t.ShuffleTrades,
                ShuffleKeys = t.ShuffleKeys,
            };
        }

        /// <summary>A save was loaded or a new game started.</summary>
        private void OnStateActivated(RandoState st)
        {
            _nextGrant = Time.unscaledTime + 3f; // let the scene settle before handing out items
            if (!st.Remote) return;
            if (Connected && _conn.Session.RoomState.Seed == st.ApSeedName) { Resync(st); return; }
            // Loaded an Archipelago save without (the right) connection: connect to the stored server/slot.
            if (Connected) _conn.Disconnect();
            _notes.Add("Archipelago: connecting to " + st.ApServer + " as " + st.ApSlot + "...", Notifications.Kind.Info);
            Connect(st.ApServer, st.ApSlot, Plugin.Password);
        }

        // ------------------------------------------------------------------ checks

        private void OnLocationChecked(string key)
        {
            long id;
            if (!_map.LocationId.TryGetValue(key, out id))
            {
                Plugin.Log.LogWarning("No Archipelago id for location " + key);
                return;
            }
            ScoutedItemInfo info;
            if (_scouted.TryGetValue(id, out info))
            {
                bool own = info.Player != null && info.Player.Slot == _conn.Session.ConnectionInfo.Slot;
                if (!own) _notes.Add("Sent " + info.ItemDisplayName + " to " + (info.Player != null ? info.Player.Name : "?"), Notifications.Kind.Sent);
            }
            if (Connected) _conn.Session.Locations.CompleteLocationChecksAsync(null, id);
            else _notes.Add("Offline: check will be sent when reconnected", Notifications.Kind.Error);
        }

        /// <summary>After (re)connecting: report everything this save has already checked (server ignores duplicates).</summary>
        private void Resync(RandoState st)
        {
            var ids = new List<long>();
            foreach (var key in st.ApSent)
            {
                long id;
                if (_map.LocationId.TryGetValue(key, out id)) ids.Add(id);
            }
            if (ids.Count > 0) _conn.Session.Locations.CompleteLocationChecksAsync(null, ids.ToArray());
            if (st.ApGoal) _conn.Session.SetGoalAchieved();
            Plugin.Log.LogInfo("Resync: " + ids.Count + " checks, received index " + st.ApItemIndex + "/" + _conn.Session.Items.AllItemsReceived.Count);
        }

        /// <summary>Sends a chat message or server command (e.g. "!hint item") to the room.</summary>
        public void Say(string text)
        {
            if (!Connected) return;
            try { _conn.Session.Say(text); }
            catch (Exception e) { _notes.Add("Could not send: " + e.Message, Notifications.Kind.Error); }
        }

        public void ReportGoal()
        {
            var st = NoR.Randomizer.Plugin.State;
            if (st == null || !st.Remote) return;
            st.ApGoal = true;
            _notes.Add("Goal complete!", Notifications.Kind.Received);
            if (Connected) _conn.Session.SetGoalAchieved();
        }

        // ------------------------------------------------------------------ items

        /// <summary>Hands out received items one at a time while the player is in control.</summary>
        private void DeliverItems(RandoState st)
        {
            if (st == null || !st.Remote || !Connected || Time.unscaledTime < _nextGrant) return;
            if (!SafeToGrant()) return;
            var all = _conn.Session.Items.AllItemsReceived;
            if (st.ApItemIndex >= all.Count) return;

            var item = all[st.ApItemIndex];
            st.ApItemIndex++;
            _nextGrant = Time.unscaledTime + 0.8f;

            string code;
            if (!_map.ItemCode.TryGetValue(item.ItemId, out code))
            {
                Plugin.Log.LogWarning("Unknown item id " + item.ItemId + " - skipped");
                return;
            }
            ItemGranter.LastTrapMessage = null;
            ItemGranter.Grant(code, NoR.Randomizer.Plugin.Table);
            string from = item.Player != null && item.Player.Slot != _conn.Session.ConnectionInfo.Slot ? " from " + item.Player.Name : string.Empty;
            bool trap = code.StartsWith("trap:", StringComparison.Ordinal);
            _notes.Add((trap ? "Trap! " : "Received ") + item.ItemDisplayName + from, trap ? Notifications.Kind.Error : Notifications.Kind.Received);
            if (trap && !string.IsNullOrEmpty(ItemGranter.LastTrapMessage)) _notes.Add(ItemGranter.LastTrapMessage, Notifications.Kind.Error);
        }

        private static bool SafeToGrant()
        {
            if (!GameRefs.InGame) return false;
            var status = GameRefs.Status;
            if (status == null || !status._SOUSA) return false;             // menus, events, H-scenes, loading
            if (NoR.Randomizer.Shop.ShopWindow.Current != null) return false;
            return GameObject.Find("ItemGet_info") != null;
        }

        // ------------------------------------------------------------------ server messages

        /// <summary>
        /// Shows chat, hints and server notices. Item sends/receives involving this slot are already shown by the
        /// check/delivery code, so ItemSend messages are skipped here.
        /// </summary>
        private void OnServerMessage(global::Archipelago.MultiClient.Net.MessageLog.Messages.LogMessage msg)
        {
            if (!Plugin.ShowServerMessages) return;
            if (msg is global::Archipelago.MultiClient.Net.MessageLog.Messages.ItemSendLogMessage
                && !(msg is global::Archipelago.MultiClient.Net.MessageLog.Messages.HintItemSendLogMessage)) return;
            string text = msg.ToString();
            if (string.IsNullOrEmpty(text)) return;
            var kind = msg is global::Archipelago.MultiClient.Net.MessageLog.Messages.HintItemSendLogMessage ? Notifications.Kind.Sent : Notifications.Kind.Info;
            _notes.Add(text, kind, toServerLog: false); // already in the server log via ServerLogFeed
        }

        // ------------------------------------------------------------------ scouting (names for messages and the shop)

        private void ScoutAll()
        {
            var ids = new List<long>(_conn.Session.Locations.AllLocations);
            if (ids.Count == 0) return;
            _conn.Session.Locations.ScoutLocationsAsync(result => _conn.Post(() =>
            {
                foreach (var kv in result) _scouted[kv.Key] = kv.Value;
                Plugin.Log.LogInfo("Scouted " + result.Count + " locations");
            }), HintCreationPolicy.None, ids.ToArray());
        }

        private string DescribeLocation(string key)
        {
            long id;
            ScoutedItemInfo info;
            if (!_map.LocationId.TryGetValue(key, out id) || !_scouted.TryGetValue(id, out info)) return "Archipelago item";
            bool own = info.Player != null && Connected && info.Player.Slot == _conn.Session.ConnectionInfo.Slot;
            return info.ItemDisplayName + (own || info.Player == null ? string.Empty : " (" + info.Player.Name + ")");
        }

        private int PriceOf(string key)
        {
            long id;
            ScoutedItemInfo info;
            if (!_map.LocationId.TryGetValue(key, out id) || !_scouted.TryGetValue(id, out info)) return 1500;
            if ((info.Flags & ItemFlags.Advancement) != 0) return 3000;
            if ((info.Flags & ItemFlags.NeverExclude) != 0) return 2000;
            if ((info.Flags & ItemFlags.Trap) != 0) return 500;
            return 1000;
        }
    }
}
