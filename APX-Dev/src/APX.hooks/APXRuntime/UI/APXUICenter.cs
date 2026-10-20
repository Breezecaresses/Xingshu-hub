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
    /// 参照游戏设置页的独立导航、深色主框和白色设置卡片构建。
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
        private Text _pageTitle;
        private float _preferredScale = 1f;

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
                if (!APXMobileUIHelper.IsMobile)
                {
                    scale = APXMobileUIHelper.FiniteClamp(scale, APXConfigKeys.FunctionCenterDesktopScaleMin, APXConfigKeys.FunctionCenterDesktopScaleMax, APXConfigKeys.FunctionCenterDesktopScaleDefault);
                }
                else
                {
                    scale = Mathf.Clamp(scale, APXConfigKeys.MobileUserScaleMin, APXConfigKeys.MobileUserScaleMax);
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
                APXUITheme.RefreshFonts(_canvas != null ? _canvas.gameObject : null);
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
            GameObject canvasGo = new GameObject("APX_UICenter_Canvas", typeof(RectTransform));
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

            // 2. 轻量遮罩，点击空白处收起
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

            // 3. 独立导航与设置主体组成的居中框架 (1440 × 860)
            GameObject mainFrame = APXUIFactory.CreateUIObject("MainFrame", canvasGo);
            _mainFrameRt = mainFrame.GetComponent<RectTransform>();
            _mainFrameRt.anchorMin = new Vector2(0.5f, 0.5f);
            _mainFrameRt.anchorMax = new Vector2(0.5f, 0.5f);
            _mainFrameRt.pivot = new Vector2(0.5f, 0.5f);
            _mainFrameRt.anchoredPosition = Vector2.zero;
            _mainFrameRt.sizeDelta = new Vector2(1440f, 860f);
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
            _preferredScale = userScale;
            FitPanelToScreen();

            // 深色圆角主体使用切片纹理，导航栏保持独立。
            Sprite frameSprite = APXUIAssetService.GetSprite("window_frame.png");
            GameObject body = APXUIFactory.CreateUIObject("GameSettingsBody", mainFrame);
            RectTransform bodyRt = body.GetComponent<RectTransform>();
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.offsetMin = new Vector2(248f, 0f);
            bodyRt.offsetMax = Vector2.zero;
            Image baseBg = body.AddComponent<Image>();
            baseBg.sprite = frameSprite;
            baseBg.type = Image.Type.Sliced;
            baseBg.color = APXUIAssetService.IsFallbackSprite(frameSprite) ? APXUITheme.PanelBase : Color.white;
            APXUIFactory.AddShadow(body, new Color(0f, 0f, 0f, 0.24f), new Vector2(0f, -7f));

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
            // Match the CanvasScaler, then fit the entire frame with a small screen margin.
            float canvasScale = Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
            float fit = Mathf.Min((Screen.width / canvasScale - 48f) / 1440f,
                                  (Screen.height / canvasScale - 48f) / 860f);
            _mainFrameRt.localScale = Vector3.one * Mathf.Min(_preferredScale, fit);
        }

        private static RectTransform Place(GameObject go, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        private void BuildTitleBar(GameObject parent)
        {
            // A separate return tab and content header follow the game's settings layout.
            Button back = APXUIFactory.CreateButton(parent, "ReturnToGame", "‹  返回", APXUIAssetService.GetSprite("button_primary.png"),
                Color.white, Color.white, Close, 26);
            RectTransform backRt = Place(back.gameObject, Vector2.up, Vector2.up, Vector2.up, new Vector2(12f, -12f), new Vector2(206f, 68f));
            APXUIFactory.AddOutline(back.GetComponentInChildren<Text>().gameObject, new Color(0.12f,0.08f,0.18f,1f), new Vector2(2f,-2f));

            GameObject titleBar = APXUIFactory.CreateUIObject("TitleBar", parent);
            RectTransform titleRt = Place(titleBar, Vector2.up, Vector2.one, Vector2.up, new Vector2(272f, -20f), new Vector2(-296f, 68f));
            Image hitArea = titleBar.AddComponent<Image>();
            hitArea.sprite = APXUIAssetService.WhiteSprite;
            // A zero-alpha Graphic is culled by Unity and cannot receive the drag gesture.
            hitArea.color = new Color(0f, 0f, 0f, 0.01f);
            hitArea.raycastTarget = true;
            _dragHandle = titleBar.AddComponent<APXUIDragHandle>();
            _dragHandle.targetRect = _mainFrameRt;
            _dragHandle.parentRect = _canvas.transform as RectTransform;

            Image chip = APXUIFactory.CreateImage(titleBar, "PageTitleChip", APXUIAssetService.GetSprite("button_selected.png"), Color.white, Image.Type.Sliced);
            Place(chip.gameObject, Vector2.zero, Vector2.up, new Vector2(0f,0.5f), new Vector2(0f,0f), new Vector2(288f,0f));
            chip.raycastTarget = false;
            _pageTitle = APXUIFactory.CreateText(chip.gameObject, "PageTitle", TAB_NAMES[0], 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform pt = _pageTitle.rectTransform;
            pt.anchorMin = Vector2.zero; pt.anchorMax = Vector2.one; pt.offsetMin = Vector2.zero; pt.offsetMax = Vector2.zero;
            _pageTitle.raycastTarget = false;
            APXUIFactory.AddOutline(_pageTitle.gameObject, new Color(0.10f,0.08f,0.13f,1f), new Vector2(2f,-2f));

            Text brand = APXUIFactory.CreateText(titleBar, "Brand", "星枢 · 功能中心", 19, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(brand.gameObject, Vector2.zero, Vector2.up, new Vector2(0f,0.5f), new Vector2(312f,0f), new Vector2(230f,0f));
            brand.raycastTarget = false;
            Text hotkey = APXUIFactory.CreateText(titleBar, "Hotkey", APXMobileUIHelper.IsMobile ? "拖动顶部移动面板" : "F1 打开 / 收起", 14, new Color(0.82f,0.80f,0.87f), TextAnchor.MiddleRight);
            Place(hotkey.gameObject, Vector2.right, Vector2.one, new Vector2(1f,0.5f), new Vector2(-72f,0f), new Vector2(220f,0f));
            hotkey.raycastTarget = false;
            Button close = APXUIFactory.CreateButton(titleBar, "Close", "×", APXUIAssetService.GetSprite("close_normal.png"), Color.white, Color.white, Close, 30);
            RectTransform closeRt = Place(close.gameObject, new Vector2(1f,0.5f), new Vector2(1f,0.5f), new Vector2(1f,0.5f), Vector2.zero, new Vector2(52f,52f));
            _dragHandle.SetExcludedRects(closeRt, backRt);
            _dragHandle.BindEvents();

            Text footer = APXUIFactory.CreateText(parent, "ProjectSource", "项目来源  github.com/Breezecaresses/Xingshu-hub", 13, new Color(0.82f,0.80f,0.87f), TextAnchor.MiddleLeft);
            Place(footer.gameObject, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(282f,12f), new Vector2(-540f,24f));
            footer.raycastTarget = false;
            Text version = APXUIFactory.CreateText(parent, "RuntimeVersion", XingShu.Runtime.XingShuRuntimeHost.RuntimeVersion, 12, new Color(0.82f,0.80f,0.87f), TextAnchor.MiddleRight);
            Place(version.gameObject, Vector2.right, Vector2.right, Vector2.right, new Vector2(-30f,12f), new Vector2(260f,24f));
            version.raycastTarget = false;
        }

        private void BuildSidebar(GameObject parent)
        {
            Image rail = APXUIFactory.CreateImage(parent, "NavigationRail", APXUIAssetService.GetSprite("card_base.png"), Color.white, Image.Type.Sliced);
            RectTransform railRt = Place(rail.gameObject, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(10f,42f), new Vector2(204f,-146f));
            rail.raycastTarget = false;
            Image line = APXUIFactory.CreateImage(rail.gameObject, "YellowRail", null, APXUITheme.Yellow);
            RectTransform lineRt = line.rectTransform;
            lineRt.anchorMin = new Vector2(0.19f,0f); lineRt.anchorMax = new Vector2(0.19f,1f);
            lineRt.offsetMin = new Vector2(-4f,20f); lineRt.offsetMax = new Vector2(4f,-20f);
            line.raycastTarget = false;

            GameObject sidebar = APXUIFactory.CreateUIObject("Sidebar", parent);
            Place(sidebar, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(0f,58f), new Vector2(230f,-170f));
            VerticalLayoutGroup layout = sidebar.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0,0,8,8);
            layout.spacing = 12f; layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            Sprite normal = APXUIAssetService.GetSprite("sidebar_tab_normal.png");
            Sprite selected = APXUIAssetService.GetSprite("sidebar_tab_selected.png");
            Sprite hover = APXUIAssetService.GetSprite("sidebar_tab_hover.png");
            for (int i=0;i<TAB_NAMES.Length;i++)
            {
                int index = i;
                GameObject tab = APXUIFactory.CreateUIObject("Tab_"+index, sidebar);
                LayoutElement le = tab.AddComponent<LayoutElement>();
                le.minHeight = 62f; le.preferredHeight = 62f; le.flexibleHeight = 0f;
                Image image = tab.AddComponent<Image>();
                image.sprite = i==0 ? selected : normal; image.type = Image.Type.Sliced; image.color = Color.white;
                Text text = APXUIFactory.CreateText(tab,"Text",TAB_NAMES[i],21,Color.white,TextAnchor.MiddleLeft,FontStyle.Bold);
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(54f,0f); text.rectTransform.offsetMax = new Vector2(-8f,0f);
                text.raycastTarget = false;
                APXUIFactory.AddOutline(text.gameObject,new Color(0.10f,0.08f,0.13f,1f),new Vector2(1.8f,-1.8f));
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
            _contentArea = APXUIFactory.CreateUIObject("ContentArea", parent);
            RectTransform caRt = _contentArea.GetComponent<RectTransform>();
            caRt.anchorMin = new Vector2(0f, 0f);
            caRt.anchorMax = new Vector2(1f, 1f);
            caRt.pivot = new Vector2(0f, 1f);
            caRt.anchoredPosition = new Vector2(280f, -112f);
            caRt.sizeDelta = new Vector2(-312f, -158f);

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
            if (_pageTitle != null) _pageTitle.text = TAB_NAMES[index];
            Sprite normalTabSprite = APXUIAssetService.GetSprite("sidebar_tab_normal.png");
            Sprite selectedTabSprite = APXUIAssetService.GetSprite("sidebar_tab_selected.png");
            Sprite hoverTabSprite = APXUIAssetService.GetSprite("sidebar_tab_hover.png");

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool isSelected = (i == index);
                if (i < _tabImages.Count && _tabImages[i] != null)
                {
                    _tabImages[i].sprite = isSelected ? selectedTabSprite : normalTabSprite;
                    _tabImages[i].color = APXUIAssetService.IsFallbackSprite(_tabImages[i].sprite) ? (isSelected ? APXUITheme.Yellow : APXUITheme.Pink) : Color.white;
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
                    _tabTexts[i].fontSize = 21;
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
