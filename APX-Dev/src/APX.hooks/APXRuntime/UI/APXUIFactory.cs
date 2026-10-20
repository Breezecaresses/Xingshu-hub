using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// APX 原生风格 UI 组件构建工厂 (Phase 2B-R4 生产整合版)
    /// 封装标准 UGUI 对象的创建、布局约束、防穿透与排版保底
    /// </summary>
    internal static class APXUIFactory
    {
        internal static GameObject CreateUIObject(string name, GameObject parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }
            return go;
        }

        internal static Image CreateImage(GameObject parent, string name, Sprite sprite, Color color, Image.Type imageType = Image.Type.Simple)
        {
            GameObject go = CreateUIObject(name, parent);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite ?? APXUIAssetService.WhiteSprite;
            img.color = color;
            img.type = (sprite != null && sprite.border != Vector4.zero) ? Image.Type.Sliced : imageType;
            return img;
        }

        internal static Text CreateText(
            GameObject parent,
            string name,
            string content,
            int fontSize,
            Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter,
            FontStyle style = FontStyle.Normal,
            HorizontalWrapMode horizontalWrap = HorizontalWrapMode.Overflow,
            VerticalWrapMode verticalWrap = VerticalWrapMode.Truncate)
        {
            GameObject go = CreateUIObject(name, parent);
            Text txt = go.AddComponent<Text>();
            txt.text = content;
            txt.font = APXUITheme.GetFontForText(content);
            txt.fontSize = fontSize;
            txt.raycastTarget = false;
            txt.color = color;
            txt.alignment = alignment;
            txt.fontStyle = style;
            txt.horizontalOverflow = horizontalWrap;
            txt.verticalOverflow = verticalWrap;
            return txt;
        }

        internal static Shadow AddShadow(GameObject target, Color color = default, Vector2 effectDistance = default)
        {
            if (target == null) return null;
            Shadow s = target.AddComponent<Shadow>();
            s.effectColor = (color == default) ? new Color(0f, 0f, 0f, 0.65f) : color;
            s.effectDistance = (effectDistance == default) ? new Vector2(1f, -1f) : effectDistance;
            return s;
        }

        internal static Outline AddOutline(GameObject target, Color color = default, Vector2 effectDistance = default)
        {
            if (target == null) return null;
            Outline o = target.AddComponent<Outline>();
            o.effectColor = (color == default) ? new Color(0f, 0f, 0f, 0.75f) : color;
            o.effectDistance = (effectDistance == default) ? new Vector2(1f, -1f) : effectDistance;
            return o;
        }

        internal static Button CreateButton(GameObject parent, string name, string label, Sprite bgSprite, Color bgColor, Color textColor, Action onClick, int fontSize = 14)
        {
            GameObject go = CreateUIObject(name, parent);
            Image img = go.AddComponent<Image>();
            img.sprite = bgSprite ?? APXUIAssetService.WhiteSprite;
            img.color = bgColor;
            img.type = (bgSprite != null && bgSprite.border != Vector4.zero) ? Image.Type.Sliced : Image.Type.Simple;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            btn.colors = cb;

            if (onClick != null)
            {
                btn.onClick.AddListener(new UnityAction(onClick));
            }

            if (!string.IsNullOrEmpty(label))
            {
                Color labelColor = bgSprite != null && bgSprite.name.Contains("button_primary") ? Color.white : textColor;
                Text txt = CreateText(go, "Text", label, fontSize, labelColor, TextAnchor.MiddleCenter, FontStyle.Bold, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate);
                RectTransform txtRt = txt.rectTransform;
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = Vector2.zero;
                txtRt.offsetMax = Vector2.zero;
            }

            return btn;
        }

        internal static Slider CreateSlider(GameObject parent, string name, float min, float max, float value, Action<float> onValueChanged)
        {
            GameObject sliderRoot = CreateUIObject(name, parent);
            RectTransform sliderRt = sliderRoot.GetComponent<RectTransform>();
            sliderRt.sizeDelta = new Vector2(240f, 28f);

            // White capsule and official pink bar, with a four-point star handle.
            Sprite trackSprite = APXUIAssetService.GetSprite("slider_track.png");
            GameObject bgGo = CreateUIObject("Background", sliderRoot);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = trackSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = Vector2.zero;
            bgRt.sizeDelta = new Vector2(-20f, 18f);

            // The fill and star share their horizontal range, including both endpoints.
            GameObject fillArea = CreateUIObject("Fill Area", sliderRoot);
            RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.pivot = new Vector2(0.5f, 0.5f);
            fillAreaRt.anchoredPosition = Vector2.zero;
            fillAreaRt.offsetMin = new Vector2(20f, -7f);
            fillAreaRt.offsetMax = new Vector2(-20f, 7f);

            Sprite fillSprite = APXUIAssetService.GetSprite("slider_fill.png");
            GameObject fillGo = CreateUIObject("Fill", fillArea);
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = fillSprite;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = Color.white;
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;

            // Keep a 40px pointer target even in rows whose preferred height is 28px.
            GameObject handleArea = CreateUIObject("Handle Slide Area", sliderRoot);
            RectTransform handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = new Vector2(0f, 0.5f);
            handleAreaRt.anchorMax = new Vector2(1f, 0.5f);
            handleAreaRt.pivot = new Vector2(0.5f, 0.5f);
            handleAreaRt.anchoredPosition = Vector2.zero;
            handleAreaRt.offsetMin = new Vector2(20f, -20f);
            handleAreaRt.offsetMax = new Vector2(-20f, 20f);

            Sprite handleSprite = APXUIAssetService.GetSprite("slider_handle.png");
            GameObject handleGo = CreateUIObject("Handle", handleArea);
            Image handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = handleSprite;
            handleImg.type = Image.Type.Simple;
            handleImg.preserveAspect = true;
            handleImg.color = Color.white;
            RectTransform handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(40f, 0f);

            // 4. Slider Component
            Slider slider = sliderRoot.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            if (onValueChanged != null)
            {
                slider.onValueChanged.AddListener(new UnityAction<float>(onValueChanged));
            }

            return slider;
        }

        internal static Toggle CreateToggle(GameObject parent, string name, string label, bool initialValue, Action<bool> onValueChanged)
        {
            GameObject toggleRoot = CreateUIObject(name, parent);
            RectTransform toggleRt = toggleRoot.GetComponent<RectTransform>();
            toggleRt.sizeDelta = new Vector2(220f, 32f);

            // Background Switch Box (Capsule)
            Sprite trackOnSprite = APXUIAssetService.GetSprite("toggle_track_on.png");
            Sprite trackOffSprite = APXUIAssetService.GetSprite("toggle_track_off.png");
            Sprite thumbSprite = APXUIAssetService.GetSprite("toggle_thumb.png");

            GameObject bgGo = CreateUIObject("Background", toggleRoot);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = initialValue ? trackOnSprite : trackOffSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(0f, 0.5f);
            bgRt.pivot = new Vector2(0f, 0.5f);
            bgRt.anchoredPosition = Vector2.zero;
            bgRt.sizeDelta = new Vector2(64f, 32f);

            // Thumb (True Circle)
            GameObject checkGo = CreateUIObject("Checkmark", bgGo);
            Image checkImg = checkGo.AddComponent<Image>();
            checkImg.sprite = thumbSprite;
            checkImg.type = Image.Type.Simple;
            checkImg.preserveAspect = true;
            checkImg.color = Color.white;
            checkImg.raycastTarget = false;
            RectTransform checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.5f, 0.5f);
            checkRt.anchorMax = new Vector2(0.5f, 0.5f);
            checkRt.pivot = new Vector2(0.5f, 0.5f);
            checkRt.sizeDelta = new Vector2(24f, 24f);
            checkRt.anchoredPosition = initialValue ? new Vector2(16f, 0f) : new Vector2(-16f, 0f);

            // Label
            Text labelTxt = CreateText(toggleRoot, "Label", label, 16, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate);
            RectTransform labelRt = labelTxt.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(76f, 0f);
            labelRt.offsetMax = Vector2.zero;

            Toggle toggle = toggleRoot.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.graphic = null; // Do not let UGUI automatically hide checkImg when isOn is false
            toggle.targetGraphic = bgImg;
            toggle.isOn = initialValue;

            // Explicitly sync track and thumb to initial state
            bgImg.sprite = initialValue ? trackOnSprite : trackOffSprite;
            checkRt.anchoredPosition = initialValue ? new Vector2(16f, 0f) : new Vector2(-16f, 0f);
            checkImg.color = Color.white;
            checkImg.enabled = true;
            checkGo.SetActive(true);

            toggle.onValueChanged.AddListener(delegate(bool state)
            {
                bgImg.sprite = state ? trackOnSprite : trackOffSprite;
                checkRt.anchoredPosition = state ? new Vector2(16f, 0f) : new Vector2(-16f, 0f);
                if (onValueChanged != null)
                {
                    onValueChanged(state);
                }
            });

            return toggle;
        }

        internal static Toggle CreateToggleRow(GameObject parent, string name, string label, bool initialValue, Action<bool> onValueChanged)
        {
            GameObject toggleRoot = CreateUIObject(name, parent);
            RectTransform toggleRt = toggleRoot.GetComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(0f, 1f);
            toggleRt.anchorMax = new Vector2(1f, 1f);
            toggleRt.pivot = new Vector2(0.5f, 1f);
            toggleRt.sizeDelta = new Vector2(0f, 32f);

            // Label (Left side, primary text)
            Text labelTxt = CreateText(toggleRoot, "Label", label, 14, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Normal, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate);
            RectTransform labelRt = labelTxt.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.offsetMin = new Vector2(24f, 0f);
            labelRt.offsetMax = new Vector2(-100f, 0f);

            // Background Switch Box (Capsule on the right edge)
            Sprite trackOnSprite = APXUIAssetService.GetSprite("toggle_track_on.png");
            Sprite trackOffSprite = APXUIAssetService.GetSprite("toggle_track_off.png");
            Sprite thumbSprite = APXUIAssetService.GetSprite("toggle_thumb.png");

            GameObject bgGo = CreateUIObject("Background", toggleRoot);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = initialValue ? trackOnSprite : trackOffSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(1f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.pivot = new Vector2(1f, 0.5f);
            bgRt.anchoredPosition = new Vector2(-24f, 0f);
            bgRt.sizeDelta = new Vector2(56f, 28f);

            // Thumb (Circle)
            GameObject checkGo = CreateUIObject("Checkmark", bgGo);
            Image checkImg = checkGo.AddComponent<Image>();
            checkImg.sprite = thumbSprite;
            checkImg.type = Image.Type.Simple;
            checkImg.preserveAspect = true;
            checkImg.color = Color.white;
            checkImg.raycastTarget = false;
            RectTransform checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.5f, 0.5f);
            checkRt.anchorMax = new Vector2(0.5f, 0.5f);
            checkRt.pivot = new Vector2(0.5f, 0.5f);
            checkRt.sizeDelta = new Vector2(20f, 20f);
            checkRt.anchoredPosition = initialValue ? new Vector2(14f, 0f) : new Vector2(-14f, 0f);

            Toggle toggle = toggleRoot.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.graphic = null;
            toggle.targetGraphic = bgImg;
            toggle.isOn = initialValue;

            bgImg.sprite = initialValue ? trackOnSprite : trackOffSprite;
            checkRt.anchoredPosition = initialValue ? new Vector2(14f, 0f) : new Vector2(-14f, 0f);
            checkImg.color = Color.white;
            checkImg.enabled = true;
            checkGo.SetActive(true);

            toggle.onValueChanged.AddListener(delegate(bool state)
            {
                bgImg.sprite = state ? trackOnSprite : trackOffSprite;
                checkRt.anchoredPosition = state ? new Vector2(14f, 0f) : new Vector2(-14f, 0f);
                if (onValueChanged != null)
                {
                    onValueChanged(state);
                }
            });

            return toggle;
        }

        internal static void SetToggleRowStateWithoutNotify(Toggle toggle, bool state)
        {
            if (toggle == null) return;
            toggle.SetIsOnWithoutNotify(state);
            UpdateToggleRowVisual(toggle, state);
        }

        internal static void UpdateToggleRowVisual(Toggle toggle, bool state)
        {
            if (toggle == null) return;
            Transform bgTrans = toggle.transform.Find("Background");
            if (bgTrans != null)
            {
                Image bgImg = bgTrans.GetComponent<Image>();
                if (bgImg != null)
                {
                    bgImg.sprite = state ? APXUIAssetService.GetSprite("toggle_track_on.png") : APXUIAssetService.GetSprite("toggle_track_off.png");
                }
                Transform checkTrans = bgTrans.Find("Checkmark");
                if (checkTrans != null)
                {
                    RectTransform checkRt = checkTrans.GetComponent<RectTransform>();
                    if (checkRt != null)
                    {
                        checkRt.anchoredPosition = state ? new Vector2(14f, 0f) : new Vector2(-14f, 0f);
                    }
                }
            }
        }

        internal static void SetToggleStateWithoutNotify(Toggle toggle, bool state)
        {
            if (toggle == null) return;
            toggle.SetIsOnWithoutNotify(state);
            UpdateToggleVisual(toggle, state);
        }

        internal static void UpdateToggleVisual(Toggle toggle, bool state)
        {
            if (toggle == null) return;
            Transform bgTrans = toggle.transform.Find("Background");
            if (bgTrans != null)
            {
                Image bgImg = bgTrans.GetComponent<Image>();
                if (bgImg != null)
                {
                    bgImg.sprite = state ? APXUIAssetService.GetSprite("toggle_track_on.png") : APXUIAssetService.GetSprite("toggle_track_off.png");
                }
                Transform checkTrans = bgTrans.Find("Checkmark");
                if (checkTrans != null)
                {
                    RectTransform checkRt = checkTrans.GetComponent<RectTransform>();
                    if (checkRt != null)
                    {
                        checkRt.anchoredPosition = state ? new Vector2(16f, 0f) : new Vector2(-16f, 0f);
                    }
                }
            }
        }

        internal static InputField CreateInputField(
            GameObject parent,
            string name,
            string placeholder,
            string initialValue,
            string contextId,
            Action<string> onValueChanged = null,
            Action<string> onEndEdit = null,
            int fontSize = 14)
        {
            GameObject root = CreateUIObject(name, parent);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(240f, 34f);

            Sprite bgSprite = APXUIAssetService.GetSprite("button_secondary.png");
            Image bgImg = root.AddComponent<Image>();
            bgImg.sprite = bgSprite ?? APXUIAssetService.WhiteSprite;
            bgImg.type = (bgSprite != null && bgSprite.border != Vector4.zero) ? Image.Type.Sliced : Image.Type.Simple;
            bgImg.color = Color.white;

            // Text
            GameObject textObj = CreateUIObject("Text", root);
            Text textComp = textObj.AddComponent<Text>();
            textComp.font = APXUITheme.GetMainFont();
            textComp.fontSize = fontSize;
            textComp.color = APXUITheme.PrimaryText;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.supportRichText = false;
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 0f);
            textRt.offsetMax = new Vector2(-10f, 0f);

            // Placeholder
            GameObject phObj = CreateUIObject("Placeholder", root);
            Text phComp = phObj.AddComponent<Text>();
            phComp.font = APXUITheme.GetMainFont();
            phComp.fontSize = fontSize;
            phComp.color = APXUITheme.MutedText;
            phComp.alignment = TextAnchor.MiddleLeft;
            phComp.text = placeholder ?? "";
            phComp.fontStyle = FontStyle.Italic;
            RectTransform phRt = phObj.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(10f, 0f);
            phRt.offsetMax = new Vector2(-10f, 0f);

            InputField inputField = root.AddComponent<InputField>();
            inputField.textComponent = textComp;
            inputField.placeholder = phComp;
            inputField.text = initialValue ?? "";
            inputField.caretColor = new Color(0.22f, 0.74f, 0.97f, 1f); // #38bdf8
            inputField.selectionColor = new Color(0.22f, 0.74f, 0.97f, 0.35f);
            inputField.caretWidth = 2;
            inputField.targetGraphic = bgImg;

            if (onValueChanged != null)
            {
                inputField.onValueChanged.AddListener(new UnityAction<string>(onValueChanged));
            }
            if (onEndEdit != null)
            {
                inputField.onEndEdit.AddListener(new UnityAction<string>(onEndEdit));
            }

            APXUITextEntryBridge bridge = root.AddComponent<APXUITextEntryBridge>();
            bridge.ContextId = contextId ?? "";

            return inputField;
        }

        internal static ScrollRect CreateVerticalScrollView(
            GameObject parent,
            string name,
            out GameObject contentArea)
        {
            GameObject scrollRoot = CreateUIObject(name, parent);
            RectTransform scrollRt = scrollRoot.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            ScrollRect scrollRect = scrollRoot.AddComponent<ScrollRect>();

            GameObject viewport = CreateUIObject("Viewport", scrollRoot);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            vpRt.pivot = new Vector2(0f, 1f);
            viewport.AddComponent<RectMask2D>();

            contentArea = CreateUIObject("Content", viewport);
            RectTransform contentRt = contentArea.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = contentArea.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            ContentSizeFitter csf = contentArea.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = vpRt;
            scrollRect.content = contentRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25f;

            return scrollRect;
        }
    }
}
