using System;
using System.Collections.Generic;
using XingShuRuntime.Core;
using XingShuRuntime.Features;
using XingShuRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    /// <summary>
    /// 功能中心 - 游戏全局变速 Modern UI 页面 (Phase 2D-2B2 双引擎整合版)
    /// 支持 [游戏内加速 (Managed XingShu)] 与 [原生时间加速 (Native Hook)] 两个互斥引擎无缝切换
    /// 遵循 R3.2 严整的垂直韵律，使用 R3.2 生产级原子资产，消除反馈循环与冗余分配
    /// </summary>
    internal sealed class XingShuGameSpeedView
    {
        private readonly GameObject _root;

        // 引擎切换 Segmented Buttons
        private Button _btnManagedEngine;
        private Image _imgManagedEngine;
        private Text _txtManagedEngine;
        private Button _btnNativeEngine;
        private Image _imgNativeEngine;
        private Text _txtNativeEngine;

        // 头部与开关卡片
        private Text _titleText;
        private Text _subtitleText;
        private Text _statusIndicatorText;
        private Text _hotkeyBadgeText;
        private Toggle _enableToggle;
        private Text _toggleLabelText;
        private Toggle _smartPauseToggle;
        private Text _smartPauseLabelText;

        // 调速滑块与预设卡片
        private Text _speedCardTitle;
        private Slider _speedSlider;
        private Text _speedValueText;
        private Text _tipText;

        private readonly List<Button> _presetButtons = new List<Button>();
        private readonly List<Image> _presetImages = new List<Image>();
        private readonly List<Text> _presetTexts = new List<Text>();
        private static readonly float[] PRESET_VALUES = new float[] { 0.5f, 1.0f, 1.5f, 2.0f, 3.0f, 5.0f };
        private static readonly string[] PRESET_LABELS = new string[] { "0.5x 慢速", "1.0x 原速", "1.5x", "2.0x 常用", "3.0x", "5.0x 极速" };

        private XingShuSpeedEngineMode _lastObservedEngine = XingShuSpeedEngineMode.ManagedXingShu;
        private float _lastObservedSpeed = -1f;
        private bool _lastObservedEnabled = false;
        private KeyCode _lastObservedHotkey = KeyCode.None;
        private bool _lastObservedSuspended = false;
        private AccelerationSuspendReason _lastObservedReasons = AccelerationSuspendReason.None;
        private bool _lastObservedSmartPauseEnabled = false;
        private bool _suppressCallbacks = false;

        internal GameObject Root => _root;

        internal XingShuGameSpeedView(Transform parent)
        {
            _root = XingShuUIFactory.CreateUIObject("Page_GameSpeed", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildUI();
        }

        private void BuildUI()
        {
            Sprite cardBaseSprite = XingShuUIAssetService.GetSprite("card_base.png");
            Sprite cfhSprite = XingShuUIAssetService.GetSprite("card_feature_header.png");
            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = XingShuUIAssetService.GetSprite("button_selected.png");

            // ==========================================
            // 0. 顶部引擎切换选择器 (Engine Selector Card: 48px, y: 0)
            // ==========================================
            GameObject selectorCard = XingShuUIFactory.CreateUIObject("EngineSelectorCard", _root);
            RectTransform selRt = selectorCard.GetComponent<RectTransform>();
            selRt.anchorMin = new Vector2(0f, 1f);
            selRt.anchorMax = new Vector2(1f, 1f);
            selRt.pivot = new Vector2(0.5f, 1f);
            selRt.anchoredPosition = new Vector2(0f, 0f);
            selRt.sizeDelta = new Vector2(0f, 48f);

            Image selBg = selectorCard.AddComponent<Image>();
            selBg.sprite = cardBaseSprite;
            selBg.type = Image.Type.Sliced;
            selBg.color = XingShuUIAssetService.IsFallbackSprite(cardBaseSprite) ? XingShuUITheme.Card : Color.white;

            // "加速引擎:" 标签
            Text engineLabel = XingShuUIFactory.CreateText(selectorCard, "EngineLabel", "加速引擎:", 14, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform elRt = engineLabel.rectTransform;
            elRt.anchorMin = new Vector2(0f, 0.5f);
            elRt.anchorMax = new Vector2(0f, 0.5f);
            elRt.pivot = new Vector2(0f, 0.5f);
            elRt.anchoredPosition = new Vector2(20f, 0f);
            elRt.sizeDelta = new Vector2(80f, 26f);

            // [ 游戏内加速 ] 按钮 (Managed XingShu)
            _btnManagedEngine = XingShuUIFactory.CreateButton(selectorCard, "Btn_ManagedEngine", "游戏内加速",
                btnSelSprite, Color.white, new Color(0.08f, 0.10f, 0.06f, 1f),
                delegate { SwitchEngineRequested(XingShuSpeedEngineMode.ManagedXingShu); }, 13);
            RectTransform bmeRt = _btnManagedEngine.GetComponent<RectTransform>();
            bmeRt.anchorMin = new Vector2(0f, 0.5f);
            bmeRt.anchorMax = new Vector2(0f, 0.5f);
            bmeRt.pivot = new Vector2(0f, 0.5f);
            bmeRt.anchoredPosition = new Vector2(105f, 0f);
            bmeRt.sizeDelta = new Vector2(130f, 32f);
            _imgManagedEngine = _btnManagedEngine.GetComponent<Image>();
            _txtManagedEngine = _btnManagedEngine.GetComponentInChildren<Text>();

            // [ 原生时间加速 ] 按钮 (Native Hook)
            _btnNativeEngine = XingShuUIFactory.CreateButton(selectorCard, "Btn_NativeEngine", "原生时间加速",
                btnSecSprite, Color.white, XingShuUITheme.SecondaryText,
                delegate { SwitchEngineRequested(XingShuSpeedEngineMode.NativeHook); }, 13);
            RectTransform bneRt = _btnNativeEngine.GetComponent<RectTransform>();
            bneRt.anchorMin = new Vector2(0f, 0.5f);
            bneRt.anchorMax = new Vector2(0f, 0.5f);
            bneRt.pivot = new Vector2(0f, 0.5f);
            bneRt.anchoredPosition = new Vector2(245f, 0f);
            bneRt.sizeDelta = new Vector2(130f, 32f);
            _imgNativeEngine = _btnNativeEngine.GetComponent<Image>();
            _txtNativeEngine = _btnNativeEngine.GetComponentInChildren<Text>();
            if (!XingShuSpeedEngineCoordinator.SupportsNativeEngine)
            {
                _btnNativeEngine.interactable = false;
                _txtNativeEngine.text = "原生 · 仅 Windows";
                bneRt.sizeDelta = new Vector2(165f, 32f);
            }

            // ==========================================
            // 1. 特色功能卡片 (Feature Header Card: 76px, y: -60px)
            // ==========================================
            GameObject headerGo = XingShuUIFactory.CreateUIObject("FeatureHeaderCard", _root);
            RectTransform headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -60f);
            headerRt.sizeDelta = new Vector2(0f, 76f);

            Image headerBg = headerGo.AddComponent<Image>();
            headerBg.sprite = cfhSprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = XingShuUIAssetService.IsFallbackSprite(cfhSprite) ? XingShuUITheme.CardFeatureBase : Color.white;

            // 标题: 游戏全局变速
            _titleText = XingShuUIFactory.CreateText(headerGo, "Title", "游戏全局变速", 20, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = _titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 10f);
            titleRt.sizeDelta = new Vector2(300f, 26f);

            // 说明文案
            _subtitleText = XingShuUIFactory.CreateText(headerGo, "Subtitle", "调整游戏运行速度，可通过快捷键快速打开或关闭变速设置", 13, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform subRt = _subtitleText.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(24f, -14f);
            subRt.sizeDelta = new Vector2(480f, 22f);

            // 状态指示文本 (● 已启用 / ○ 已暂停)
            _statusIndicatorText = XingShuUIFactory.CreateText(headerGo, "StatusIndicator", "● 已就绪", 14, XingShuUITheme.Positive, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform statusRt = _statusIndicatorText.rectTransform;
            statusRt.anchorMin = new Vector2(1f, 0.5f);
            statusRt.anchorMax = new Vector2(1f, 0.5f);
            statusRt.pivot = new Vector2(1f, 0.5f);
            statusRt.anchoredPosition = new Vector2(-88f, 0f);
            statusRt.sizeDelta = new Vector2(320f, 26f);

            // 动态热键徽章 [F7]
            _lastObservedHotkey = GameSpeedControllerGUI.CurrentHotkey;
            GameObject badgeGo = XingShuUIFactory.CreateUIObject("HotkeyBadge", headerGo);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 0.5f);
            badgeRt.anchorMax = new Vector2(1f, 0.5f);
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.anchoredPosition = new Vector2(-24f, 0f);
            badgeRt.sizeDelta = new Vector2(52f, 26f);

            Image badgeImg = badgeGo.AddComponent<Image>();
            badgeImg.sprite = XingShuUIAssetService.GetSprite("hotkey_badge.png");
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = XingShuUIAssetService.IsFallbackSprite(badgeImg.sprite) ? new Color(0.15f, 0.18f, 0.24f, 0.85f) : Color.white;

            _hotkeyBadgeText = XingShuUIFactory.CreateText(badgeGo, "Text", XingShuMobileUIHelper.IsMobile ? "触控" : $"[{_lastObservedHotkey}]", 12, XingShuUITheme.CyanAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform btRt = _hotkeyBadgeText.rectTransform;
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.pivot = new Vector2(0.5f, 0.5f);
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;

            // ==========================================
            // 2. 启用开关卡片 (Enable Row Card: 60px, y: -148px)
            // ==========================================
            GameObject switchCard = XingShuUIFactory.CreateUIObject("EnableRowCard", _root);
            RectTransform switchRt = switchCard.GetComponent<RectTransform>();
            switchRt.anchorMin = new Vector2(0f, 1f);
            switchRt.anchorMax = new Vector2(1f, 1f);
            switchRt.pivot = new Vector2(0.5f, 1f);
            switchRt.anchoredPosition = new Vector2(0f, -148f);
            switchRt.sizeDelta = new Vector2(0f, 60f);

            Image switchCardBg = switchCard.AddComponent<Image>();
            switchCardBg.sprite = cardBaseSprite;
            switchCardBg.type = Image.Type.Sliced;
            switchCardBg.color = XingShuUIAssetService.IsFallbackSprite(cardBaseSprite) ? XingShuUITheme.Card : Color.white;

            // 启用开关 (Toggle)
            _enableToggle = XingShuUIFactory.CreateToggle(switchCard, "EnableToggle", "启用全局加速功能", true, delegate(bool val)
            {
                if (_suppressCallbacks) return;
                if (XingShuSpeedEngineCoordinator.CurrentEngine == XingShuSpeedEngineMode.ManagedXingShu)
                {
                    GameSpeedControllerGUI.SetEnabled(val);
                }
                else
                {
                    XingShuSpeedEngineCoordinator.SetNativeEnabled(val);
                }
                UpdateStatusText(val);
            });
            RectTransform toggleRt = _enableToggle.GetComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(0f, 0.5f);
            toggleRt.anchorMax = new Vector2(1f, 0.5f);
            toggleRt.pivot = new Vector2(0.5f, 0.5f);
            toggleRt.anchoredPosition = Vector2.zero;
            toggleRt.sizeDelta = new Vector2(-48f, 32f);

            Transform labelT = _enableToggle.transform.Find("Label");
            if (labelT != null)
            {
                _toggleLabelText = labelT.GetComponent<Text>();
                RectTransform lRt = labelT.GetComponent<RectTransform>();
                lRt.anchorMin = new Vector2(0f, 0f);
                lRt.anchorMax = new Vector2(0f, 1f);
                lRt.pivot = new Vector2(0f, 0.5f);
                lRt.offsetMin = new Vector2(0f, 0f);
                lRt.offsetMax = new Vector2(280f, 0f);
            }
            Transform bgT = _enableToggle.transform.Find("Background");
            if (bgT != null)
            {
                RectTransform bRt = bgT.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(1f, 0.5f);
                bRt.anchorMax = new Vector2(1f, 0.5f);
                bRt.pivot = new Vector2(1f, 0.5f);
                bRt.anchoredPosition = Vector2.zero;
                bRt.sizeDelta = new Vector2(64f, 32f);
            }

            // 自身本回合原速开关 (Toggle: 原生时间加速专属，轮到自身操作时临时恢复 1.0x，回合结束后自动恢复设定倍率)
            _smartPauseToggle = XingShuUIFactory.CreateToggle(switchCard, "SmartPauseToggle", "自身本回合原速", false, delegate(bool val)
            {
                if (_suppressCallbacks) return;
                XingShuSpeedEngineCoordinator.SetSmartPauseEnabled(val);
                UpdateStatusText(_lastObservedEnabled);
            });
            RectTransform spToggleRt = _smartPauseToggle.GetComponent<RectTransform>();
            spToggleRt.anchorMin = new Vector2(0.52f, 0.5f);
            spToggleRt.anchorMax = new Vector2(1f, 0.5f);
            spToggleRt.pivot = new Vector2(0.5f, 0.5f);
            spToggleRt.anchoredPosition = Vector2.zero;
            spToggleRt.sizeDelta = new Vector2(-24f, 32f);

            Transform spLabelT = _smartPauseToggle.transform.Find("Label");
            if (spLabelT != null)
            {
                _smartPauseLabelText = spLabelT.GetComponent<Text>();
                RectTransform lRt = spLabelT.GetComponent<RectTransform>();
                lRt.anchorMin = new Vector2(0f, 0f);
                lRt.anchorMax = new Vector2(0f, 1f);
                lRt.pivot = new Vector2(0f, 0.5f);
                lRt.offsetMin = new Vector2(0f, 0f);
                lRt.offsetMax = new Vector2(220f, 0f);
            }
            Transform spBgT = _smartPauseToggle.transform.Find("Background");
            if (spBgT != null)
            {
                RectTransform bRt = spBgT.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(1f, 0.5f);
                bRt.anchorMax = new Vector2(1f, 0.5f);
                bRt.pivot = new Vector2(1f, 0.5f);
                bRt.anchoredPosition = Vector2.zero;
                bRt.sizeDelta = new Vector2(64f, 32f);
            }

            // ==========================================
            // 3. 核心调速卡片 (Speed Control Card: 236px, y: -220px)
            // ==========================================
            GameObject speedCard = XingShuUIFactory.CreateUIObject("SpeedControlCard", _root);
            RectTransform speedCardRt = speedCard.GetComponent<RectTransform>();
            speedCardRt.anchorMin = new Vector2(0f, 1f);
            speedCardRt.anchorMax = new Vector2(1f, 1f);
            speedCardRt.pivot = new Vector2(0.5f, 1f);
            speedCardRt.anchoredPosition = new Vector2(0f, -220f);
            speedCardRt.sizeDelta = new Vector2(0f, 236f);

            Image speedCardBg = speedCard.AddComponent<Image>();
            speedCardBg.sprite = cardBaseSprite;
            speedCardBg.type = Image.Type.Sliced;
            speedCardBg.color = XingShuUIAssetService.IsFallbackSprite(cardBaseSprite) ? XingShuUITheme.Card : Color.white;

            // 标题: 倍率参数微调
            _speedCardTitle = XingShuUIFactory.CreateText(speedCard, "CardTitle", "倍率参数微调 (0.1x ~ 10.0x)", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform sctRt = _speedCardTitle.rectTransform;
            sctRt.anchorMin = new Vector2(0f, 1f);
            sctRt.anchorMax = new Vector2(0f, 1f);
            sctRt.pivot = new Vector2(0f, 1f);
            sctRt.anchoredPosition = new Vector2(24f, -20f);
            sctRt.sizeDelta = new Vector2(300f, 26f);

            // 大号黄色倍率数字
            _speedValueText = XingShuUIFactory.CreateText(speedCard, "SpeedValue", "1.00x", 32, XingShuUITheme.NumericAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            // The game's display font has taller line metrics than the system fallback.
            _speedValueText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform svtRt = _speedValueText.rectTransform;
            svtRt.anchorMin = new Vector2(1f, 1f);
            svtRt.anchorMax = new Vector2(1f, 1f);
            svtRt.pivot = new Vector2(1f, 1f);
            svtRt.anchoredPosition = new Vector2(-24f, -14f);
            svtRt.sizeDelta = new Vector2(160f, 38f);

            // 滑块：白色轨道、粉色进度与官方星形滑块。
            _speedSlider = XingShuUIFactory.CreateSlider(speedCard, "SpeedSlider", 0.1f, 10f, 1.0f, delegate(float val)
            {
                if (_suppressCallbacks) return;
                float clamped = Mathf.Round(val * 20f) / 20f;
                if (XingShuSpeedEngineCoordinator.CurrentEngine == XingShuSpeedEngineMode.ManagedXingShu)
                {
                    GameSpeedControllerGUI.SetTimeScale(clamped);
                }
                else
                {
                    clamped = Mathf.Clamp(clamped, XingShuConfigKeys.NativeSpeedValueMin, XingShuConfigKeys.NativeSpeedValueMax);
                    XingShuSpeedEngineCoordinator.SetNativeSpeed(clamped);
                }
                _speedValueText.text = $"{clamped:F2}x";
                UpdatePresetButtonVisuals(clamped);
            });
            RectTransform sliderRt = _speedSlider.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0f, 1f);
            sliderRt.anchorMax = new Vector2(1f, 1f);
            sliderRt.pivot = new Vector2(0.5f, 1f);
            sliderRt.anchoredPosition = new Vector2(0f, -76f);
            sliderRt.sizeDelta = new Vector2(-48f, 28f);

            // 预设按钮组
            CreatePresetButtons(speedCard, 1.0f);

            // ==========================================
            // 4. 底部说明贴士卡片 (Tip Card: 42px, y: -468px)
            // ==========================================
            GameObject tipCard = XingShuUIFactory.CreateUIObject("TipCard", _root);
            RectTransform tipRt = tipCard.GetComponent<RectTransform>();
            tipRt.anchorMin = new Vector2(0f, 1f);
            tipRt.anchorMax = new Vector2(1f, 1f);
            tipRt.pivot = new Vector2(0.5f, 1f);
            tipRt.anchoredPosition = new Vector2(0f, -468f);
            tipRt.sizeDelta = new Vector2(0f, 42f);

            Image tipBg = tipCard.AddComponent<Image>();
            tipBg.sprite = cardBaseSprite;
            tipBg.type = Image.Type.Sliced;
            tipBg.color = XingShuUIAssetService.IsFallbackSprite(cardBaseSprite) ? XingShuUITheme.Card : Color.white;

            _tipText = XingShuUIFactory.CreateText(tipCard, "TipText", XingShuMobileUIHelper.IsMobile ? "点击开关启停加速 · 拖动滑条调倍率 · 思考时间按真实时间计算" : "[F7] 呼出控制面板  ·  [左Ctrl] 快速开关加速", 13, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform tipTxtRt = _tipText.rectTransform;
            tipTxtRt.anchorMin = Vector2.zero;
            tipTxtRt.anchorMax = Vector2.one;
            tipTxtRt.offsetMin = new Vector2(20f, 0f);
            tipTxtRt.offsetMax = new Vector2(-20f, 0f);

            // 初始化视图状态
            _lastObservedEngine = XingShuSpeedEngineCoordinator.CurrentEngine;
            RefreshEngineModeVisuals(_lastObservedEngine, force: true);
        }

        private void CreatePresetButtons(GameObject parent, float currentSpeed)
        {
            GameObject container = XingShuUIFactory.CreateUIObject("PresetContainer", parent);
            RectTransform containerRt = container.GetComponent<RectTransform>();
            containerRt.anchorMin = new Vector2(0f, 1f);
            containerRt.anchorMax = new Vector2(1f, 1f);
            containerRt.pivot = new Vector2(0.5f, 1f);
            containerRt.anchoredPosition = new Vector2(0f, -154f);
            containerRt.sizeDelta = new Vector2(-48f, 42f);

            HorizontalLayoutGroup hlg = container.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = XingShuUIAssetService.GetSprite("button_selected.png");

            _presetButtons.Clear();
            _presetImages.Clear();
            _presetTexts.Clear();

            for (int i = 0; i < PRESET_VALUES.Length; i++)
            {
                float targetSpeed = PRESET_VALUES[i];
                string label = PRESET_LABELS[i];
                bool isMatching = Mathf.Abs(currentSpeed - targetSpeed) < 0.04f;

                Sprite activeSprite = isMatching ? btnSelSprite : btnSecSprite;
                Color activeColor = XingShuUIAssetService.IsFallbackSprite(activeSprite)
                    ? (isMatching ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                    : Color.white;

                Button btn = XingShuUIFactory.CreateButton(container, "Btn_Preset_" + targetSpeed, label,
                    activeSprite,
                    activeColor,
                    isMatching ? new Color(0.08f, 0.10f, 0.06f, 1f) : XingShuUITheme.SecondaryText,
                    delegate
                    {
                        PresetClicked(targetSpeed);
                    }, 14);

                Image btnImg = btn.GetComponent<Image>();
                Text btnTxt = btn.GetComponentInChildren<Text>();
                if (btnTxt != null)
                {
                    btnTxt.alignment = TextAnchor.MiddleCenter;
                    btnTxt.fontStyle = FontStyle.Bold;
                    RectTransform pbtRt = btnTxt.rectTransform;
                    pbtRt.anchorMin = Vector2.zero;
                    pbtRt.anchorMax = Vector2.one;
                    pbtRt.pivot = new Vector2(0.5f, 0.5f);
                    pbtRt.offsetMin = Vector2.zero;
                    pbtRt.offsetMax = Vector2.zero;
                }

                _presetButtons.Add(btn);
                _presetImages.Add(btnImg);
                _presetTexts.Add(btnTxt);
            }
        }

        private void PresetClicked(float targetSpeed)
        {
            if (XingShuSpeedEngineCoordinator.CurrentEngine == XingShuSpeedEngineMode.ManagedXingShu)
            {
                GameSpeedControllerGUI.SetTimeScale(targetSpeed);
            }
            else
            {
                float clamped = Mathf.Clamp(targetSpeed, XingShuConfigKeys.NativeSpeedValueMin, XingShuConfigKeys.NativeSpeedValueMax);
                XingShuSpeedEngineCoordinator.SetNativeSpeed(clamped);
            }

            _suppressCallbacks = true;
            try
            {
                _speedSlider.value = targetSpeed;
            }
            finally
            {
                _suppressCallbacks = false;
            }
            _speedValueText.text = $"{targetSpeed:F2}x";
            UpdatePresetButtonVisuals(targetSpeed);
        }

        private void SwitchEngineRequested(XingShuSpeedEngineMode mode)
        {
            XingShuSpeedEngineCoordinator.SwitchEngine(mode);
            RefreshEngineModeVisuals(XingShuSpeedEngineCoordinator.CurrentEngine, force: true);
        }

        private void RefreshEngineModeVisuals(XingShuSpeedEngineMode mode, bool force = false)
        {
            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = XingShuUIAssetService.GetSprite("button_selected.png");

            bool isManaged = (mode == XingShuSpeedEngineMode.ManagedXingShu);

            // 1. 更新顶部 Selector 按钮外观
            if (_imgManagedEngine != null)
            {
                _imgManagedEngine.sprite = isManaged ? btnSelSprite : btnSecSprite;
                _imgManagedEngine.color = XingShuUIAssetService.IsFallbackSprite(_imgManagedEngine.sprite)
                    ? (isManaged ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                    : Color.white;
            }
            if (_txtManagedEngine != null)
            {
                _txtManagedEngine.color = isManaged ? new Color(0.08f, 0.10f, 0.06f, 1f) : XingShuUITheme.SecondaryText;
                _txtManagedEngine.fontStyle = isManaged ? FontStyle.Bold : FontStyle.Normal;
            }

            if (_imgNativeEngine != null)
            {
                _imgNativeEngine.sprite = !isManaged ? btnSelSprite : btnSecSprite;
                _imgNativeEngine.color = XingShuUIAssetService.IsFallbackSprite(_imgNativeEngine.sprite)
                    ? (!isManaged ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                    : Color.white;
            }
            if (_txtNativeEngine != null)
            {
                _txtNativeEngine.color = !isManaged ? new Color(0.08f, 0.10f, 0.06f, 1f) : XingShuUITheme.SecondaryText;
                _txtNativeEngine.fontStyle = !isManaged ? FontStyle.Bold : FontStyle.Normal;
            }

            // 2. 更新标题与说明文案
            if (_titleText != null)
            {
                _titleText.text = isManaged ? "游戏全局变速 (托管层)" : "原生时间加速 (系统底层)";
            }
            if (_subtitleText != null)
            {
                if (isManaged)
                {
                    _subtitleText.text = "托管层变速调整 Unity 引擎时钟与动画，不改变服务器计时";
                }
                else
                {
                    bool nativeAvail = XingShuSpeedEngineCoordinator.IsNativeAvailable;
                    _subtitleText.text = nativeAvail
                        ? "原生时间加速拦截底层 QPC/GTC 高精度时钟，物理引擎与动画平滑推进"
                        : XingShuNativeSpeedAdapter.GetStatusDescription();
                }
            }

            // 3. 动态调整滑块范围与状态
            _suppressCallbacks = true;
            try
            {
                if (isManaged)
                {
                    _speedSlider.minValue = 0.1f;
                    _speedSlider.maxValue = 10.0f;
                    float curSpeed = GameSpeedControllerGUI.CurrentTimeScale;
                    _speedSlider.value = curSpeed;
                    _speedValueText.text = $"{curSpeed:F2}x";
                    if (_speedCardTitle != null)
                    {
                        _speedCardTitle.text = "倍率参数微调 (0.1x ~ 10.0x)";
                    }

                    bool curEnabled = GameSpeedControllerGUI.IsSpeedEnabled;
                    _enableToggle.isOn = curEnabled;
                    _enableToggle.interactable = true;
                    if (_toggleLabelText != null)
                    {
                        _toggleLabelText.text = "启用游戏内托管加速功能";
                    }

                    if (_smartPauseToggle != null)
                    {
                        _smartPauseToggle.gameObject.SetActive(false);
                    }
                    RectTransform toggleRt = _enableToggle.GetComponent<RectTransform>();
                    if (!XingShuMobileUIHelper.IsMobile)
                    {
                        toggleRt.anchorMin = new Vector2(0f, 0.5f);
                        toggleRt.anchorMax = new Vector2(1f, 0.5f);
                        toggleRt.sizeDelta = new Vector2(-48f, 32f);
                    }

                    UpdateStatusText(curEnabled);
                    UpdatePresetButtonVisuals(curSpeed);
                }
                else
                {
                    _speedSlider.minValue = XingShuConfigKeys.NativeSpeedValueMin;
                    _speedSlider.maxValue = XingShuConfigKeys.NativeSpeedValueMax;
                    float nativeSpeed = XingShuConfigService.GetFloat(
                        XingShuConfigKeys.NativeSpeedValue,
                        XingShuConfigKeys.NativeSpeedValueDefault,
                        XingShuConfigKeys.NativeSpeedValueMin,
                        XingShuConfigKeys.NativeSpeedValueMax);
                    _speedSlider.value = nativeSpeed;
                    _speedValueText.text = $"{nativeSpeed:F2}x";
                    if (_speedCardTitle != null)
                    {
                        _speedCardTitle.text = "倍率参数微调 (0.25x ~ 10.0x)";
                    }

                    bool nativeAvail = XingShuSpeedEngineCoordinator.IsNativeAvailable;
                    bool nativeEnabled = XingShuConfigService.GetBool(
                        XingShuConfigKeys.NativeSpeedEnabled,
                        XingShuConfigKeys.NativeSpeedEnabledDefault);

                    _enableToggle.isOn = nativeEnabled;
                    _enableToggle.interactable = nativeAvail;
                    if (_toggleLabelText != null)
                    {
                        _toggleLabelText.text = nativeAvail ? "启用原生时间加速" : "启用原生时间加速 (组件未就绪)";
                    }

                    if (_smartPauseToggle != null)
                    {
                        _smartPauseToggle.gameObject.SetActive(true);
                        _smartPauseToggle.isOn = XingShuSpeedEngineCoordinator.SmartPauseEnabled;
                        _smartPauseToggle.interactable = nativeAvail;
                    }
                    RectTransform toggleRt = _enableToggle.GetComponent<RectTransform>();
                    toggleRt.anchorMin = new Vector2(0f, 0.5f);
                    toggleRt.anchorMax = new Vector2(0.50f, 0.5f);
                    toggleRt.sizeDelta = new Vector2(-24f, 32f);

                    UpdateStatusText(nativeEnabled);
                    UpdatePresetButtonVisuals(nativeSpeed);
                }
            }
            finally
            {
                _suppressCallbacks = false;
            }
        }

        private void UpdatePresetButtonVisuals(float currentSpeed)
        {
            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = XingShuUIAssetService.GetSprite("button_selected.png");

            for (int i = 0; i < _presetButtons.Count; i++)
            {
                if (i >= PRESET_VALUES.Length) break;
                bool isMatching = Mathf.Abs(currentSpeed - PRESET_VALUES[i]) < 0.04f;
                Sprite activeSprite = isMatching ? btnSelSprite : btnSecSprite;
                if (i < _presetImages.Count && _presetImages[i] != null)
                {
                    _presetImages[i].sprite = activeSprite;
                    _presetImages[i].color = XingShuUIAssetService.IsFallbackSprite(activeSprite)
                        ? (isMatching ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                        : Color.white;
                }
                if (i < _presetTexts.Count && _presetTexts[i] != null)
                {
                    _presetTexts[i].color = isMatching ? new Color(0.08f, 0.10f, 0.06f, 1f) : XingShuUITheme.SecondaryText;
                    _presetTexts[i].fontStyle = FontStyle.Bold;
                }
            }
        }

        private void UpdateStatusText(bool enabled)
        {
            if (_statusIndicatorText == null) return;

            if (XingShuSpeedEngineCoordinator.CurrentEngine == XingShuSpeedEngineMode.ManagedXingShu)
            {
                _statusIndicatorText.text = enabled ? "● 已启用" : "○ 已暂停";
                _statusIndicatorText.color = enabled ? XingShuUITheme.Positive : XingShuUITheme.SecondaryText;
            }
            else
            {
                bool nativeAvail = XingShuSpeedEngineCoordinator.IsNativeAvailable;
                if (!nativeAvail)
                {
                    switch (XingShuNativeSpeedAdapter.Status)
                    {
                        case XingShuNativeSpeedAdapter.NativeAdapterStatus.NotRunning:
                            _statusIndicatorText.text = "○ 原生组件未运行";
                            break;
                        case XingShuNativeSpeedAdapter.NativeAdapterStatus.WaitingForNative:
                            _statusIndicatorText.text = "◌ 正在等待原生组件";
                            break;
                        case XingShuNativeSpeedAdapter.NativeAdapterStatus.IncompatibleVersion:
                            _statusIndicatorText.text = "✕ IPC版本不兼容";
                            break;
                        case XingShuNativeSpeedAdapter.NativeAdapterStatus.Error:
                            _statusIndicatorText.text = "✕ 原生组件错误";
                            break;
                        default:
                            _statusIndicatorText.text = "○ 未就绪";
                            break;
                    }
                    _statusIndicatorText.color = XingShuUITheme.SecondaryText;
                }
                else
                {
                    if (!enabled)
                    {
                        _statusIndicatorText.text = "○ 原生已暂停";
                        _statusIndicatorText.color = XingShuUITheme.SecondaryText;
                    }
                    else if (XingShuSpeedEngineCoordinator.IsSuspended)
                    {
                        _statusIndicatorText.text = "◌ 自身本回合原速 · 1.00x";
                        _statusIndicatorText.color = XingShuUITheme.CyanAccent;
                    }
                    else
                    {
                        float eff = XingShuSpeedEngineCoordinator.NativeEffectiveSpeed;
                        _statusIndicatorText.text = $"● 原生已启用 · {eff:F2}x";
                        _statusIndicatorText.color = XingShuUITheme.Positive;
                    }
                }
            }
        }

        /// <summary>
        /// 每帧检测外部状态变更 (同步 UI)
        /// 无变更时严格保持 0 字符串拼接、0 GC 分配、0 UI 属性写入
        /// </summary>
        internal void UpdateSync()
        {
            XingShuSpeedEngineMode currentMode = XingShuSpeedEngineCoordinator.CurrentEngine;
            if (currentMode != _lastObservedEngine)
            {
                _lastObservedEngine = currentMode;
                RefreshEngineModeVisuals(currentMode);
            }

            if (currentMode == XingShuSpeedEngineMode.ManagedXingShu)
            {
                float curSpeed = GameSpeedControllerGUI.CurrentTimeScale;
                if (Mathf.Abs(curSpeed - _lastObservedSpeed) > 0.01f)
                {
                    _lastObservedSpeed = curSpeed;
                    if (_speedSlider != null && Mathf.Abs(_speedSlider.value - curSpeed) > 0.01f)
                    {
                        _suppressCallbacks = true;
                        try
                        {
                            _speedSlider.value = curSpeed;
                        }
                        finally
                        {
                            _suppressCallbacks = false;
                        }
                    }
                    if (_speedValueText != null)
                    {
                        _speedValueText.text = $"{curSpeed:F2}x";
                    }
                    UpdatePresetButtonVisuals(curSpeed);
                }

                bool curEnabled = GameSpeedControllerGUI.IsSpeedEnabled;
                if (curEnabled != _lastObservedEnabled)
                {
                    _lastObservedEnabled = curEnabled;
                    if (_enableToggle != null && _enableToggle.isOn != curEnabled)
                    {
                        _suppressCallbacks = true;
                        try
                        {
                            _enableToggle.isOn = curEnabled;
                        }
                        finally
                        {
                            _suppressCallbacks = false;
                        }
                    }
                    UpdateStatusText(curEnabled);
                }
            }
            else // NativeHook
            {
                float curSpeed = XingShuConfigService.GetFloat(
                    XingShuConfigKeys.NativeSpeedValue,
                    XingShuConfigKeys.NativeSpeedValueDefault,
                    XingShuConfigKeys.NativeSpeedValueMin,
                    XingShuConfigKeys.NativeSpeedValueMax);
                if (Mathf.Abs(curSpeed - _lastObservedSpeed) > 0.01f)
                {
                    _lastObservedSpeed = curSpeed;
                    if (_speedSlider != null && Mathf.Abs(_speedSlider.value - curSpeed) > 0.01f)
                    {
                        _suppressCallbacks = true;
                        try
                        {
                            _speedSlider.value = curSpeed;
                        }
                        finally
                        {
                            _suppressCallbacks = false;
                        }
                    }
                    if (_speedValueText != null)
                    {
                        _speedValueText.text = $"{curSpeed:F2}x";
                    }
                    UpdatePresetButtonVisuals(curSpeed);
                }

                bool curEnabled = XingShuConfigService.GetBool(
                    XingShuConfigKeys.NativeSpeedEnabled,
                    XingShuConfigKeys.NativeSpeedEnabledDefault);
                if (curEnabled != _lastObservedEnabled)
                {
                    _lastObservedEnabled = curEnabled;
                    if (_enableToggle != null && _enableToggle.isOn != curEnabled)
                    {
                        _suppressCallbacks = true;
                        try
                        {
                            _enableToggle.isOn = curEnabled;
                        }
                        finally
                        {
                            _suppressCallbacks = false;
                        }
                    }
                    UpdateStatusText(curEnabled);
                }

                bool curSuspended = XingShuSpeedEngineCoordinator.IsSuspended;
                AccelerationSuspendReason curReasons = XingShuSpeedEngineCoordinator.ActiveSuspendReasons;
                if (curSuspended != _lastObservedSuspended || curReasons != _lastObservedReasons)
                {
                    _lastObservedSuspended = curSuspended;
                    _lastObservedReasons = curReasons;
                    UpdateStatusText(curEnabled);
                }

                bool curSpEnabled = XingShuSpeedEngineCoordinator.SmartPauseEnabled;
                if (curSpEnabled != _lastObservedSmartPauseEnabled)
                {
                    _lastObservedSmartPauseEnabled = curSpEnabled;
                    if (_smartPauseToggle != null && _smartPauseToggle.isOn != curSpEnabled)
                    {
                        _suppressCallbacks = true;
                        try
                        {
                            _smartPauseToggle.isOn = curSpEnabled;
                        }
                        finally
                        {
                            _suppressCallbacks = false;
                        }
                    }
                }
            }

            KeyCode curHotkey = GameSpeedControllerGUI.CurrentHotkey;
            if (curHotkey != _lastObservedHotkey)
            {
                _lastObservedHotkey = curHotkey;
                if (_hotkeyBadgeText != null)
                {
                    _hotkeyBadgeText.text = XingShuMobileUIHelper.IsMobile ? "触控" : $"[{curHotkey}]";
                }
            }
        }
    }
}
