using System.Collections;
using System.Linq;
using System.Text;
using Com.LuisPedroFonseca.ProCamera2D;
using HarmonyLib;
using UnityEngine;

namespace NoR.Widescreen
{
    /// <summary>
    /// Diagnostic: detects the camera oscillating (render position or size reversing direction many times within half a
    /// second while the player stands still) and logs one snapshot of the camera state, at most every 5 seconds.
    /// </summary>
    public sealed class CameraJitterProbe : MonoBehaviour
    {
        private Vector3 _prevPos;
        private float _prevSize;
        private float _prevDx, _prevDs;
        private int _flips;
        private float _windowStart;
        private float _nextReport;
        private int _frames;

        public static void Create()
        {
            var go = new GameObject("NoR.CameraJitterProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraJitterProbe>();
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            var pc2d = ProCamera2D.Instance;
            if (cam == null || pc2d == null) return;
            Vector3 pos = cam.transform.position; // world position includes the shake container
            float size = pc2d.ScreenSizeInWorldCoordinates.y;
            float dx = pos.x - _prevPos.x, ds = size - _prevSize;
            if (Mathf.Abs(dx) > 0.002f && Mathf.Abs(_prevDx) > 0.002f && Mathf.Sign(dx) != Mathf.Sign(_prevDx)) _flips++;
            if (Mathf.Abs(ds) > 0.002f && Mathf.Abs(_prevDs) > 0.002f && Mathf.Sign(ds) != Mathf.Sign(_prevDs)) _flips++;
            _prevDx = dx; _prevDs = ds; _prevPos = pos; _prevSize = size; _frames++;

            if (Time.unscaledTime - _windowStart > 0.5f)
            {
                if (_flips >= 6 && Time.unscaledTime > _nextReport && PlayerSlow())
                {
                    _nextReport = Time.unscaledTime + 5f;
                    Report(cam, pc2d, dx, ds);
                }
                _flips = 0; _frames = 0;
                _windowStart = Time.unscaledTime;
            }
        }

        private int _windowFrames;

        private static bool PlayerSlow()
        {
            var go = GameObject.FindWithTag("Player");
            var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
            return rb == null || Mathf.Abs(rb.velocity.x) < 1f;
        }

        private void Report(Camera cam, ProCamera2D pc2d, float dx, float ds)
        {
            try
            {
                var t = Traverse.Create(pc2d);
                var sb = new StringBuilder("[jitter] camera oscillating: flips=" + _flips + " in 0.5s");
                sb.Append(" | fps=").Append((1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)).ToString("0"))
                  .Append(" fixedDt=").Append(Time.fixedDeltaTime.ToString("0.000"))
                  .Append(" timeScale=").Append(Time.timeScale.ToString("0.00"))
                  .Append(" update=").Append(pc2d.UpdateType);
                sb.Append(" | pos=").Append(cam.transform.position.ToString("F3")).Append(" dx=").Append(dx.ToString("0.0000"))
                  .Append(" local=").Append(cam.transform.localPosition.ToString("F3"))
                  .Append(" parent=").Append(cam.transform.parent != null ? cam.transform.parent.name + cam.transform.parent.localPosition.ToString("F3") : "-");
                sb.Append(" | size=").Append((pc2d.ScreenSizeInWorldCoordinates.y * 0.5f).ToString("0.000")).Append(" ds=").Append(ds.ToString("0.0000"))
                  .Append(" boundsMax=").Append(CameraZoom.BoundaryMaxHalfHeight(pc2d).ToString("0.000"))
                  .Append(" zoomLevel=").Append(CameraZoom.ZoomLevel.Value.ToString("0.00"))
                  .Append(" aspect=").Append(cam.aspect.ToString("0.000"));
                var fit = pc2d.GetComponent<ProCamera2DZoomToFitTargets>();
                if (fit != null)
                    sb.Append(" | fit enabled=").Append(fit.enabled).Append(" oneTarget=").Append(fit.DisableWhenOneTarget)
                      .Append(" init=").Append(Traverse.Create(fit).Field("_initialCamSize").GetValue<float>().ToString("0.000"))
                      .Append(" maxIn=").Append(fit.MaxZoomInAmount);
                var sp = pc2d.GetComponent<ProCamera2DSpeedBasedZoom>();
                if (sp != null)
                    sb.Append(" | speed enabled=").Append(sp.enabled).Append(" vel=").Append(sp.CurrentVelocity.ToString("0.00"))
                      .Append(" init=").Append(Traverse.Create(sp).Field("_initialCamSize").GetValue<float>().ToString("0.000"))
                      .Append(" maxIn=").Append(sp.MaxZoomInAmount).Append(" maxOut=").Append(sp.MaxZoomOutAmount);
                sb.Append(" | targets=").Append(pc2d.CameraTargets.Count);
                foreach (var name in new[] { "_preMovers", "_positionDeltaChangers", "_positionOverriders", "_sizeDeltaChangers", "_sizeOverriders", "_postMovers" })
                {
                    var list = t.Field(name).GetValue() as IEnumerable;
                    if (list == null) continue;
                    sb.Append(" | ").Append(name.TrimStart('_')).Append('=');
                    foreach (var o in list) sb.Append(o.GetType().Name.Replace("ProCamera2D", "")).Append(',');
                }
                var shake = pc2d.GetComponent<ProCamera2DShake>();
                if (shake != null)
                    sb.Append(" | shake constant=").Append(Traverse.Create(shake).Field("_isConstantShaking").GetValue<bool>());
                var comps = pc2d.GetComponents<MonoBehaviour>().Where(c => c != null && c.enabled).Select(c => c.GetType().Name);
                sb.Append(" | camera scripts=").Append(string.Join(",", comps.ToArray()));
                Plugin.Log.LogWarning(sb.ToString());
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[jitter] report failed: " + e.Message); }
        }
    }
}
