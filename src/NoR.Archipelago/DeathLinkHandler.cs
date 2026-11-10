using System;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Archipelago
{
    /// <summary>
    /// DeathLink (D6).
    ///   Outgoing: Aradia's death (playercon._Death rising edge) is sent to the other players.
    ///   Incoming, mode defeat_state: Aradia is knocked down (vanilla DOWN state) and cannot get up until an enemy
    ///     grabs her (eroflag) or the player gives up (G / hold the jump button 2 s) -> playercon.SpDeath().
    ///   Incoming, mode instant_respawn: playercon.SpDeath() immediately (normal game over, respawn at save point).
    /// Deaths caused by an incoming DeathLink are not sent back.
    /// </summary>
    public sealed class DeathLinkHandler
    {
        public enum Mode { DefeatState = 0, InstantRespawn = 1 }

        private readonly Notifications _notes;
        private DeathLinkService _service;
        private string _slot;
        private Mode _mode;
        private bool _wasDead;
        private float _suppressSendUntil;

        private bool _forcedDown;
        private bool _grabbed;
        private string _source;
        private GUIStyle _style;

        public DeathLinkHandler(Notifications notes) { _notes = notes; }

        public bool Enabled { get { return _service != null; } }

        public void Attach(ArchipelagoSession session, string slot, bool enabled, Mode mode, Connection conn)
        {
            Detach();
            if (!enabled) return;
            _slot = slot;
            _mode = mode;
            _service = session.CreateDeathLinkService();
            _service.OnDeathLinkReceived += dl => conn.Post(() => Incoming(dl));
            _service.EnableDeathLink();
            Plugin.Log.LogInfo("DeathLink enabled (" + mode + ")");
        }

        public void Detach()
        {
            if (_service != null) { try { _service.DisableDeathLink(); } catch { } }
            _service = null;
            _forcedDown = false;
        }

        // ------------------------------------------------------------------ per frame

        public void Update()
        {
            var pc = Player();
            if (pc == null) { _wasDead = false; _forcedDown = false; return; }

            bool dead = pc._Death;
            if (dead && !_wasDead) OnPlayerDied();
            _wasDead = dead;

            if (_forcedDown) HoldDown(pc);
        }

        private void OnPlayerDied()
        {
            _forcedDown = false;
            if (_service == null || Time.unscaledTime < _suppressSendUntil) return;
            try
            {
                _service.SendDeathLink(new DeathLink(_slot, _slot + " was defeated in the Night of Revenge."));
                _notes.Add("DeathLink sent", Notifications.Kind.Sent);
            }
            catch (Exception e) { Plugin.Log.LogError("DeathLink send failed: " + e.Message); }
        }

        private void Incoming(DeathLink dl)
        {
            var pc = Player();
            string who = dl.Source ?? "someone";
            if (pc == null || pc._Death || !GameRefs.InGame)
            {
                Plugin.Log.LogInfo("DeathLink from " + who + " ignored (not in control)");
                return;
            }
            _notes.Add("DeathLink from " + who + (string.IsNullOrEmpty(dl.Cause) ? string.Empty : ": " + dl.Cause), Notifications.Kind.Error);
            _suppressSendUntil = Time.unscaledTime + 60f; // the resulting death is not sent back

            if (_mode == Mode.InstantRespawn)
            {
                pc.SpDeath();   // normal death; the DeathSkip prompt then offers to skip the animation
                return;
            }
            _source = who;
            _forcedDown = true;
            _grabbed = false;
            KnockDown(pc);
        }

        // ------------------------------------------------------------------ defeat state

        private static void KnockDown(playercon pc)
        {
            pc.mutekitime = false;            // no invincibility: enemies must be able to grab her
            pc.nowdamage = true;
            if (pc.erodown == 0) pc.erodown = 1;
            var t = Traverse.Create(pc);
            t.Field("nockbackspeed").SetValue(0f);  // low knockback on the ground -> anime state "DOWN"
            t.Field("downup").SetValue(0);
        }

        /// <summary>
        /// A DeathLink always ends in death; the player only chooses between watching and skipping.
        ///   Down phase: Aradia cannot get up (the vanilla get-up needs downup to reach 2; we keep resetting it).
        ///   Grabbed phase: the game's grab sequence plays. Escape logic lives in each enemy's own code, so instead of
        ///     blocking it: if she is released while still alive, she dies right then.
        ///   Any time: G / hold Jump 2 s = skip straight to the respawn (shared DeathSkip).
        /// Once she is dead, the general DeathSkip prompt takes over.
        /// </summary>
        private void HoldDown(playercon pc)
        {
            _suppressSendUntil = Time.unscaledTime + 60f; // whatever happens now is this DeathLink's death
            if (pc._Death)
            {
                _forcedDown = false;
                _grabbed = false;
                return;
            }
            if (pc.eroflag)
            {
                _grabbed = true;
            }
            else if (_grabbed)
            {
                // Broke free or the sequence ended without death: a DeathLink cannot be escaped.
                _grabbed = false;
                _forcedDown = false;
                pc.SpDeath();
                return;
            }
            else
            {
                if (!pc.nowdamage || pc.erodown == 0) KnockDown(pc);
                Traverse.Create(pc).Field("downup").SetValue(-1000);
            }

            var skip = NoR.Randomizer.DeathSkip.Instance;
            if (skip != null && skip.PollSkipInput(pc))
            {
                _forcedDown = false;
                _grabbed = false;
                skip.SkipAndRespawn();
            }
        }

        public void Draw()
        {
            if (!_forcedDown) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _style.fontSize = Mathf.Max(16, Screen.height / 40);
            string text = _grabbed
                ? "DeathLink from " + _source + "\nPress G or hold Jump (2 s) to skip and respawn."
                : "DeathLink from " + _source + ": Aradia cannot get up.\nPress G or hold Jump (2 s) to skip and respawn.";
            var r = new Rect(0, Screen.height * 0.22f, Screen.width, _style.fontSize * 3.5f);
            _style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, _style);
            _style.normal.textColor = new Color(1f, 0.55f, 0.55f);
            GUI.Label(r, text, _style);
        }

        private static playercon Player()
        {
            var go = GameRefs.Player;
            return go != null ? go.GetComponent<playercon>() : null;
        }
    }
}
