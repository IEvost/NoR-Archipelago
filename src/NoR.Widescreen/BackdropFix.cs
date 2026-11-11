using HarmonyLib;
using UnityEngine;

namespace NoR.Widescreen
{
    /// <summary>
    /// SceneStartEffect.Start parents the level's sky picture ("back", e.g. 480x291 scaled 6x) to the main camera at a
    /// fixed local distance (backpos, z = 10). It moves and zooms with the camera and is sized for 16:9: on wider screens
    /// its edges show as blank strips at the left/right, and zooming cannot help. Scale it uniformly (no stretching) just
    /// enough to cover the actual view at that distance.
    /// </summary>
    [HarmonyPatch(typeof(SceneStartEffect), "Start")]
    internal static class BackdropFix
    {
        private static void Postfix(GameObject ___back)
        {
            if (Plugin.Mode.Value != Plugin.AspectMode.Expand || ___back == null) return;
            var cam = Camera.main;
            if (cam == null || cam.orthographic) return;
            Fit(___back.transform, cam);
        }

        internal static void Fit(Transform back, Camera cam)
        {
            var renderers = back.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            // Picture bounds in camera-local space (camera is unrotated in this game).
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            Vector3 c = cam.transform.InverseTransformPoint(b.center);
            float dist = c.z;
            if (dist <= 0f || b.extents.x <= 0f || b.extents.y <= 0f) return;

            float halfH = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            // Required extents measured from the camera axis, including any offset of the picture's centre; 3% margin.
            float needX = (halfW + Mathf.Abs(c.x)) * 1.03f;
            float needY = (halfH + Mathf.Abs(c.y)) * 1.03f;
            float k = Mathf.Max(1f, Mathf.Max(needX / b.extents.x, needY / b.extents.y));
            if (k <= 1.001f) return;
            back.localScale *= k;
            Plugin.Log.LogInfo("Backdrop '" + back.name + "' scaled x" + k.ToString("0.00") + " to cover the " +
                               cam.aspect.ToString("0.00") + ":1 view");
        }
    }
}
