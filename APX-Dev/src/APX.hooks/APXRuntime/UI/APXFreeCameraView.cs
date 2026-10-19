using System;
using APXRuntime.Core;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心 - 自由镜头 (MapCamera) Modern UI 页面 (Phase 2D-3B1)
    /// 遵循 Route A 极简迁移路线：纯 View 组件，绝不直接操作 Camera.transform 或轮询 Input
    /// 彻底消除 IMGUI 浮窗，以卡片化结构呈现状态、控制、灵敏度与说明
    /// </summary>
    internal sealed class APXFreeCameraView
    {
        private readonly GameObject _root;

        // Card 1: 顶部主功能与状态卡片
        private Toggle _enableToggle;
        private Image _statusBadgeBg;
        private Text _statusBadgeText;
        private Text _statusDetailText;
        private Text _zoomInfoText;

        // Card 2: 视角控制与灵敏度调节卡片
        private Button _btnFit;
        private Button _btnRestoreClose;
        private Slider _scrollSpeedSlider;
        private Text _scrollSpeedValueText;
        private Text _projectionStatusText;

        // 缓存观测值 (0 GC / 仅状态变化时更新 UI)
        private bool _lastObservedRequested;
        private bool _lastObservedEnabled;
        private string _lastObservedStatusText = "";
        private float _lastObservedZoom = -1f;
        private float _lastObservedScrollSpeed = -1f;
        private string _lastObservedProjection = "";
        private bool _suppressCallbacks = false;

        internal GameObject Root => _root;

        internal APXFreeCameraView(Transform parent)
        {
            _root = APXUIFactory.CreateUIObject("Page_FreeCamera", parent.gameObject);
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

            // ==========================================
            // 1. 顶部主功能与状态卡片 (Card 1: 120px, y: 0)
            // ==========================================
            GameObject card1 = APXUIFactory.CreateUIObject("Card_HeaderStatus", _root);
            RectTransform c1Rt = card1.GetComponent<RectTransform>();
            c1Rt.anchorMin = new Vector2(0f, 1f);
            c1Rt.anchorMax = new Vector2(1f, 1f);
            c1Rt.pivot = new Vector2(0.5f, 1f);
            c1Rt.anchoredPosition = new Vector2(0f, 0f);
            c1Rt.sizeDelta = new Vector2(0f, 120f);

            Image c1Bg = card1.AddComponent<Image>();
            c1Bg.sprite = cfhSprite;
            c1Bg.type = Image.Type.Sliced;
            c1Bg.color = APXUIAssetService.IsFallbackSprite(cfhSprite) ? APXUITheme.CardFeatureBase : Color.white;

            // 标题 "地图自由镜头"
            Text titleText = APXUIFactory.CreateText(card1, "Title", "地图自由镜头", 20, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ttRt = titleText.rectTransform;
            ttRt.anchorMin = new Vector2(0f, 1f);
            ttRt.anchorMax = new Vector2(0f, 1f);
            ttRt.pivot = new Vector2(0f, 1f);
            ttRt.anchoredPosition = new Vector2(24f, -16f);
            ttRt.sizeDelta = new Vector2(240f, 26f);

            // 副标题
            Text subText = APXUIFactory.CreateText(card1, "Subtitle", "打破原版固定视角限制 · 自由平移与平滑缩放", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subText.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -44f);
            stRt.sizeDelta = new Vector2(360f, 20f);

            // 状态徽章 (右上角)
            GameObject badgeGo = APXUIFactory.CreateUIObject("StatusBadge", card1);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot = new Vector2(1f, 1f);
            badgeRt.anchoredPosition = new Vector2(-24f, -16f);
            badgeRt.sizeDelta = new Vector2(160f, 26f);

            _statusBadgeBg = badgeGo.AddComponent<Image>();
            _statusBadgeBg.sprite = cardBaseSprite;
            _statusBadgeBg.type = Image.Type.Sliced;
            _statusBadgeBg.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);

            _statusBadgeText = APXUIFactory.CreateText(badgeGo, "Text", "已关闭", 12, APXUITheme.MutedText, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform sbtRt = _statusBadgeText.rectTransform;
            sbtRt.anchorMin = Vector2.zero;
            sbtRt.anchorMax = Vector2.one;
            sbtRt.offsetMin = Vector2.zero;
            sbtRt.offsetMax = Vector2.zero;

            // 自由镜头总开关 (Toggle, 左下角)
            _enableToggle = APXUIFactory.CreateToggle(card1, "EnableToggle", "自由镜头总开关", MapCameraGUI.IsRequested, OnToggleValueChanged);
            RectTransform toggleRt = _enableToggle.GetComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(0f, 0f);
            toggleRt.anchorMax = new Vector2(0f, 0f);
            toggleRt.pivot = new Vector2(0f, 0f);
            toggleRt.anchoredPosition = new Vector2(24f, 16f);
            toggleRt.sizeDelta = new Vector2(220f, 32f);

            // 状态细节说明与缩放尺寸 (右下角)
            _statusDetailText = APXUIFactory.CreateText(card1, "StatusDetail", "首次默认关闭；开关跨局自动保存", 12, APXUITheme.MutedText, TextAnchor.MiddleRight);
            RectTransform sdtRt = _statusDetailText.rectTransform;
            sdtRt.anchorMin = new Vector2(1f, 0f);
            sdtRt.anchorMax = new Vector2(1f, 0f);
            sdtRt.pivot = new Vector2(1f, 0f);
            sdtRt.anchoredPosition = new Vector2(-24f, 22f);
            sdtRt.sizeDelta = new Vector2(300f, 20f);

            _zoomInfoText = APXUIFactory.CreateText(card1, "ZoomInfo", "", 12, APXUITheme.SecondaryText, TextAnchor.MiddleRight);
            RectTransform zitRt = _zoomInfoText.rectTransform;
            zitRt.anchorMin = new Vector2(1f, 0f);
            zitRt.anchorMax = new Vector2(1f, 0f);
            zitRt.pivot = new Vector2(1f, 0f);
            zitRt.anchoredPosition = new Vector2(-24f, 42f);
            zitRt.sizeDelta = new Vector2(220f, 18f);

            // ==========================================
            // 2. 视角控制与灵敏度调节卡片 (Card 2: 175px, y: -130)
            // ==========================================
            GameObject card2 = APXUIFactory.CreateUIObject("Card_Controls", _root);
            RectTransform c2Rt = card2.GetComponent<RectTransform>();
            c2Rt.anchorMin = new Vector2(0f, 1f);
            c2Rt.anchorMax = new Vector2(1f, 1f);
            c2Rt.pivot = new Vector2(0.5f, 1f);
            c2Rt.anchoredPosition = new Vector2(0f, -130f);
            c2Rt.sizeDelta = new Vector2(0f, 175f);

            Image c2Bg = card2.AddComponent<Image>();
            c2Bg.sprite = cardBaseSprite;
            c2Bg.type = Image.Type.Sliced;
            c2Bg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 卡片标题
            Text c2Title = APXUIFactory.CreateText(card2, "Card2Title", "视角操作与灵敏度调节", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform c2tRt = c2Title.rectTransform;
            c2tRt.anchorMin = new Vector2(0f, 1f);
            c2tRt.anchorMax = new Vector2(0f, 1f);
            c2tRt.pivot = new Vector2(0f, 1f);
            c2tRt.anchoredPosition = new Vector2(20f, -14f);
            c2tRt.sizeDelta = new Vector2(260f, 22f);

            // 动作按钮容器 (全图适配 / 还原并关闭)
            _btnFit = APXUIFactory.CreateButton(card2, "Btn_Fit", "全图适配", btnSecSprite, APXUITheme.ButtonSecondaryBase, APXUITheme.PrimaryText, OnFitClicked, 13);
            RectTransform fitRt = _btnFit.GetComponent<RectTransform>();
            fitRt.anchorMin = new Vector2(0f, 1f);
            fitRt.anchorMax = new Vector2(0f, 1f);
            fitRt.pivot = new Vector2(0f, 1f);
            fitRt.anchoredPosition = new Vector2(20f, -44f);
            fitRt.sizeDelta = new Vector2(140f, 36f);

            _btnRestoreClose = APXUIFactory.CreateButton(card2, "Btn_RestoreClose", "还原并关闭", btnSecSprite, APXUITheme.ButtonSecondaryBase, APXUITheme.Danger, OnRestoreCloseClicked, 13);
            RectTransform rcRt = _btnRestoreClose.GetComponent<RectTransform>();
            rcRt.anchorMin = new Vector2(0f, 1f);
            rcRt.anchorMax = new Vector2(0f, 1f);
            rcRt.pivot = new Vector2(0f, 1f);
            rcRt.anchoredPosition = new Vector2(170f, -44f);
            rcRt.sizeDelta = new Vector2(140f, 36f);

            // 滚轮缩放灵敏度标签与数值
            Text sliderLabel = APXUIFactory.CreateText(card2, "SliderLabel", "PC 滚轮缩放灵敏度", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform slRt = sliderLabel.rectTransform;
            slRt.anchorMin = new Vector2(0f, 1f);
            slRt.anchorMax = new Vector2(0f, 1f);
            slRt.pivot = new Vector2(0f, 1f);
            slRt.anchoredPosition = new Vector2(20f, -92f);
            slRt.sizeDelta = new Vector2(160f, 20f);

            _scrollSpeedValueText = APXUIFactory.CreateText(card2, "SliderValue", MapCameraGUI.ScrollSpeed.ToString("0.00"), 14, APXUITheme.NumericAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform svtRt = _scrollSpeedValueText.rectTransform;
            svtRt.anchorMin = new Vector2(1f, 1f);
            svtRt.anchorMax = new Vector2(1f, 1f);
            svtRt.pivot = new Vector2(1f, 1f);
            svtRt.anchoredPosition = new Vector2(-20f, -92f);
            svtRt.sizeDelta = new Vector2(80f, 20f);

            // 灵敏度滑块
            _scrollSpeedSlider = APXUIFactory.CreateSlider(card2, "ScrollSpeedSlider", APXConfigKeys.MapScrollSpeedMin, APXConfigKeys.MapScrollSpeedMax, MapCameraGUI.ScrollSpeed, OnScrollSpeedChanged);
            RectTransform ssRt = _scrollSpeedSlider.GetComponent<RectTransform>();
            ssRt.anchorMin = new Vector2(0f, 1f);
            ssRt.anchorMax = new Vector2(1f, 1f);
            ssRt.pivot = new Vector2(0.5f, 1f);
            ssRt.anchoredPosition = new Vector2(0f, -118f);
            ssRt.sizeDelta = new Vector2(-40f, 24f);

            // 头顶定位与投影同步状态行
            _projectionStatusText = APXUIFactory.CreateText(card2, "ProjectionStatus", MapCameraGUI.ProjectionStatus, 11, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform psRt = _projectionStatusText.rectTransform;
            psRt.anchorMin = new Vector2(0f, 0f);
            psRt.anchorMax = new Vector2(1f, 0f);
            psRt.pivot = new Vector2(0.5f, 0f);
            psRt.anchoredPosition = new Vector2(0f, 8f);
            psRt.sizeDelta = new Vector2(-40f, 18f);

            // ==========================================
            // 3. 键鼠与触控操作指南卡片 (Card 3: 145px, y: -315)
            // ==========================================
            GameObject card3 = APXUIFactory.CreateUIObject("Card_Guide", _root);
            RectTransform c3Rt = card3.GetComponent<RectTransform>();
            c3Rt.anchorMin = new Vector2(0f, 1f);
            c3Rt.anchorMax = new Vector2(1f, 1f);
            c3Rt.pivot = new Vector2(0.5f, 1f);
            c3Rt.anchoredPosition = new Vector2(0f, -315f);
            c3Rt.sizeDelta = new Vector2(0f, 145f);

            Image c3Bg = card3.AddComponent<Image>();
            c3Bg.sprite = cardBaseSprite;
            c3Bg.type = Image.Type.Sliced;
            c3Bg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            Text c3Title = APXUIFactory.CreateText(card3, "Card3Title", "操作指南 (PC & 触屏)", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform c3tRt = c3Title.rectTransform;
            c3tRt.anchorMin = new Vector2(0f, 1f);
            c3tRt.anchorMax = new Vector2(0f, 1f);
            c3tRt.pivot = new Vector2(0f, 1f);
            c3tRt.anchoredPosition = new Vector2(20f, -14f);
            c3tRt.sizeDelta = new Vector2(260f, 22f);

            string guideContent =
                "• 键盘平移：方向键 (↑ ↓ ← →) 平移地图视野\n" +
                "• 鼠标平移：鼠标左键按下并拖拽空白地块\n" +
                "• 视角缩放：滑动鼠标滚轮 (触屏支持双指张合拉近/拉远)\n" +
                "• 视角旋转：暂无 (视角锁定在地图俯仰角，不响应右键/中键)\n" +
                "• 安全保护：选怪与战斗演出期间视角保留，仅暂停用户输入";

            Text guideTxt = APXUIFactory.CreateText(card3, "GuideContent", guideContent, 12, APXUITheme.SecondaryText, TextAnchor.UpperLeft);
            RectTransform gtRt = guideTxt.rectTransform;
            gtRt.anchorMin = new Vector2(0f, 0f);
            gtRt.anchorMax = new Vector2(1f, 1f);
            gtRt.pivot = new Vector2(0f, 1f);
            gtRt.offsetMin = new Vector2(20f, 10f);
            gtRt.offsetMax = new Vector2(-20f, -40f);
        }

        private void OnToggleValueChanged(bool value)
        {
            if (_suppressCallbacks) return;
            MapCameraGUI.IsRequested = value;
        }

        private void OnFitClicked()
        {
            MapCameraGUI.RequestFit();
        }

        private void OnRestoreCloseClicked()
        {
            MapCameraGUI.RequestCloseCamera();
        }

        private void OnScrollSpeedChanged(float value)
        {
            if (_suppressCallbacks) return;
            MapCameraGUI.ScrollSpeed = value;
            if (_scrollSpeedValueText != null)
            {
                _scrollSpeedValueText.text = value.ToString("0.00");
            }
        }

        internal void UpdateSync()
        {
            bool requested = MapCameraGUI.IsRequested;
            bool enabled = MapCameraGUI.IsCameraEnabled;
            string statusDesc = MapCameraGUI.StatusDescription;
            float zoom = MapCameraGUI.CurrentZoom;
            float speed = MapCameraGUI.ScrollSpeed;
            string projection = MapCameraGUI.ProjectionStatus;

            // 1. 状态变更判定
            bool requestedChanged = (requested != _lastObservedRequested);
            bool enabledChanged = (enabled != _lastObservedEnabled);
            bool statusChanged = (statusDesc != _lastObservedStatusText);

            if (requestedChanged)
            {
                _lastObservedRequested = requested;
                if (_enableToggle != null && _enableToggle.isOn != requested)
                {
                    _suppressCallbacks = true;
                    _enableToggle.isOn = requested;
                    _suppressCallbacks = false;
                }
            }

            // 2. 同步状态徽章与详情
            if (requestedChanged || enabledChanged || statusChanged)
            {
                _lastObservedEnabled = enabled;
                _lastObservedStatusText = statusDesc;

                if (_statusBadgeText != null)
                {
                    if (!requested)
                    {
                        _statusBadgeText.text = "已关闭";
                        _statusBadgeText.color = APXUITheme.MutedText;
                        if (_statusBadgeBg != null) _statusBadgeBg.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);
                    }
                    else if (!enabled)
                    {
                        _statusBadgeText.text = "已开启 · 等待进入对局";
                        _statusBadgeText.color = APXUITheme.NumericAccent;
                        if (_statusBadgeBg != null) _statusBadgeBg.color = new Color(0.25f, 0.20f, 0.08f, 0.85f);
                    }
                    else
                    {
                        _statusBadgeText.text = "运行中";
                        _statusBadgeText.color = APXUITheme.Positive;
                        if (_statusBadgeBg != null) _statusBadgeBg.color = new Color(0.08f, 0.22f, 0.16f, 0.85f);
                    }
                }

                if (_statusDetailText != null)
                {
                    _statusDetailText.text = statusDesc;
                }
            }

            // 3. 同步缩放信息
            if (Math.Abs(zoom - _lastObservedZoom) > 0.05f)
            {
                _lastObservedZoom = zoom;
                if (_zoomInfoText != null)
                {
                    _zoomInfoText.text = enabled ? $"当前视距: {zoom:0.0}" : "";
                }
            }

            // 4. 同步灵敏度数值
            if (Math.Abs(speed - _lastObservedScrollSpeed) > 0.001f)
            {
                _lastObservedScrollSpeed = speed;
                if (_scrollSpeedSlider != null && Math.Abs(_scrollSpeedSlider.value - speed) > 0.001f)
                {
                    _suppressCallbacks = true;
                    _scrollSpeedSlider.value = speed;
                    _suppressCallbacks = false;
                }
                if (_scrollSpeedValueText != null)
                {
                    _scrollSpeedValueText.text = speed.ToString("0.00");
                }
            }

            // 5. 同步投影信息
            if (projection != _lastObservedProjection)
            {
                _lastObservedProjection = projection;
                if (_projectionStatusText != null)
                {
                    _projectionStatusText.text = projection;
                }
            }
        }
    }
}
