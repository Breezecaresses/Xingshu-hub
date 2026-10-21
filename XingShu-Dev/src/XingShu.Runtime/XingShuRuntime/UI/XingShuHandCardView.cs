using System;
using System.Collections.Generic;
using System.Text;
using XingShuRuntime.Utilities;
using XingShu.Cards;
using XingShu.Truth;
using Tools;
using UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    /// <summary>
    /// 功能中心 - 局内手牌监视 Modern UI 页面 (Phase 2C-3A)
    /// 纯粹的 View 层，100% 委托 HandCardDisplayManager 底层状态与控制逻辑
    /// 遵循 Pure Glass 视觉框架，集成垂直 ScrollView 滚动视图与状态动态投影
    /// </summary>
    internal sealed class XingShuHandCardView
    {
        private readonly GameObject _root;

        // Switch Card
        private Toggle _enableToggle;
        private Text _enableHintText;
        private Toggle _spectatorAutoOpenToggle;
        private Text _spectatorAutoOpenHintText;

        // Card Scale Card
        private Slider _scaleSlider;
        private Text _scaleValueText;
        private readonly List<Button> _scalePresetButtons = new List<Button>();
        private readonly List<Image> _scalePresetImages = new List<Image>();
        private readonly List<Text> _scalePresetTexts = new List<Text>();
        private static readonly float[] SCALE_PRESETS = new float[] { .4f, HandCardDock.DefaultScale, .8f, 1f, 1.2f };
        private static readonly string[] SCALE_PRESET_LABELS = new string[] { "40%", "60% 默认", "80%", "100%", "120%" };

        // Play Button Size Card
        private Slider _btnSizeSlider;
        private Text _btnSizeValueText;
        private readonly List<Button> _btnSizePresetButtons = new List<Button>();
        private readonly List<Image> _btnSizePresetImages = new List<Image>();
        private readonly List<Text> _btnSizePresetTexts = new List<Text>();
        private static readonly float[] BTN_SIZE_PRESETS = new float[] { 20f, 24f, 36f, 60f, 100f };
        private static readonly string[] BTN_SIZE_PRESET_LABELS = new string[] { "20px (紧凑)", "24px (标准)", "36px (清晰)", "60px (特大)", "100px (醒目)" };

        // Sync & Runtime Status Card
        private Text _syncStatusText;
        private Text _serverResponseText;
        private Button _syncButton;
        private Image _syncButtonImage;
        private Text _syncButtonText;

        // Players Hand Status Card
        private Text _playersHeaderLabel;
        private Text _playersListText;
        private LayoutElement _playersCardLayoutElement;

        // Observed State for 0 GC projection
        private bool _suppressCallbacks;
        private bool _lastObservedEnabled;
        private bool _lastObservedSpectatorAutoOpen;
        private float _lastObservedScale = -1f;
        private float _lastObservedBtnSize = -1f;
        private bool _lastObservedNeedFetch;
        private bool _lastObservedActiveDisplay;
        private bool _lastObservedPveMode;
        private bool _lastObservedSpectatorMode;
        private HandTruthSourceMode _lastObservedTruthMode = HandTruthSourceMode.None;
        private string _lastObservedResponse = "";
        private int _lastObservedPlayersCount = -1;
        private string _lastObservedHandsSignature = "";

        internal GameObject Root => _root;

        internal XingShuHandCardView(Transform parent)
        {
            _root = XingShuUIFactory.CreateUIObject("Page_HandCard", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildUI();
        }

        private void BuildUI()
        {
            ScrollRect scrollRect = XingShuUIFactory.CreateVerticalScrollView(_root, "HandCardScrollView", out GameObject contentArea);
            VerticalLayoutGroup vlg = contentArea.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.spacing = 14f;
                vlg.padding = new RectOffset(0, 10, 0, 24);
            }

            // 0. 特色功能卡片 (Feature Header Card: 76px)
            BuildHeaderCard(contentArea);

            // 1. 全局总开关控制 (Switch Card: 96px)
            BuildSwitchCard(contentArea);

            // 2. 卡牌尺寸缩放设置 (Scale Card: 148px)
            BuildCardScaleCard(contentArea);

            // 3. 播放标识大小设置 (BtnSize Card: 148px)
            // Per-avatar play toggles were replaced by the floating detail dock.

            // 4. 同步与运行时状态卡片 (Status & Sync Card: 154px)
            BuildSyncStatusCard(contentArea);

            // 5. 战局玩家手牌状态监控区 (Players Hands Card: min 200px)
            BuildPlayersHandCard(contentArea);
        }

        private GameObject CreateCardContainer(GameObject parent, string name, float height)
        {
            GameObject cardGo = XingShuUIFactory.CreateUIObject(name, parent);
            LayoutElement le = cardGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleHeight = 0f;

            Sprite cardSprite = XingShuUIAssetService.GetSprite("card_base.png");
            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = cardSprite;
            cardBg.type = Image.Type.Sliced;
            cardBg.color = XingShuUIAssetService.IsFallbackSprite(cardSprite) ? XingShuUITheme.Card : Color.white;

            return cardGo;
        }

        private void BuildHeaderCard(GameObject parent)
        {
            GameObject headerGo = XingShuUIFactory.CreateUIObject("FeatureHeaderCard", parent);
            LayoutElement le = headerGo.AddComponent<LayoutElement>();
            le.preferredHeight = 76f;
            le.minHeight = 76f;
            le.flexibleHeight = 0f;

            Sprite cfhSprite = XingShuUIAssetService.GetSprite("card_feature_header.png");
            Image headerBg = headerGo.AddComponent<Image>();
            headerBg.sprite = cfhSprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = XingShuUIAssetService.IsFallbackSprite(cfhSprite) ? XingShuUITheme.CardFeatureBase : Color.white;

            Text titleText = XingShuUIFactory.CreateText(headerGo, "Title", "局内手牌监视与同步", 18, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 12f);
            titleRt.sizeDelta = new Vector2(360f, 28f);

            Text descText = XingShuUIFactory.CreateText(headerGo, "Description", XingShuMobileUIHelper.IsMobile ? "手牌交错叠放，点卡牌放大查看，再点屏幕收起" : "手牌交错叠放，牌多自动压紧，悬停抬起查看完整信息", 13, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform descRt = descText.rectTransform;
            descRt.anchorMin = new Vector2(0f, 0.5f);
            descRt.anchorMax = new Vector2(0f, 0.5f);
            descRt.pivot = new Vector2(0f, 0.5f);
            descRt.anchoredPosition = new Vector2(24f, -14f);
            descRt.sizeDelta = new Vector2(500f, 22f);

            GameObject badgeGo = XingShuUIFactory.CreateUIObject("HotkeyBadge", headerGo);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 0.5f);
            badgeRt.anchorMax = new Vector2(1f, 0.5f);
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.anchoredPosition = new Vector2(-24f, 0f);
            badgeRt.sizeDelta = new Vector2(130f, 32f);

            Sprite badgeSprite = XingShuUIAssetService.GetSprite("button_selected.png");
            Image badgeImg = badgeGo.AddComponent<Image>();
            badgeImg.sprite = badgeSprite;
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = XingShuUIAssetService.IsFallbackSprite(badgeSprite) ? new Color(0.15f, 0.18f, 0.24f, 0.85f) : Color.white;

            var openDock = badgeGo.AddComponent<Button>();
            openDock.targetGraphic = badgeImg;
            openDock.onClick.AddListener(() => HandCardDock.Toggle());
            Text badgeText = XingShuUIFactory.CreateText(badgeGo, "Text", "开启 / 收起挂件", 13, XingShuUITheme.PrimaryText, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform btRt = badgeText.rectTransform;
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;
        }

        private void BuildSwitchCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "SwitchCard", 148f);

            // Row 1: 实时手牌悬浮窗控制
            Text cardTitle = XingShuUIFactory.CreateText(cardGo, "CardTitle", "实时手牌悬浮窗", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -16f);
            ctRt.sizeDelta = new Vector2(240f, 24f);

            _enableHintText = XingShuUIFactory.CreateText(cardGo, "HintText", "通过悬浮球打开手牌详情挂件；可收起、拖动和切换玩家，关闭仅隐藏显示", 12, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform htRt = _enableHintText.rectTransform;
            htRt.anchorMin = new Vector2(0f, 1f);
            htRt.anchorMax = new Vector2(0f, 1f);
            htRt.pivot = new Vector2(0f, 1f);
            htRt.anchoredPosition = new Vector2(24f, -42f);
            htRt.sizeDelta = new Vector2(460f, 20f);

            _enableToggle = XingShuUIFactory.CreateToggle(cardGo, "EnableToggle", "手牌同步", HandCardDisplayManager.IsEnabled, OnEnableToggleChanged);
            RectTransform etRt = _enableToggle.GetComponent<RectTransform>();
            etRt.anchorMin = new Vector2(1f, 1f);
            etRt.anchorMax = new Vector2(1f, 1f);
            etRt.pivot = new Vector2(1f, 1f);
            etRt.anchoredPosition = new Vector2(-24f, -22f);
            etRt.sizeDelta = new Vector2(150f, 32f);

            // Row 2: 进入观战自动开启偏好
            Text autoTitle = XingShuUIFactory.CreateText(cardGo, "AutoOpenTitle", "进入观战自动开启", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform atRt = autoTitle.rectTransform;
            atRt.anchorMin = new Vector2(0f, 1f);
            atRt.anchorMax = new Vector2(0f, 1f);
            atRt.pivot = new Vector2(0f, 1f);
            atRt.anchoredPosition = new Vector2(24f, -80f);
            atRt.sizeDelta = new Vector2(240f, 24f);

            _spectatorAutoOpenHintText = XingShuUIFactory.CreateText(cardGo, "AutoOpenHintText", "检测到加入新观战房间时自动启用实时手牌悬浮窗；对局中若手动关闭则当局保持关闭，下局观战自动恢复开启", 12, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform ahtRt = _spectatorAutoOpenHintText.rectTransform;
            ahtRt.anchorMin = new Vector2(0f, 1f);
            ahtRt.anchorMax = new Vector2(0f, 1f);
            ahtRt.pivot = new Vector2(0f, 1f);
            ahtRt.anchoredPosition = new Vector2(24f, -106f);
            ahtRt.sizeDelta = new Vector2(460f, 20f);

            _spectatorAutoOpenToggle = XingShuUIFactory.CreateToggle(cardGo, "SpectatorAutoOpenToggle", "观战自动开启", HandCardDisplayManager.SpectatorAutoOpen, OnSpectatorAutoOpenToggleChanged);
            RectTransform satRt = _spectatorAutoOpenToggle.GetComponent<RectTransform>();
            satRt.anchorMin = new Vector2(1f, 1f);
            satRt.anchorMax = new Vector2(1f, 1f);
            satRt.pivot = new Vector2(1f, 1f);
            satRt.anchoredPosition = new Vector2(-24f, -86f);
            satRt.sizeDelta = new Vector2(150f, 32f);
        }

        private void BuildCardScaleCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "CardScaleCard", 148f);

            Text cardTitle = XingShuUIFactory.CreateText(cardGo, "CardTitle", "手牌挂件整体大小（40%–120%）", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -16f);
            ctRt.sizeDelta = new Vector2(360f, 24f);

            _scaleValueText = XingShuUIFactory.CreateText(cardGo, "ScaleValueText", $"{HandCardDock.Scale:P0}", 16, XingShuUITheme.NumericAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform svtRt = _scaleValueText.rectTransform;
            svtRt.anchorMin = new Vector2(1f, 1f);
            svtRt.anchorMax = new Vector2(1f, 1f);
            svtRt.pivot = new Vector2(1f, 1f);
            svtRt.anchoredPosition = new Vector2(-24f, -16f);
            svtRt.sizeDelta = new Vector2(100f, 24f);

            _scaleSlider = XingShuUIFactory.CreateSlider(cardGo, "ScaleSlider", HandCardDock.MinScale, HandCardDock.MaxScale, HandCardDock.Scale, OnScaleSliderChanged);
            RectTransform ssRt = _scaleSlider.GetComponent<RectTransform>();
            ssRt.anchorMin = new Vector2(0f, 1f);
            ssRt.anchorMax = new Vector2(1f, 1f);
            ssRt.pivot = new Vector2(0.5f, 1f);
            ssRt.anchoredPosition = new Vector2(0f, -50f);
            ssRt.sizeDelta = new Vector2(-48f, 28f);

            // 整体缩放同步影响窗口、按钮和卡牌。
            GameObject presetsGo = XingShuUIFactory.CreateUIObject("ScalePresets", cardGo);
            RectTransform prt = presetsGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 16f);
            prt.sizeDelta = new Vector2(-48f, 36f);

            HorizontalLayoutGroup hlg = presetsGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            for (int i = 0; i < SCALE_PRESETS.Length; i++)
            {
                int index = i;
                float targetScale = SCALE_PRESETS[i];
                string label = SCALE_PRESET_LABELS[i];

                Button btn = XingShuUIFactory.CreateButton(presetsGo, $"Preset_{index}", label,
                    btnSecSprite,
                    XingShuUIAssetService.IsFallbackSprite(btnSecSprite) ? XingShuUITheme.ButtonSecondaryBase : Color.white,
                    XingShuUITheme.SecondaryText,
                    delegate
                    {
                        HandCardDock.Scale = targetScale;
                    }, 14);

                _scalePresetButtons.Add(btn);
                _scalePresetImages.Add(btn.GetComponent<Image>());
                _scalePresetTexts.Add(btn.GetComponentInChildren<Text>());
            }
        }

        private void BuildPlayBtnSizeCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "PlayBtnSizeCard", 148f);

            Text cardTitle = XingShuUIFactory.CreateText(cardGo, "CardTitle", "播放标识大小 (Play Button Size)", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -16f);
            ctRt.sizeDelta = new Vector2(300f, 24f);

            _btnSizeValueText = XingShuUIFactory.CreateText(cardGo, "BtnSizeValueText", $"{HandCardDisplayManager.PlayBtnSize:F0} px", 16, XingShuUITheme.NumericAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform bsvtRt = _btnSizeValueText.rectTransform;
            bsvtRt.anchorMin = new Vector2(1f, 1f);
            bsvtRt.anchorMax = new Vector2(1f, 1f);
            bsvtRt.pivot = new Vector2(1f, 1f);
            bsvtRt.anchoredPosition = new Vector2(-24f, -16f);
            bsvtRt.sizeDelta = new Vector2(100f, 24f);

            _btnSizeSlider = XingShuUIFactory.CreateSlider(cardGo, "BtnSizeSlider", 16f, 200f, HandCardDisplayManager.PlayBtnSize, OnBtnSizeSliderChanged);
            RectTransform bssRt = _btnSizeSlider.GetComponent<RectTransform>();
            bssRt.anchorMin = new Vector2(0f, 1f);
            bssRt.anchorMax = new Vector2(1f, 1f);
            bssRt.pivot = new Vector2(0.5f, 1f);
            bssRt.anchoredPosition = new Vector2(0f, -50f);
            bssRt.sizeDelta = new Vector2(-48f, 28f);

            // 预设按钮行 (20px, 24px, 36px, 60px, 100px)
            GameObject presetsGo = XingShuUIFactory.CreateUIObject("BtnSizePresets", cardGo);
            RectTransform prt = presetsGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 16f);
            prt.sizeDelta = new Vector2(-48f, 36f);

            HorizontalLayoutGroup hlg = presetsGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            for (int i = 0; i < BTN_SIZE_PRESETS.Length; i++)
            {
                int index = i;
                float targetSize = BTN_SIZE_PRESETS[i];
                string label = BTN_SIZE_PRESET_LABELS[i];

                Button btn = XingShuUIFactory.CreateButton(presetsGo, $"BtnPreset_{index}", label,
                    btnSecSprite,
                    XingShuUIAssetService.IsFallbackSprite(btnSecSprite) ? XingShuUITheme.ButtonSecondaryBase : Color.white,
                    XingShuUITheme.SecondaryText,
                    delegate
                    {
                        HandCardDisplayManager.PlayBtnSize = targetSize;
                    }, 13);

                _btnSizePresetButtons.Add(btn);
                _btnSizePresetImages.Add(btn.GetComponent<Image>());
                _btnSizePresetTexts.Add(btn.GetComponentInChildren<Text>());
            }
        }

        private void BuildSyncStatusCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "SyncStatusCard", 154f);

            Text cardTitle = XingShuUIFactory.CreateText(cardGo, "CardTitle", "战局同步与状态反馈", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ctRt = cardTitle.rectTransform;
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(0f, 1f);
            ctRt.pivot = new Vector2(0f, 1f);
            ctRt.anchoredPosition = new Vector2(24f, -16f);
            ctRt.sizeDelta = new Vector2(240f, 24f);

            // 状态指示文本
            _syncStatusText = XingShuUIFactory.CreateText(cardGo, "SyncStatusText", "状态检测中...", 14, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform sstRt = _syncStatusText.rectTransform;
            sstRt.anchorMin = new Vector2(0f, 1f);
            sstRt.anchorMax = new Vector2(1f, 1f);
            sstRt.pivot = new Vector2(0f, 1f);
            sstRt.anchoredPosition = new Vector2(24f, -48f);
            sstRt.sizeDelta = new Vector2(-48f, 22f);

            // 服务端最近报文
            _serverResponseText = XingShuUIFactory.CreateText(cardGo, "ServerResponseText", "服务端报文: 无", 12, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform srtRt = _serverResponseText.rectTransform;
            srtRt.anchorMin = new Vector2(0f, 1f);
            srtRt.anchorMax = new Vector2(1f, 1f);
            srtRt.pivot = new Vector2(0f, 1f);
            srtRt.anchoredPosition = new Vector2(24f, -74f);
            srtRt.sizeDelta = new Vector2(-48f, 20f);

            // 手动同步按钮
            Sprite btnSecSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            _syncButton = XingShuUIFactory.CreateButton(cardGo, "ManualSyncButton", "🔄 手动同步房间手牌",
                btnSecSprite,
                XingShuUIAssetService.IsFallbackSprite(btnSecSprite) ? XingShuUITheme.ButtonSecondaryBase : Color.white,
                XingShuUITheme.SecondaryText,
                OnManualSyncClicked, 14);

            _syncButtonImage = _syncButton.GetComponent<Image>();
            _syncButtonText = _syncButton.GetComponentInChildren<Text>();
            RectTransform sbrt = _syncButton.GetComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(0f, 0f);
            sbrt.anchorMax = new Vector2(0f, 0f);
            sbrt.pivot = new Vector2(0f, 0f);
            sbrt.anchoredPosition = new Vector2(24f, 14f);
            sbrt.sizeDelta = new Vector2(200f, 36f);
        }

        private void BuildPlayersHandCard(GameObject parent)
        {
            GameObject cardGo = CreateCardContainer(parent, "PlayersHandCard", 220f);
            _playersCardLayoutElement = cardGo.GetComponent<LayoutElement>();

            _playersHeaderLabel = XingShuUIFactory.CreateText(cardGo, "PlayersHeaderLabel", "当前战局玩家手牌状态 (0 人)", 16, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform phlRt = _playersHeaderLabel.rectTransform;
            phlRt.anchorMin = new Vector2(0f, 1f);
            phlRt.anchorMax = new Vector2(1f, 1f);
            phlRt.pivot = new Vector2(0f, 1f);
            phlRt.anchoredPosition = new Vector2(24f, -16f);
            phlRt.sizeDelta = new Vector2(-48f, 24f);

            _playersListText = XingShuUIFactory.CreateText(cardGo, "PlayersListText", "(当前未在支持的战局中，无活跃玩家手牌监控数据)", 13, XingShuUITheme.SecondaryText, TextAnchor.UpperLeft);
            RectTransform pltRt = _playersListText.rectTransform;
            pltRt.anchorMin = new Vector2(0f, 0f);
            pltRt.anchorMax = new Vector2(1f, 1f);
            pltRt.pivot = new Vector2(0.5f, 1f);
            pltRt.anchoredPosition = Vector2.zero;
            pltRt.offsetMin = new Vector2(24f, 16f);
            pltRt.offsetMax = new Vector2(-24f, -48f);
        }

        private void OnEnableToggleChanged(bool val)
        {
            if (_suppressCallbacks) return;
            HandCardDisplayManager.IsEnabled = val;
            if (val && XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession())
            {
                HandCardDisplayManager.RefreshAllWidgets();
            }
        }

        private void OnSpectatorAutoOpenToggleChanged(bool val)
        {
            if (_suppressCallbacks) return;
            HandCardDisplayManager.SpectatorAutoOpen = val;
        }

        private void OnScaleSliderChanged(float val)
        {
            if (_suppressCallbacks) return;
            HandCardDock.Scale = val;
        }

        private void OnBtnSizeSliderChanged(float val)
        {
            if (_suppressCallbacks) return;
            HandCardDisplayManager.PlayBtnSize = val;
        }

        private void OnManualSyncClicked()
        {
            if (XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession())
            {
                HandCardDisplayManager.MarkOverlayDirty("ManualSyncGUI");
                HandCardDisplayManager.RefreshAllWidgets();
                return;
            }
            if (HandCardDisplayManager.IsPVEMode())
            {
                HandCardDisplayManager.MarkOverlayDirty("ManualSyncPVE");
                HandCardDisplayManager.TriggerFetchCards("ManualGUI");
            }
        }

        /// <summary>
        /// 每帧由 XingShuUICenter.Update() 驱动同步，投影底层状态
        /// 具备变化检测机制，无变更时执行 0 次 UI 写入与 0 GC 分配
        /// </summary>
        internal void UpdateSync()
        {
            bool curEnabled = HandCardDisplayManager.IsEnabled;
            bool curSpectatorAutoOpen = HandCardDisplayManager.SpectatorAutoOpen;
            float curScale = HandCardDock.Scale;
            float curBtnSize = HandCardDisplayManager.PlayBtnSize;
            bool curNeedFetch = HandCardDisplayManager.NeedFetchFromServer;
            bool curActiveDisplay = HandCardDock.IsOpen;
            bool curPveMode = HandCardDisplayManager.IsPVEMode();
            bool curSpectatorMode = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
            HandTruthSourceMode curTruthMode = HandTruthSource.CurrentMode;
            string curResponse = HandCardDisplayManager.LastServerResponseText ?? "";
            var playerHands = HandCardDisplayManager.PlayerHands;

            // Roster Invariant Filter: Strictly constrain playerHands to the active authoritative room roster
            var truthPids = HandTruthSource.GetActiveRoomPlayerIds();
            if (truthPids != null && truthPids.Count > 0)
            {
                var filtered = new Dictionary<long, TrackedPlayerHand>();
                if (playerHands != null)
                {
                    foreach (var kvp in playerHands)
                    {
                        if (truthPids.Contains(kvp.Key))
                        {
                            filtered[kvp.Key] = kvp.Value;
                        }
                    }
                }
                playerHands = filtered;
            }
            else if (curSpectatorMode)
            {
                var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();
                var filtered = new Dictionary<long, TrackedPlayerHand>();
                if (playerHands != null)
                {
                    foreach (var kvp in playerHands)
                    {
                        if (validPids.Contains(kvp.Key))
                        {
                            filtered[kvp.Key] = kvp.Value;
                        }
                    }
                }
                playerHands = filtered;
            }
            int curPlayersCount = playerHands != null ? playerHands.Count : 0;

            // 1. 同步开关状态
            if (_lastObservedEnabled != curEnabled)
            {
                _lastObservedEnabled = curEnabled;
                _suppressCallbacks = true;
                if (_enableToggle != null)
                {
                    _enableToggle.isOn = curEnabled;
                }
                _suppressCallbacks = false;
            }

            // 1b. 同步进入观战自动开启偏好
            if (_lastObservedSpectatorAutoOpen != curSpectatorAutoOpen)
            {
                _lastObservedSpectatorAutoOpen = curSpectatorAutoOpen;
                _suppressCallbacks = true;
                if (_spectatorAutoOpenToggle != null)
                {
                    _spectatorAutoOpenToggle.isOn = curSpectatorAutoOpen;
                }
                _suppressCallbacks = false;
            }

            // 2. 同步卡牌缩放
            if (Mathf.Abs(_lastObservedScale - curScale) > 0.005f)
            {
                _lastObservedScale = curScale;
                _suppressCallbacks = true;
                if (_scaleSlider != null)
                {
                    _scaleSlider.value = curScale;
                }
                if (_scaleValueText != null)
                {
                    _scaleValueText.text = $"{curScale:P0}";
                }
                UpdateScalePresetHighlight(curScale);
                _suppressCallbacks = false;
            }

            // 3. 同步按钮尺寸
            if (Mathf.Abs(_lastObservedBtnSize - curBtnSize) > 0.1f)
            {
                _lastObservedBtnSize = curBtnSize;
                _suppressCallbacks = true;
                if (_btnSizeSlider != null)
                {
                    _btnSizeSlider.value = curBtnSize;
                }
                if (_btnSizeValueText != null)
                {
                    _btnSizeValueText.text = $"{curBtnSize:F0} px";
                }
                UpdateBtnSizePresetHighlight(curBtnSize);
                _suppressCallbacks = false;
            }

            // 4. 同步状态指示与同步按钮状态
            if (_lastObservedNeedFetch != curNeedFetch ||
                _lastObservedActiveDisplay != curActiveDisplay ||
                _lastObservedPveMode != curPveMode ||
                _lastObservedSpectatorMode != curSpectatorMode ||
                _lastObservedTruthMode != curTruthMode ||
                _lastObservedResponse != curResponse ||
                _lastObservedEnabled != curEnabled)
            {
                _lastObservedNeedFetch = curNeedFetch;
                _lastObservedActiveDisplay = curActiveDisplay;
                _lastObservedPveMode = curPveMode;
                _lastObservedSpectatorMode = curSpectatorMode;
                _lastObservedTruthMode = curTruthMode;
                _lastObservedResponse = curResponse;

                if (_syncStatusText != null)
                {
                    string overlayTag = curEnabled
                        ? "<color=#087059>同步已启用</color>"
                        : "<color=#94a3b8>同步已关闭</color>";
                    string fetchTag = curNeedFetch ? "<color=#f59e0b>🟡 存在变动</color>" : "<color=#10b981>🟢 最新</color>";
                    string displayTag = curActiveDisplay ? "<color=#00e5ff>详情挂件已开启</color>" : "<color=#94a3b8>详情挂件已收起</color>";
                    string envTag;
                    if (curTruthMode == HandTruthSourceMode.LOCAL_SPECTATOR || curSpectatorMode)
                    {
                        var (rId, epoch, seq) = XingShu.Spectator.SpectatorStateStore.GetSessionInfo();
                        envTag = $"<color=#38bdf8>👁️ 官方观战 (房号: {rId})</color>";
                    }
                    else if (curTruthMode == HandTruthSourceMode.HUB_MIRROR_FROM_SPECTATOR)
                    {
                        var snap = HandTruthSource.CurrentSnapshot;
                        envTag = $"<color=#a855f7>🪞 观战镜像 (房号: {snap?.RoomId})</color>";
                    }
                    else if (curTruthMode == HandTruthSourceMode.LOCAL_PVE_AUTHORITATIVE || curPveMode)
                    {
                        envTag = "<color=#10b981>🎮 PVE 战斗中</color>";
                    }
                    else
                    {
                        envTag = "<color=#64748b>⏸ 未在支持局内</color>";
                    }
                    _syncStatusText.text = $"[显示: {overlayTag}]  [待拉取: {fetchTag}]  [挂件: {displayTag}]  [环境: {envTag}]";
                }

                if (_serverResponseText != null)
                {
                    var snap = HandTruthSource.CurrentSnapshot;
                    if (curTruthMode == HandTruthSourceMode.HUB_MIRROR_FROM_SPECTATOR)
                    {
                        _serverResponseText.text = $"<b>星枢观战镜像:</b> {snap?.GetSourceDescription()}, Epoch={snap?.Epoch}, Seq={snap?.Sequence}, 玩家数={snap?.TotalPlayers}";
                    }
                    else if (curSpectatorMode)
                    {
                        var (rId, epoch, seq) = XingShu.Spectator.SpectatorStateStore.GetSessionInfo();
                        _serverResponseText.text = $"<b>观战状态仓:</b> RoomId={rId}, Epoch={epoch}, Seq={seq}, 玩家数={XingShu.Spectator.SpectatorStateStore.RoomPlayers}";
                    }
                    else if (curTruthMode == HandTruthSourceMode.LOCAL_PVE_AUTHORITATIVE)
                    {
                        _serverResponseText.text = $"<b>本地PVE真相源:</b> {snap?.GetSourceDescription()}, 玩家数={snap?.TotalPlayers}";
                    }
                    else if (string.IsNullOrEmpty(curResponse))
                    {
                        _serverResponseText.text = "服务端最近报文: 暂无报文";
                    }
                    else
                    {
                        _serverResponseText.text = $"<b>服务端最近报文:</b> {curResponse}";
                    }
                }

                if (_syncButton != null)
                {
                    bool isSupported = (curPveMode || curSpectatorMode || curTruthMode != HandTruthSourceMode.None);
                    _syncButton.interactable = isSupported;
                    if (_syncButtonImage != null)
                    {
                        _syncButtonImage.color = isSupported ? Color.white : new Color(0.4f, 0.4f, 0.4f, 0.6f);
                    }
                    if (_syncButtonText != null)
                    {
                        _syncButtonText.color = isSupported ? XingShuUITheme.PrimaryText : XingShuUITheme.SecondaryText;
                    }
                }
            }

            // 5. 同步战局玩家手牌列表
            UpdatePlayerHandsProjection(playerHands, curPlayersCount);
        }

        private void UpdatePlayerHandsProjection(IReadOnlyDictionary<long, TrackedPlayerHand> playerHands, int curPlayersCount)
        {
            if (playerHands == null || curPlayersCount == 0)
            {
                if (_lastObservedPlayersCount != 0)
                {
                    _lastObservedPlayersCount = 0;
                    _lastObservedHandsSignature = "";
                    if (_playersHeaderLabel != null)
                    {
                        _playersHeaderLabel.text = "当前战局玩家手牌状态 (0 人)";
                    }
                    if (_playersListText != null)
                    {
                        _playersListText.text = "(当前未在支持的战局中，无活跃玩家手牌监控数据)";
                    }
                    if (_playersCardLayoutElement != null)
                    {
                        _playersCardLayoutElement.preferredHeight = 120f;
                        _playersCardLayoutElement.minHeight = 120f;
                    }
                }
                return;
            }

            // 计算简要指纹以避免每帧字符串重构
            StringBuilder sbSig = new StringBuilder();
            foreach (var kvp in playerHands)
            {
                TrackedPlayerHand h = kvp.Value;
                if (h != null)
                {
                    sbSig.Append(h.PlayerId).Append(':').Append(h.SyncState).Append(':').Append(h.Cards.Count).Append(':').Append(h.MaskedHandCount).Append(';');
                }
            }
            string curSig = sbSig.ToString();

            if (_lastObservedPlayersCount == curPlayersCount && _lastObservedHandsSignature == curSig)
            {
                return; // 无数据变更，0 写入
            }

            _lastObservedPlayersCount = curPlayersCount;
            _lastObservedHandsSignature = curSig;

            if (_playersHeaderLabel != null)
            {
                _playersHeaderLabel.text = $"当前战局玩家手牌状态 ({curPlayersCount} 人):";
            }

            StringBuilder sb = new StringBuilder();
            foreach (var kvp in playerHands)
            {
                TrackedPlayerHand h = kvp.Value;
                if (h == null) continue;

                string statusTag = h.IsBot ? "<color=#00e5ff>[🤖Bot]</color>" :
                    ((h.SyncState == HandSyncState.Latest) ? "<color=#10b981>[🟢最新]</color>" :
                    ((h.SyncState == HandSyncState.Pending) ? "<color=#f59e0b>[🟡待更新]</color>" :
                    "<color=#94a3b8>[⚪仅数量]</color>"));

                int knownCount = 0;
                int unknownCount = 0;
                StringBuilder cardsDesc = new StringBuilder();

                for (int i = 0; i < h.Cards.Count; i++)
                {
                    TrackedCard tc = h.Cards[i];
                    if (tc != null && tc.CardId > 0)
                    {
                        knownCount++;
                        string cardDisplay = CardRegistryGenerated.FormatCardDisplay(tc.CardId);
                        if (tc.BattleCost > 0)
                        {
                            cardsDesc.Append($"[{cardDisplay} 费:{tc.BattleCost}] ");
                        }
                        else
                        {
                            cardsDesc.Append($"[{cardDisplay}] ");
                        }
                    }
                    else
                    {
                        unknownCount++;
                    }
                }

                int totalCount = Math.Max(h.Cards.Count, h.MaskedHandCount);
                string displayText;
                if (knownCount > 0 && unknownCount == 0 && h.MaskedHandCount <= knownCount)
                {
                    displayText = $"{knownCount} 张已识别: {cardsDesc}";
                }
                else if (knownCount > 0)
                {
                    int remainingMasked = Math.Max(unknownCount, h.MaskedHandCount - knownCount);
                    displayText = $"{knownCount} 张已识别: {cardsDesc}+ <color=#f59e0b>[等待观战端完整手牌数据]</color> <color=#94a3b8>(另持有 {remainingMasked} 张)</color>";
                }
                else if (totalCount > 0)
                {
                    displayText = $"<color=#f59e0b>[等待观战端完整手牌数据]</color> <color=#94a3b8>(持有 {totalCount} 张)</color>";
                }
                else
                {
                    displayText = "无手牌 (0 张)";
                }

                sb.AppendLine($"• {statusTag} 玩家 {h.PlayerName} / {h.PlayerId} (Slot={h.Slot}) [手牌: {totalCount}张]: {displayText}");
            }

            if (_playersListText != null)
            {
                _playersListText.text = sb.ToString();
            }

            // 动态调节卡片高度以容纳玩家行
            float targetHeight = Mathf.Max(140f, 60f + (curPlayersCount * 42f));
            if (_playersCardLayoutElement != null)
            {
                _playersCardLayoutElement.preferredHeight = targetHeight;
                _playersCardLayoutElement.minHeight = targetHeight;
            }
        }

        private void UpdateScalePresetHighlight(float currentScale)
        {
            Sprite normalSp = XingShuUIAssetService.GetSprite("button_secondary.png");
            Sprite selSp = XingShuUIAssetService.GetSprite("button_selected.png");

            for (int i = 0; i < _scalePresetButtons.Count; i++)
            {
                if (i >= SCALE_PRESETS.Length) break;
                bool isSelected = Mathf.Abs(SCALE_PRESETS[i] - currentScale) < 0.05f;

                if (i < _scalePresetImages.Count && _scalePresetImages[i] != null)
                {
                    _scalePresetImages[i].sprite = isSelected ? selSp : normalSp;
                    _scalePresetImages[i].color = XingShuUIAssetService.IsFallbackSprite(_scalePresetImages[i].sprite)
                        ? (isSelected ? XingShuUITheme.ButtonSelectedBase : XingShuUITheme.ButtonSecondaryBase)
                        : Color.white;
                }

                if (i < _scalePresetTexts.Count && _scalePresetTexts[i] != null)
                {
                    _scalePresetTexts[i].color = isSelected ? XingShuUITheme.PrimaryText : XingShuUITheme.SecondaryText;
                    _scalePresetTexts[i].fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private void UpdateBtnSizePresetHighlight(float currentSize)
        {
            Sprite normalSp = XingShuUIAssetService.GetSprite("button_secondary.png");
            Sprite selSp = XingShuUIAssetService.GetSprite("button_selected.png");

            for (int i = 0; i < _btnSizePresetButtons.Count; i++)
            {
                if (i >= BTN_SIZE_PRESETS.Length) break;
                bool isSelected = Mathf.Abs(BTN_SIZE_PRESETS[i] - currentSize) < 1.0f;

                if (i < _btnSizePresetImages.Count && _btnSizePresetImages[i] != null)
                {
                    _btnSizePresetImages[i].sprite = isSelected ? selSp : normalSp;
                    _btnSizePresetImages[i].color = XingShuUIAssetService.IsFallbackSprite(_btnSizePresetImages[i].sprite)
                        ? (isSelected ? XingShuUITheme.ButtonSelectedBase : XingShuUITheme.ButtonSecondaryBase)
                        : Color.white;
                }

                if (i < _btnSizePresetTexts.Count && _btnSizePresetTexts[i] != null)
                {
                    _btnSizePresetTexts[i].color = isSelected ? XingShuUITheme.PrimaryText : XingShuUITheme.SecondaryText;
                    _btnSizePresetTexts[i].fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }
    }
}
