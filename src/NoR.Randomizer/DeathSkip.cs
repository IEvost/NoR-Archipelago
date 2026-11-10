using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer
{
    /// <summary>
    /// "Skip and respawn" after a real death (HP 0 / playercon._Death), never while merely stunned.
    /// Vanilla shows "YOU DIED", lets the enemy animation continue and waits for any key, then a game-over panel whose
    /// Restart button calls PlayerStatus.REstrat(). This skips straight to REstrat().
    ///   Keyboard: G.  Gamepad: hold Jump for 2 seconds.
    /// Also used by the Archipelago DeathLink handler.
    /// </summary>
    public sealed class DeathSkip : MonoBehaviour
    {
        public static bool Enabled = true;
        public const float HoldSeconds = 2f;

        private static bool _restarting;
        private float _holdStart = -1f;
        private float _pendingRestartAt = -1f;
        private GUIStyle _style;

        public static DeathSkip Instance { get; private set; }

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("NoR.DeathSkip");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DeathSkip>();
        }

        private static playercon Player()
        {
            var go = GameRefs.Player;
            return go != null ? go.GetComponent<playercon>() : null;
        }

        /// <summary>True when the skip input (G or Jump held 2 s) was completed this frame.</summary>
        public bool PollSkipInput(playercon pc)
        {
            if (Input.GetKeyDown(KeyCode.G)) return true;
            var rp = HarmonyLib.Traverse.Create(pc).Field("player").GetValue<Rewired.Player>();
            if (rp != null && rp.GetButton("Jump"))
            {
                if (_holdStart < 0f) _holdStart = Time.unscaledTime;
                else if (Time.unscaledTime - _holdStart > HoldSeconds) { _holdStart = -1f; return true; }
            }
            else _holdStart = -1f;
            return false;
        }

        /// <summary>Kills Aradia if she is alive and goes straight to the respawn (no animation, no game-over panel).</summary>
        public void SkipAndRespawn()
        {
            var pc = Player();
            if (pc == null || _restarting) return;
            _restarting = true;
            if (!pc._Death)
            {
                pc.SpDeath();                                 // normal death bookkeeping (Idea loss by difficulty etc.)
                _pendingRestartAt = Time.unscaledTime + 0.3f;  // let the death register before restarting
            }
            else
            {
                Restart();
            }
        }

        private void Restart()
        {
            _pendingRestartAt = -1f;
            var st = GameRefs.Status;
            if (st == null) return;

            // The vanilla death flow keeps running on timers: PlayerStatus "fun_invoke_DIED" (1.5 s) -> map_title.text_DEATH
            // ("YOU'VE BEEN DEFEATED") -> "GOeny_set" (4.5 s) -> press-any-key -> game-over panel. Cancel all of it.
            st.CancelInvoke("fun_invoke_DIED");
            var title = HarmonyLib.Traverse.Create(st).Field("maptitle").GetValue<map_title>();
            if (title != null)
            {
                title.CancelInvoke("GOeny_set");
                var banner = HarmonyLib.Traverse.Create(title).Field("title").GetValue<GameObject>();
                if (banner != null) banner.SetActive(false);
            }
            HideGameOverPanel();

            st.REstrat();
            _respawnedAt = Time.unscaledTime;
            Plugin.Log.LogInfo("Death skipped: respawning at the last save point");
        }

        private float _respawnedAt = -100f;

        /// <summary>Some enemy defeat sequences open the game-over panel themselves when their animation ends.</summary>
        private static void HideGameOverPanel()
        {
            var go = GameObject.Find("GameOvercanvas");
            if (go == null) return;
            var canvas = go.GetComponent<Canvas>();
            if (canvas != null && canvas.enabled)
            {
                canvas.enabled = false;
                var group = go.GetComponent<CanvasGroup>();
                if (group != null) group.interactable = false;
                Plugin.Log.LogInfo("Closed a leftover game-over panel after skip-respawn");
            }
        }

        private void Update()
        {
            var pc = Player();
            if (pc == null || !pc._Death)
            {
                _restarting = false;   // alive again (respawned)
                _pendingRestartAt = -1f;
                if (pc != null && Time.unscaledTime - _respawnedAt < 15f) HideGameOverPanel();
                return;
            }
            if (_pendingRestartAt > 0f && Time.unscaledTime >= _pendingRestartAt) { Restart(); return; }
            if (!Enabled || _restarting) return;
            if (PollSkipInput(pc)) SkipAndRespawn();
        }

        private void OnGUI()
        {
            var pc = Player();
            if (!Enabled || pc == null || !pc._Death || _restarting) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _style.fontSize = Mathf.Max(14, Screen.height / 50);
            const string text = "Press G or hold Jump (2 s) to skip and respawn";
            var r = new Rect(0, Screen.height * 0.86f, Screen.width, _style.fontSize * 2f);
            _style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, _style);
            _style.normal.textColor = new Color(0.95f, 0.85f, 0.85f);
            GUI.Label(r, text, _style);
        }
    }
}
