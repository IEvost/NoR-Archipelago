using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoR.Widescreen
{
    /// <summary>
    /// The censor quads use the shader Ist/MosaicField, which samples a *named* GrabPass ("_FrameBuffer1"). Unity grabs a
    /// named pass only once per frame, when the first object using it is drawn. The quads sit in different sorting
    /// layers/orders (some at -5 in a lower layer), so when one of those is on screen the frame is grabbed before the
    /// characters are drawn and every later quad pastes that early copy: a square of background over the scene (also
    /// with uncensor patches that set the block size to 1). Zooming in far enough moved the early quad off screen.
    ///
    /// Fix: move every MosaicField renderer to the topmost sorting layer with a high order, so the grab always happens
    /// after all characters. With block size 1 the copy is exact (invisible); with the vanilla material the mosaic still
    /// covers the same area.
    /// </summary>
    public sealed class MosaicOrderFix : MonoBehaviour
    {
        internal static ConfigEntry<bool> Enabled;
        private const int TopOrder = 32000;

        private readonly HashSet<int> _done = new HashSet<int>();
        private int _topLayerId;
        private bool _haveLayer;
        private float _next;
        private bool _fullScan = true;

        public static void Create(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Camera", "FixMosaicOrder", true,
                "Draw censor quads after all characters (fixes squares of background appearing over scenes).");
            var go = new GameObject("NoR.MosaicOrderFix");
            DontDestroyOnLoad(go);
            go.AddComponent<MosaicOrderFix>();
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += (s, m) => { _done.Clear(); _next = 0f; _fullScan = true; };
        }

        private void Update()
        {
            if (!Enabled.Value || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + (PlayerGrabbed() ? 0.2f : 1f);
            if (!_haveLayer)
            {
                var layers = SortingLayer.layers;
                if (layers == null || layers.Length == 0) return;
                int best = int.MinValue;
                foreach (var l in layers)
                {
                    int v = SortingLayer.GetLayerValueFromID(l.id);
                    if (v > best) { best = v; _topLayerId = l.id; }
                }
                _haveLayer = true;
                Plugin.Log.LogInfo("Mosaic order fix: top sorting layer " + SortingLayer.IDToName(_topLayerId));
            }
            // Full scan (inactive objects too, incl. the enemy templates the randomizer clones) once per scene load; clones
            // copy the sorting fields. Afterwards only active renderers, for anything created later.
            var renderers = _fullScan ? Resources.FindObjectsOfTypeAll<MeshRenderer>() : FindObjectsOfType<MeshRenderer>();
            _fullScan = false;
            foreach (var r in renderers)
            {
                if (r == null || !_done.Add(r.GetInstanceID())) continue;
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "Ist/MosaicField") continue;
                r.sortingLayerID = _topLayerId;
                r.sortingOrder = TopOrder;
            }
            if (_done.Count > 100000) _done.Clear();
        }

        private static bool PlayerGrabbed()
        {
            var go = GameObject.FindWithTag("Player");
            var pc = go != null ? go.GetComponent<playercon>() : null;
            return pc != null && pc.eroflag;
        }
    }
}
