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
        public static float RushMultiplier = 1.8f;
        public static float DrainPerSecond = 0.03f;
        public static float EnemyHealBonus = 25f;

        public static GrabQte Instance { get; private set; }

        private static readonly string[] Movement = { "UP", "DOWN", "LEFT", "RIGHT", "ATTACK" };

        private int _prevErodown;
        private float _downSince = -100f;
        private readonly List<string> _sequence = new List<string>();
        private int _progress;
        private bool _active;
        private float _flashWrongUntil;
        private float _prevH, _prevV;
        private bool _injectEscape;
        private bool _prevEroflag;
        private int _sortFixPending;
        private float _escapeAt = -100f;
        private float _nextDrain;
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
            if (pc.eroflag && !_prevEroflag) _sortFixPending = 10; // grab started (any cause, also DeathLink)
            _prevEroflag = pc.eroflag;
            if (_sortFixPending > 0 && --_sortFixPending % 5 == 0) GrabSorting.Fix(pc, Grabber());

            if (!grabbed || EscapeBlocked || !QteEnabled)
            {
                _active = false;
                return;
            }
            if (!_active) { _active = true; NewSequence(); LogGrabbers(); _nextDrain = Time.time + 1f; }

            if (DrainPerSecond > 0f && Time.time >= _nextDrain)
            {
                _nextDrain += 1f;
                if (Damage(pc, DrainPerSecond, false)) return;
            }

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
            if (Damage(pc, WrongInputDamage, true)) return;
            NewSequence();
        }

        /// <summary>
        /// Takes share * max HP from the player and heals the grabbing enemy by (damage dealt + EnemyHealBonus), capped at
        /// its max HP. Returns true if the player died. The passive drain is not lethal: it stops at the HP one wrong input
        /// takes (10% by default), so only a failed input can kill.
        /// </summary>
        private bool Damage(playercon pc, float share, bool lethal)
        {
            var st = GameRefs.Status;
            if (st == null || share <= 0f) return false;
            float floor = lethal ? 0f : st.MaxHp * WrongInputDamage;
            float dealt = Mathf.Min(Mathf.Max(0f, st.Hp - floor), st.MaxHp * share);
            if (dealt <= 0f) return false;
            st.Hp -= dealt;
            var grabber = Grabber();
            if (grabber != null && grabber.Hp > 0f)
                grabber.Hp = Mathf.Min(grabber.MaxHp, grabber.Hp + dealt + EnemyHealBonus);
            if (st.Hp <= 0f) { st.Hp = 0f; pc.SpDeath(); _active = false; return true; }
            return false;
        }

        private EnemyDate _grabber;

        private EnemyDate Grabber()
        {
            if (_grabber != null && _grabber.eroflag) return _grabber;
            _grabber = null;
            foreach (var e in FindObjectsOfType<EnemyDate>())
                if (e.eroflag) { _grabber = e; break; }
            return _grabber;
        }

        /// <summary>
        /// Escape through the vanilla stand-up in playercon.fun_nowdamage (the grabbing enemy releases when erodown == 0):
        /// downup is set to 1 and one Submit press is injected, so the game runs its own full reset (voice bus, gravity,
        /// damage timer, HUD face). Fallback after 0.3 s: the same reset done by hand.
        /// </summary>
        private void Escape(playercon pc)
        {
            _active = false;
            _injectEscape = true;
            _escapeAt = Time.time;
            _downSince = -100f;
            Plugin.Log.LogInfo("Grab QTE: escaped");
        }

        internal static bool TakeInjectedEscape(playercon pc)
        {
            if (Instance == null || !Instance._injectEscape) return false;
            Instance._injectEscape = false;
            var t = Traverse.Create(pc);
            t.Field("downup").SetValue(1);
            t.Field("easyESC").SetValue(false);
            return true;
        }

        private void LateUpdate()
        {
            if (_escapeAt < 0f || Time.time - _escapeAt < 0.3f) return;
            _escapeAt = -100f;
            var pc = Player();
            if (pc == null || pc.erodown == 0) return;
            Plugin.Log.LogWarning("Grab QTE: vanilla stand-up did not run (grounded=" + pc.m_Grounded + "), forcing release");
            var t = Traverse.Create(pc);
            DarkTonic.MasterAudio.MasterAudio.StopBus("EroVoice");
            pc.erodown = 0;
            pc.nowdamage = false;
            pc.tough = pc.maxtough;
            t.Field("damecount").SetValue(0f);
            t.Field("downup").SetValue(0);
            t.Field("walldown").SetValue(0f);
            t.Field("easyESC").SetValue(false);
            var rb = pc.rigi2d;
            if (rb != null) rb.gravityScale = t.Field("gravity").GetValue<float>();
        }

        /// <summary>Diagnostic: which enemies hold the player when a QTE starts (to track grabs without an animation).</summary>
        private static void LogGrabbers()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var e in FindObjectsOfType<EnemyDate>())
                {
                    if (!e.eroflag) continue;
                    var data = e.erodata;
                    sb.Append(' ').Append(e.name).Append(" (ero object ")
                      .Append(data == null ? "none" : data.activeInHierarchy ? "active" : "INACTIVE").Append(')');
                }
                Plugin.Log.LogInfo("Grab QTE: grabbed by" + (sb.Length == 0 ? " nobody (no enemy has eroflag)" : sb.ToString()));
            }
            catch (System.Exception ex) { Plugin.Log.LogDebug("Grab log failed: " + ex.Message); }
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
            if (GrabQte.TakeInjectedEscape(__instance)) { ___key_submit = true; return; }
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

    /// <summary>
    /// Enemies walking to a downed (not yet grabbed) player rush: EROWALK movement through the shared EnemyDate speed
    /// helpers is multiplied by RushMultiplier. Animations are unchanged.
    /// </summary>
    [HarmonyPatch]
    internal static class DownedRushPatch
    {
        private static readonly Dictionary<System.Type, System.Reflection.FieldInfo> StateFields = new Dictionary<System.Type, System.Reflection.FieldInfo>();

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EnemyDate), "fun_rigidSpeed_minus");
            yield return AccessTools.Method(typeof(EnemyDate), "fun_rigidSpeed_plus");
        }

        private static void Prefix(EnemyDate __instance, ref float multiply)
        {
            if (GrabQte.RushMultiplier == 1f) return;
            var pc = __instance.com_player;
            if (pc == null || pc.erodown == 0 || pc.eroflag) return;
            System.Reflection.FieldInfo f;
            var type = __instance.GetType();
            if (!StateFields.TryGetValue(type, out f))
            {
                f = AccessTools.Field(type, "state");
                StateFields[type] = f;
            }
            if (f == null) return;
            var st = f.GetValue(__instance);
            if (st != null && st.ToString() == "EROWALK") multiply *= GrabQte.RushMultiplier;
        }
    }

    /// <summary>
    /// Grab animations are sorted for the enemy's home area. A randomized enemy in another area can end up in a sorting
    /// layer below that area's scenery (the village stone wall covered the bandit's grab). When a grab starts, a grab
    /// object drawn in a lower layer than the player is moved to the player's layer just above her, keeping its
    /// internal order. Censor quads are left alone (MosaicOrderFix handles them).
    /// </summary>
    internal static class GrabSorting
    {
        public static void Fix(playercon pc, EnemyDate grabber)
        {
            if (pc == null || grabber == null) return;
            var spine = Traverse.Create(pc).Field("spineanime").GetValue<Component>();
            var playerR = spine != null ? spine.GetComponent<MeshRenderer>() : pc.GetComponentInChildren<MeshRenderer>();
            if (playerR == null) return;
            int pLayer = playerR.sortingLayerID, pOrder = playerR.sortingOrder;
            int pValue = SortingLayer.GetLayerValueFromID(pLayer);

            var roots = new List<GameObject>();
            if (grabber.erodata != null) roots.Add(grabber.erodata);
            var data2 = Traverse.Create(grabber).Field("erodata2").GetValue<GameObject>();
            if (data2 != null) roots.Add(data2);

            foreach (var root in roots)
            {
                var rs = new List<Renderer>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name == "Ist/MosaicField") continue;
                    rs.Add(r);
                }
                if (rs.Count == 0) continue;
                bool below = false;
                int minOrder = int.MaxValue;
                foreach (var r in rs)
                {
                    int v = SortingLayer.GetLayerValueFromID(r.sortingLayerID);
                    if (v < pValue || (v == pValue && r.sortingOrder < pOrder)) below = true;
                    minOrder = Mathf.Min(minOrder, r.sortingOrder);
                }
                if (!below) continue;
                foreach (var r in rs)
                {
                    int rel = r.sortingOrder - minOrder;
                    r.sortingLayerID = pLayer;
                    r.sortingOrder = pOrder + 1 + rel;
                }
                Plugin.Log.LogInfo("Grab sorting: " + grabber.name + "/" + root.name + " moved to layer " +
                                   SortingLayer.IDToName(pLayer) + " (" + rs.Count + " renderers)");
            }
        }
    }
}
