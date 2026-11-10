using System;
using System.Collections.Generic;
using DarkTonic.MasterAudio;
using NoR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoR.Randomizer.Shop
{
    /// <summary>
    /// "Witch's Market": a shop opened at save points (the vanilla shop of 1.0.7 is unfinished and unreachable).
    ///   - one-time slots: randomizer locations "shop:N", contents from the shuffle, bought state in
    ///     _TreasureItemArrayStage[ShopCheckRow, N] (saved by the vanilla save code)
    ///   - restock: vanilla shop consumables, buyable any number of times
    /// Currency: Idea (PlayerStatus.Exppoint).
    /// </summary>
    public sealed class ShopWindow : MonoBehaviour
    {
        public static ShopWindow Current { get; private set; }
        public static float ClosedAt = -10f;

        private static readonly int[] Restock = { 3, 4, 5, 6, 7, 8, 29 }; // UseItem indices of the vanilla shop list

        private sealed class Row
        {
            public Button Button;
            public Text Name, Price;
            public Image Icon;
            public string Code;      // item code
            public int Cost;
            public LocationInfo Slot; // null for restock rows
        }

        private readonly List<Row> _rows = new List<Row>();
        private Text _idea, _message;
        private Font _font;
        private float _openedAt;

        public static void Open()
        {
            if (Current != null || Plugin.State == null) return;
            var go = new GameObject("NoR.WitchMarket");
            Current = go.AddComponent<ShopWindow>();
        }

        private void Start()
        {
            _openedAt = Time.unscaledTime;
            var st = GameRefs.Status;
            st._SOUSA = false;
            st._SOUSAMNG = false;
            _font = FindFont();
            Build();
            Refresh();
            MasterAudio.PlaySound("snd_getitem1");
        }

        private void Update()
        {
            // Cancel = the game's "Jump" action (same as all vanilla menus) or Escape.
            var pc = GameRefs.Player != null ? GameRefs.Player.GetComponent<playercon>() : null;
            bool cancel = Input.GetKeyDown(KeyCode.Escape) || (pc != null && pc._key_cancel);
            if (cancel && Time.unscaledTime - _openedAt > 0.3f) Close();

            // Keep a button selected so gamepad navigation never gets lost.
            var es = EventSystem.current;
            if (es != null && (es.currentSelectedGameObject == null || es.currentSelectedGameObject.GetComponentInParent<ShopWindow>() != this) && _rows.Count > 0)
                es.SetSelectedGameObject(_rows[0].Button.gameObject);
        }

        private void Close()
        {
            var st = GameRefs.Status;
            if (st != null) { st._SOUSA = true; st._SOUSAMNG = true; }
            Current = null;
            ClosedAt = Time.unscaledTime;
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ purchase

        private void Buy(Row r)
        {
            var st = GameRefs.Status;
            if (r.Slot != null && IsBought(r.Slot)) { Say("Sold out."); return; }
            if (st.Exppoint < r.Cost) { Say("Not enough Idea."); return; }

            st.Exppoint -= r.Cost;
            if (r.Slot != null)
            {
                GameRefs.Flags._TreasureItemArrayStage[LocationTable.ShopCheckRow, r.Slot.B] = true;
                Plugin.Log.LogInfo("Checked " + r.Slot.Key + " -> " + r.Code + " for " + r.Cost + " Idea");
            }
            if (r.Slot != null) Checks.Collect(r.Slot.Key, r.Code); else ItemGranter.Grant(r.Code, Plugin.Table);
            Say("Thank you.");
            Refresh();
        }

        private static bool IsBought(LocationInfo slot)
        {
            return GameRefs.Flags._TreasureItemArrayStage[LocationTable.ShopCheckRow, slot.B];
        }

        public static int PriceOf(string code)
        {
            int total = 0;
            var inv = GameRefs.Inventory;
            foreach (var part in code.Split('+'))
            {
                var p = part.Split(':');
                int price;
                switch (p[0])
                {
                    case "weapon": price = 3000; break;
                    case "crestring": price = 2500; break;
                    case "magic": price = 3000; break;
                    case "UseItem":
                        int idx = int.Parse(p[1]);
                        price = inv != null && idx < inv._USE.Count && inv._USE[idx].buy > 0 ? inv._USE[idx].buy : 800;
                        break;
                    default: price = 1000; break;
                }
                total += price;
            }
            return Mathf.Max(1, Mathf.RoundToInt(total * Plugin.ShopPriceMultiplier));
        }

        // ------------------------------------------------------------------ UI

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var es = new GameObject("NoR.EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            var slots = Plugin.Table.ShopSlots;
            int slotCount = Mathf.Min(Plugin.State.ShopSlots, slots.Count);
            int rowsTotal = slotCount + Restock.Length;
            float rowH = 46f, top = 150f;
            float height = top + rowsTotal * rowH + 40f + (slotCount > 0 ? 34f : 0f) + 34f;

            var panel = Panel(transform, new Vector2(1000, height), new Color(0.05f, 0.03f, 0.06f, 0.93f));
            Label(panel, "Witch's Market", 44, new Vector2(0, -20), new Vector2(940, 60), TextAnchor.UpperCenter);
            _idea = Label(panel, "", 28, new Vector2(0, -78), new Vector2(940, 36), TextAnchor.UpperCenter);
            _message = Label(panel, "", 24, new Vector2(0, -(height - 44)), new Vector2(940, 32), TextAnchor.UpperCenter);

            float y = -top;
            if (slotCount > 0)
            {
                Label(panel, "Rare goods (one each)", 24, new Vector2(-300, y + 6), new Vector2(360, 30), TextAnchor.MiddleLeft).color = new Color(0.85f, 0.7f, 1f);
                y -= 34f;
                for (int i = 0; i < slotCount; i++)
                {
                    string code;
                    if (!Plugin.State.Placement.TryGetValue(slots[i].Key, out code)) continue;
                    _rows.Add(MakeRow(panel, y, code, slots[i]));
                    y -= rowH;
                }
            }
            Label(panel, "Supplies", 24, new Vector2(-300, y + 6), new Vector2(360, 30), TextAnchor.MiddleLeft).color = new Color(0.85f, 0.7f, 1f);
            y -= 34f;
            foreach (var idx in Restock)
            {
                _rows.Add(MakeRow(panel, y, "UseItem:" + idx, null));
                y -= rowH;
            }
            Label(panel, "Confirm: buy    Cancel: leave", 22, new Vector2(0, -(height - 80)), new Vector2(940, 30), TextAnchor.UpperCenter).color = new Color(0.7f, 0.7f, 0.7f);

            for (int i = 0; i < _rows.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = _rows[(i - 1 + _rows.Count) % _rows.Count].Button;
                nav.selectOnDown = _rows[(i + 1) % _rows.Count].Button;
                _rows[i].Button.navigation = nav;
            }
            if (_rows.Count > 0 && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_rows[0].Button.gameObject);
        }

        private Row MakeRow(RectTransform panel, float y, string code, LocationInfo slot)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(920, 42);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.06f);
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.06f);
            colors.highlightedColor = new Color(0.8f, 0.5f, 1f, 0.35f);
            colors.pressedColor = new Color(0.8f, 0.5f, 1f, 0.6f);
            btn.colors = colors;
            btn.targetGraphic = img;

            int cost = code == RandoState.RemoteItem
                ? Mathf.Max(1, Mathf.RoundToInt((Checks.RemotePrice != null ? Checks.RemotePrice(slot.Key) : 1500) * Plugin.ShopPriceMultiplier))
                : PriceOf(code);
            var row = new Row { Button = btn, Code = code, Slot = slot, Cost = cost };
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(26, 0);
            irt.sizeDelta = new Vector2(36, 36);
            row.Icon = iconGo.GetComponent<Image>();
            row.Icon.preserveAspect = true;

            row.Name = Label(rt, "", 26, new Vector2(-40, 0), new Vector2(700, 40), TextAnchor.MiddleLeft, middle: true);
            row.Price = Label(rt, "", 26, new Vector2(370, 0), new Vector2(160, 40), TextAnchor.MiddleRight, middle: true);
            var captured = row;
            btn.onClick.AddListener(() => Buy(captured));
            return row;
        }

        private void Refresh()
        {
            var st = GameRefs.Status;
            _idea.text = "Idea: " + st.Exppoint;
            foreach (var r in _rows)
            {
                bool sold = r.Slot != null && IsBought(r.Slot);
                string name;
                Sprite icon;
                if (r.Code == RandoState.RemoteItem)
                {
                    name = Checks.RemoteDescribe != null ? Checks.RemoteDescribe(r.Slot.Key) : "Archipelago item";
                    icon = null;
                }
                else Describe(r.Code, out name, out icon);
                r.Name.text = name;
                r.Icon.sprite = icon;
                r.Icon.enabled = icon != null;
                r.Price.text = sold ? "SOLD" : r.Cost.ToString();
                var c = sold ? new Color(0.5f, 0.5f, 0.5f) : (st.Exppoint >= r.Cost ? Color.white : new Color(1f, 0.55f, 0.55f));
                r.Name.color = c;
                r.Price.color = c;
            }
        }

        /// <summary>Name/icon from the live game data (English when NoR.Localization is installed).</summary>
        private static void Describe(string code, out string name, out Sprite icon)
        {
            var inv = GameRefs.Inventory;
            var pl = GameRefs.Status;
            var names = new List<string>();
            icon = null;
            foreach (var part in code.Split('+'))
            {
                var p = part.Split(':');
                string n = part, sprite = null;
                try
                {
                    int idx = p.Length > 1 ? int.Parse(p[p.Length > 2 ? 2 : 1]) : -1;
                    switch (p[0])
                    {
                        case "weapon": n = inv._WP[idx].Itemname; sprite = inv._WP[idx].name; break;
                        case "crestring": n = inv._RING[idx].Itemname; sprite = inv._RING[idx].name; break;
                        case "UseItem": n = inv._USE[idx].Itemname; sprite = inv._USE[idx].name; break;
                        case "magic":
                            var src = Plugin.Table.MagicSource(part);
                            if (src != null) { n = src.DisplayJp; sprite = src.MagicName; }
                            break;
                    }
                    if (icon == null && sprite != null) icon = pl._atlas_sprite.GetSprite(sprite);
                }
                catch (Exception) { /* keep code as name */ }
                names.Add(n);
            }
            name = string.Join(" + ", names.ToArray());
        }

        private void Say(string text)
        {
            _message.text = text;
        }

        private static RectTransform Panel(Transform parent, Vector2 size, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private Text Label(RectTransform parent, string text, float size, Vector2 pos, Vector2 box, TextAnchor align, bool middle = false)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = middle ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
            rt.pivot = middle ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = Mathf.RoundToInt(size);
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            return t;
        }

        /// <summary>
        /// Dynamic OS font (always has every glyph). The game's TMP fonts are partial or MSDF atlases that render
        /// Latin text with broken glyphs outside the developer's own setup.
        /// </summary>
        private static Font FindFont()
        {
            var f = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman", "Cambria", "Arial" }, 28);
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            Plugin.Log.LogInfo("Shop font: " + (f != null ? f.name : "none"));
            return f;
        }
    }
}
