using System;
using System.Collections;
using BepInEx.Configuration;
using Com.LuisPedroFonseca.ProCamera2D;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.Widescreen
{
    /// <summary>
    /// Camera zoom level and stuck-zoom fix (docs/research-feedback.md section 4).
    ///
    /// The main camera is a perspective camera driven by ProCamera2D; "zoom" is its screen size in world units.
    /// Vanilla zooms in during grabs/rescues with ProCamera2DZoomToFitTargets and zooms back to the baselines
    /// ProCamera2D._startScreenSizeInWorldCoordinates / ZoomToFitTargets._initialCamSize. Some events disable the zoom
    /// component mid-zoom, which freezes the camera zoomed in.
    ///
    ///   Zoom level: PageUp = zoom in, PageDown = zoom out, Home = reset (saved in the config). The chosen level becomes
    ///     the baseline, so every vanilla "zoom back" returns to it.
    ///   Watchdog: once per second, if the camera is zoomed in further than the baseline while no grab/death is going on
    ///     and the zoom component is off, ease back to the baseline.
    /// </summary>
    public sealed class CameraZoom : MonoBehaviour
    {
        internal static ConfigEntry<float> ZoomLevel;
        internal static ConfigEntry<bool> FixStuckZoom;
        internal static ConfigEntry<KeyCode> KeyIn, KeyOut, KeyReset;

        private float _vanillaHalfHeight = -1f;   // vanilla baseline (half screen height in world units)
        private int _cameraId;
        private float _nextWatch;

        public static void Create(ConfigFile cfg)
        {
            ZoomLevel = cfg.Bind("Camera", "ZoomLevel", 1.0f, "Camera zoom: 1 = vanilla, larger = see more (zoomed out), smaller = closer. Range 0.6 - 1.8.");
            FixStuckZoom = cfg.Bind("Camera", "FixStuckZoom", true, "Return the camera to the normal zoom when an event leaves it stuck zoomed in.");
            KeyIn = cfg.Bind("Camera", "KeyZoomIn", KeyCode.PageUp, "Zoom in.");
            KeyOut = cfg.Bind("Camera", "KeyZoomOut", KeyCode.PageDown, "Zoom out.");
            KeyReset = cfg.Bind("Camera", "KeyZoomReset", KeyCode.Home, "Reset zoom to vanilla.");
            var go = new GameObject("NoR.CameraZoom");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraZoom>();
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += (s, m) => StartCoroutine(ApplyAfterStart());
        }

        /// <summary>ZoomToFitTargets stores its baseline in Start after WaitForEndOfFrame - apply ours after that.</summary>
        private IEnumerator ApplyAfterStart()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;
            Apply(0.3f);
        }

        private void Update()
        {
            float level = ZoomLevel.Value;
            if (Input.GetKeyDown(KeyIn.Value)) level -= 0.1f;
            if (Input.GetKeyDown(KeyOut.Value)) level += 0.1f;
            if (Input.GetKeyDown(KeyReset.Value)) level = 1f;
            level = Mathf.Clamp((float)Math.Round(level, 2), 0.6f, 1.8f);
            if (Math.Abs(level - ZoomLevel.Value) > 0.001f)
            {
                ZoomLevel.Value = level;
                Apply(0.3f);
            }

            if (FixStuckZoom.Value && Time.unscaledTime > _nextWatch)
            {
                _nextWatch = Time.unscaledTime + 1f;
                Watchdog();
            }
        }

        private float Desired()
        {
            return _vanillaHalfHeight * ZoomLevel.Value;
        }

        private void Apply(float duration)
        {
            var cam = ProCamera2D.Instance;
            if (cam == null) return;
            var t = Traverse.Create(cam);
            if (cam.GetInstanceID() != _cameraId || _vanillaHalfHeight <= 0f)
            {
                var start = t.Field("_startScreenSizeInWorldCoordinates").GetValue<Vector2>();
                if (start.y <= 0f) return;
                _cameraId = cam.GetInstanceID();
                _vanillaHalfHeight = start.y * 0.5f;
            }
            float desired = Desired();
            float aspect = (float)Screen.width / Screen.height;
            t.Field("_startScreenSizeInWorldCoordinates").SetValue(new Vector2(desired * 2f * aspect, desired * 2f));
            var fit = cam.GetComponent<ProCamera2DZoomToFitTargets>() ?? FindObjectOfType<ProCamera2DZoomToFitTargets>();
            if (fit != null) Traverse.Create(fit).Field("_initialCamSize").SetValue(desired);
            if (!PlayerBusy()) cam.UpdateScreenSize(desired, duration);
        }

        private void Watchdog()
        {
            var cam = ProCamera2D.Instance;
            if (cam == null || _vanillaHalfHeight <= 0f || PlayerBusy()) return;
            var fit = cam.GetComponent<ProCamera2DZoomToFitTargets>() ?? FindObjectOfType<ProCamera2DZoomToFitTargets>();
            if (fit != null && fit.enabled) return; // a zoom is in progress
            float current = cam.ScreenSizeInWorldCoordinates.y * 0.5f;
            float desired = Desired();
            if (current < desired - 0.05f)
            {
                Plugin.Log.LogInfo("Camera stuck zoomed in (" + current.ToString("0.00") + " < " + desired.ToString("0.00") + ") - resetting");
                cam.UpdateScreenSize(desired, 0.5f);
            }
        }

        /// <summary>Grabs and deaths zoom in on purpose.</summary>
        private static bool PlayerBusy()
        {
            var go = GameObject.FindWithTag("Player");
            var pc = go != null ? go.GetComponent<playercon>() : null;
            return pc != null && (pc.eroflag || pc._Death);
        }
    }
}
