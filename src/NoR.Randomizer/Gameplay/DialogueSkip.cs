using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NoR.Core;
using UnityEngine;

namespace NoR.Randomizer.Gameplay
{
    /// <summary>
    /// Dialogue fast-forward (hold Left Ctrl, or the gamepad Step button).
    /// All game dialogue uses custom typewriter controllers that advance on playercon._key_submit
    /// (docs/research-feedback.md section 2). While the key is held:
    ///   - the current line completes instantly (timeUntilDisplay = 0, what vanilla does on Submit mid-line),
    ///   - inside the controller's Update, _key_submit reads true every few frames, so lines advance as if the player
    ///     clicked through. Flags, rewards and scene moves therefore happen exactly as in vanilla.
    /// Choices and timed waits stop it naturally. Shop / Vendetta controllers only get instant text (auto-presses would
    /// open or close their menus).
    /// </summary>
    public static class DialogueSkip
    {
        public static bool Enabled = true;
        public static KeyCode Key = KeyCode.LeftControl;
        public static int FramesPerLine = 3;

        private static readonly string[] Advancing =
        {
            "DialogControllerMainEVunder", "DialogControllerNpcEVunder", "DialogControllerEroEVunderWindow", "DialogControllerEroEv",
            "TextController", "TextControllerGO", "TextControllerGAGO", "Dialogtextcontrol",
        };

        private static readonly string[] InstantOnly =
        {
            "ShopDialogtextcontrol", "ShopTalk", "VendettaDialogtextcontrol", "VendettaDialogtextcontrol_second", "Treasureboxdialog",
        };

        [ThreadStatic] private static bool _insideAdvancing;
        private static int _lastPressFrame;

        public static bool Held
        {
            get
            {
                if (!Enabled) return false;
                if (Input.GetKey(Key) || Input.GetKey(KeyCode.RightControl)) return true;
                var go = GameRefs.Player;
                var pc = go != null ? go.GetComponent<playercon>() : null;
                var rp = pc != null ? Traverse.Create(pc).Field("player").GetValue<Rewired.Player>() : null;
                return rp != null && rp.GetButton("Step");
            }
        }

        public static void Apply(Harmony harmony)
        {
            var asm = typeof(playercon).Assembly;
            var prefixAdv = new HarmonyMethod(typeof(DialogueSkip).GetMethod("PrefixAdvancing", BindingFlags.Static | BindingFlags.NonPublic));
            var postfixAdv = new HarmonyMethod(typeof(DialogueSkip).GetMethod("PostfixAdvancing", BindingFlags.Static | BindingFlags.NonPublic));
            var prefixInst = new HarmonyMethod(typeof(DialogueSkip).GetMethod("PrefixInstant", BindingFlags.Static | BindingFlags.NonPublic));
            int n = 0;
            foreach (var name in Advancing.Concat(InstantOnly))
            {
                var t = asm.GetType(name, false);
                var update = t != null ? AccessTools.DeclaredMethod(t, "Update") : null;
                if (update == null) { Plugin.Log.LogWarning("Dialogue skip: no Update on " + name); continue; }
                bool advancing = Array.IndexOf(Advancing, name) >= 0;
                harmony.Patch(update, prefix: advancing ? prefixAdv : prefixInst, postfix: advancing ? postfixAdv : null);
                n++;
            }
            var getter = AccessTools.PropertyGetter(typeof(playercon), "_key_submit");
            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(DialogueSkip).GetMethod("KeySubmitPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            Plugin.Log.LogInfo("Dialogue skip: " + n + " controllers hooked");
        }

        private static void CompleteLine(object controller)
        {
            var f = AccessTools.Field(controller.GetType(), "timeUntilDisplay");
            if (f != null && f.FieldType == typeof(float)) f.SetValue(controller, 0f);
        }

        private static void PrefixAdvancing(MonoBehaviour __instance)
        {
            if (!Held) return;
            CompleteLine(__instance);
            _insideAdvancing = true;
        }

        private static void PostfixAdvancing()
        {
            _insideAdvancing = false;
        }

        private static void PrefixInstant(MonoBehaviour __instance)
        {
            if (Held) CompleteLine(__instance);
        }

        /// <summary>Inside an advancing controller's Update, report Submit as pressed every few frames.</summary>
        private static void KeySubmitPostfix(ref bool __result)
        {
            if (!_insideAdvancing || __result) return;
            if (Time.frameCount - _lastPressFrame < FramesPerLine) return;
            _lastPressFrame = Time.frameCount;
            __result = true;
        }
    }
}
