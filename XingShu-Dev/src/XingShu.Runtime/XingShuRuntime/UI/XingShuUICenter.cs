using System;
using System.Collections.Generic;
using XingShuRuntime.Core;
using XingShuRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    /// <summary>
    /// 功能中心主控制器 (Phase 2B-R4 生产整合版)
    /// 参照游戏设置页的独立导航、深色主框和白色设置卡片构建。
    /// </summary>
    [DefaultExecutionOrder(-9900)]
    internal sealed class XingShuUICenter : MonoBehaviour
    {
        internal static XingShuUICenter Instance { get; private set; }

        internal static bool IsOpen => Instance != null && Instance._isOpen;

        private bool _isOpen;
        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _mainFrameRt;
        private Text _pageTitle;
        private GameObject _mobileNavigation;
        private float _preferredScale = 1f;

        private GameObject _contentArea;
        private XingShuGameSpeedView _gameSpeedView;
        private XingShuHandCardView _handCardView;
        private XingShuKnockbackView _knockbackView;
        private XingShuStaticPortraitView _staticPortraitView;
        private XingShuReplayView _replayView;
        private XingShuFreeCameraView _freeCameraView;
        private XingShuActionGuardView _actionGuardView;
        private XingShuBattleStatsView _battleStatsView;
        private XingShuAdvancedSettingsView _advancedSettingsView;
        private GameObject _comingSoonView;
        private Text _comingSoonText;

        private XingShuUIDragHandle _dragHandle;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Rect _lastSafeScreenRect;

        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<Image> _tabImages = new List<Image>();
        private readonly List<Text> _tabTexts = new List<Text>();
        private int _currentTabIndex = 0;

        private static readonly string[] TAB_NAMES = new string[]
        {
            "游戏变速",
            "手牌监视",
            "击飞预测",
            "模组加载",
            "录像回放",
            "自由镜头",
            "自动化与社交",
            "对局数据",
            "高级设置"
        };

        internal static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<XingShuUICenter>() == null)
            {
                host.AddComponent<XingShuUICenter>();
                Debug.Log("[XingShu Plugin] 功能中心 (XingShuUICenter) 已成功初始化");
            }
            try
            {
                AssistBall.RegisterAction("xingshu_center", "星枢功能中心", Toggle, 10, "打开星枢玩家实用功能中心", "🛠", () => IsOpen);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu AssistBall] 注册功能中心动作异常: " + ex.Message);
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
            XingShuOverlayInput.Register(this, () => _isOpen, () => XingShuMobileUIHelper.IsMobile
                ? XingShuOverlayInput.ScreenRect(_mainFrameRt) : new Rect(0f, 0f, Screen.width, Screen.height));
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                XingShuUIAssetService.Cleanup();
                Instance = null;
            }
        }

        private void Update()
        {
            // 全局快捷键 F1 集中管理切换呼出 (受 CentralInput 文本输入与焦点门禁控制)
            if (XingShuCentralInputManager.TryConsumeHotkey("XingShu.UICenter", KeyCode.F1))
            {
                Toggle();
            }

            if (_isOpen)
            {
                // 分辨率变化自适应 Clamp (0 GC / 仅变化时触发)
                if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height || _lastSafeScreenRect != XingShuMobileUIHelper.SafeScreenRect)
                {
                    _lastScreenWidth = Screen.width;
                    _lastScreenHeight = Screen.height;
                    _lastSafeScreenRect = XingShuMobileUIHelper.SafeScreenRect;
                    FitPanelToScreen();
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
                if (!XingShuMobileUIHelper.IsMobile)
                {
                    scale = XingShuMobileUIHelper.FiniteClamp(scale, XingShuConfigKeys.FunctionCenterDesktopScaleMin, XingShuConfigKeys.FunctionCenterDesktopScaleMax, XingShuConfigKeys.FunctionCenterDesktopScaleDefault);
                }
                else
                {
                    scale = Mathf.Clamp(scale, XingShuConfigKeys.MobileUserScaleMin, XingShuConfigKeys.MobileUserScaleMax);
                }
                Instance._preferredScale = scale;
                Instance.FitPanelToScreen();
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
                XingShuUITheme.RefreshFonts(_canvas != null ? _canvas.gameObject : null);
                XingShuCentralInputManager.EnsureEventSystemAvailable("XingShu.UICenter");
                SelectTab(_currentTabIndex);
                if (_dragHandle != null)
                {
                    _dragHandle.ClampPosition();
                }

                // 互斥门禁: 打开新功能中心时确保关闭旧功能助手菜单 (Simultaneous Visible: 0)
                try
                {
                    XingShuRuntime.Utilities.AssistBallGUI assistBall = UnityEngine.Object.FindObjectOfType<XingShuRuntime.Utilities.AssistBallGUI>();
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
            XingShuUIAssetService.ValidateCoreAssets();

            // 1. Root Canvas (1920x1080 标准缩放)
            GameObject canvasGo = new GameObject("XINGSHU_UICenter_Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 31000;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (XingShuMobileUIHelper.IsMobile)
            {
                scaler.referenceResolution = new Vector2(960, XingShuMobilePageLayout.ReferenceHeight);
                scaler.matchWidthOrHeight = 1f;
            }

            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            // 2. 轻量遮罩，点击空白处收起
            GameObject backdropGo = XingShuUIFactory.CreateUIObject("Backdrop", canvasGo);
            RectTransform backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;

            Image backdropImg = backdropGo.AddComponent<Image>();
            backdropImg.sprite = XingShuUIAssetService.WhiteSprite;
            backdropImg.color = new Color(0.03f, 0.04f, 0.06f, XingShuUITheme.AlphaBackdrop);
            backdropImg.type = Image.Type.Simple;

            Button backdropBtn = backdropGo.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(Close);
            if (XingShuMobileUIHelper.IsMobile) backdropGo.SetActive(false);

            // 3. 独立导航与设置主体组成的居中框架 (1440 × 860)
            GameObject mainFrame = XingShuUIFactory.CreateUIObject("MainFrame", canvasGo);
            _mainFrameRt = mainFrame.GetComponent<RectTransform>();
            _mainFrameRt.anchorMin = new Vector2(0.5f, 0.5f);
            _mainFrameRt.anchorMax = new Vector2(0.5f, 0.5f);
            _mainFrameRt.pivot = new Vector2(0.5f, 0.5f);
            _mainFrameRt.anchoredPosition = Vector2.zero;
            _mainFrameRt.sizeDelta = new Vector2(1440f, 860f);
            if (XingShuMobileUIHelper.IsMobile)
            {
                BuildMobileCenter(mainFrame);
                FitPanelToScreen();
                return;
            }
            float userScale = XingShuConfigService.GetFloat(XingShuConfigKeys.MobileUserScale, XingShuConfigKeys.MobileUserScaleDefault);
            if (!XingShuMobileUIHelper.IsMobile)
            {
                float safeScale = XingShuMobileUIHelper.FiniteClamp(userScale, XingShuConfigKeys.FunctionCenterDesktopScaleMin, XingShuConfigKeys.FunctionCenterDesktopScaleMax, XingShuConfigKeys.FunctionCenterDesktopScaleDefault);
                if (safeScale != userScale)
                {
                    XingShuConfigService.SetFloat(XingShuConfigKeys.MobileUserScale, safeScale);
                    XingShuConfigService.Save();
                    userScale = safeScale;
                }
            }
            else
            {
                userScale = Mathf.Clamp(userScale, XingShuConfigKeys.MobileUserScaleMin, XingShuConfigKeys.MobileUserScaleMax);
            }
            _preferredScale = userScale;
            FitPanelToScreen();

            // 深色圆角主体使用切片纹理，导航栏保持独立。
            Sprite frameSprite = XingShuUIAssetService.GetSprite("window_frame.png");
            GameObject body = XingShuUIFactory.CreateUIObject("GameSettingsBody", mainFrame);
            RectTransform bodyRt = body.GetComponent<RectTransform>();
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.offsetMin = new Vector2(248f, 0f);
            bodyRt.offsetMax = Vector2.zero;
            Image baseBg = body.AddComponent<Image>();
            baseBg.sprite = frameSprite;
            baseBg.type = Image.Type.Sliced;
            baseBg.color = XingShuUIAssetService.IsFallbackSprite(frameSprite) ? XingShuUITheme.PanelBase : Color.white;
            XingShuUIFactory.AddShadow(body, new Color(0f, 0f, 0f, 0.24f), new Vector2(0f, -7f));

            // 4. 顶部标题栏
            BuildTitleBar(mainFrame);

            // 5. 左侧导航侧栏 (宽度 225px)
            BuildSidebar(mainFrame);

            // 6. 纵向分割线
            BuildVerticalDivider(mainFrame);

            // 7. 右侧主内容展示区
            BuildContentArea(mainFrame);
        }

        private void FitPanelToScreen()
        {
            if (_mainFrameRt == null || Screen.width <= 0 || Screen.height <= 0) return;
            if (XingShuMobileUIHelper.IsMobile)
            {
                float scale = Screen.height / XingShuMobilePageLayout.ReferenceHeight;
                Rect mobileSafe = XingShuMobileUIHelper.SafeScreenRect;
                _mainFrameRt.localScale = Vector3.one;
                float ratio = XingShuMobilePageLayout.PanelScale;
                float width = Mathf.Min(mobileSafe.width / scale - 16, Mathf.Max(360, (mobileSafe.width / scale * .5f - 12) * ratio));
                float height = Mathf.Min(mobileSafe.height / scale - 16, Mathf.Max(300, mobileSafe.height / scale * .90f * ratio));
                _mainFrameRt.sizeDelta = new Vector2(width, height);
                float x = XingShuMobileUIHelper.FiniteClamp(XingShuConfigService.GetFloat(XingShuConfigKeys.MobilePanelX, .70f), 0, 1, .70f);
                float y = XingShuMobileUIHelper.FiniteClamp(XingShuConfigService.GetFloat(XingShuConfigKeys.MobilePanelY, .50f), 0, 1, .50f);
                Vector2 half = new Vector2(width, height) * scale * .5f;
                Vector2 center = new Vector2(
                    Mathf.Clamp(mobileSafe.xMin + x * mobileSafe.width, mobileSafe.xMin + half.x, mobileSafe.xMax - half.x),
                    Mathf.Clamp(mobileSafe.yMin + y * mobileSafe.height, mobileSafe.yMin + half.y, mobileSafe.yMax - half.y));
                _mainFrameRt.anchoredPosition = (center - new Vector2(Screen.width, Screen.height) * .5f) / scale;
                if (_mobileNavigation != null)
                {
                    var drawer = _mobileNavigation.transform.Find("Drawer") as RectTransform;
                    if (drawer != null) drawer.offsetMax = new Vector2(Mathf.Min(360, width - 24), 0);
                }
                return;
            }
            // Match the CanvasScaler, then fit the entire frame with a small screen margin.
            float canvasScale = Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
            Rect safe = XingShuMobileUIHelper.SafeScreenRect;
            float fit = Mathf.Min((safe.width / canvasScale - 48f) / 1440f,
                                  (safe.height / canvasScale - 48f) / 860f);
            _mainFrameRt.localScale = Vector3.one * Mathf.Max(.05f, Mathf.Min(_preferredScale, fit));
            if (XingShuMobileUIHelper.IsMobile && _dragHandle == null)
                _mainFrameRt.anchoredPosition = (safe.center - new Vector2(Screen.width, Screen.height) * .5f) / canvasScale;
        }

        private static RectTransform Place(GameObject go, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        internal static void RefreshMobileText()
        {
            if (Instance != null && Instance._canvas != null)
                XingShuMobilePageLayout.RefreshAll(Instance._canvas.gameObject);
        }

        internal static void RefreshMobileWindow()
        {
            if (Instance == null || !XingShuMobileUIHelper.IsMobile) return;
            Instance.FitPanelToScreen();
            Instance._dragHandle?.ClampPosition();
        }

        private void SaveMobilePosition()
        {
            Rect safe = XingShuMobileUIHelper.SafeScreenRect;
            if (safe.width <= 0 || safe.height <= 0) return;
            float scale = Screen.height / XingShuMobilePageLayout.ReferenceHeight;
            Vector2 center = _mainFrameRt.anchoredPosition * scale + new Vector2(Screen.width, Screen.height) * .5f;
            XingShuConfigService.SetFloat(XingShuConfigKeys.MobilePanelX, Mathf.Clamp01((center.x - safe.xMin) / safe.width));
            XingShuConfigService.SetFloat(XingShuConfigKeys.MobilePanelY, Mathf.Clamp01((center.y - safe.yMin) / safe.height));
            XingShuConfigService.Save();
        }

        private void BuildMobileCenter(GameObject frame)
        {
            Sprite frameSprite = XingShuUIAssetService.GetSprite("window_frame.png");
            var bg = frame.AddComponent<Image>(); bg.sprite = frameSprite; bg.type = Image.Type.Sliced;
            bg.color = XingShuUIAssetService.IsFallbackSprite(frameSprite) ? XingShuUITheme.PanelBase : Color.white;
            var titleBar = XingShuUIFactory.CreateUIObject("MobileDragBar", frame);
            Place(titleBar, Vector2.up, Vector2.one, Vector2.up, new Vector2(128, -8), new Vector2(-256, 64));
            titleBar.AddComponent<Image>().color = new Color(0, 0, 0, .01f);
            _dragHandle = titleBar.AddComponent<XingShuUIDragHandle>();
            _dragHandle.targetRect = _mainFrameRt;
            _dragHandle.parentRect = _canvas.transform as RectTransform;
            _dragHandle.onDragFinished = SaveMobilePosition;
            _dragHandle.BindEvents();
            var menu = XingShuUIFactory.CreateButton(frame, "MobileMenu", "功能菜单",
                XingShuUIAssetService.GetSprite("button_primary.png"), Color.white, Color.white,
                () => _mobileNavigation.SetActive(!_mobileNavigation.activeSelf), 22);
            Place(menu.gameObject, Vector2.up, Vector2.up, Vector2.up, new Vector2(12, -8), new Vector2(112, 64));
            _pageTitle = XingShuUIFactory.CreateText(titleBar, "MobileTitle", TAB_NAMES[0], 24,
                Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            XingShuDetailLayout.Fill(_pageTitle.rectTransform, 6, 0, 6, 0);
            _pageTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            var close = XingShuUIFactory.CreateButton(frame, "MobileClose", "返回",
                XingShuUIAssetService.GetSprite("button_secondary.png"), Color.white, XingShuUITheme.PrimaryText, Close, 22);
            Place(close.gameObject, Vector2.one, Vector2.one, Vector2.one, new Vector2(-12, -8), new Vector2(112, 64));
            BuildContentArea(frame);
            foreach (Transform page in _contentArea.transform)
                if (page.gameObject != _comingSoonView) XingShuMobilePageLayout.Adapt(page.gameObject);
            var footer = XingShuUIFactory.CreateText(frame, "MobileFooter", "拖动顶部移动 · 上下滑动查看设置", 18,
                new Color(.8f, .78f, .86f), TextAnchor.MiddleCenter);
            Place(footer.gameObject, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(12, 4), new Vector2(-24, 28));

            // Drawer is last in sibling order and blocks the content underneath.
            _mobileNavigation = XingShuUIFactory.CreateUIObject("MobileNavigation", frame);
            XingShuDetailLayout.Fill(_mobileNavigation.GetComponent<RectTransform>());
            var shade = _mobileNavigation.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .7f);
            var dismiss = _mobileNavigation.AddComponent<Button>(); dismiss.targetGraphic = shade;
            dismiss.onClick.AddListener(() => _mobileNavigation.SetActive(false));
            var drawer = XingShuUIFactory.CreateUIObject("Drawer", _mobileNavigation);
            var dr = drawer.GetComponent<RectTransform>();
            dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.up; dr.pivot = Vector2.zero;
            dr.offsetMin = Vector2.zero; dr.offsetMax = new Vector2(360, 0);
            var drawerBg = drawer.AddComponent<Image>(); drawerBg.color = new Color(.1f, .08f, .16f, 1f);
            var scroll = XingShuUIFactory.CreateVerticalScrollView(drawer, "MobileFunctions", out GameObject nav);
            var flow = nav.GetComponent<VerticalLayoutGroup>();
            flow.spacing = 12; flow.padding = new RectOffset(16, 16, 16, 24);
            var caption = XingShuUIFactory.CreateText(nav, "Choose", "选择功能", 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            caption.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            for (int i = 0; i < TAB_NAMES.Length; i++)
            {
                int index = i;
                var button = XingShuUIFactory.CreateButton(nav, "MobileTab_" + i, TAB_NAMES[i],
                    XingShuUIAssetService.GetSprite("sidebar_tab_normal.png"), Color.white, Color.white, () => SelectTab(index), 24);
                var element = button.gameObject.AddComponent<LayoutElement>();
                element.minHeight = element.preferredHeight = 68;
                _tabButtons.Add(button); _tabImages.Add(button.GetComponent<Image>()); _tabTexts.Add(button.GetComponentInChildren<Text>());
            }
            _mobileNavigation.SetActive(false);
        }

        private void BuildTitleBar(GameObject parent)
        {
            // A separate return tab and content header follow the game's settings layout.
            Button back = XingShuUIFactory.CreateButton(parent, "ReturnToGame", "‹  返回", XingShuUIAssetService.GetSprite("button_primary.png"),
                Color.white, Color.white, Close, 26);
            RectTransform backRt = Place(back.gameObject, Vector2.up, Vector2.up, Vector2.up, new Vector2(12f, -12f), new Vector2(206f, 68f));
            XingShuUIFactory.AddOutline(back.GetComponentInChildren<Text>().gameObject, new Color(0.12f,0.08f,0.18f,1f), new Vector2(2f,-2f));

            GameObject titleBar = XingShuUIFactory.CreateUIObject("TitleBar", parent);
            RectTransform titleRt = Place(titleBar, Vector2.up, Vector2.one, Vector2.up, new Vector2(272f, -20f), new Vector2(-296f, 68f));
            Image hitArea = titleBar.AddComponent<Image>();
            hitArea.sprite = XingShuUIAssetService.WhiteSprite;
            // A zero-alpha Graphic is culled by Unity and cannot receive the drag gesture.
            hitArea.color = new Color(0f, 0f, 0f, 0.01f);
            hitArea.raycastTarget = true;
            _dragHandle = titleBar.AddComponent<XingShuUIDragHandle>();
            _dragHandle.targetRect = _mainFrameRt;
            _dragHandle.parentRect = _canvas.transform as RectTransform;

            Image chip = XingShuUIFactory.CreateImage(titleBar, "PageTitleChip", XingShuUIAssetService.GetSprite("button_selected.png"), Color.white, Image.Type.Sliced);
            Place(chip.gameObject, Vector2.zero, Vector2.up, new Vector2(0f,0.5f), new Vector2(0f,0f), new Vector2(288f,0f));
            chip.raycastTarget = false;
            _pageTitle = XingShuUIFactory.CreateText(chip.gameObject, "PageTitle", TAB_NAMES[0], 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform pt = _pageTitle.rectTransform;
            pt.anchorMin = Vector2.zero; pt.anchorMax = Vector2.one; pt.offsetMin = Vector2.zero; pt.offsetMax = Vector2.zero;
            _pageTitle.raycastTarget = false;
            XingShuUIFactory.AddOutline(_pageTitle.gameObject, new Color(0.10f,0.08f,0.13f,1f), new Vector2(2f,-2f));

            Text brand = XingShuUIFactory.CreateText(titleBar, "Brand", "星枢 · 功能中心", 19, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(brand.gameObject, Vector2.zero, Vector2.up, new Vector2(0f,0.5f), new Vector2(312f,0f), new Vector2(230f,0f));
            brand.raycastTarget = false;
            Text hotkey = XingShuUIFactory.CreateText(titleBar, "Hotkey", XingShuMobileUIHelper.IsMobile ? "拖动顶部移动面板" : "F1 打开 / 收起", 14, new Color(0.82f,0.80f,0.87f), TextAnchor.MiddleRight);
            Place(hotkey.gameObject, Vector2.right, Vector2.one, new Vector2(1f,0.5f), new Vector2(-72f,0f), new Vector2(220f,0f));
            hotkey.raycastTarget = false;
            Button close = XingShuUIFactory.CreateButton(titleBar, "Close", "×", XingShuUIAssetService.GetSprite("close_normal.png"), Color.white, Color.white, Close, 30);
            RectTransform closeRt = Place(close.gameObject, new Vector2(1f,0.5f), new Vector2(1f,0.5f), new Vector2(1f,0.5f), Vector2.zero, new Vector2(52f,52f));
            _dragHandle.SetExcludedRects(closeRt, backRt);
            _dragHandle.BindEvents();

            Text footer = XingShuUIFactory.CreateText(parent, "ProjectSource", "项目来源  github.com/Breezecaresses/Xingshu-hub", 13, new Color(0.82f,0.80f,0.87f), TextAnchor.MiddleLeft);
            Place(footer.gameObject, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(282f,12f), new Vector2(-540f,24f));
            footer.raycastTarget = false;
            Text version = XingShuUIFactory.CreateText(parent, "RuntimeVersion", XingShu.Runtime.XingShuRuntimeHost.RuntimeVersion, 12, new Color(0.82f,0.80f,0.87f), TextAnchor.MiddleRight);
            Place(version.gameObject, Vector2.right, Vector2.right, Vector2.right, new Vector2(-30f,12f), new Vector2(260f,24f));
            version.raycastTarget = false;
        }

        private void BuildSidebar(GameObject parent)
        {
            Image rail = XingShuUIFactory.CreateImage(parent, "NavigationRail", XingShuUIAssetService.GetSprite("card_base.png"), Color.white, Image.Type.Sliced);
            RectTransform railRt = Place(rail.gameObject, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(10f,42f), new Vector2(204f,-146f));
            rail.raycastTarget = false;
            Image line = XingShuUIFactory.CreateImage(rail.gameObject, "YellowRail", null, XingShuUITheme.Yellow);
            RectTransform lineRt = line.rectTransform;
            lineRt.anchorMin = new Vector2(0.19f,0f); lineRt.anchorMax = new Vector2(0.19f,1f);
            lineRt.offsetMin = new Vector2(-4f,20f); lineRt.offsetMax = new Vector2(4f,-20f);
            line.raycastTarget = false;

            GameObject sidebar = XingShuUIFactory.CreateUIObject("Sidebar", parent);
            Place(sidebar, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(0f,58f), new Vector2(230f,-170f));
            VerticalLayoutGroup layout = sidebar.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0,0,8,8);
            layout.spacing = 12f; layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            Sprite normal = XingShuUIAssetService.GetSprite("sidebar_tab_normal.png");
            Sprite selected = XingShuUIAssetService.GetSprite("sidebar_tab_selected.png");
            Sprite hover = XingShuUIAssetService.GetSprite("sidebar_tab_hover.png");
            for (int i=0;i<TAB_NAMES.Length;i++)
            {
                int index = i;
                GameObject tab = XingShuUIFactory.CreateUIObject("Tab_"+index, sidebar);
                LayoutElement le = tab.AddComponent<LayoutElement>();
                le.minHeight = 62f; le.preferredHeight = 62f; le.flexibleHeight = 0f;
                Image image = tab.AddComponent<Image>();
                image.sprite = i==0 ? selected : normal; image.type = Image.Type.Sliced; image.color = Color.white;
                Text text = XingShuUIFactory.CreateText(tab,"Text",TAB_NAMES[i],21,Color.white,TextAnchor.MiddleLeft,FontStyle.Bold);
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(54f,0f); text.rectTransform.offsetMax = new Vector2(-8f,0f);
                text.raycastTarget = false;
                XingShuUIFactory.AddOutline(text.gameObject,new Color(0.10f,0.08f,0.13f,1f),new Vector2(1.8f,-1.8f));
                Button button = tab.AddComponent<Button>(); button.targetGraphic = image;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState { highlightedSprite=hover, pressedSprite=selected };
                button.onClick.AddListener(delegate { SelectTab(index); });
                _tabButtons.Add(button); _tabImages.Add(image); _tabTexts.Add(text);
            }
        }

        private void BuildVerticalDivider(GameObject parent)
        {
            // The detached navigation rail replaces the old glass divider.
        }

        private void BuildContentArea(GameObject parent)
        {
            _contentArea = XingShuUIFactory.CreateUIObject("ContentArea", parent);
            RectTransform caRt = _contentArea.GetComponent<RectTransform>();
            caRt.anchorMin = new Vector2(0f, 0f);
            caRt.anchorMax = new Vector2(1f, 1f);
            caRt.pivot = new Vector2(0f, 1f);
            caRt.anchoredPosition = new Vector2(280f, -112f);
            caRt.sizeDelta = new Vector2(-312f, -158f);
            if (XingShuMobileUIHelper.IsMobile)
            {
                caRt.offsetMin = new Vector2(14, 36);
                caRt.offsetMax = new Vector2(-14, -84);
            }

            // 1. 游戏加速页面 (Active Feature - Tab 0)
            _gameSpeedView = new XingShuGameSpeedView(_contentArea.transform);

            // 2. 局内手牌监视页面 (Active Feature - Tab 1)
            _handCardView = new XingShuHandCardView(_contentArea.transform);
            _handCardView.Root.SetActive(false);

            // 3. 击飞预测页面 (Active Feature - Tab 2)
            _knockbackView = new XingShuKnockbackView(_contentArea.transform);
            _knockbackView.Root.SetActive(false);

            // 4. 静态立绘页面 (Active Feature - Tab 3)
            _staticPortraitView = new XingShuStaticPortraitView(_contentArea.transform);
            _staticPortraitView.Root.SetActive(false);

            // 4. 录像回放页面 (Active Feature - Tab 4)
            _replayView = new XingShuReplayView(_contentArea.transform);
            _replayView.Root.SetActive(false);

            // 5. 自由镜头页面 (Active Feature - Tab 5)
            _freeCameraView = new XingShuFreeCameraView(_contentArea.transform);
            _freeCameraView.Root.SetActive(false);

            // 6. 自动化与社交 (战局守护) 页面 (Active Feature - Tab 6)
            _actionGuardView = new XingShuActionGuardView(_contentArea.transform);
            _actionGuardView.Root.SetActive(false);

            // 7. 对局数据页面 (Active Feature - Tab 7)
            _battleStatsView = new XingShuBattleStatsView(_contentArea.transform);
            _battleStatsView.Root.SetActive(false);

            // 8. 高级设置页面 (Active Feature - Tab 8)
            _advancedSettingsView = new XingShuAdvancedSettingsView(_contentArea.transform);
            _advancedSettingsView.Root.SetActive(false);

            // 占位提示页面 (Coming Soon)
            _comingSoonView = XingShuUIFactory.CreateUIObject("Page_ComingSoon", _contentArea);
            RectTransform csRt = _comingSoonView.GetComponent<RectTransform>();
            csRt.anchorMin = Vector2.zero;
            csRt.anchorMax = Vector2.one;
            csRt.offsetMin = Vector2.zero;
            csRt.offsetMax = Vector2.zero;

            // 拟态磨砂玻璃卡片切片 (card_base.png, 圆角 12px)
            Sprite csSprite = XingShuUIAssetService.GetSprite("card_base.png");
            Image csBase = _comingSoonView.AddComponent<Image>();
            csBase.sprite = csSprite;
            csBase.type = Image.Type.Sliced;
            csBase.color = XingShuUIAssetService.IsFallbackSprite(csSprite) ? XingShuUITheme.Card : Color.white;

            _comingSoonText = XingShuUIFactory.CreateText(_comingSoonView, "ComingSoonText", "功能正在开发与适配中", 16, XingShuUITheme.SecondaryText, TextAnchor.MiddleCenter);
            RectTransform cstRt = _comingSoonText.rectTransform;
            cstRt.anchorMin = Vector2.zero;
            cstRt.anchorMax = Vector2.one;
            cstRt.offsetMin = new Vector2(30f, 30f);
            cstRt.offsetMax = new Vector2(-30f, -30f);

            _comingSoonView.SetActive(false);
        }

        private void SelectTab(int index)
        {
            if (_mobileNavigation != null) _mobileNavigation.SetActive(false);
            _currentTabIndex = index;
            if (_pageTitle != null) _pageTitle.text = TAB_NAMES[index];
            Sprite normalTabSprite = XingShuUIAssetService.GetSprite("sidebar_tab_normal.png");
            Sprite selectedTabSprite = XingShuUIAssetService.GetSprite("sidebar_tab_selected.png");
            Sprite hoverTabSprite = XingShuUIAssetService.GetSprite("sidebar_tab_hover.png");

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool isSelected = (i == index);
                if (i < _tabImages.Count && _tabImages[i] != null)
                {
                    _tabImages[i].sprite = isSelected ? selectedTabSprite : normalTabSprite;
                    _tabImages[i].color = XingShuUIAssetService.IsFallbackSprite(_tabImages[i].sprite) ? (isSelected ? XingShuUITheme.Yellow : XingShuUITheme.Pink) : Color.white;
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
                    _tabTexts[i].color = Color.white;
                    _tabTexts[i].fontSize = XingShuMobileUIHelper.IsMobile ? 24 : 21;
                    _tabTexts[i].fontStyle = FontStyle.Bold;
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
