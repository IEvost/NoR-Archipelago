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

            TrackBoundaries();

            if (FixStuckZoom.Value && Time.unscaledTime > _nextWatch)
            {
                _nextWatch = Time.unscaledTime + 1f;
                Watchdog();
            }
        }

        private float _appliedBaseline = -1f;
        private float _nextBoundsCheck;

        /// <summary>The zoom level, but never wider than the level's numeric boundaries allow.</summary>
        private float Desired()
        {
            float d = _vanillaHalfHeight * ZoomLevel.Value;
            var cam = ProCamera2D.Instance;
            float max = cam != null ? BoundaryMaxHalfHeight(cam) : float.PositiveInfinity;
            return Mathf.Min(d, max * 0.995f);
        }

        /// <summary>
        /// Largest half screen height the numeric boundaries allow (they shrink the view when it is wider/taller than the
        /// bounded area). Narrow areas are narrower than an ultrawide view; a baseline above this makes the boundaries
        /// and the zoom extensions fight every frame (the camera jitters while standing still).
        /// </summary>
        internal static float BoundaryMaxHalfHeight(ProCamera2D cam)
        {
            var nb = cam.GetComponent<ProCamera2DNumericBoundaries>();
            if (nb == null || !nb.enabled || !nb.UseNumericBoundaries || cam.GameCamera == null) return float.PositiveInfinity;
            float max = float.PositiveInfinity;
            if (nb.UseLeftBoundary && nb.UseRightBoundary)
                max = Mathf.Min(max, (nb.RightBoundary - nb.LeftBoundary) / cam.GameCamera.aspect / 2f);
            if (nb.UseTopBoundary && nb.UseBottomBoundary)
                max = Mathf.Min(max, (nb.TopBoundary - nb.BottomBoundary) / 2f);
            return max > 0f ? max : float.PositiveInfinity;
        }

        /// <summary>Re-applies the baselines when the effective size changes (boundaries differ per area/room).</summary>
        private void TrackBoundaries()
        {
            if (_vanillaHalfHeight <= 0f || Time.unscaledTime < _nextBoundsCheck) return;
            _nextBoundsCheck = Time.unscaledTime + 0.25f;
            float d = Desired();
            if (Mathf.Abs(d - _appliedBaseline) < 0.01f) return;
            SetBaselines(d);
        }

        private void SetBaselines(float desired)
        {
            var cam = ProCamera2D.Instance;
            if (cam == null) return;
            _appliedBaseline = desired;
            float aspect = (float)Screen.width / Screen.height;
            Traverse.Create(cam).Field("_startScreenSizeInWorldCoordinates").SetValue(new Vector2(desired * 2f * aspect, desired * 2f));
            var fit = cam.GetComponent<ProCamera2DZoomToFitTargets>() ?? FindObjectOfType<ProCamera2DZoomToFitTargets>();
            if (fit != null) Traverse.Create(fit).Field("_initialCamSize").SetValue(desired);
            var speed = SpeedZoom(cam);
            if (speed != null) Traverse.Create(speed).Field("_initialCamSize").SetValue(desired);
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
            // Speed Based Zoom keeps its own baseline from Awake; left at the vanilla size it pulls the camera back while
            // standing still and the watchdog pushes it out again (camera pumping every ~0.5 s). SetBaselines sets it too.
            SetBaselines(desired);
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
            // Speed Based Zoom legitimately zooms in down to baseline / MaxZoomInAmount while standing still.
            var speed = SpeedZoom(cam);
            float floor = speed != null && speed.enabled ? desired / Mathf.Max(1f, speed.MaxZoomInAmount) : desired;
            if (current < floor - 0.05f)
            {
                Plugin.Log.LogInfo("Camera stuck zoomed in (" + current.ToString("0.00") + " < " + desired.ToString("0.00") + ") - resetting");
                cam.UpdateScreenSize(desired, 0.5f);
            }
        }

        private static ProCamera2DSpeedBasedZoom SpeedZoom(ProCamera2D cam)
        {
            return cam.GetComponent<ProCamera2DSpeedBasedZoom>() ?? FindObjectOfType<ProCamera2DSpeedBasedZoom>();
        }

        /// <summary>Grabs and deaths zoom in on purpose.</summary>
        private static bool PlayerBusy()
        {
            var go = GameObject.FindWithTag("Player");
            var pc = go != null ? go.GetComponent<playercon>() : null;
            return pc != null && (pc.eroflag || pc._Death);
        }
    }

    /// <summary>
    /// While the numeric boundaries hold the view (it is as wide/tall as the bounded area), Speed Based Zoom reads the
    /// position corrections of the boundaries as camera speed, zooms out, gets clamped again, and so on every frame.
    /// Skip it in that state; it still works everywhere else.
    /// </summary>
    [HarmonyPatch(typeof(ProCamera2DSpeedBasedZoom), "AdjustSize")]
    internal static class SpeedZoomAtBoundsPatch
    {
        private static bool Prefix(ProCamera2DSpeedBasedZoom __instance, float originalDelta, ref float __result)
        {
            var cam = ProCamera2D.Instance;
            if (cam == null) return true;
            float max = CameraZoom.BoundaryMaxHalfHeight(cam);
            if (float.IsInfinity(max)) return true;
            float current = cam.ScreenSizeInWorldCoordinates.y * 0.5f;
            if (current < max * 0.98f) return true;
            // Keep its velocity tracking in sync so it does not see one big jump when it resumes.
            var lp = cam.LocalPosition;
            Traverse.Create(__instance).Field("_previousCameraPosition").SetValue(new Vector3(lp.x, lp.y, 0f));
            Traverse.Create(__instance).Field("_zoomVelocity").SetValue(0f);
            __result = originalDelta;
            return false;
        }
    }
}

namespace NoR.Widescreen
{
    /// <summary>
    /// Geometry Boundaries (3D collider walls, e.g. the fog at area exits): when the camera has no movement of its own
    /// (player idle), a corner ray inside a wall forces a 0.1 nudge away from it. With a view wider than the game was
    /// made for (ultrawide, zoomed out) the view already overlaps the wall while centred on the player, so the nudge and
    /// the camera follow pull against each other: +-0.1 every frame (ghosting), and Speed Based Zoom reads ~22 u/s and
    /// zooms out. Drop the idle nudge on that axis; walls still block the camera while it moves.
    /// </summary>
    [HarmonyPatch(typeof(ProCamera2DGeometryBoundaries), "AdjustDelta")]
    internal static class GeometryBoundsIdleNudgePatch
    {
        private static void Postfix(ProCamera2DGeometryBoundaries __instance, Vector3 originalDelta, ref Vector3 __result)
        {
            if (!__instance.enabled) return;
            if (originalDelta.x == 0f) __result.x = 0f;
            if (originalDelta.y == 0f) __result.y = 0f;
            ClampToWalls(__instance, ref __result);
        }

        /// <summary>
        /// Keep the view's left/right edges inside the boundary walls (the vanilla corner rays only nudge, which a view
        /// wider than 16:9 slips past - showing the empty area behind an exit fog). Rays at three heights from the
        /// camera centre find the nearest wall on each side; the camera centre is clamped so the view ends at the wall,
        /// or centred between both walls when the room is narrower than the view. A clamp to a fixed value cannot
        /// oscillate.
        /// </summary>
        private static void ClampToWalls(ProCamera2DGeometryBoundaries gb, ref Vector3 delta)
        {
            var pc = ProCamera2D.Instance;
            if (pc == null) return;
            Vector3 c = pc.transform.localPosition;
            Vector2 size = pc.ScreenSizeInWorldCoordinates;
            float hw = size.x * 0.5f, hh = size.y * 0.5f;
            int mask = gb.BoundariesLayerMask;
            float nextX = c.x + delta.x;
            float reach = hw + Mathf.Abs(delta.x) + 1f;

            float left = float.NegativeInfinity, right = float.PositiveInfinity;
            for (int i = -1; i <= 1; i++)
            {
                var o = new Vector3(c.x, c.y + i * hh * 0.5f, 0f);
                RaycastHit h;
                if (Physics.Raycast(o, Vector3.left, out h, reach, mask)) left = Mathf.Max(left, h.point.x);
                if (Physics.Raycast(o, Vector3.right, out h, reach, mask)) right = Mathf.Min(right, h.point.x);
            }
            bool hasL = !float.IsNegativeInfinity(left), hasR = !float.IsPositiveInfinity(right);

            if (hasL || hasR)
            {
                float x = nextX;
                if (hasL && hasR && right - left < size.x) x = (left + right) * 0.5f;
                else
                {
                    if (hasL) x = Mathf.Max(x, left + hw);
                    if (hasR) x = Mathf.Min(x, right - hw);
                }
                delta.x = x - c.x;
            }

            // Same for floor and ceiling, rays at three horizontal positions.
            float nextY = c.y + delta.y;
            float reachY = hh + Mathf.Abs(delta.y) + 1f;
            float bottom = float.NegativeInfinity, top = float.PositiveInfinity;
            for (int i = -1; i <= 1; i++)
            {
                var o = new Vector3(c.x + i * hw * 0.5f, c.y, 0f);
                RaycastHit h;
                if (Physics.Raycast(o, Vector3.down, out h, reachY, mask)) bottom = Mathf.Max(bottom, h.point.y);
                if (Physics.Raycast(o, Vector3.up, out h, reachY, mask)) top = Mathf.Min(top, h.point.y);
            }
            bool hasB = !float.IsNegativeInfinity(bottom), hasT = !float.IsPositiveInfinity(top);
            if (hasB || hasT)
            {
                float y = nextY;
                if (hasB && hasT && top - bottom < size.y) y = (bottom + top) * 0.5f;
                else
                {
                    if (hasB) y = Mathf.Max(y, bottom + hh);
                    if (hasT) y = Mathf.Min(y, top - hh);
                }
                delta.y = y - c.y;
            }
        }
    }
}
