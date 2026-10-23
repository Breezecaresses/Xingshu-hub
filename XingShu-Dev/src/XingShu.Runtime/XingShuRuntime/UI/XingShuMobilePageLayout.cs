using System.Collections.Generic;
using XingShuRuntime.Core;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    // Mobile presentation uses the same controls and callbacks as desktop. Only
    // the layout changes: no scaled-down desktop rectangles or duplicate state.
    internal sealed class XingShuMobilePageLayout : MonoBehaviour
    {
        internal const float ReferenceHeight = 540f;
        internal const float TouchHeight = 64f;
        internal const int BodyFont = 22;
        private readonly List<Text> _texts = new List<Text>();
        private readonly List<int> _fontSizes = new List<int>();
        private readonly List<GridLayoutGroup> _grids = new List<GridLayoutGroup>();

        internal static float TextScale => Utilities.XingShuMobileUIHelper.FiniteClamp(
            XingShuConfigService.GetFloat(XingShuConfigKeys.MobileTextScale, XingShuConfigKeys.MobileTextScaleDefault),
            XingShuConfigKeys.MobileTextScaleMin, XingShuConfigKeys.MobileTextScaleMax, XingShuConfigKeys.MobileTextScaleDefault);

        internal static float PanelScale => Utilities.XingShuMobileUIHelper.FiniteClamp(
            XingShuConfigService.GetFloat(XingShuConfigKeys.MobilePanelScale, 1f),
            XingShuConfigKeys.MobilePanelScaleMin, XingShuConfigKeys.MobilePanelScaleMax, 1f);

        internal static void Adapt(GameObject page)
        {
            var layout = page.AddComponent<XingShuMobilePageLayout>();
            ScrollRect scroll = page.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null)
            {
                var cards = Children(page.transform);
                scroll = XingShuUIFactory.CreateVerticalScrollView(page, "MobileScroll", out GameObject content);
                foreach (Transform card in cards) card.SetParent(content.transform, false);
            }
            scroll.inertia = true;
            scroll.decelerationRate = .09f;
            scroll.scrollSensitivity = 48f;
            var viewportHit = scroll.viewport.GetComponent<Image>();
            if (viewportHit != null) viewportHit.color = new Color(0, 0, 0, .001f);
            layout.Flow(scroll.content.gameObject, true);
            // Capture after reflow; refresh never compounds a previous font scale.
            foreach (Text text in page.GetComponentsInChildren<Text>(true))
            {
                layout._texts.Add(text);
                layout._fontSizes.Add(text.fontSize);
            }
            layout.RefreshText();
        }

        internal static void RefreshAll(GameObject canvas)
        {
            if (canvas == null) return;
            foreach (var page in canvas.GetComponentsInChildren<XingShuMobilePageLayout>(true)) page.RefreshText();
        }

        private void RefreshText()
        {
            float scale = TextScale;
            for (int i = 0; i < _texts.Count; i++)
                if (_texts[i] != null) _texts[i].fontSize = Mathf.RoundToInt(_fontSizes[i] * scale);
        }

        private static List<Transform> Children(Transform root)
        {
            var result = new List<Transform>();
            for (int i = 0; i < root.childCount; i++) result.Add(root.GetChild(i));
            return result;
        }

        private static LayoutElement Element(GameObject go, float height = -1f)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.ignoreLayout = false;
            element.minWidth = 0;
            element.preferredWidth = -1;
            element.flexibleWidth = 1;
            element.minHeight = height < 0 ? 0 : height;
            element.preferredHeight = height;
            element.flexibleHeight = 0;
            return element;
        }

        private static void RemoveLayout(GameObject go)
        {
            foreach (var group in go.GetComponents<LayoutGroup>()) DestroyImmediate(group);
        }

        private void Flow(GameObject go, bool content = false)
        {
            // Player cards have an authored three-column mobile layout.
            if (go.transform.Find("PlayerDetailCard") != null) return;
            var children = Children(go.transform);
            bool ordered = go.GetComponent<LayoutGroup>() != null;
            if (!ordered)
            {
                // Desktop cards use absolute positions; keep their reading order.
                children.Sort((a, b) => {
                    var ar = (RectTransform)a; var br = (RectTransform)b;
                    int rowA = Mathf.RoundToInt((a.localPosition.y + ar.rect.yMax) / 8f);
                    int rowB = Mathf.RoundToInt((b.localPosition.y + br.rect.yMax) / 8f);
                    return rowA != rowB ? rowB.CompareTo(rowA) : a.localPosition.x.CompareTo(b.localPosition.x);
                });
            }
            RemoveLayout(go);
            var flow = go.AddComponent<VerticalLayoutGroup>();
            flow.padding = content ? new RectOffset(2, 2, 4, 24) : new RectOffset(18, 18, 16, 16);
            flow.spacing = content ? 16 : 12;
            flow.childControlWidth = flow.childControlHeight = true;
            flow.childForceExpandWidth = true;
            flow.childForceExpandHeight = false;
            flow.childAlignment = TextAnchor.UpperLeft;
            if (!content) Element(go);
            GameObject buttonRow = null;
            foreach (Transform child in children)
            {
                GameObject item = child.gameObject;
                if (item.name.Contains("Hotkey") || item.name == "Btn_NativeEngine" || item.name == "EngineSelectorCard")
                {
                    item.SetActive(false);
                    continue;
                }
                child.SetAsLastSibling();
                var button = item.GetComponent<Button>();
                if (button != null)
                {
                    if (buttonRow == null)
                    {
                        buttonRow = XingShuUIFactory.CreateUIObject("MobileActions", go);
                        Grid(buttonRow);
                    }
                    child.SetParent(buttonRow.transform, false);
                    Control(item);
                    continue;
                }
                buttonRow = null;
                if (item.GetComponent<Selectable>() != null) { Control(item); continue; }
                Text text = item.GetComponent<Text>();
                if (text != null)
                {
                    Readable(text);
                    Element(item);
                    continue;
                }
                if (child.childCount > 0) Flow(item);
                else
                {
                    // Color samples remain compact but visible; decorative lines
                    // do not inherit a desktop stretch height.
                    Element(item, item.name.Contains("Swatch") ? 32 : 8);
                }
            }
        }

        private void Grid(GameObject go)
        {
            Element(go);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.spacing = new Vector2(12, 12);
            grid.cellSize = new Vector2(300, TouchHeight);
            _grids.Add(grid);
        }

        private static void Readable(Text text)
        {
            text.fontSize = Mathf.Clamp(text.fontSize, BodyFont, 32);
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.1f;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
        }

        private static void Control(GameObject go)
        {
            Element(go, TouchHeight);
            foreach (Text text in go.GetComponentsInChildren<Text>(true)) Readable(text);
            var button = go.GetComponent<Button>();
            if (button != null)
            {
                var text = go.GetComponentInChildren<Text>(true);
                if (text != null)
                {
                    text.alignment = TextAnchor.MiddleCenter;
                    text.rectTransform.offsetMin = new Vector2(14, 4);
                    text.rectTransform.offsetMax = new Vector2(-14, -4);
                }
                return;
            }
            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                var hit = go.GetComponent<Image>();
                if (hit != null) hit.color = new Color(0, 0, 0, .001f);
                var label = go.transform.Find("Label") as RectTransform;
                if (label != null)
                {
                    label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
                    label.offsetMin = new Vector2(4, 0); label.offsetMax = new Vector2(-112, 0);
                }
                var track = go.transform.Find("Background") as RectTransform;
                if (track != null)
                {
                    track.anchorMin = track.anchorMax = new Vector2(1, .5f);
                    track.pivot = new Vector2(1, .5f);
                    track.anchoredPosition = new Vector2(-8, 0);
                    // Scale the track as a unit so existing thumb callbacks keep
                    // their correct on/off endpoints for both switch variants.
                    track.localScale = Vector3.one * 1.4f;
                }
                return;
            }
            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                var hit = go.GetComponent<Image>();
                if (hit != null) hit.color = new Color(0, 0, 0, .001f);
                var area = slider.handleRect.parent as RectTransform;
                area.offsetMin = new Vector2(28, -28); area.offsetMax = new Vector2(-28, 28);
                slider.handleRect.sizeDelta = new Vector2(56, 0);
                var fill = slider.fillRect.parent as RectTransform;
                fill.offsetMin = new Vector2(28, -8); fill.offsetMax = new Vector2(-28, 8);
                XingShuMobileSliderInput.Bind(slider);
            }
        }

        private void LateUpdate()
        {
            float scale = TextScale;
            foreach (var grid in _grids)
            {
                float width = ((RectTransform)grid.transform).rect.width;
                int columns = width < 500 ? 1 : 2;
                Vector2 cell = new Vector2(Mathf.Max(1, (width - (columns - 1) * 12) / columns), TouchHeight * scale);
                if (grid.constraintCount != columns) grid.constraintCount = columns;
                if (grid.cellSize != cell) grid.cellSize = cell;
            }
        }
    }
}
