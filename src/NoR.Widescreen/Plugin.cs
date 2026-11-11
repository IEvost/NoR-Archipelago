using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NoR.Widescreen
{
    /// <summary>
    /// Widescreen / ultrawide support.
    ///
    /// The game's player settings only allow 4:3, 5:4, 16:10 and 16:9 ("Others" disabled), so the Unity launcher
    /// never offers 21:9 / 32:9 resolutions. This plugin switches to the configured (default: native) resolution
    /// after startup and adapts the UI:
    ///   Expand    - render the full width; UI canvases scale by height so the HUD keeps its size, and
    ///               full-screen overlay images (fades, backgrounds) are stretched to cover the sides.
    ///   Pillarbox - keep the original 16:9 view centered with black bars (fallback if Expand shows glitches).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nor.archipelago.widescreen";
        public const string Name = "NoR Widescreen";
        public const string Version = "0.2.1";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<string> Resolution;
        internal static ConfigEntry<bool> Fullscreen;
        internal static ConfigEntry<AspectMode> Mode;
        internal static ConfigEntry<bool> StretchOverlays;
        internal static ConfigEntry<bool> LogCanvases;

        public enum AspectMode { Expand, Pillarbox, Off }

        private int _appliedW, _appliedH;
        private Camera _barsCamera;

        private void Awake()
        {
            Log = Logger;
            Resolution = Config.Bind("Display", "Resolution", "native",
                "'native' = desktop resolution of the main monitor, or WIDTHxHEIGHT (e.g. 3440x1440), or 'game' to keep the launcher choice.");
            Fullscreen = Config.Bind("Display", "Fullscreen", true, "Fullscreen mode.");
            Mode = Config.Bind("Display", "AspectMode", AspectMode.Expand,
                "Expand: use the full wide screen. Pillarbox: keep a centered 16:9 view with black bars. Off: no changes.");
            StretchOverlays = Config.Bind("UI", "StretchFullscreenOverlays", true,
                "Stretch full-screen UI images (fades, dark backgrounds) so they also cover the extra width.");
            LogCanvases = Config.Bind("Debug", "LogCanvases", false, "Log every canvas adjusted (for troubleshooting).");

            CameraZoom.Create(Config);
            MosaicOrderFix.Create(Config);
            if (Mode.Value == AspectMode.Off) return;
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            SceneManager.sceneLoaded += (s, m) => UiFixer.FixAll();
        }

        private void Start()
        {
            if (Mode.Value == AspectMode.Off) return;
            ApplyResolution();
        }

        private void ApplyResolution()
        {
            int w, h;
            string r = Resolution.Value.Trim().ToLowerInvariant();
            if (r == "game") return;
            if (r == "native")
            {
                w = Display.main.systemWidth;
                h = Display.main.systemHeight;
            }
            else
            {
                var p = r.Split('x');
                if (p.Length != 2 || !int.TryParse(p[0], out w) || !int.TryParse(p[1], out h))
                {
                    Log.LogWarning("Invalid Resolution setting '" + Resolution.Value + "'");
                    return;
                }
            }
            if (Screen.width == w && Screen.height == h && Screen.fullScreen == Fullscreen.Value) return;
            Log.LogInfo("Switching resolution " + Screen.width + "x" + Screen.height + " -> " + w + "x" + h + (Fullscreen.Value ? " fullscreen" : " windowed"));
            Screen.SetResolution(w, h, Fullscreen.Value);
            _appliedW = w;
            _appliedH = h;
        }

        private void LateUpdate()
        {
            if (Mode.Value != AspectMode.Pillarbox) return;
            ApplyPillarbox();
        }

        /// <summary>Centered 16:9 viewport on every camera, plus a full-screen camera that clears the bars to black.</summary>
        private void ApplyPillarbox()
        {
            float target = 16f / 9f;
            float aspect = (float)Screen.width / Screen.height;
            Rect rect = new Rect(0, 0, 1, 1);
            if (aspect > target + 0.01f)
            {
                float w = target / aspect;
                rect = new Rect((1f - w) / 2f, 0, w, 1);
            }
            else if (aspect < target - 0.01f)
            {
                float h = aspect / target;
                rect = new Rect(0, (1f - h) / 2f, 1, h);
            }

            if (_barsCamera == null)
            {
                var go = new GameObject("NoR.PillarboxBars");
                DontDestroyOnLoad(go);
                _barsCamera = go.AddComponent<Camera>();
                _barsCamera.depth = -100;
                _barsCamera.cullingMask = 0;
                _barsCamera.clearFlags = CameraClearFlags.SolidColor;
                _barsCamera.backgroundColor = Color.black;
            }

            foreach (var cam in Camera.allCameras)
            {
                if (cam == _barsCamera || cam.targetTexture != null) continue;
                if (cam.rect != rect) cam.rect = rect;
            }
        }
    }

    internal static class UiFixer
    {
        public static void FixAll()
        {
            foreach (var scaler in Resources.FindObjectsOfTypeAll<CanvasScaler>())
            {
                if (scaler != null && scaler.gameObject.scene.IsValid()) Fix(scaler);
            }
        }

        public static void Fix(CanvasScaler scaler)
        {
            if (Plugin.Mode.Value == Plugin.AspectMode.Off) return;
            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= 16f / 9f + 0.01f) return; // nothing to do on 16:9 or narrower

            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                // Scale by height: HUD keeps its 16:9 size; edge-anchored elements move to the real screen edges.
                if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.MatchWidthOrHeight && scaler.matchWidthOrHeight < 1f)
                    scaler.matchWidthOrHeight = 1f;

                if (Plugin.StretchOverlays.Value && Plugin.Mode.Value == Plugin.AspectMode.Expand)
                    StretchOverlays(scaler);
            }
            Reanchor(scaler);
            if (Plugin.LogCanvases.Value)
                Plugin.Log.LogInfo("Canvas " + scaler.gameObject.name + " mode=" + scaler.uiScaleMode + " ref=" + scaler.referenceResolution + " match=" + scaler.screenMatchMode + "/" + scaler.matchWidthOrHeight);
        }

        /// <summary>
        /// HUD elements that the game anchors to the screen centre although their icons are anchored bottom-left /
        /// bottom-right: the red "X" over weapon/magic slots (not enough stats), the "not enough stats" text and the zoom
        /// key hint. They only line up at 16:9 (docs/research-feedback.md 5a). Re-anchor them like their icons
        /// (1280x720 reference: centre (640,360) == bottom-left + (640,360)).
        /// </summary>
        private static readonly string[] BottomLeftElements = { "wp_notact", "mg_notact1", "mg_notact2", "mg_notact3", "mg_notact4", "Lowattribute", "Can'tAction" };

        private static void Reanchor(CanvasScaler scaler)
        {
            foreach (var rt in scaler.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.anchorMin != new Vector2(0.5f, 0.5f) || rt.anchorMax != new Vector2(0.5f, 0.5f)) continue;
                string n = rt.name;
                if (System.Array.IndexOf(BottomLeftElements, n) >= 0)
                {
                    var pos = rt.anchoredPosition + new Vector2(640f, 360f);
                    rt.anchorMin = rt.anchorMax = Vector2.zero;
                    rt.anchoredPosition = pos;
                }
                else if (n == "Zoomkey")
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-142f, 120f);
                }
            }
        }

        /// <summary>
        /// Images that cover (almost) the whole reference resolution with fixed anchors are full-screen overlays
        /// designed for 16:9; anchor them to all four edges so they cover any aspect ratio.
        /// </summary>
        private static void StretchOverlays(CanvasScaler scaler)
        {
            Vector2 refRes = scaler.referenceResolution;
            foreach (var g in scaler.GetComponentsInChildren<Graphic>(true))
            {
                if (!(g is Image) && !(g is RawImage)) continue;
                var rt = g.rectTransform;
                if (rt.anchorMin != rt.anchorMax) continue; // already stretching
                Vector2 size = rt.rect.size;
                if (size.x < refRes.x * 0.97f || size.y < refRes.y * 0.97f) continue;
                var parent = rt.parent as RectTransform;
                if (parent == null) continue;
                // Only direct full-screen children of the canvas (or of another full-screen container).
                if (parent.rect.size.y < refRes.y * 0.97f) continue;

                Vector2 extra = new Vector2(size.x - refRes.x, size.y - refRes.y); // keep any intentional overscan
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(Mathf.Max(0f, extra.x), Mathf.Max(0f, extra.y));
                if (Plugin.LogCanvases.Value) Plugin.Log.LogInfo("  stretched overlay " + g.gameObject.name);
            }
        }
    }

    /// <summary>Canvases instantiated after a scene load (menus, dialogs) are fixed when they become active.</summary>
    [HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
    internal static class CanvasScalerEnable
    {
        private static void Postfix(CanvasScaler __instance)
        {
            try { UiFixer.Fix(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("Canvas fix failed: " + e.Message); }
        }
    }
}
