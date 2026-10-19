using System;
using APXRuntime.Core;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心 - 高级设置 (Advanced Settings) Modern UI 页面 (Phase 2D-7A-R1)
    /// 纯 UGUI View 表现层组件：整合悬浮交互、数据挂件外观及全局显示适配。
    /// 严格遵循 12 项迁移范围契约，无多余未审计动作（无“恢复默认外观”、无“重置悬浮球位置”、无“一键重置所有配置”等）。
    /// 所有配置读写严格通过 APXConfigService 完成，零直接 PlayerPrefs 操作。
    /// </summary>
    internal sealed class APXAdvancedSettingsView
    {
        private readonly GameObject _root;

        // 平台适配键名选择器 (与 StatsWidgetView 逻辑完全对齐，Windows 桌面环境下以 PC 为权威真相源)
        private static string StatsBgKey => APXMobileUIHelper.IsMobile ? APXConfigKeys.StatsWidgetAndroidBackground : APXConfigKeys.StatsWidgetPcBackground;
        private static string StatsTextKey => APXMobileUIHelper.IsMobile ? APXConfigKeys.StatsWidgetAndroidText : APXConfigKeys.StatsWidgetPcText;
        private static string StatsScaleKey => APXMobileUIHelper.IsMobile ? APXConfigKeys.StatsWidgetAndroidScale : APXConfigKeys.StatsWidgetPcScale;
        private static string StatsLockKey => APXMobileUIHelper.IsMobile ? APXConfigKeys.StatsWidgetAndroidLock : APXConfigKeys.StatsWidgetPcLock;
        private static string StatsXKey => APXMobileUIHelper.IsMobile ? APXConfigKeys.StatsWidgetAndroidX : APXConfigKeys.StatsWidgetPcX;
        private static string StatsYKey => APXMobileUIHelper.IsMobile ? APXConfigKeys.StatsWidgetAndroidY : APXConfigKeys.StatsWidgetPcY;

        // Card A: 对局数据挂件设置 (Battle Stats Overlay)
        private Toggle _observeToggle;
        private Toggle _widgetLockToggle;
        private Slider _widgetBgSlider;
        private Text _widgetBgValueText;
        private Slider _widgetTextSlider;
        private Text _widgetTextValueText;
        private Slider _widgetScaleSlider;
        private Text _widgetScaleValueText;

        // Card B: 悬浮交互与辅助球 (Assist Ball & Menu)
        private Toggle _autoHideToggle;
        private Text _hotkeyDisplayText;
        private Slider _assistSizeSlider;
        private Text _assistSizeValueText;
        private Slider _assistAlphaSlider;
        private Text _assistAlphaValueText;
        private Slider _assistScaleSlider;
        private Text _assistScaleValueText;

        // Card C: 全局显示适配 (Global UI Scaling)
        private Slider _globalScaleSlider;
        private Text _globalScaleValueText;
        private ScrollRect _scrollRect;

        // Card D: 恢复默认操作 (Track D)
        private Button _resetDefaultsBtn;
        private Text _resetDefaultsBtnText;
        private float _resetConfirmUntil;

        // 内部更新防重入与有界轮询缓存
        private bool _suppressCallbacks;
        private float _nextSyncTime;
        private bool? _lastObservedObserve = null;
        private bool? _lastObservedLock = null;
        private float _lastObservedBg = -1f;
        private float _lastObservedTextAlpha = -1f;
        private float _lastObservedScale = -1f;
        private bool? _lastObservedAutoHide = null;
        private KeyCode? _lastObservedHotkey = null;
        private float _lastObservedSize = -1f;
        private float _lastObservedAlpha = -1f;
        private float _lastObservedDScale = -1f;
        private float _lastObservedGScale = -1f;

        internal GameObject Root => _root;

        internal APXAdvancedSettingsView(Transform parent)
        {
            _root = APXUIFactory.CreateUIObject("Page_AdvancedSettings", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildUI();
        }

        private void BuildUI()
        {
            _suppressCallbacks = true;
            try
            {
                _scrollRect = APXUIFactory.CreateVerticalScrollView(_root, "AdvancedSettingsScrollView", out GameObject contentArea);
                VerticalLayoutGroup vlg = contentArea.GetComponent<VerticalLayoutGroup>();
                if (vlg != null)
                {
                    vlg.spacing = 14f;
                    vlg.padding = new RectOffset(0, 10, 0, 24);
                }

                // 0. 特色功能卡片 (Feature Header Card: 76px)
                BuildHeaderCard(contentArea);

                // 1. 卡片 A: 对局数据挂件设置 (Card A: 356px)
                BuildStatsWidgetCard(contentArea);

                // 2. 卡片 B: 悬浮交互与辅助球设置 (Card B: 320px)
                BuildAssistBallCard(contentArea);

                // 3. 卡片 C: 全局显示适配 (Card C: 155px)
                BuildDisplayScaleCard(contentArea);

                // 4. 卡片 D: 恢复默认操作 (Card D: 76px)
                BuildResetDefaultsCard(contentArea);

                // 初始化控件数值
                SyncFromConfig();
            }
            finally
            {
                _suppressCallbacks = false;
            }
        }

        private GameObject CreateCardContainer(GameObject parent, string name, float height)
        {
            GameObject cardGo = APXUIFactory.CreateUIObject(name, parent);
            LayoutElement le = cardGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleHeight = 0f;

            Sprite cardSprite = APXUIAssetService.GetSprite("card_base.png");
            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = cardSprite;
            cardBg.type = Image.Type.Sliced;
            cardBg.color = APXUIAssetService.IsFallbackSprite(cardSprite) ? APXUITheme.Card : Color.white;

            return cardGo;
        }

        private void BuildHeaderCard(GameObject parent)
        {
            GameObject headerGo = APXUIFactory.CreateUIObject("FeatureHeaderCard", parent);
            LayoutElement le = headerGo.AddComponent<LayoutElement>();
            le.preferredHeight = 76f;
            le.minHeight = 76f;
            le.flexibleHeight = 0f;

            Sprite cfhSprite = APXUIAssetService.GetSprite("card_feature_header.png");
            Image headerBg = headerGo.AddComponent<Image>();
            headerBg.sprite = cfhSprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = APXUIAssetService.IsFallbackSprite(cfhSprite) ? APXUITheme.CardFeatureBase : Color.white;

            Text titleText = APXUIFactory.CreateText(headerGo, "Title", "高级设置", 20, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 12f);
            titleRt.sizeDelta = new Vector2(300f, 26f);

            Text descText = APXUIFactory.CreateText(headerGo, "Description", "对局统计挂件参数 · 悬浮交互与辅助球样式 · 全局渲染显示适配", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform descRt = descText.rectTransform;
            descRt.anchorMin = new Vector2(0f, 0.5f);
            descRt.anchorMax = new Vector2(0f, 0.5f);
            descRt.pivot = new Vector2(0f, 0.5f);
            descRt.anchoredPosition = new Vector2(24f, -14f);
            descRt.sizeDelta = new Vector2(500f, 22f);
        }

        private void BuildStatsWidgetCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "Card_StatsWidgetSettings", 356f);

            Text cardTitle = APXUIFactory.CreateText(cardGo, "CardTitle", "对局数据挂件设置", 16, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -14f);
            ctRt.sizeDelta = new Vector2(240f, 22f);

            Text subtitle = APXUIFactory.CreateText(cardGo, "Subtitle", "轻量对局统计悬浮挂件独立采集、透明度、缩放与停靠锁定", 12, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subtitle.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -38f);
            stRt.sizeDelta = new Vector2(460f, 18f);

            // Row 1: 开启数据采集 ToggleRow (左侧标签，右侧开关)
            bool initObserve = APXConfigService.GetBool(APXConfigKeys.MatchObserveEnabled, APXConfigKeys.MatchObserveEnabledDefault);
            _observeToggle = APXUIFactory.CreateToggleRow(cardGo, "ObserveToggle", "开启数据采集", initObserve, OnObserveToggleChanged);
            RectTransform otRt = _observeToggle.GetComponent<RectTransform>();
            otRt.anchoredPosition = new Vector2(0f, -64f);
            otRt.sizeDelta = new Vector2(0f, 32f);

            // Row 2: 锁定挂件位置 ToggleRow (左侧标签，右侧开关)
            bool initLock = APXConfigService.GetBool(StatsLockKey, APXConfigKeys.StatsWidgetLockDefault);
            _widgetLockToggle = APXUIFactory.CreateToggleRow(cardGo, "LockToggle", "锁定挂件位置", initLock, OnWidgetLockToggleChanged);
            RectTransform ltRt = _widgetLockToggle.GetComponent<RectTransform>();
            ltRt.anchoredPosition = new Vector2(0f, -100f);
            ltRt.sizeDelta = new Vector2(0f, 32f);

            // Row 3: 背景不透明度 Slider (标签居左，数值居右，全宽滑动条居下)
            Text bgLabel = APXUIFactory.CreateText(cardGo, "BgLabel", "背景不透明度", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform bglRt = bgLabel.rectTransform;
            bglRt.anchorMin = new Vector2(0f, 1f);
            bglRt.anchorMax = new Vector2(0f, 1f);
            bglRt.pivot = new Vector2(0f, 1f);
            bglRt.anchoredPosition = new Vector2(24f, -138f);
            bglRt.sizeDelta = new Vector2(160f, 20f);

            float initBg = APXConfigService.GetFloat(StatsBgKey, APXConfigKeys.StatsWidgetBackgroundDefault);
            _widgetBgValueText = APXUIFactory.CreateText(cardGo, "BgValue", $"{Mathf.RoundToInt(initBg * 100f)}%", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform bgvRt = _widgetBgValueText.rectTransform;
            bgvRt.anchorMin = new Vector2(1f, 1f);
            bgvRt.anchorMax = new Vector2(1f, 1f);
            bgvRt.pivot = new Vector2(1f, 1f);
            bgvRt.anchoredPosition = new Vector2(-24f, -138f);
            bgvRt.sizeDelta = new Vector2(80f, 20f);

            _widgetBgSlider = APXUIFactory.CreateSlider(cardGo, "BgSlider", 0.15f, 1.0f, initBg, OnWidgetBgSliderChanged);
            RectTransform bgsRt = _widgetBgSlider.GetComponent<RectTransform>();
            bgsRt.anchorMin = new Vector2(0f, 1f);
            bgsRt.anchorMax = new Vector2(1f, 1f);
            bgsRt.pivot = new Vector2(0.5f, 1f);
            bgsRt.anchoredPosition = new Vector2(0f, -160f);
            bgsRt.sizeDelta = new Vector2(-48f, 24f);

            // Row 4: 文字不透明度 Slider
            Text textLabel = APXUIFactory.CreateText(cardGo, "TextLabel", "文字不透明度", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform tlRt = textLabel.rectTransform;
            tlRt.anchorMin = new Vector2(0f, 1f);
            tlRt.anchorMax = new Vector2(0f, 1f);
            tlRt.pivot = new Vector2(0f, 1f);
            tlRt.anchoredPosition = new Vector2(24f, -190f);
            tlRt.sizeDelta = new Vector2(160f, 20f);

            float initText = APXConfigService.GetFloat(StatsTextKey, APXConfigKeys.StatsWidgetTextAlphaDefault);
            _widgetTextValueText = APXUIFactory.CreateText(cardGo, "TextValue", $"{Mathf.RoundToInt(initText * 100f)}%", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform tvRt = _widgetTextValueText.rectTransform;
            tvRt.anchorMin = new Vector2(1f, 1f);
            tvRt.anchorMax = new Vector2(1f, 1f);
            tvRt.pivot = new Vector2(1f, 1f);
            tvRt.anchoredPosition = new Vector2(-24f, -190f);
            tvRt.sizeDelta = new Vector2(80f, 20f);

            _widgetTextSlider = APXUIFactory.CreateSlider(cardGo, "TextSlider", 0.55f, 1.0f, initText, OnWidgetTextSliderChanged);
            RectTransform tsRt = _widgetTextSlider.GetComponent<RectTransform>();
            tsRt.anchorMin = new Vector2(0f, 1f);
            tsRt.anchorMax = new Vector2(1f, 1f);
            tsRt.pivot = new Vector2(0.5f, 1f);
            tsRt.anchoredPosition = new Vector2(0f, -212f);
            tsRt.sizeDelta = new Vector2(-48f, 24f);

            // Row 5: 挂件缩放比例 Slider
            Text scaleLabel = APXUIFactory.CreateText(cardGo, "ScaleLabel", "挂件缩放比例", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform sclRt = scaleLabel.rectTransform;
            sclRt.anchorMin = new Vector2(0f, 1f);
            sclRt.anchorMax = new Vector2(0f, 1f);
            sclRt.pivot = new Vector2(0f, 1f);
            sclRt.anchoredPosition = new Vector2(24f, -242f);
            sclRt.sizeDelta = new Vector2(160f, 20f);

            float initScale = APXConfigService.GetFloat(StatsScaleKey, APXConfigKeys.StatsWidgetScaleDefault);
            _widgetScaleValueText = APXUIFactory.CreateText(cardGo, "ScaleValue", $"{Mathf.RoundToInt(initScale * 100f)}%", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform scvRt = _widgetScaleValueText.rectTransform;
            scvRt.anchorMin = new Vector2(1f, 1f);
            scvRt.anchorMax = new Vector2(1f, 1f);
            scvRt.pivot = new Vector2(1f, 1f);
            scvRt.anchoredPosition = new Vector2(-24f, -242f);
            scvRt.sizeDelta = new Vector2(80f, 20f);

            _widgetScaleSlider = APXUIFactory.CreateSlider(cardGo, "ScaleSlider", 0.60f, 2.00f, initScale, OnWidgetScaleSliderChanged);
            RectTransform scsRt = _widgetScaleSlider.GetComponent<RectTransform>();
            scsRt.anchorMin = new Vector2(0f, 1f);
            scsRt.anchorMax = new Vector2(1f, 1f);
            scsRt.pivot = new Vector2(0.5f, 1f);
            scsRt.anchoredPosition = new Vector2(0f, -264f);
            scsRt.sizeDelta = new Vector2(-48f, 24f);

            // Row 6: 重置挂件位置 Button (右对齐)
            Sprite btnSprite = APXUIAssetService.GetSprite("button_secondary.png");
            Button resetPosBtn = APXUIFactory.CreateButton(cardGo, "ResetPosBtn", "重置挂件位置",
                btnSprite,
                APXUIAssetService.IsFallbackSprite(btnSprite) ? APXUITheme.ButtonSecondaryBase : Color.white,
                APXUITheme.PrimaryText,
                OnResetWidgetPositionClicked, 13);
            RectTransform rpbRt = resetPosBtn.GetComponent<RectTransform>();
            rpbRt.anchorMin = new Vector2(1f, 1f);
            rpbRt.anchorMax = new Vector2(1f, 1f);
            rpbRt.pivot = new Vector2(1f, 1f);
            rpbRt.anchoredPosition = new Vector2(-24f, -298f);
            rpbRt.sizeDelta = new Vector2(130f, 30f);

            Text noteText = APXUIFactory.CreateText(cardGo, "Note", "位置重置为屏幕右上默认停靠点 (X: 1.0, Y: 0.12)；缩放范围为 0.60x ~ 2.00x", 11, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform ntRt = noteText.rectTransform;
            ntRt.anchorMin = new Vector2(0f, 0f);
            ntRt.anchorMax = new Vector2(1f, 0f);
            ntRt.pivot = new Vector2(0f, 0f);
            ntRt.anchoredPosition = new Vector2(24f, 8f);
            ntRt.sizeDelta = new Vector2(-48f, 18f);
        }

        private void BuildAssistBallCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "Card_AssistBallSettings", 320f);

            Text cardTitle = APXUIFactory.CreateText(cardGo, "CardTitle", "悬浮交互与辅助球", 16, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -14f);
            ctRt.sizeDelta = new Vector2(240f, 22f);

            Text subtitle = APXUIFactory.CreateText(cardGo, "Subtitle", "全局悬浮快捷入口物理尺寸、不透明度、贴边隐藏与呼出热键", 12, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subtitle.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -38f);
            stRt.sizeDelta = new Vector2(460f, 18f);

            // Row 1: 贴边自动隐藏 ToggleRow (左侧标签，右侧开关)
            bool initAutoHide = APXConfigService.GetBool(APXConfigKeys.AssistAutoHide, APXConfigKeys.AssistAutoHideDefault);
            _autoHideToggle = APXUIFactory.CreateToggleRow(cardGo, "AutoHideToggle", "贴边自动隐藏", initAutoHide, OnAutoHideToggleChanged);
            RectTransform ahtRt = _autoHideToggle.GetComponent<RectTransform>();
            ahtRt.anchoredPosition = new Vector2(0f, -64f);
            ahtRt.sizeDelta = new Vector2(0f, 32f);

            // Row 2: 快捷呼出键显示行 (左侧标题，右侧键名显示)
            Text hkLabel = APXUIFactory.CreateText(cardGo, "HotkeyLabel", "悬浮球显示/隐藏快捷键", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform hklRt = hkLabel.rectTransform;
            hklRt.anchorMin = new Vector2(0f, 1f);
            hklRt.anchorMax = new Vector2(0f, 1f);
            hklRt.pivot = new Vector2(0f, 1f);
            hklRt.anchoredPosition = new Vector2(24f, -100f);
            hklRt.sizeDelta = new Vector2(200f, 20f);

            KeyCode initHotkey = APXConfigService.GetKeyCode(APXConfigKeys.AssistHotkey, APXConfigKeys.AssistHotkeyDefault);
            _hotkeyDisplayText = APXUIFactory.CreateText(cardGo, "HotkeyDisplay", $"{initHotkey}", 12, APXUITheme.CyanAccent, TextAnchor.MiddleRight);
            RectTransform hkRt = _hotkeyDisplayText.rectTransform;
            hkRt.anchorMin = new Vector2(1f, 1f);
            hkRt.anchorMax = new Vector2(1f, 1f);
            hkRt.pivot = new Vector2(1f, 1f);
            hkRt.anchoredPosition = new Vector2(-24f, -100f);
            hkRt.sizeDelta = new Vector2(280f, 20f);

            // Row 3: 悬浮球大小 Slider
            Text sizeLabel = APXUIFactory.CreateText(cardGo, "SizeLabel", "悬浮球大小", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform slRt = sizeLabel.rectTransform;
            slRt.anchorMin = new Vector2(0f, 1f);
            slRt.anchorMax = new Vector2(0f, 1f);
            slRt.pivot = new Vector2(0f, 1f);
            slRt.anchoredPosition = new Vector2(24f, -126f);
            slRt.sizeDelta = new Vector2(160f, 20f);

            float initSize = APXConfigService.GetFloat(APXConfigKeys.AssistSize, APXConfigKeys.AssistSizeDesktopDefault);
            _assistSizeValueText = APXUIFactory.CreateText(cardGo, "SizeValue", $"{Mathf.RoundToInt(initSize)} px", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform svRt = _assistSizeValueText.rectTransform;
            svRt.anchorMin = new Vector2(1f, 1f);
            svRt.anchorMax = new Vector2(1f, 1f);
            svRt.pivot = new Vector2(1f, 1f);
            svRt.anchoredPosition = new Vector2(-24f, -126f);
            svRt.sizeDelta = new Vector2(80f, 20f);

            _assistSizeSlider = APXUIFactory.CreateSlider(cardGo, "SizeSlider", 36f, 200f, initSize, OnAssistSizeSliderChanged);
            RectTransform ssRt = _assistSizeSlider.GetComponent<RectTransform>();
            ssRt.anchorMin = new Vector2(0f, 1f);
            ssRt.anchorMax = new Vector2(1f, 1f);
            ssRt.pivot = new Vector2(0.5f, 1f);
            ssRt.anchoredPosition = new Vector2(0f, -148f);
            ssRt.sizeDelta = new Vector2(-48f, 24f);

            // Row 4: 悬浮球透明度 Slider
            Text alphaLabel = APXUIFactory.CreateText(cardGo, "AlphaLabel", "悬浮球透明度", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform alRt = alphaLabel.rectTransform;
            alRt.anchorMin = new Vector2(0f, 1f);
            alRt.anchorMax = new Vector2(0f, 1f);
            alRt.pivot = new Vector2(0f, 1f);
            alRt.anchoredPosition = new Vector2(24f, -178f);
            alRt.sizeDelta = new Vector2(160f, 20f);

            float initAlpha = APXConfigService.GetFloat(APXConfigKeys.AssistAlpha, APXConfigKeys.AssistAlphaDefault);
            _assistAlphaValueText = APXUIFactory.CreateText(cardGo, "AlphaValue", $"{Mathf.RoundToInt(initAlpha * 100f)}%", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform avRt = _assistAlphaValueText.rectTransform;
            avRt.anchorMin = new Vector2(1f, 1f);
            avRt.anchorMax = new Vector2(1f, 1f);
            avRt.pivot = new Vector2(1f, 1f);
            avRt.anchoredPosition = new Vector2(-24f, -178f);
            avRt.sizeDelta = new Vector2(80f, 20f);

            _assistAlphaSlider = APXUIFactory.CreateSlider(cardGo, "AlphaSlider", 0.2f, 1.0f, initAlpha, OnAssistAlphaSliderChanged);
            RectTransform asRt = _assistAlphaSlider.GetComponent<RectTransform>();
            asRt.anchorMin = new Vector2(0f, 1f);
            asRt.anchorMax = new Vector2(1f, 1f);
            asRt.pivot = new Vector2(0.5f, 1f);
            asRt.anchoredPosition = new Vector2(0f, -200f);
            asRt.sizeDelta = new Vector2(-48f, 24f);

            // Row 5: 悬浮菜单缩放 Slider
            Text dScaleLabel = APXUIFactory.CreateText(cardGo, "DScaleLabel", "悬浮菜单缩放", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform dslRt = dScaleLabel.rectTransform;
            dslRt.anchorMin = new Vector2(0f, 1f);
            dslRt.anchorMax = new Vector2(0f, 1f);
            dslRt.pivot = new Vector2(0f, 1f);
            dslRt.anchoredPosition = new Vector2(24f, -230f);
            dslRt.sizeDelta = new Vector2(160f, 20f);

            float initDScale = APXConfigService.GetFloat(APXConfigKeys.AssistDesktopScale, APXConfigKeys.AssistDesktopScaleDefault);
            _assistScaleValueText = APXUIFactory.CreateText(cardGo, "DScaleValue", $"{initDScale:F2}x", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform dsvRt = _assistScaleValueText.rectTransform;
            dsvRt.anchorMin = new Vector2(1f, 1f);
            dsvRt.anchorMax = new Vector2(1f, 1f);
            dsvRt.pivot = new Vector2(1f, 1f);
            dsvRt.anchoredPosition = new Vector2(-24f, -230f);
            dsvRt.sizeDelta = new Vector2(80f, 20f);

            _assistScaleSlider = APXUIFactory.CreateSlider(cardGo, "DScaleSlider", 1.0f, 3.0f, initDScale, OnAssistScaleSliderChanged);
            RectTransform dssRt = _assistScaleSlider.GetComponent<RectTransform>();
            dssRt.anchorMin = new Vector2(0f, 1f);
            dssRt.anchorMax = new Vector2(1f, 1f);
            dssRt.pivot = new Vector2(0.5f, 1f);
            dssRt.anchoredPosition = new Vector2(0f, -252f);
            dssRt.sizeDelta = new Vector2(-48f, 24f);

            Text noteText = APXUIFactory.CreateText(cardGo, "Note", "热键目前维持默认配置读取与安全绑定；悬浮菜单缩放影响快捷菜单渲染尺寸", 11, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform ntRt = noteText.rectTransform;
            ntRt.anchorMin = new Vector2(0f, 0f);
            ntRt.anchorMax = new Vector2(1f, 0f);
            ntRt.pivot = new Vector2(0f, 0f);
            ntRt.anchoredPosition = new Vector2(24f, 8f);
            ntRt.sizeDelta = new Vector2(-48f, 18f);
        }

        private void BuildDisplayScaleCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "Card_DisplaySettings", 155f);

            Text cardTitle = APXUIFactory.CreateText(cardGo, "CardTitle", "显示适配", 16, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -14f);
            ctRt.sizeDelta = new Vector2(240f, 22f);

            Text subtitle = APXUIFactory.CreateText(cardGo, "Subtitle", "全局 UI 渲染与子系统基准缩放比率控制，调节即时生效并持久化", 12, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subtitle.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -38f);
            stRt.sizeDelta = new Vector2(460f, 18f);

            Text scaleLabel = APXUIFactory.CreateText(cardGo, "GlobalScaleLabel", "全局 UI 缩放倍率", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform gslRt = scaleLabel.rectTransform;
            gslRt.anchorMin = new Vector2(0f, 1f);
            gslRt.anchorMax = new Vector2(0f, 1f);
            gslRt.pivot = new Vector2(0f, 1f);
            gslRt.anchoredPosition = new Vector2(24f, -66f);
            gslRt.sizeDelta = new Vector2(160f, 20f);

            float minScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleMin : APXConfigKeys.FunctionCenterDesktopScaleMin;
            float maxScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleMax : APXConfigKeys.FunctionCenterDesktopScaleMax;
            float defScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleDefault : APXConfigKeys.FunctionCenterDesktopScaleDefault;

            float initGScale = APXConfigService.GetFloat(APXConfigKeys.MobileUserScale, defScale);
            if (!APXMobileUIHelper.IsMobile)
            {
                initGScale = APXMobileUIHelper.FiniteClamp(initGScale, minScale, maxScale, defScale);
            }
            _globalScaleValueText = APXUIFactory.CreateText(cardGo, "GlobalScaleValue", $"{initGScale:F2}x", 13, APXUITheme.CyanAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform gsvRt = _globalScaleValueText.rectTransform;
            gsvRt.anchorMin = new Vector2(1f, 1f);
            gsvRt.anchorMax = new Vector2(1f, 1f);
            gsvRt.pivot = new Vector2(1f, 1f);
            gsvRt.anchoredPosition = new Vector2(-24f, -66f);
            gsvRt.sizeDelta = new Vector2(80f, 20f);

            _globalScaleSlider = APXUIFactory.CreateSlider(cardGo, "GlobalScaleSlider", minScale, maxScale, initGScale, OnGlobalScaleSliderChanged);
            RectTransform gssRt = _globalScaleSlider.GetComponent<RectTransform>();
            gssRt.anchorMin = new Vector2(0f, 1f);
            gssRt.anchorMax = new Vector2(1f, 1f);
            gssRt.pivot = new Vector2(0.5f, 1f);
            gssRt.anchoredPosition = new Vector2(0f, -88f);
            gssRt.sizeDelta = new Vector2(-48f, 24f);

            string noteStr = APXMobileUIHelper.IsMobile
                ? "调整全局渲染基准比例；推荐保持在 1.00x 以获得最佳排版清晰度"
                : "仅调整 APX 功能中心显示比例\n桌面安全范围 1.00x ~ 1.25x\n不会缩放游戏原生 UI";
            Text noteText = APXUIFactory.CreateText(cardGo, "Note", noteStr, 11, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform ntRt = noteText.rectTransform;
            ntRt.anchorMin = new Vector2(0f, 0f);
            ntRt.anchorMax = new Vector2(1f, 0f);
            ntRt.pivot = new Vector2(0f, 0f);
            ntRt.anchoredPosition = new Vector2(24f, 6f);
            ntRt.sizeDelta = new Vector2(-48f, 34f);
        }

        private void BuildResetDefaultsCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "Card_ResetDefaults", 76f);

            Text cardTitle = APXUIFactory.CreateText(cardGo, "CardTitle", "恢复默认设置", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -14f);
            ctRt.sizeDelta = new Vector2(240f, 22f);

            Text subtitle = APXUIFactory.CreateText(cardGo, "Subtitle", "仅将本页各项设置恢复为官方初始默认值，不影响其他功能配置", 12, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subtitle.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -40f);
            stRt.sizeDelta = new Vector2(460f, 18f);

            Sprite btnSprite = APXUIAssetService.GetSprite("button_secondary.png");
            _resetDefaultsBtn = APXUIFactory.CreateButton(cardGo, "ResetDefaultsBtn", "恢复本页默认设置",
                btnSprite,
                APXUIAssetService.IsFallbackSprite(btnSprite) ? APXUITheme.ButtonSecondaryBase : Color.white,
                APXUITheme.PrimaryText,
                OnResetDefaultsClicked, 13);
            _resetDefaultsBtnText = _resetDefaultsBtn.GetComponentInChildren<Text>();
            RectTransform rdbRt = _resetDefaultsBtn.GetComponent<RectTransform>();
            rdbRt.anchorMin = new Vector2(1f, 0.5f);
            rdbRt.anchorMax = new Vector2(1f, 0.5f);
            rdbRt.pivot = new Vector2(1f, 0.5f);
            rdbRt.anchoredPosition = new Vector2(-24f, 0f);
            rdbRt.sizeDelta = new Vector2(170f, 32f);
        }

        private void SyncFromConfig()
        {
            _suppressCallbacks = true;
            try
            {
                // Card A
                bool obs = APXConfigService.GetBool(APXConfigKeys.MatchObserveEnabled, APXConfigKeys.MatchObserveEnabledDefault);
                if (!_lastObservedObserve.HasValue || _lastObservedObserve.Value != obs)
                {
                    _lastObservedObserve = obs;
                    if (_observeToggle != null) APXUIFactory.SetToggleRowStateWithoutNotify(_observeToggle, obs);
                }

                bool lockPos = APXConfigService.GetBool(StatsLockKey, APXConfigKeys.StatsWidgetLockDefault);
                if (!_lastObservedLock.HasValue || _lastObservedLock.Value != lockPos)
                {
                    _lastObservedLock = lockPos;
                    if (_widgetLockToggle != null) APXUIFactory.SetToggleRowStateWithoutNotify(_widgetLockToggle, lockPos);
                }

                float bg = APXConfigService.GetFloat(StatsBgKey, APXConfigKeys.StatsWidgetBackgroundDefault);
                if (!Mathf.Approximately(_lastObservedBg, bg))
                {
                    _lastObservedBg = bg;
                    if (_widgetBgSlider != null) _widgetBgSlider.SetValueWithoutNotify(bg);
                    if (_widgetBgValueText != null) _widgetBgValueText.text = $"{Mathf.RoundToInt(bg * 100f)}%";
                }

                float textAlpha = APXConfigService.GetFloat(StatsTextKey, APXConfigKeys.StatsWidgetTextAlphaDefault);
                if (!Mathf.Approximately(_lastObservedTextAlpha, textAlpha))
                {
                    _lastObservedTextAlpha = textAlpha;
                    if (_widgetTextSlider != null) _widgetTextSlider.SetValueWithoutNotify(textAlpha);
                    if (_widgetTextValueText != null) _widgetTextValueText.text = $"{Mathf.RoundToInt(textAlpha * 100f)}%";
                }

                float scale = APXConfigService.GetFloat(StatsScaleKey, APXConfigKeys.StatsWidgetScaleDefault);
                if (!Mathf.Approximately(_lastObservedScale, scale))
                {
                    _lastObservedScale = scale;
                    if (_widgetScaleSlider != null) _widgetScaleSlider.SetValueWithoutNotify(scale);
                    if (_widgetScaleValueText != null) _widgetScaleValueText.text = $"{Mathf.RoundToInt(scale * 100f)}%";
                }

                // Card B
                bool autoHide = APXConfigService.GetBool(APXConfigKeys.AssistAutoHide, APXConfigKeys.AssistAutoHideDefault);
                if (!_lastObservedAutoHide.HasValue || _lastObservedAutoHide.Value != autoHide)
                {
                    _lastObservedAutoHide = autoHide;
                    if (_autoHideToggle != null) APXUIFactory.SetToggleRowStateWithoutNotify(_autoHideToggle, autoHide);
                }

                KeyCode hotkey = APXConfigService.GetKeyCode(APXConfigKeys.AssistHotkey, APXConfigKeys.AssistHotkeyDefault);
                if (!_lastObservedHotkey.HasValue || _lastObservedHotkey.Value != hotkey)
                {
                    _lastObservedHotkey = hotkey;
                    if (_hotkeyDisplayText != null) _hotkeyDisplayText.text = $"{hotkey}";
                }

                float size = APXConfigService.GetFloat(APXConfigKeys.AssistSize, APXConfigKeys.AssistSizeDesktopDefault);
                if (!Mathf.Approximately(_lastObservedSize, size))
                {
                    _lastObservedSize = size;
                    if (_assistSizeSlider != null) _assistSizeSlider.SetValueWithoutNotify(size);
                    if (_assistSizeValueText != null) _assistSizeValueText.text = $"{Mathf.RoundToInt(size)} px";
                }

                float alpha = APXConfigService.GetFloat(APXConfigKeys.AssistAlpha, APXConfigKeys.AssistAlphaDefault);
                if (!Mathf.Approximately(_lastObservedAlpha, alpha))
                {
                    _lastObservedAlpha = alpha;
                    if (_assistAlphaSlider != null) _assistAlphaSlider.SetValueWithoutNotify(alpha);
                    if (_assistAlphaValueText != null) _assistAlphaValueText.text = $"{Mathf.RoundToInt(alpha * 100f)}%";
                }

                float dscale = APXConfigService.GetFloat(APXConfigKeys.AssistDesktopScale, APXConfigKeys.AssistDesktopScaleDefault);
                if (!Mathf.Approximately(_lastObservedDScale, dscale))
                {
                    _lastObservedDScale = dscale;
                    if (_assistScaleSlider != null) _assistScaleSlider.SetValueWithoutNotify(dscale);
                    if (_assistScaleValueText != null) _assistScaleValueText.text = $"{dscale:F2}x";
                }

                // Card C
                float minScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleMin : APXConfigKeys.FunctionCenterDesktopScaleMin;
                float maxScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleMax : APXConfigKeys.FunctionCenterDesktopScaleMax;
                float defScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleDefault : APXConfigKeys.FunctionCenterDesktopScaleDefault;

                float gscale = APXConfigService.GetFloat(APXConfigKeys.MobileUserScale, defScale);
                if (!APXMobileUIHelper.IsMobile)
                {
                    gscale = APXMobileUIHelper.FiniteClamp(gscale, minScale, maxScale, defScale);
                }
                if (!Mathf.Approximately(_lastObservedGScale, gscale))
                {
                    _lastObservedGScale = gscale;
                    if (_globalScaleSlider != null) _globalScaleSlider.SetValueWithoutNotify(gscale);
                    if (_globalScaleValueText != null) _globalScaleValueText.text = $"{gscale:F2}x";
                }
            }
            finally
            {
                _suppressCallbacks = false;
            }
        }

        // ==========================================
        // Card A 回调
        // ==========================================
        private void OnObserveToggleChanged(bool val)
        {
            if (_suppressCallbacks) return;
            // 单一真相源归属：BattleStatsOverlay 存在时由其自身 SetObserving 处理重置与持久化；不存在时走 ConfigService 回退
            if (BattleStatsOverlay.Instance != null)
            {
                BattleStatsOverlay.IsObserving = val;
            }
            else
            {
                APXConfigService.SetBool(APXConfigKeys.MatchObserveEnabled, val);
                APXConfigService.Save();
            }
        }

        private void OnWidgetLockToggleChanged(bool val)
        {
            if (_suppressCallbacks) return;
            APXConfigService.SetBool(StatsLockKey, val);
            APXConfigService.Save();
            BattleStatsOverlay.RefreshWidgetPreferences();
        }

        private void OnWidgetBgSliderChanged(float val)
        {
            if (_widgetBgValueText != null) _widgetBgValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(StatsBgKey, val);
            APXConfigService.Save();
            BattleStatsOverlay.RefreshWidgetPreferences();
        }

        private void OnWidgetTextSliderChanged(float val)
        {
            if (_widgetTextValueText != null) _widgetTextValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(StatsTextKey, val);
            APXConfigService.Save();
            BattleStatsOverlay.RefreshWidgetPreferences();
        }

        private void OnWidgetScaleSliderChanged(float val)
        {
            if (_widgetScaleValueText != null) _widgetScaleValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(StatsScaleKey, val);
            APXConfigService.Save();
            BattleStatsOverlay.RefreshWidgetPreferences();
        }

        private void OnResetWidgetPositionClicked()
        {
            // 使用单一真相源预设常量 (StatsWidgetXDefault: 1.0f, StatsWidgetYDefault: 0.12f)
            APXConfigService.SetFloat(StatsXKey, APXConfigKeys.StatsWidgetXDefault);
            APXConfigService.SetFloat(StatsYKey, APXConfigKeys.StatsWidgetYDefault);
            APXConfigService.Save();
            BattleStatsOverlay.RefreshWidgetPreferences();
        }

        // ==========================================
        // Card B 回调
        // ==========================================
        private void OnAutoHideToggleChanged(bool val)
        {
            if (_suppressCallbacks) return;
            APXConfigService.SetBool(APXConfigKeys.AssistAutoHide, val);
            APXConfigService.Save();
            if (AssistBallGUI.Instance != null)
            {
                AssistBallGUI.Instance.SetAutoHide(val);
            }
            else
            {
                AssistBallGUI.RefreshFromConfig();
            }
        }

        private void OnAssistSizeSliderChanged(float val)
        {
            if (_assistSizeValueText != null) _assistSizeValueText.text = $"{Mathf.RoundToInt(val)} px";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(APXConfigKeys.AssistSize, val);
            APXConfigService.Save();
            AssistBallGUI.RefreshFromConfig();
        }

        private void OnAssistAlphaSliderChanged(float val)
        {
            if (_assistAlphaValueText != null) _assistAlphaValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(APXConfigKeys.AssistAlpha, val);
            APXConfigService.Save();
            AssistBallGUI.RefreshFromConfig();
        }

        private void OnAssistScaleSliderChanged(float val)
        {
            if (_assistScaleValueText != null) _assistScaleValueText.text = $"{val:F2}x";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(APXConfigKeys.AssistDesktopScale, val);
            APXConfigService.Save();
            AssistBallGUI.RefreshFromConfig();
        }

        // ==========================================
        // Card C 回调
        // ==========================================
        private void OnGlobalScaleSliderChanged(float val)
        {
            if (!APXMobileUIHelper.IsMobile)
            {
                val = APXMobileUIHelper.FiniteClamp(val, APXConfigKeys.FunctionCenterDesktopScaleMin, APXConfigKeys.FunctionCenterDesktopScaleMax, APXConfigKeys.FunctionCenterDesktopScaleDefault);
            }
            if (_globalScaleValueText != null) _globalScaleValueText.text = $"{val:F2}x";
            if (_suppressCallbacks) return;
            APXConfigService.SetFloat(APXConfigKeys.MobileUserScale, val);
            APXMobileUIHelper.UserScaleMultiplier = val;
            APXConfigService.Save();
            APXUICenter.ApplyUserScale(val);
            AssistBallGUI.RefreshFromConfig();
        }

        // ==========================================
        // Card D 回调: 恢复本页默认设置
        // ==========================================
        private void OnResetDefaultsClicked()
        {
            if (Time.unscaledTime < _resetConfirmUntil)
            {
                _resetConfirmUntil = 0f;
                UpdateResetDefaultsBtnText();
                PerformResetDefaults();
            }
            else
            {
                _resetConfirmUntil = Time.unscaledTime + 3f;
                UpdateResetDefaultsBtnText();
            }
        }

        private void UpdateResetDefaultsBtnText()
        {
            if (_resetDefaultsBtnText != null)
            {
                bool isConfirming = Time.unscaledTime < _resetConfirmUntil;
                _resetDefaultsBtnText.text = isConfirming ? "再次点击确认恢复默认" : "恢复本页默认设置";
                _resetDefaultsBtnText.color = isConfirming ? APXUITheme.NumericAccent : APXUITheme.PrimaryText;
            }
        }

        private void PerformResetDefaults()
        {
            // A. 开启数据采集
            APXConfigService.SetBool(APXConfigKeys.MatchObserveEnabled, APXConfigKeys.MatchObserveEnabledDefault);

            // B. 统计挂件背景不透明度
            APXConfigService.SetFloat(StatsBgKey, APXConfigKeys.StatsWidgetBackgroundDefault);

            // C. 统计挂件文字不透明度
            APXConfigService.SetFloat(StatsTextKey, APXConfigKeys.StatsWidgetTextAlphaDefault);

            // D. 统计挂件缩放
            APXConfigService.SetFloat(StatsScaleKey, APXConfigKeys.StatsWidgetScaleDefault);

            // E. 统计挂件锁定
            APXConfigService.SetBool(StatsLockKey, APXConfigKeys.StatsWidgetLockDefault);

            // F. 统计挂件位置 X/Y
            APXConfigService.SetFloat(StatsXKey, APXConfigKeys.StatsWidgetXDefault);
            APXConfigService.SetFloat(StatsYKey, APXConfigKeys.StatsWidgetYDefault);

            // G. Assist AutoHide
            APXConfigService.SetBool(APXConfigKeys.AssistAutoHide, APXConfigKeys.AssistAutoHideDefault);

            // H. Assist Size
            float defSize = APXMobileUIHelper.IsMobile ? APXConfigKeys.AssistSizeMobileDefault : APXConfigKeys.AssistSizeDesktopDefault;
            APXConfigService.SetFloat(APXConfigKeys.AssistSize, defSize);

            // I. Assist Alpha
            APXConfigService.SetFloat(APXConfigKeys.AssistAlpha, APXConfigKeys.AssistAlphaDefault);

            // J. Assist Desktop Scale
            if (!APXMobileUIHelper.IsMobile)
            {
                APXConfigService.SetFloat(APXConfigKeys.AssistDesktopScale, APXConfigKeys.AssistDesktopScaleDefault);
            }

            // K. Global UI Scale (Function Center desktop scale)
            float defUserScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.MobileUserScaleDefault : APXConfigKeys.FunctionCenterDesktopScaleDefault;
            APXConfigService.SetFloat(APXConfigKeys.MobileUserScale, defUserScale);
            APXMobileUIHelper.UserScaleMultiplier = defUserScale;

            // Single batch save
            APXConfigService.Save();

            // Immediately apply to active runtimes
            BattleStatsOverlay.IsObserving = APXConfigKeys.MatchObserveEnabledDefault;
            BattleStatsOverlay.RefreshWidgetPreferences();
            AssistBallGUI.RefreshFromConfig();
            APXUICenter.ApplyUserScale(defUserScale);

            // Sync all page controls to reflected defaults
            SyncFromConfig();
        }

        internal void FocusSection(string section)
        {
            if (_scrollRect != null)
            {
                _scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        internal void UpdateSync()
        {
            if (_resetConfirmUntil > 0f && Time.unscaledTime >= _resetConfirmUntil)
            {
                _resetConfirmUntil = 0f;
                UpdateResetDefaultsBtnText();
            }

            // 有界轮询 (0.25s / 约 4Hz)，仅在数值改变时更新 UI 控件，避免与滑动条交互冲突
            if (Time.unscaledTime < _nextSyncTime)
            {
                return;
            }
            _nextSyncTime = Time.unscaledTime + 0.25f;

            SyncFromConfig();
        }
    }
}
