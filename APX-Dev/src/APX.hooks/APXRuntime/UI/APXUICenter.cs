using System;
using System.Collections.Generic;
using APXRuntime.Core;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心主控制器 (Phase 2B-R4 生产整合版)
    /// 遵循 R3.2 原版二次元视觉设计规范，提供 100% 中性通用、平整克制的游戏原生 HUD 功能中心
    /// </summary>
    [DefaultExecutionOrder(-9900)]
    internal sealed class APXUICenter : MonoBehaviour
    {
        internal static APXUICenter Instance { get; private set; }

        internal static bool IsOpen => Instance != null && Instance._isOpen;

        private bool _isOpen;
        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _mainFrameRt;

        private GameObject _contentArea;
        private APXGameSpeedView _gameSpeedView;
        private APXHandCardView _handCardView;
        private APXKnockbackView _knockbackView;
        private APXStaticPortraitView _staticPortraitView;
        private APXReplayView _replayView;
        private APXFreeCameraView _freeCameraView;
        private APXActionGuardView _actionGuardView;
        private APXBattleStatsView _battleStatsView;
        private APXAdvancedSettingsView _advancedSettingsView;
        private GameObject _comingSoonView;
        private Text _comingSoonText;

        private APXUIDragHandle _dragHandle;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<Image> _tabImages = new List<Image>();
        private readonly List<Text> _tabTexts = new List<Text>();
        private int _currentTabIndex = 0;

        private static readonly string[] TAB_NAMES = new string[]
        {
            "游戏变速",
            "手牌监视",
            "击飞预测",
            "静态立绘",
            "录像回放",
            "自由镜头",
            "自动化与社交",
            "对局数据",
            "高级设置"
        };

        internal static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<APXUICenter>() == null)
            {
                host.AddComponent<APXUICenter>();
                Debug.Log("[APX Plugin] 功能中心 (APXUICenter) 已成功初始化");
            }
            try
            {
                AssistBall.RegisterAction("apx_center", "星枢功能中心", Toggle, 10, "打开星枢玩家实用功能中心", "🛠", () => IsOpen);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[APX AssistBall] 注册功能中心动作异常: " + ex.Message);
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            BuildCenterCanvas();
            SetOpenState(false);
            APXOverlayInput.Register(this, () => _isOpen, () => new Rect(0f, 0f, Screen.width, Screen.height));
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                APXUIAssetService.Cleanup();
                Instance = null;
            }
        }

        private void Update()
        {
            // 全局快捷键 F1 集中管理切换呼出 (受 CentralInput 文本输入与焦点门禁控制)
            if (APXCentralInputManager.TryConsumeHotkey("APX.UICenter", KeyCode.F1))
            {
                Toggle();
            }

            if (_isOpen)
            {
                // 分辨率变化自适应 Clamp (0 GC / 仅变化时触发)
                if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height)
                {
                    _lastScreenWidth = Screen.width;
                    _lastScreenHeight = Screen.height;
                    if (_dragHandle != null)
                    {
                        _dragHandle.ClampPosition();
                    }
                }

                // 页面数据同步 (无变更 0 GC / 0 UI 写入)
                if (_currentTabIndex == 0 && _gameSpeedView != null)
                {
                    _gameSpeedView.UpdateSync();
                }
                else if (_currentTabIndex == 1 && _handCardView != null)
                {
                    _handCardView.UpdateSync();
                }
                else if (_currentTabIndex == 2 && _knockbackView != null)
                {
                    _knockbackView.UpdateSync();
                }
                else if (_currentTabIndex == 3 && _staticPortraitView != null)
                {
                    _staticPortraitView.UpdateSync();
                }
                else if (_currentTabIndex == 4 && _replayView != null)
                {
                    _replayView.UpdateSync();
                }
                else if (_currentTabIndex == 5 && _freeCameraView != null)
                {
                    _freeCameraView.UpdateSync();
                }
                else if (_currentTabIndex == 6 && _actionGuardView != null)
                {
                    _actionGuardView.UpdateSync();
                }
                else if (_currentTabIndex == 7 && _battleStatsView != null)
                {
                    _battleStatsView.UpdateSync();
                }
                else if (_currentTabIndex == 8 && _advancedSettingsView != null)
                {
                    _advancedSettingsView.UpdateSync();
                }
            }
        }

        internal static void Open()
        {
            if (Instance != null)
            {
                Instance.SetOpenState(true);
            }
        }

        internal static void OpenGameSpeedPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(0);
            }
        }

        internal static void ToggleGameSpeedPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                // CASE A: Center CLOSED -> OPEN and SELECT GameSpeed
                Instance.SetOpenState(true);
                Instance.SelectTab(0);
            }
            else if (Instance._currentTabIndex == 0)
            {
                // CASE B: Center OPEN && Current Page == GameSpeed -> CLOSE Center
                Instance.SetOpenState(false);
            }
            else
            {
                // CASE C: Center OPEN && Current Page != GameSpeed -> KEEP OPEN, SELECT GameSpeed
                Instance.SelectTab(0);
            }
        }

        internal static void OpenHandCardPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(1);
            }
        }

        internal static void ToggleHandCardPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                // CASE A: Center CLOSED -> OPEN and SELECT HandCard
                Instance.SetOpenState(true);
                Instance.SelectTab(1);
            }
            else if (Instance._currentTabIndex == 1)
            {
                // CASE B: Center OPEN && Current Page == HandCard -> CLOSE Center
                Instance.SetOpenState(false);
            }
            else
            {
                // CASE C: Center OPEN && Current Page != HandCard -> KEEP OPEN, SELECT HandCard
                Instance.SelectTab(1);
            }
        }

        internal static void OpenKnockbackPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(2);
            }
        }

        internal static void OpenStaticPortraitPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(3);
            }
        }

        internal static void ToggleStaticPortraitPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                // CASE A: Center CLOSED -> OPEN and SELECT StaticPortrait
                Instance.SetOpenState(true);
                Instance.SelectTab(3);
            }
            else if (Instance._currentTabIndex == 3)
            {
                // CASE B: Center OPEN && Current Page == StaticPortrait -> CLOSE Center
                Instance.SetOpenState(false);
            }
            else
            {
                // CASE C: Center OPEN && Current Page != StaticPortrait -> KEEP OPEN, SELECT StaticPortrait
                Instance.SelectTab(3);
            }
        }

        internal static void OpenReplayPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(4);
            }
        }

        internal static void ToggleReplayPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                // CASE A: Center CLOSED -> OPEN and SELECT Replay
                Instance.SetOpenState(true);
                Instance.SelectTab(4);
            }
            else if (Instance._currentTabIndex == 4)
            {
                // CASE B: Center OPEN && Current Page == Replay -> CLOSE Center
                Instance.SetOpenState(false);
            }
            else
            {
                // CASE C: Center OPEN && Current Page != Replay -> KEEP OPEN, SELECT Replay
                Instance.SelectTab(4);
            }
        }

        internal static void OpenFreeCameraPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(5);
            }
        }

        internal static void ToggleFreeCameraPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                // CASE A: Center CLOSED -> OPEN and SELECT FreeCamera
                Instance.SetOpenState(true);
                Instance.SelectTab(5);
            }
            else if (Instance._currentTabIndex == 5)
            {
                // CASE B: Center OPEN && Current Page == FreeCamera -> CLOSE Center
                Instance.SetOpenState(false);
            }
            else
            {
                // CASE C: Center OPEN && Current Page != FreeCamera -> KEEP OPEN, SELECT FreeCamera
                Instance.SelectTab(5);
            }
        }

        internal static void OpenActionGuardPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(6);
            }
        }

        internal static void ToggleActionGuardPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                // CASE A: Center CLOSED -> OPEN and SELECT ActionGuard
                Instance.SetOpenState(true);
                Instance.SelectTab(6);
            }
            else if (Instance._currentTabIndex == 6)
            {
                // CASE B: Center OPEN && Current Page == ActionGuard -> CLOSE Center
                Instance.SetOpenState(false);
            }
            else
            {
                // CASE C: Center OPEN && Current Page != ActionGuard -> KEEP OPEN, SELECT ActionGuard
                Instance.SelectTab(6);
            }
        }

        internal static void OpenBattleStatsPage()
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(7);
            }
        }

        internal static void ToggleBattleStatsPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                Instance.SetOpenState(true);
                Instance.SelectTab(7);
            }
            else if (Instance._currentTabIndex == 7)
            {
                Instance.SetOpenState(false);
            }
            else
            {
                Instance.SelectTab(7);
            }
        }

        internal static void OpenAdvancedSettings(string focusSection = null)
        {
            if (Instance != null)
            {
                if (!Instance._isOpen)
                {
                    Instance.SetOpenState(true);
                }
                Instance.SelectTab(8);
                if (Instance._advancedSettingsView != null && !string.IsNullOrEmpty(focusSection))
                {
                    Instance._advancedSettingsView.FocusSection(focusSection);
                }
            }
        }

        internal static void OpenAdvancedSettingsPage()
        {
            OpenAdvancedSettings(null);
        }

        internal static void ApplyUserScale(float scale)
        {
            if (Instance != null && Instance._mainFrameRt != null)
            {
                if (!APXMobileUIHelper.IsMobile)
                {
                    scale = APXMobileUIHelper.FiniteClamp(scale, APXConfigKeys.FunctionCenterDesktopScaleMin, APXConfigKeys.FunctionCenterDesktopScaleMax, APXConfigKeys.FunctionCenterDesktopScaleDefault);
                }
                else
                {
                    scale = Mathf.Clamp(scale, APXConfigKeys.MobileUserScaleMin, APXConfigKeys.MobileUserScaleMax);
                }
                Instance._mainFrameRt.localScale = Vector3.one * scale;
                if (Instance._dragHandle != null)
                {
                    Instance._dragHandle.ClampPosition();
                }
            }
        }

        internal static void ToggleAdvancedSettingsPage()
        {
            if (Instance == null) return;

            if (!Instance._isOpen)
            {
                Instance.SetOpenState(true);
                Instance.SelectTab(8);
            }
            else if (Instance._currentTabIndex == 8)
            {
                Instance.SetOpenState(false);
            }
            else
            {
                Instance.SelectTab(8);
            }
        }

        internal static void Close()
        {
            if (Instance != null)
            {
                Instance.SetOpenState(false);
            }
        }

        internal static void Toggle()
        {
            if (Instance != null)
            {
                Instance.SetOpenState(!Instance._isOpen);
            }
        }

        private void SetOpenState(bool open)
        {
            _isOpen = open;
            if (_canvas != null)
            {
                _canvas.enabled = open;
            }

            if (open)
            {
                APXCentralInputManager.EnsureEventSystemAvailable("APX.UICenter");
                SelectTab(_currentTabIndex);
                if (_dragHandle != null)
                {
                    _dragHandle.ClampPosition();
                }

                // 互斥门禁: 打开新功能中心时确保关闭旧功能助手菜单 (Simultaneous Visible: 0)
                try
                {
                    APXRuntime.Utilities.AssistBallGUI assistBall = UnityEngine.Object.FindObjectOfType<APXRuntime.Utilities.AssistBallGUI>();
                    if (assistBall != null)
                    {
                        assistBall.SetMenuOpen(false);
                    }
                }
                catch { }
            }
        }

        private void BuildCenterCanvas()
        {
            // 0. 核心生产资产运行时确定性自检
            APXUIAssetService.ValidateCoreAssets();

            // 1. Root Canvas (1920x1080 标准缩放)
            GameObject canvasGo = new GameObject("APX_UICenter_Canvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 28000;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            // 2. 全屏深色遮罩 (Backdrop: 40% 透明度拟态磨砂玻璃暗化，点击背景关闭)
            GameObject backdropGo = APXUIFactory.CreateUIObject("Backdrop", canvasGo);
            RectTransform backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;

            Image backdropImg = backdropGo.AddComponent<Image>();
            backdropImg.sprite = APXUIAssetService.WhiteSprite;
            backdropImg.color = new Color(0.03f, 0.04f, 0.06f, APXUITheme.AlphaBackdrop);
            backdropImg.type = Image.Type.Simple;

            Button backdropBtn = backdropGo.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(Close);

            // 3. 居中主面板容器 (1180 × 700 px) - R3.2 / R4-R3 规范架构
            GameObject mainFrame = APXUIFactory.CreateUIObject("MainFrame", canvasGo);
            _mainFrameRt = mainFrame.GetComponent<RectTransform>();
            _mainFrameRt.anchorMin = new Vector2(0.5f, 0.5f);
            _mainFrameRt.anchorMax = new Vector2(0.5f, 0.5f);
            _mainFrameRt.pivot = new Vector2(0.5f, 0.5f);
            _mainFrameRt.anchoredPosition = Vector2.zero;
            _mainFrameRt.sizeDelta = new Vector2(1180f, 700f);
            float userScale = APXConfigService.GetFloat(APXConfigKeys.MobileUserScale, APXConfigKeys.MobileUserScaleDefault);
            if (!APXMobileUIHelper.IsMobile)
            {
                float safeScale = APXMobileUIHelper.FiniteClamp(userScale, APXConfigKeys.FunctionCenterDesktopScaleMin, APXConfigKeys.FunctionCenterDesktopScaleMax, APXConfigKeys.FunctionCenterDesktopScaleDefault);
                if (safeScale != userScale)
                {
                    APXConfigService.SetFloat(APXConfigKeys.MobileUserScale, safeScale);
                    APXConfigService.Save();
                    userScale = safeScale;
                }
            }
            else
            {
                userScale = Mathf.Clamp(userScale, APXConfigKeys.MobileUserScaleMin, APXConfigKeys.MobileUserScaleMax);
            }
            _mainFrameRt.localScale = Vector3.one * userScale;

            // Layer 1: MainFrame 外层拟态磨砂玻璃圆角切片 (window_frame.png, 9-slice: 36,36,36,36, 圆角 20px)
            // 杜绝 90° 纯矩形保底层覆盖外轮廓圆角
            Sprite frameSprite = APXUIAssetService.GetSprite("window_frame.png");
            Image baseBg = mainFrame.AddComponent<Image>();
            baseBg.sprite = frameSprite;
            baseBg.type = Image.Type.Sliced;
            baseBg.color = APXUIAssetService.IsFallbackSprite(frameSprite) ? APXUITheme.PanelBase : Color.white;

            // 4. 顶部标题栏
            BuildTitleBar(mainFrame);

            // 5. 左侧导航侧栏 (宽度 225px)
            BuildSidebar(mainFrame);

            // 6. 纵向分割线
            BuildVerticalDivider(mainFrame);

            // 7. 右侧主内容展示区
            BuildContentArea(mainFrame);
        }

        private void BuildTitleBar(GameObject parent)
        {
            GameObject titleBarGo = APXUIFactory.CreateUIObject("TitleBar", parent);
            RectTransform tbRt = titleBarGo.GetComponent<RectTransform>();
            tbRt.anchorMin = new Vector2(0f, 1f);
            tbRt.anchorMax = new Vector2(1f, 1f);
            tbRt.pivot = new Vector2(0.5f, 1f);
            tbRt.anchoredPosition = Vector2.zero;
            tbRt.sizeDelta = new Vector2(0f, 64f);

            // 标题栏基底: header_bar.png 切片 (顶部左/右圆角 20px 与主窗体曲率严格一致，底部平整)
            // 杜绝 90° 矩形图层盖住窗体圆角
            Sprite headerSprite = APXUIAssetService.GetSprite("header_bar.png");
            Image tbBase = titleBarGo.AddComponent<Image>();
            tbBase.sprite = headerSprite;
            tbBase.type = Image.Type.Sliced;
            tbBase.color = APXUIAssetService.IsFallbackSprite(headerSprite) ? APXUITheme.Header : Color.white;
            tbBase.raycastTarget = true; // 作为 Header 拖拽手柄的主要受体

            // 挂载标题栏拖动手柄 (Header-only Drag Handle, 自动 Clamp)
            APXUIDragHandle dragHandle = titleBarGo.AddComponent<APXUIDragHandle>();
            dragHandle.targetRect = _mainFrameRt;
            dragHandle.parentRect = _canvas.transform as RectTransform;
            _dragHandle = dragHandle;

            // 主标题: 星枢 · 功能中心 (22px Bold, #F4F7FA, raycastTarget=false 允许穿透拖动)
            Text titleText = APXUIFactory.CreateText(titleBarGo, "TitleText", "星枢 · 功能中心", 22, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            titleText.raycastTarget = false;
            RectTransform ttRt = titleText.rectTransform;
            ttRt.anchorMin = new Vector2(0f, 0.5f);
            ttRt.anchorMax = new Vector2(0f, 0.5f);
            ttRt.pivot = new Vector2(0f, 0.5f);
            ttRt.anchoredPosition = new Vector2(32f, 0f);
            ttRt.sizeDelta = new Vector2(160f, 32f);

            // 副标题: 星枢实用功能与对局辅助 (13px, MutedText #7F8998, raycastTarget=false 允许穿透拖动)
            Text subText = APXUIFactory.CreateText(titleBarGo, "SubtitleText", "星枢实用功能与对局辅助", 13, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            subText.raycastTarget = false;
            RectTransform subRt = subText.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(202f, -1f);
            subRt.sizeDelta = new Vector2(240f, 24f);

            // 右侧信息: 星枢 v3.3.0 (弱化标签, raycastTarget=false, 与 [F1] 严格共轴)
            Text verText = APXUIFactory.CreateText(titleBarGo, "VersionText", "星枢 v3.3.0", 13, new Color(0.435f, 0.482f, 0.549f, 1f), TextAnchor.MiddleRight);
            verText.raycastTarget = false;
            RectTransform vtRt = verText.rectTransform;
            vtRt.anchorMin = new Vector2(1f, 0.5f);
            vtRt.anchorMax = new Vector2(1f, 0.5f);
            vtRt.pivot = new Vector2(1f, 0.5f);
            vtRt.anchoredPosition = new Vector2(-156f, 0f);
            vtRt.sizeDelta = new Vector2(100f, 26f);

            // 右侧快捷键徽章: [F1] (排他区: 徽章拦截点击，不触发窗口拖动)
            GameObject badgeGo = APXUIFactory.CreateUIObject("HotkeyBadge_F1", titleBarGo);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 0.5f);
            badgeRt.anchorMax = new Vector2(1f, 0.5f);
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.anchoredPosition = new Vector2(-96f, 0f);
            badgeRt.sizeDelta = new Vector2(50f, 26f);

            Image badgeImg = badgeGo.AddComponent<Image>();
            badgeImg.sprite = APXUIAssetService.GetSprite("hotkey_badge.png");
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = APXUIAssetService.IsFallbackSprite(badgeImg.sprite) ? new Color(0.15f, 0.18f, 0.24f, 0.85f) : Color.white;
            badgeImg.raycastTarget = true;

            Text badgeTxt = APXUIFactory.CreateText(badgeGo, "BadgeText", "[F1]", 12, new Color(0.557f, 0.612f, 0.682f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            badgeTxt.raycastTarget = false;
            RectTransform btRt = badgeTxt.rectTransform;
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.pivot = new Vector2(0.5f, 0.5f);
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;

            // 右侧关闭按钮 (40x40 实际可点击区域, 彻底消除拖拽冲突并确保 × 永远独立渲染)
            GameObject closeGo = APXUIFactory.CreateUIObject("Btn_Close", titleBarGo);
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 0.5f);
            closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot = new Vector2(1f, 0.5f);
            closeRt.anchoredPosition = new Vector2(-20f, 0f);
            closeRt.sizeDelta = new Vector2(40f, 40f);

            // 关闭按钮背景视觉 (34x34 居中圆角框)
            GameObject closeBgGo = APXUIFactory.CreateUIObject("Background", closeGo);
            RectTransform cbRt = closeBgGo.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(0.5f, 0.5f);
            cbRt.anchorMax = new Vector2(0.5f, 0.5f);
            cbRt.pivot = new Vector2(0.5f, 0.5f);
            cbRt.anchoredPosition = Vector2.zero;
            cbRt.sizeDelta = new Vector2(34f, 34f);

            Image closeImg = closeBgGo.AddComponent<Image>();
            closeImg.sprite = APXUIAssetService.GetSprite("close_normal.png");
            closeImg.type = Image.Type.Sliced;
            closeImg.color = APXUIAssetService.IsFallbackSprite(closeImg.sprite) ? new Color(0.12f, 0.15f, 0.20f, 0.85f) : Color.white;

            // 独立 X Glyph 子对象 (UGUI Text 显示 ×，几何与光学死中居中于 34x34 视觉框)
            Text closeTxt = APXUIFactory.CreateText(closeBgGo, "Glyph", "×", 22, APXUITheme.CloseGlyph, TextAnchor.MiddleCenter, FontStyle.Normal);
            closeTxt.raycastTarget = false;
            RectTransform ctRt = closeTxt.rectTransform;
            ctRt.anchorMin = Vector2.zero;
            ctRt.anchorMax = Vector2.one;
            ctRt.pivot = new Vector2(0.5f, 0.5f);
            ctRt.offsetMin = Vector2.zero;
            ctRt.offsetMax = Vector2.zero;

            Button closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            closeBtn.transition = Selectable.Transition.SpriteSwap;
            SpriteState ss = new SpriteState();
            ss.highlightedSprite = APXUIAssetService.GetSprite("close_hover.png");
            ss.pressedSprite = APXUIAssetService.GetSprite("close_hover.png");
            closeBtn.spriteState = ss;
            closeBtn.onClick.AddListener(Close);

            // 显式排他区注册 (F1 快捷键徽章与关闭按钮严格禁止拖拽窗口)
            _dragHandle.SetExcludedRects(badgeRt, closeRt);
        }

        private void BuildSidebar(GameObject parent)
        {
            GameObject sidebarGo = APXUIFactory.CreateUIObject("Sidebar", parent);
            RectTransform sbRt = sidebarGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(0f, 0f);
            sbRt.anchorMax = new Vector2(0f, 1f);
            sbRt.pivot = new Vector2(0f, 0.5f);
            sbRt.anchoredPosition = new Vector2(20f, -32f);
            sbRt.sizeDelta = new Vector2(225f, -96f);

            VerticalLayoutGroup vlg = sidebarGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 4, 4);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            Sprite normalTabSprite = APXUIAssetService.GetSprite("sidebar_tab_normal.png");
            Sprite selectedTabSprite = APXUIAssetService.GetSprite("sidebar_tab_selected.png");
            Sprite hoverTabSprite = APXUIAssetService.GetSprite("sidebar_tab_hover.png");

            for (int i = 0; i < TAB_NAMES.Length; i++)
            {
                int tabIndex = i;
                string tabName = TAB_NAMES[i];
                bool isSelected = (tabIndex == 0);

                GameObject tabGo = APXUIFactory.CreateUIObject("Tab_" + tabIndex, sidebarGo);
                LayoutElement le = tabGo.AddComponent<LayoutElement>();
                le.minHeight = 50f;
                le.preferredHeight = 50f;
                le.flexibleHeight = 0f;

                Image tabImg = tabGo.AddComponent<Image>();
                tabImg.sprite = isSelected ? selectedTabSprite : normalTabSprite;
                tabImg.type = Image.Type.Sliced;
                tabImg.color = APXUIAssetService.IsFallbackSprite(tabImg.sprite) ? (isSelected ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.08f, 0.10f, 0.14f, 0.5f)) : Color.white;

                Text tabTxt = APXUIFactory.CreateText(tabGo, "Text", tabName, isSelected ? 16 : 15,
                    isSelected ? APXUITheme.PrimaryText : APXUITheme.SecondaryText,
                    TextAnchor.MiddleLeft,
                    isSelected ? FontStyle.Bold : FontStyle.Normal);

                RectTransform txtRt = tabTxt.rectTransform;
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(24f, 0f);
                txtRt.offsetMax = new Vector2(-12f, 0f);

                Button btn = tabGo.AddComponent<Button>();
                btn.targetGraphic = tabImg;
                btn.transition = Selectable.Transition.SpriteSwap;
                SpriteState tabSs = new SpriteState();
                tabSs.highlightedSprite = isSelected ? selectedTabSprite : hoverTabSprite;
                tabSs.pressedSprite = isSelected ? selectedTabSprite : hoverTabSprite;
                btn.spriteState = tabSs;
                btn.onClick.AddListener(delegate { SelectTab(tabIndex); });

                _tabButtons.Add(btn);
                _tabImages.Add(tabImg);
                _tabTexts.Add(tabTxt);
            }
        }

        private void BuildVerticalDivider(GameObject parent)
        {
            GameObject divGo = APXUIFactory.CreateUIObject("VerticalDivider", parent);
            RectTransform divRt = divGo.GetComponent<RectTransform>();
            divRt.anchorMin = new Vector2(0f, 0f);
            divRt.anchorMax = new Vector2(0f, 1f);
            divRt.pivot = new Vector2(0.5f, 0.5f);
            divRt.anchoredPosition = new Vector2(260f, -40f);
            divRt.sizeDelta = new Vector2(1f, -120f); // 上下各留 18~20px Inset，不顶到外框圆角

            Image divImg = divGo.AddComponent<Image>();
            divImg.sprite = APXUIAssetService.WhiteSprite;
            divImg.color = APXUITheme.DividerColor;
        }

        private void BuildContentArea(GameObject parent)
        {
            _contentArea = APXUIFactory.CreateUIObject("ContentArea", parent);
            RectTransform caRt = _contentArea.GetComponent<RectTransform>();
            caRt.anchorMin = new Vector2(0f, 0f);
            caRt.anchorMax = new Vector2(1f, 1f);
            caRt.pivot = new Vector2(0f, 1f);
            caRt.anchoredPosition = new Vector2(275f, -80f);
            caRt.sizeDelta = new Vector2(-300f, -104f);

            // 1. 游戏加速页面 (Active Feature - Tab 0)
            _gameSpeedView = new APXGameSpeedView(_contentArea.transform);

            // 2. 局内手牌监视页面 (Active Feature - Tab 1)
            _handCardView = new APXHandCardView(_contentArea.transform);
            _handCardView.Root.SetActive(false);

            // 3. 击飞预测页面 (Active Feature - Tab 2)
            _knockbackView = new APXKnockbackView(_contentArea.transform);
            _knockbackView.Root.SetActive(false);

            // 4. 静态立绘页面 (Active Feature - Tab 3)
            _staticPortraitView = new APXStaticPortraitView(_contentArea.transform);
            _staticPortraitView.Root.SetActive(false);

            // 4. 录像回放页面 (Active Feature - Tab 4)
            _replayView = new APXReplayView(_contentArea.transform);
            _replayView.Root.SetActive(false);

            // 5. 自由镜头页面 (Active Feature - Tab 5)
            _freeCameraView = new APXFreeCameraView(_contentArea.transform);
            _freeCameraView.Root.SetActive(false);

            // 6. 自动化与社交 (战局守护) 页面 (Active Feature - Tab 6)
            _actionGuardView = new APXActionGuardView(_contentArea.transform);
            _actionGuardView.Root.SetActive(false);

            // 7. 对局数据页面 (Active Feature - Tab 7)
            _battleStatsView = new APXBattleStatsView(_contentArea.transform);
            _battleStatsView.Root.SetActive(false);

            // 8. 高级设置页面 (Active Feature - Tab 8)
            _advancedSettingsView = new APXAdvancedSettingsView(_contentArea.transform);
            _advancedSettingsView.Root.SetActive(false);

            // 占位提示页面 (Coming Soon)
            _comingSoonView = APXUIFactory.CreateUIObject("Page_ComingSoon", _contentArea);
            RectTransform csRt = _comingSoonView.GetComponent<RectTransform>();
            csRt.anchorMin = Vector2.zero;
            csRt.anchorMax = Vector2.one;
            csRt.offsetMin = Vector2.zero;
            csRt.offsetMax = Vector2.zero;

            // 拟态磨砂玻璃卡片切片 (card_base.png, 圆角 12px)
            Sprite csSprite = APXUIAssetService.GetSprite("card_base.png");
            Image csBase = _comingSoonView.AddComponent<Image>();
            csBase.sprite = csSprite;
            csBase.type = Image.Type.Sliced;
            csBase.color = APXUIAssetService.IsFallbackSprite(csSprite) ? APXUITheme.Card : Color.white;

            _comingSoonText = APXUIFactory.CreateText(_comingSoonView, "ComingSoonText", "功能正在开发与适配中", 16, APXUITheme.SecondaryText, TextAnchor.MiddleCenter);
            RectTransform cstRt = _comingSoonText.rectTransform;
            cstRt.anchorMin = Vector2.zero;
            cstRt.anchorMax = Vector2.one;
            cstRt.offsetMin = new Vector2(30f, 30f);
            cstRt.offsetMax = new Vector2(-30f, -30f);

            _comingSoonView.SetActive(false);
        }

        private void SelectTab(int index)
        {
            _currentTabIndex = index;
            Sprite normalTabSprite = APXUIAssetService.GetSprite("sidebar_tab_normal.png");
            Sprite selectedTabSprite = APXUIAssetService.GetSprite("sidebar_tab_selected.png");
            Sprite hoverTabSprite = APXUIAssetService.GetSprite("sidebar_tab_hover.png");

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool isSelected = (i == index);
                if (i < _tabImages.Count && _tabImages[i] != null)
                {
                    _tabImages[i].sprite = isSelected ? selectedTabSprite : normalTabSprite;
                    _tabImages[i].color = APXUIAssetService.IsFallbackSprite(_tabImages[i].sprite) ? (isSelected ? new Color(0.12f, 0.22f, 0.20f, 0.85f) : new Color(0.08f, 0.10f, 0.14f, 0.5f)) : Color.white;
                }
                if (i < _tabButtons.Count && _tabButtons[i] != null)
                {
                    SpriteState tabSs = new SpriteState();
                    tabSs.highlightedSprite = isSelected ? selectedTabSprite : hoverTabSprite;
                    tabSs.pressedSprite = isSelected ? selectedTabSprite : hoverTabSprite;
                    _tabButtons[i].spriteState = tabSs;
                }
                if (i < _tabTexts.Count && _tabTexts[i] != null)
                {
                    _tabTexts[i].color = isSelected ? APXUITheme.PrimaryText : APXUITheme.SecondaryText;
                    _tabTexts[i].fontSize = isSelected ? 16 : 15;
                    _tabTexts[i].fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            if (_gameSpeedView != null) _gameSpeedView.Root.SetActive(index == 0);
            if (_handCardView != null) _handCardView.Root.SetActive(index == 1);
            if (_knockbackView != null) _knockbackView.Root.SetActive(index == 2);
            if (_staticPortraitView != null) _staticPortraitView.Root.SetActive(index == 3);
            if (_replayView != null) _replayView.Root.SetActive(index == 4);
            if (_freeCameraView != null) _freeCameraView.Root.SetActive(index == 5);
            if (_actionGuardView != null) _actionGuardView.Root.SetActive(index == 6);
            if (_battleStatsView != null)
            {
                _battleStatsView.Root.SetActive(index == 7);
                if (index == 7)
                {
                    _battleStatsView.UpdateSync();
                }
            }
            if (_advancedSettingsView != null)
            {
                _advancedSettingsView.Root.SetActive(index == 8);
                if (index == 8)
                {
                    _advancedSettingsView.UpdateSync();
                }
            }

            if (_comingSoonView != null)
            {
                bool isComingSoon = index < 0 || index >= TAB_NAMES.Length;
                _comingSoonView.SetActive(isComingSoon);
                if (isComingSoon && _comingSoonText != null)
                {
                    _comingSoonText.text = $"「{TAB_NAMES[Mathf.Clamp(index, 0, TAB_NAMES.Length - 1)]}」正在进行原版化适配，将在后续阶段上线\n(当前底层功能正常，可通过快捷键独立运行)";
                }
            }
        }
    }
}
