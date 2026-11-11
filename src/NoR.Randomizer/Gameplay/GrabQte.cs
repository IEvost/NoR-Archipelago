using System.Collections.Generic;
using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer.Gameplay
{
    /// <summary>
    /// Grab QTE and knockdown lockout (user request, docs/research-feedback.md section 1).
    ///
    /// Vanilla: the only struggle input is in playercon.fun_nowdamage - two Submit/Attack presses while grounded set
    /// erodown = 0, and every grabbing enemy releases when it sees erodown == 0. A downed player can stand up on the very
    /// first frame, so enemies rarely get to grab.
    ///
    /// Lockout: after a non-lethal knockdown (erodown 0 -> non-zero, not grabbed) the get-up presses are ignored for
    ///   LockSeconds, giving enemies a chance to grab.
    /// QTE: while grabbed (eroflag && erodown != 0) the vanilla struggle is disabled; the player must enter 4-6 prompted
    ///   inputs. All correct = the vanilla stand-up runs (the enemy releases on its next frame). A wrong input costs
    ///   10% of max HP (death at 0) and restarts the sequence with new prompts.
    /// EscapeBlocked (set by the DeathLink handler): no QTE and no vanilla escape - the grab ends only in death or a skip.
    /// </summary>
    public sealed class GrabQte : MonoBehaviour
    {
        public enum KeySet { FullKeyboard, MovementAttack }

        public static bool QteEnabled = true;
        public static float LockSeconds = 3f;
        public static KeySet Keys = KeySet.FullKeyboard;
        public static float WrongInputDamage = 0.10f;
        public static bool EscapeBlocked;

        public static GrabQte Instance { get; private set; }

        private static readonly string[] Movement = { "UP", "DOWN", "LEFT", "RIGHT", "ATTACK" };

        private int _prevErodown;
        private float _downSince = -100f;
        private readonly List<string> _sequence = new List<string>();
        private int _progress;
        private bool _active;
        private float _flashWrongUntil;
        private float _prevH, _prevV;
        private GUIStyle _style, _boxStyle;

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("NoR.GrabQte");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GrabQte>();
        }

        private static playercon Player()
        {
            var go = GameRefs.Player;
            return go != null ? go.GetComponent<playercon>() : null;
        }

        /// <summary>True while vanilla get-up / struggle presses must be ignored.</summary>
        internal static bool BlockVanillaPresses(playercon pc)
        {
            if (Instance == null || pc == null) return false;
            if (pc.eroflag) return EscapeBlocked || (QteEnabled && Instance._active);
            return pc.erodown != 0 && Time.time - Instance._downSince < LockSeconds;
        }

        private void Update()
        {
            var pc = Player();
            if (pc == null || pc._Death) { _active = false; _prevErodown = 0; EscapeBlocked = false; return; }

            // Knockdown edge: start the lockout (not for grabs).
            if (pc.erodown != 0 && _prevErodown == 0 && !pc.eroflag) _downSince = Time.time;
            _prevErodown = pc.erodown;

            bool grabbed = pc.eroflag && pc.erodown != 0;
            if (!grabbed || EscapeBlocked || !QteEnabled)
            {
                _active = false;
                return;
            }
            if (!_active) { _active = true; NewSequence(); }

            string pressed = ReadInput(pc);
            if (pressed == null) return;
            if (pressed == _sequence[_progress])
            {
                _progress++;
                if (_progress >= _sequence.Count) Escape(pc);
            }
            else
            {
                Wrong(pc);
            }
        }

        private void NewSequence()
        {
            _sequence.Clear();
            _progress = 0;
            int len = Random.Range(4, 7);
            for (int i = 0; i < len; i++)
            {
                string next;
                do
                {
                    next = Keys == KeySet.FullKeyboard ? ((char)('A' + Random.Range(0, 26))).ToString() : Movement[Random.Range(0, Movement.Length)];
                } while (i > 0 && next == _sequence[i - 1]); // no immediate repeats
                _sequence.Add(next);
            }
        }

        /// <summary>The input pressed this frame within the active key set, or null.</summary>
        private string ReadInput(playercon pc)
        {
            if (Keys == KeySet.FullKeyboard)
            {
                for (var k = KeyCode.A; k <= KeyCode.Z; k++)
                    if (Input.GetKeyDown(k)) return ((char)('A' + (k - KeyCode.A))).ToString();
                return null;
            }
            var rp = Traverse.Create(pc).Field("player").GetValue<Rewired.Player>();
            if (rp == null) return null;
            float h = rp.GetAxisRaw("keyHorizontal"), v = rp.GetAxisRaw("keyVertical");
            string result = null;
            if (rp.GetButtonDown("Attack")) result = "ATTACK";
            else if (h > 0.5f && _prevH <= 0.5f) result = "RIGHT";
            else if (h < -0.5f && _prevH >= -0.5f) result = "LEFT";
            else if (v > 0.5f && _prevV <= 0.5f) result = "UP";
            else if (v < -0.5f && _prevV >= -0.5f) result = "DOWN";
            _prevH = h;
            _prevV = v;
            return result;
        }

        private void Wrong(playercon pc)
        {
            _flashWrongUntil = Time.unscaledTime + 0.4f;
            var st = GameRefs.Status;
            if (st != null)
            {
                st.Hp -= st.MaxHp * WrongInputDamage;
                if (st.Hp <= 0f) { st.Hp = 0f; pc.SpDeath(); _active = false; return; }
            }
            NewSequence();
        }

        /// <summary>The vanilla stand-up (playercon.fun_nowdamage) - the grabbing enemy releases when erodown == 0.</summary>
        private void Escape(playercon pc)
        {
            _active = false;
            pc.erodown = 0;
            pc.nowdamage = false;
            pc.tough = pc.maxtough;
            var t = Traverse.Create(pc);
            t.Field("downup").SetValue(0);
            t.Field("easyESC").SetValue(false);
            _downSince = -100f;
            Plugin.Log.LogInfo("Grab QTE: escaped");
        }

        private void OnGUI()
        {
            if (!_active || _sequence.Count == 0) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            int fs = Mathf.Max(16, Screen.height / 30);
            _style.fontSize = fs;
            _boxStyle.fontSize = fs;
            float boxW = Keys == KeySet.FullKeyboard ? fs * 2.2f : fs * 5.2f, boxH = fs * 2f, gap = fs * 0.4f;
            float total = _sequence.Count * boxW + (_sequence.Count - 1) * gap;
            float x = (Screen.width - total) / 2f, y = Screen.height * 0.30f;

            _style.normal.textColor = Time.unscaledTime < _flashWrongUntil ? new Color(1f, 0.4f, 0.4f) : Color.white;
            GUI.Label(new Rect(0, y - boxH, Screen.width, boxH), Time.unscaledTime < _flashWrongUntil ? "Wrong! -10% HP" : "Break free!", _style);
            for (int i = 0; i < _sequence.Count; i++)
            {
                var old = GUI.color;
                GUI.color = i < _progress ? new Color(0.4f, 1f, 0.4f) : (i == _progress ? Color.white : new Color(1f, 1f, 1f, 0.55f));
                GUI.Box(new Rect(x + i * (boxW + gap), y, boxW, boxH), _sequence[i], _boxStyle);
                GUI.color = old;
            }
        }
    }

    /// <summary>Blocks the vanilla get-up / struggle presses during the knockdown lockout and while the QTE runs.</summary>
    [HarmonyPatch(typeof(playercon), "fun_nowdamage")]
    internal static class NowDamagePatch
    {
        private static void Prefix(playercon __instance, ref bool ___key_submit, ref bool ___key_atk)
        {
            if (!GrabQte.BlockVanillaPresses(__instance)) return;
            ___key_submit = false;
            ___key_atk = false;
        }
    }

    /// <summary>The potion-switch button (Rewired "Itemcursor") stays live while grabbed; QTE letters can share its key.</summary>
    [HarmonyPatch(typeof(playercon), "fun_changeItem")]
    internal static class ChangeItemPatch
    {
        private static bool Prefix(playercon __instance)
        {
            return !GrabQte.BlockVanillaPresses(__instance);
        }
    }
}
