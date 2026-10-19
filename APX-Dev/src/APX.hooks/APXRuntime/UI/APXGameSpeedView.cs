using System;
using System.Collections.Generic;
using APXRuntime.Core;
using APXRuntime.Features;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心 - 游戏全局变速 Modern UI 页面 (Phase 2D-2B2 双引擎整合版)
    /// 支持 [游戏内加速 (Managed APX)] 与 [原生时间加速 (Native Hook)] 两个互斥引擎无缝切换
    /// 遵循 R3.2 严整的垂直韵律，使用 R3.2 生产级原子资产，消除反馈循环与冗余分配
    /// </summary>
    internal sealed class APXGameSpeedView
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

        private APXSpeedEngineMode _lastObservedEngine = APXSpeedEngineMode.ManagedAPX;
        private float _lastObservedSpeed = -1f;
        private bool _lastObservedEnabled = false;
        private KeyCode _lastObservedHotkey = KeyCode.None;
        private bool _lastObservedSuspended = false;
        private AccelerationSuspendReason _lastObservedReasons = AccelerationSuspendReason.None;
        private bool _lastObservedSmartPauseEnabled = false;
        private bool _suppressCallbacks = false;

        internal GameObject Root => _root;

        internal APXGameSpeedView(Transform parent)
        {
            _root = APXUIFactory.CreateUIObject("Page_GameSpeed", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildUI();
        }

        private void BuildUI()
        {
            Sprite cardBaseSprite = APXUIAssetService.GetSprite("card_base.png");
            Sprite cfhSprite = APXUIAssetService.GetSprite("card_feature_header.png");
            Sprite btnSecSprite = APXUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = APXUIAssetService.GetSprite("button_selected.png");

            // ==========================================
            // 0. 顶部引擎切换选择器 (Engine Selector Card: 48px, y: 0)
            // ==========================================
            GameObject selectorCard = APXUIFactory.CreateUIObject("EngineSelectorCard", _root);
            RectTransform selRt = selectorCard.GetComponent<RectTransform>();
            selRt.anchorMin = new Vector2(0f, 1f);
            selRt.anchorMax = new Vector2(1f, 1f);
            selRt.pivot = new Vector2(0.5f, 1f);
            selRt.anchoredPosition = new Vector2(0f, 0f);
            selRt.sizeDelta = new Vector2(0f, 48f);

            Image selBg = selectorCard.AddComponent<Image>();
            selBg.sprite = cardBaseSprite;
            selBg.type = Image.Type.Sliced;
            selBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // "加速引擎:" 标签
            Text engineLabel = APXUIFactory.CreateText(selectorCard, "EngineLabel", "加速引擎:", 14, APXUITheme.SecondaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform elRt = engineLabel.rectTransform;
            elRt.anchorMin = new Vector2(0f, 0.5f);
            elRt.anchorMax = new Vector2(0f, 0.5f);
            elRt.pivot = new Vector2(0f, 0.5f);
            elRt.anchoredPosition = new Vector2(20f, 0f);
            elRt.sizeDelta = new Vector2(80f, 26f);

            // [ 游戏内加速 ] 按钮 (Managed APX)
            _btnManagedEngine = APXUIFactory.CreateButton(selectorCard, "Btn_ManagedEngine", "游戏内加速",
                btnSelSprite, Color.white, new Color(0.08f, 0.10f, 0.06f, 1f),
                delegate { SwitchEngineRequested(APXSpeedEngineMode.ManagedAPX); }, 13);
            RectTransform bmeRt = _btnManagedEngine.GetComponent<RectTransform>();
            bmeRt.anchorMin = new Vector2(0f, 0.5f);
            bmeRt.anchorMax = new Vector2(0f, 0.5f);
            bmeRt.pivot = new Vector2(0f, 0.5f);
            bmeRt.anchoredPosition = new Vector2(105f, 0f);
            bmeRt.sizeDelta = new Vector2(130f, 32f);
            _imgManagedEngine = _btnManagedEngine.GetComponent<Image>();
            _txtManagedEngine = _btnManagedEngine.GetComponentInChildren<Text>();

            // [ 原生时间加速 ] 按钮 (Native Hook)
            _btnNativeEngine = APXUIFactory.CreateButton(selectorCard, "Btn_NativeEngine", "原生时间加速",
                btnSecSprite, Color.white, APXUITheme.SecondaryText,
                delegate { SwitchEngineRequested(APXSpeedEngineMode.NativeHook); }, 13);
            RectTransform bneRt = _btnNativeEngine.GetComponent<RectTransform>();
            bneRt.anchorMin = new Vector2(0f, 0.5f);
            bneRt.anchorMax = new Vector2(0f, 0.5f);
            bneRt.pivot = new Vector2(0f, 0.5f);
            bneRt.anchoredPosition = new Vector2(245f, 0f);
            bneRt.sizeDelta = new Vector2(130f, 32f);
            _imgNativeEngine = _btnNativeEngine.GetComponent<Image>();
            _txtNativeEngine = _btnNativeEngine.GetComponentInChildren<Text>();

            // ==========================================
            // 1. 特色功能卡片 (Feature Header Card: 76px, y: -60px)
            // ==========================================
            GameObject headerGo = APXUIFactory.CreateUIObject("FeatureHeaderCard", _root);
            RectTransform headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -60f);
            headerRt.sizeDelta = new Vector2(0f, 76f);

            Image headerBg = headerGo.AddComponent<Image>();
            headerBg.sprite = cfhSprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = APXUIAssetService.IsFallbackSprite(cfhSprite) ? APXUITheme.CardFeatureBase : Color.white;

            // 标题: 游戏全局变速
            _titleText = APXUIFactory.CreateText(headerGo, "Title", "游戏全局变速", 20, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = _titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 10f);
            titleRt.sizeDelta = new Vector2(300f, 26f);

            // 说明文案
            _subtitleText = APXUIFactory.CreateText(headerGo, "Subtitle", "调整游戏运行速度，可通过快捷键快速打开或关闭变速设置", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform subRt = _subtitleText.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(24f, -14f);
            subRt.sizeDelta = new Vector2(480f, 22f);

            // 状态指示文本 (● 已启用 / ○ 已暂停)
            _statusIndicatorText = APXUIFactory.CreateText(headerGo, "StatusIndicator", "● 已就绪", 14, APXUITheme.Positive, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform statusRt = _statusIndicatorText.rectTransform;
            statusRt.anchorMin = new Vector2(1f, 0.5f);
            statusRt.anchorMax = new Vector2(1f, 0.5f);
            statusRt.pivot = new Vector2(1f, 0.5f);
            statusRt.anchoredPosition = new Vector2(-88f, 0f);
            statusRt.sizeDelta = new Vector2(320f, 26f);

            // 动态热键徽章 [F7]
            _lastObservedHotkey = GameSpeedControllerGUI.CurrentHotkey;
            GameObject badgeGo = APXUIFactory.CreateUIObject("HotkeyBadge", headerGo);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 0.5f);
            badgeRt.anchorMax = new Vector2(1f, 0.5f);
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.anchoredPosition = new Vector2(-24f, 0f);
            badgeRt.sizeDelta = new Vector2(52f, 26f);

            Image badgeImg = badgeGo.AddComponent<Image>();
            badgeImg.sprite = APXUIAssetService.GetSprite("hotkey_badge.png");
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = APXUIAssetService.IsFallbackSprite(badgeImg.sprite) ? new Color(0.15f, 0.18f, 0.24f, 0.85f) : Color.white;

            _hotkeyBadgeText = APXUIFactory.CreateText(badgeGo, "Text", $"[{_lastObservedHotkey}]", 12, APXUITheme.CyanAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform btRt = _hotkeyBadgeText.rectTransform;
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.pivot = new Vector2(0.5f, 0.5f);
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;

            // ==========================================
            // 2. 启用开关卡片 (Enable Row Card: 60px, y: -148px)
            // ==========================================
            GameObject switchCard = APXUIFactory.CreateUIObject("EnableRowCard", _root);
            RectTransform switchRt = switchCard.GetComponent<RectTransform>();
            switchRt.anchorMin = new Vector2(0f, 1f);
            switchRt.anchorMax = new Vector2(1f, 1f);
            switchRt.pivot = new Vector2(0.5f, 1f);
            switchRt.anchoredPosition = new Vector2(0f, -148f);
            switchRt.sizeDelta = new Vector2(0f, 60f);

            Image switchCardBg = switchCard.AddComponent<Image>();
            switchCardBg.sprite = cardBaseSprite;
            switchCardBg.type = Image.Type.Sliced;
            switchCardBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 启用开关 (Toggle)
            _enableToggle = APXUIFactory.CreateToggle(switchCard, "EnableToggle", "启用全局加速功能", true, delegate(bool val)
            {
                if (_suppressCallbacks) return;
                if (APXSpeedEngineCoordinator.CurrentEngine == APXSpeedEngineMode.ManagedAPX)
                {
                    GameSpeedControllerGUI.SetEnabled(val);
                }
                else
                {
                    APXSpeedEngineCoordinator.SetNativeEnabled(val);
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
            _smartPauseToggle = APXUIFactory.CreateToggle(switchCard, "SmartPauseToggle", "自身本回合原速", false, delegate(bool val)
            {
                if (_suppressCallbacks) return;
                APXSpeedEngineCoordinator.SetSmartPauseEnabled(val);
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
            GameObject speedCard = APXUIFactory.CreateUIObject("SpeedControlCard", _root);
            RectTransform speedCardRt = speedCard.GetComponent<RectTransform>();
            speedCardRt.anchorMin = new Vector2(0f, 1f);
            speedCardRt.anchorMax = new Vector2(1f, 1f);
            speedCardRt.pivot = new Vector2(0.5f, 1f);
            speedCardRt.anchoredPosition = new Vector2(0f, -220f);
            speedCardRt.sizeDelta = new Vector2(0f, 236f);

            Image speedCardBg = speedCard.AddComponent<Image>();
            speedCardBg.sprite = cardBaseSprite;
            speedCardBg.type = Image.Type.Sliced;
            speedCardBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 标题: 倍率参数微调
            _speedCardTitle = APXUIFactory.CreateText(speedCard, "CardTitle", "倍率参数微调 (0.1x ~ 10.0x)", 16, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform sctRt = _speedCardTitle.rectTransform;
            sctRt.anchorMin = new Vector2(0f, 1f);
            sctRt.anchorMax = new Vector2(0f, 1f);
            sctRt.pivot = new Vector2(0f, 1f);
            sctRt.anchoredPosition = new Vector2(24f, -20f);
            sctRt.sizeDelta = new Vector2(300f, 26f);

            // 大号黄色倍率数字
            _speedValueText = APXUIFactory.CreateText(speedCard, "SpeedValue", "1.00x", 32, APXUITheme.NumericAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform svtRt = _speedValueText.rectTransform;
            svtRt.anchorMin = new Vector2(1f, 1f);
            svtRt.anchorMax = new Vector2(1f, 1f);
            svtRt.pivot = new Vector2(1f, 1f);
            svtRt.anchoredPosition = new Vector2(-24f, -14f);
            svtRt.sizeDelta = new Vector2(160f, 38f);

            // 滑块 (Slider: 胶囊轨道 + 28px 真圆手柄)
            _speedSlider = APXUIFactory.CreateSlider(speedCard, "SpeedSlider", 0.1f, 10f, 1.0f, delegate(float val)
            {
                if (_suppressCallbacks) return;
                float clamped = Mathf.Round(val * 20f) / 20f;
                if (APXSpeedEngineCoordinator.CurrentEngine == APXSpeedEngineMode.ManagedAPX)
                {
                    GameSpeedControllerGUI.SetTimeScale(clamped);
                }
                else
                {
                    clamped = Mathf.Clamp(clamped, 0.25f, 5.0f);
                    APXSpeedEngineCoordinator.SetNativeSpeed(clamped);
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
            GameObject tipCard = APXUIFactory.CreateUIObject("TipCard", _root);
            RectTransform tipRt = tipCard.GetComponent<RectTransform>();
            tipRt.anchorMin = new Vector2(0f, 1f);
            tipRt.anchorMax = new Vector2(1f, 1f);
            tipRt.pivot = new Vector2(0.5f, 1f);
            tipRt.anchoredPosition = new Vector2(0f, -468f);
            tipRt.sizeDelta = new Vector2(0f, 42f);

            Image tipBg = tipCard.AddComponent<Image>();
            tipBg.sprite = cardBaseSprite;
            tipBg.type = Image.Type.Sliced;
            tipBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? new Color(0.12f, 0.15f, 0.20f, 0.50f) : new Color(1f, 1f, 1f, 0.65f);

            _tipText = APXUIFactory.CreateText(tipCard, "TipText", "[F7] 呼出控制面板  ·  [左Ctrl] 快速开关加速", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform tipTxtRt = _tipText.rectTransform;
            tipTxtRt.anchorMin = Vector2.zero;
            tipTxtRt.anchorMax = Vector2.one;
            tipTxtRt.offsetMin = new Vector2(20f, 0f);
            tipTxtRt.offsetMax = new Vector2(-20f, 0f);

            // 初始化视图状态
            _lastObservedEngine = APXSpeedEngineCoordinator.CurrentEngine;
            RefreshEngineModeVisuals(_lastObservedEngine, force: true);
        }

        private void CreatePresetButtons(GameObject parent, float currentSpeed)
        {
            GameObject container = APXUIFactory.CreateUIObject("PresetContainer", parent);
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

            Sprite btnSecSprite = APXUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = APXUIAssetService.GetSprite("button_selected.png");

            _presetButtons.Clear();
            _presetImages.Clear();
            _presetTexts.Clear();

            for (int i = 0; i < PRESET_VALUES.Length; i++)
            {
                float targetSpeed = PRESET_VALUES[i];
                string label = PRESET_LABELS[i];
                bool isMatching = Mathf.Abs(currentSpeed - targetSpeed) < 0.04f;

                Sprite activeSprite = isMatching ? btnSelSprite : btnSecSprite;
                Color activeColor = APXUIAssetService.IsFallbackSprite(activeSprite)
                    ? (isMatching ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                    : Color.white;

                Button btn = APXUIFactory.CreateButton(container, "Btn_Preset_" + targetSpeed, label,
                    activeSprite,
                    activeColor,
                    isMatching ? new Color(0.08f, 0.10f, 0.06f, 1f) : APXUITheme.SecondaryText,
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
            if (APXSpeedEngineCoordinator.CurrentEngine == APXSpeedEngineMode.ManagedAPX)
            {
                GameSpeedControllerGUI.SetTimeScale(targetSpeed);
            }
            else
            {
                float clamped = Mathf.Clamp(targetSpeed, 0.25f, 5.0f);
                APXSpeedEngineCoordinator.SetNativeSpeed(clamped);
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

        private void SwitchEngineRequested(APXSpeedEngineMode mode)
        {
            APXSpeedEngineCoordinator.SwitchEngine(mode);
            RefreshEngineModeVisuals(mode, force: true);
        }

        private void RefreshEngineModeVisuals(APXSpeedEngineMode mode, bool force = false)
        {
            Sprite btnSecSprite = APXUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = APXUIAssetService.GetSprite("button_selected.png");

            bool isManaged = (mode == APXSpeedEngineMode.ManagedAPX);

            // 1. 更新顶部 Selector 按钮外观
            if (_imgManagedEngine != null)
            {
                _imgManagedEngine.sprite = isManaged ? btnSelSprite : btnSecSprite;
                _imgManagedEngine.color = APXUIAssetService.IsFallbackSprite(_imgManagedEngine.sprite)
                    ? (isManaged ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                    : Color.white;
            }
            if (_txtManagedEngine != null)
            {
                _txtManagedEngine.color = isManaged ? new Color(0.08f, 0.10f, 0.06f, 1f) : APXUITheme.SecondaryText;
                _txtManagedEngine.fontStyle = isManaged ? FontStyle.Bold : FontStyle.Normal;
            }

            if (_imgNativeEngine != null)
            {
                _imgNativeEngine.sprite = !isManaged ? btnSelSprite : btnSecSprite;
                _imgNativeEngine.color = APXUIAssetService.IsFallbackSprite(_imgNativeEngine.sprite)
                    ? (!isManaged ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                    : Color.white;
            }
            if (_txtNativeEngine != null)
            {
                _txtNativeEngine.color = !isManaged ? new Color(0.08f, 0.10f, 0.06f, 1f) : APXUITheme.SecondaryText;
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
                    bool nativeAvail = APXSpeedEngineCoordinator.IsNativeAvailable;
                    _subtitleText.text = nativeAvail
                        ? "原生时间加速拦截底层 QPC/GTC 高精度时钟，物理引擎与动画平滑推进"
                        : APXNativeSpeedAdapter.GetStatusDescription();
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
                    toggleRt.anchorMin = new Vector2(0f, 0.5f);
                    toggleRt.anchorMax = new Vector2(1f, 0.5f);
                    toggleRt.sizeDelta = new Vector2(-48f, 32f);

                    UpdateStatusText(curEnabled);
                    UpdatePresetButtonVisuals(curSpeed);
                }
                else
                {
                    _speedSlider.minValue = 0.25f;
                    _speedSlider.maxValue = 5.0f;
                    float nativeSpeed = APXConfigService.GetFloat(
                        APXConfigKeys.NativeSpeedValue,
                        APXConfigKeys.NativeSpeedValueDefault,
                        APXConfigKeys.NativeSpeedValueMin,
                        APXConfigKeys.NativeSpeedValueMax);
                    _speedSlider.value = nativeSpeed;
                    _speedValueText.text = $"{nativeSpeed:F2}x";
                    if (_speedCardTitle != null)
                    {
                        _speedCardTitle.text = "倍率参数微调 (0.25x ~ 5.0x)";
                    }

                    bool nativeAvail = APXSpeedEngineCoordinator.IsNativeAvailable;
                    bool nativeEnabled = APXConfigService.GetBool(
                        APXConfigKeys.NativeSpeedEnabled,
                        APXConfigKeys.NativeSpeedEnabledDefault);

                    _enableToggle.isOn = nativeEnabled;
                    _enableToggle.interactable = nativeAvail;
                    if (_toggleLabelText != null)
                    {
                        _toggleLabelText.text = nativeAvail ? "启用原生时间加速" : "启用原生时间加速 (组件未就绪)";
                    }

                    if (_smartPauseToggle != null)
                    {
                        _smartPauseToggle.gameObject.SetActive(true);
                        _smartPauseToggle.isOn = APXSpeedEngineCoordinator.SmartPauseEnabled;
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
            Sprite btnSecSprite = APXUIAssetService.GetSprite("button_secondary.png");
            Sprite btnSelSprite = APXUIAssetService.GetSprite("button_selected.png");

            for (int i = 0; i < _presetButtons.Count; i++)
            {
                if (i >= PRESET_VALUES.Length) break;
                bool isMatching = Mathf.Abs(currentSpeed - PRESET_VALUES[i]) < 0.04f;
                Sprite activeSprite = isMatching ? btnSelSprite : btnSecSprite;
                if (i < _presetImages.Count && _presetImages[i] != null)
                {
                    _presetImages[i].sprite = activeSprite;
                    _presetImages[i].color = APXUIAssetService.IsFallbackSprite(activeSprite)
                        ? (isMatching ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.15f, 0.18f, 0.24f, 1f))
                        : Color.white;
                }
                if (i < _presetTexts.Count && _presetTexts[i] != null)
                {
                    _presetTexts[i].color = isMatching ? new Color(0.08f, 0.10f, 0.06f, 1f) : APXUITheme.SecondaryText;
                    _presetTexts[i].fontStyle = FontStyle.Bold;
                }
            }
        }

        private void UpdateStatusText(bool enabled)
        {
            if (_statusIndicatorText == null) return;

            if (APXSpeedEngineCoordinator.CurrentEngine == APXSpeedEngineMode.ManagedAPX)
            {
                _statusIndicatorText.text = enabled ? "● 已启用" : "○ 已暂停";
                _statusIndicatorText.color = enabled ? APXUITheme.Positive : APXUITheme.SecondaryText;
            }
            else
            {
                bool nativeAvail = APXSpeedEngineCoordinator.IsNativeAvailable;
                if (!nativeAvail)
                {
                    switch (APXNativeSpeedAdapter.Status)
                    {
                        case APXNativeSpeedAdapter.NativeAdapterStatus.NotRunning:
                            _statusIndicatorText.text = "○ 原生组件未运行";
                            break;
                        case APXNativeSpeedAdapter.NativeAdapterStatus.WaitingForNative:
                            _statusIndicatorText.text = "◌ 正在等待原生组件";
                            break;
                        case APXNativeSpeedAdapter.NativeAdapterStatus.IncompatibleVersion:
                            _statusIndicatorText.text = "✕ IPC版本不兼容";
                            break;
                        case APXNativeSpeedAdapter.NativeAdapterStatus.Error:
                            _statusIndicatorText.text = "✕ 原生组件错误";
                            break;
                        default:
                            _statusIndicatorText.text = "○ 未就绪";
                            break;
                    }
                    _statusIndicatorText.color = APXUITheme.SecondaryText;
                }
                else
                {
                    if (!enabled)
                    {
                        _statusIndicatorText.text = "○ 原生已暂停";
                        _statusIndicatorText.color = APXUITheme.SecondaryText;
                    }
                    else if (APXSpeedEngineCoordinator.IsSuspended)
                    {
                        _statusIndicatorText.text = "◌ 自身本回合原速 · 1.00x";
                        _statusIndicatorText.color = APXUITheme.CyanAccent;
                    }
                    else
                    {
                        float eff = APXSpeedEngineCoordinator.NativeEffectiveSpeed;
                        _statusIndicatorText.text = $"● 原生已启用 · {eff:F2}x";
                        _statusIndicatorText.color = APXUITheme.Positive;
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
            APXSpeedEngineMode currentMode = APXSpeedEngineCoordinator.CurrentEngine;
            if (currentMode != _lastObservedEngine)
            {
                _lastObservedEngine = currentMode;
                RefreshEngineModeVisuals(currentMode);
            }

            if (currentMode == APXSpeedEngineMode.ManagedAPX)
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
                float curSpeed = APXConfigService.GetFloat(
                    APXConfigKeys.NativeSpeedValue,
                    APXConfigKeys.NativeSpeedValueDefault,
                    APXConfigKeys.NativeSpeedValueMin,
                    APXConfigKeys.NativeSpeedValueMax);
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

                bool curEnabled = APXConfigService.GetBool(
                    APXConfigKeys.NativeSpeedEnabled,
                    APXConfigKeys.NativeSpeedEnabledDefault);
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

                bool curSuspended = APXSpeedEngineCoordinator.IsSuspended;
                AccelerationSuspendReason curReasons = APXSpeedEngineCoordinator.ActiveSuspendReasons;
                if (curSuspended != _lastObservedSuspended || curReasons != _lastObservedReasons)
                {
                    _lastObservedSuspended = curSuspended;
                    _lastObservedReasons = curReasons;
                    UpdateStatusText(curEnabled);
                }

                bool curSpEnabled = APXSpeedEngineCoordinator.SmartPauseEnabled;
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
                    _hotkeyBadgeText.text = $"[{curHotkey}]";
                }
            }
        }
    }
}
