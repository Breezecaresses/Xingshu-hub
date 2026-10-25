using System;
using System.IO;
using XingShuRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    /// <summary>
    /// 功能中心 - 静态立绘管理与配置 Modern UI 页面 (Phase 2C-1A)
    /// 纯粹的 View 层，100% 委托 StaticPortraitGUI 底层配置与文件操作
    /// 遵循原版统一视觉规范 (卡片圆角、纯色半透毛玻璃、无纹理干涉)
    /// </summary>
    internal sealed class XingShuStaticPortraitView
    {
        private readonly GameObject _root;
        private Text _statusIndicatorText;
        private Text _hotkeyBadgeText;

        // Card 1: 运行时开关
        private Toggle _enableLoadToggle;
        private Toggle _enableDumpToggle;

        // Card 2: 基础路径前缀
        private InputField _basePathInput;
        private Text _basePathStatusText;
        private Text _basePathResolvedText;
        private Button _createBasePathBtn;
        private string _stagedBasePath = "";

        // Card 3: MODIMG
        private InputField _modImgInput;
        private Text _modImgStatusText;
        private Text _modImgResolvedText;
        private Button _createModImgBtn;
        private string _stagedModImgPath = "";

        // Card 4: MODOUT
        private InputField _modOutInput;
        private Text _modOutStatusText;
        private Text _modOutResolvedText;
        private Button _createModOutBtn;
        private string _stagedModOutPath = "";

        // Card 5: 操作栏与状态反馈
        private Text _statusMsgText;

        private bool _suppressCallbacks;
        private float _lastDiskCheckTime;
        private KeyCode _lastObservedHotkey = KeyCode.None;
        private bool _lastObservedLoadMod;
        private bool _lastObservedAutoDump;
        private string _lastObservedStatusMsg = "";

        internal GameObject Root => _root;

        internal XingShuStaticPortraitView(Transform parent)
        {
            _root = XingShuUIFactory.CreateUIObject("Page_StaticPortrait", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            LoadInitialStagedValues();
            BuildUI();
            RefreshDirectoryStatuses();
        }

        private void LoadInitialStagedValues()
        {
            if (StaticPortraitGUI.Instance != null)
            {
                _stagedBasePath = StaticPortraitGUI.Instance.BasePath ?? "";
                _stagedModImgPath = StaticPortraitGUI.Instance.ModImgPath ?? StaticPortraitGUI.Instance.DefaultModImg;
                _stagedModOutPath = StaticPortraitGUI.Instance.ModOutPath ?? StaticPortraitGUI.Instance.DefaultModOut;
                _lastObservedLoadMod = StaticPortraitGUI.Instance.EnableLoadMod;
                _lastObservedAutoDump = StaticPortraitGUI.Instance.EnableAutoDump;
                _lastObservedHotkey = StaticPortraitGUI.Instance.CurrentHotkey;
            }
            else
            {
                _stagedBasePath = "";
                _stagedModImgPath = "../modimg";
                _stagedModOutPath = "../outimg";
                _lastObservedLoadMod = true;
                _lastObservedAutoDump = true;
                _lastObservedHotkey = KeyCode.F8;
            }
        }

        private void BuildUI()
        {
            // 垂直滚动视口 (ScrollRect + Viewport RectMask2D + Content VerticalLayoutGroup)
            ScrollRect scrollRect = XingShuUIFactory.CreateVerticalScrollView(_root, "StaticPortraitScrollView", out GameObject contentArea);
            VerticalLayoutGroup vlg = contentArea.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.spacing = 14f;
                vlg.padding = new RectOffset(0, 10, 0, 24);
            }

            // 0. 特色功能卡片 (Feature Header Card: 76px)
            BuildHeaderCard(contentArea);

            // 1. 运行时开关控制 (Card 1: 136px)
            BuildSwitchCard(contentArea);

            // 2. 基础路径前缀 (Card 2: 216px)
            BuildBasePathCard(contentArea);

            // 3. MOD 加载目录 (Card 3: 154px)
            BuildModImgCard(contentArea);

            // 4. 原图保存转储目录 (Card 4: 154px)
            BuildModOutCard(contentArea);

            // 5. 底部操作栏与状态反馈 (Card 5: 84px)
            BuildActionBarCard(contentArea);
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

            // 标题: 立绘替换 (StaticPortrait)
            Text titleTxt = XingShuUIFactory.CreateText(headerGo, "Title", "模组加载", 20, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = titleTxt.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 10f);
            titleRt.sizeDelta = new Vector2(360f, 26f);

            // 说明文案
            Text subTxt = XingShuUIFactory.CreateText(headerGo, "Subtitle", "立绘、卡牌画面与自定义贴图管理", 13, XingShuUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform subRt = subTxt.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(24f, -14f);
            subRt.sizeDelta = new Vector2(480f, 22f);

            // 状态指示文本 (● 已启用 / ○ 已暂停)
            _statusIndicatorText = XingShuUIFactory.CreateText(headerGo, "StatusIndicator", _lastObservedLoadMod ? "● 已启用" : "○ 已暂停", 15, _lastObservedLoadMod ? XingShuUITheme.Positive : XingShuUITheme.SecondaryText, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform statusRt = _statusIndicatorText.rectTransform;
            statusRt.anchorMin = new Vector2(1f, 0.5f);
            statusRt.anchorMax = new Vector2(1f, 0.5f);
            statusRt.pivot = new Vector2(1f, 0.5f);
            statusRt.anchoredPosition = new Vector2(-88f, 0f);
            statusRt.sizeDelta = new Vector2(110f, 26f);

            // 动态热键徽章
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

            _hotkeyBadgeText = XingShuUIFactory.CreateText(badgeGo, "Text", $"[{_lastObservedHotkey}]", 12, XingShuUITheme.CyanAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform btRt = _hotkeyBadgeText.rectTransform;
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.pivot = new Vector2(0.5f, 0.5f);
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;
        }

        private void BuildSwitchCard(GameObject parent)
        {
            GameObject card = CreateCardContainer(parent, "Card_Switch", 136f);

            // 卡片标题
            Text title = XingShuUIFactory.CreateText(card, "Title", "1. 运行时开关控制", 15, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRt = title.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(20f, -14f);
            tRt.sizeDelta = new Vector2(-40f, 22f);

            // Toggle 1: 启用 Mod 立绘替换
            _enableLoadToggle = XingShuUIFactory.CreateToggle(card, "Toggle_EnableLoad", "启用 Mod 立绘替换 (从 MODIMG 读取)", _lastObservedLoadMod, delegate(bool val)
            {
                if (_suppressCallbacks) return;
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.EnableLoadMod = val;
                }
                UpdateStatusIndicator(val);
            });
            FormatToggleLayout(_enableLoadToggle, -44f);

            Text sub1 = XingShuUIFactory.CreateText(card, "Sub1", "开启后，游戏加载立绘时优先从 MODIMG 目录读取同名 PNG 贴图", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform s1Rt = sub1.rectTransform;
            s1Rt.anchorMin = new Vector2(0f, 1f);
            s1Rt.anchorMax = new Vector2(1f, 1f);
            s1Rt.pivot = new Vector2(0f, 1f);
            s1Rt.anchoredPosition = new Vector2(20f, -68f);
            s1Rt.sizeDelta = new Vector2(-100f, 18f);

            // Toggle 2: 启用未匹配图片自动保存
            _enableDumpToggle = XingShuUIFactory.CreateToggle(card, "Toggle_EnableDump", "启用未匹配图片自动保存 (转储至 MODOUT)", _lastObservedAutoDump, delegate(bool val)
            {
                if (_suppressCallbacks) return;
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.EnableAutoDump = val;
                }
            });
            FormatToggleLayout(_enableDumpToggle, -88f);

            Text sub2 = XingShuUIFactory.CreateText(card, "Sub2", "开启后，若立绘未被 Mod 替换，自动将游戏原始贴图保存至 MODOUT 目录", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform s2Rt = sub2.rectTransform;
            s2Rt.anchorMin = new Vector2(0f, 1f);
            s2Rt.anchorMax = new Vector2(1f, 1f);
            s2Rt.pivot = new Vector2(0f, 1f);
            s2Rt.anchoredPosition = new Vector2(20f, -112f);
            s2Rt.sizeDelta = new Vector2(-100f, 18f);
        }

        private void FormatToggleLayout(Toggle toggle, float topY)
        {
            RectTransform rt = toggle.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, topY);
            rt.sizeDelta = new Vector2(-40f, 26f);

            Transform labelT = toggle.transform.Find("Label");
            if (labelT != null)
            {
                RectTransform lRt = labelT.GetComponent<RectTransform>();
                lRt.anchorMin = Vector2.zero;
                lRt.anchorMax = Vector2.one;
                lRt.pivot = new Vector2(0f, 0.5f);
                lRt.offsetMin = Vector2.zero;
                lRt.offsetMax = new Vector2(-70f, 0f);
            }
            Transform bgT = toggle.transform.Find("Background");
            if (bgT != null)
            {
                RectTransform bRt = bgT.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(1f, 0.5f);
                bRt.anchorMax = new Vector2(1f, 0.5f);
                bRt.pivot = new Vector2(1f, 0.5f);
                bRt.anchoredPosition = Vector2.zero;
                bRt.sizeDelta = new Vector2(56f, 26f);
            }
        }

        private void BuildBasePathCard(GameObject parent)
        {
            GameObject card = CreateCardContainer(parent, "Card_BasePath", 216f);

            // 标题
            Text title = XingShuUIFactory.CreateText(card, "Title", "2. 基础路径前缀 (Base Path)", 15, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRt = title.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(20f, -14f);
            tRt.sizeDelta = new Vector2(-40f, 22f);

            // 说明
            Text sub = XingShuUIFactory.CreateText(card, "Subtitle", "相对路径的前缀基准。留空则自动使用 Application.dataPath；输入自定义路径则以此文件夹为准。", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform sRt = sub.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0f, 1f);
            sRt.anchoredPosition = new Vector2(20f, -36f);
            sRt.sizeDelta = new Vector2(-40f, 18f);

            // 前缀标签
            Text prefixLabel = XingShuUIFactory.CreateText(card, "Label_Prefix", "前缀:", 14, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform plRt = prefixLabel.rectTransform;
            plRt.anchorMin = new Vector2(0f, 1f);
            plRt.anchorMax = new Vector2(0f, 1f);
            plRt.pivot = new Vector2(0f, 1f);
            plRt.anchoredPosition = new Vector2(20f, -62f);
            plRt.sizeDelta = new Vector2(40f, 32f);

            // 输入框
            _basePathInput = XingShuUIFactory.CreateInputField(
                card,
                "Input_BasePath",
                "基准路径 (留空默认 Application.dataPath)",
                _stagedBasePath,
                "StaticPortrait.BasePath",
                delegate(string val) { _stagedBasePath = val; },
                delegate(string val) { _stagedBasePath = val; RefreshDirectoryStatuses(); },
                13
            );
            RectTransform inputRt = _basePathInput.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 1f);
            inputRt.anchorMax = new Vector2(1f, 1f);
            inputRt.pivot = new Vector2(0f, 1f);
            inputRt.anchoredPosition = new Vector2(65f, -62f);
            inputRt.sizeDelta = new Vector2(-155f, 32f);

            // 清空按钮
            Button clearBtn = XingShuUIFactory.CreateButton(
                card,
                "Btn_ClearBasePath",
                "清空",
                XingShuUIAssetService.GetSprite("button_secondary.png"),
                Color.white,
                XingShuUITheme.SecondaryText,
                delegate
                {
                    _stagedBasePath = "";
                    _basePathInput.text = "";
                    if (StaticPortraitGUI.Instance != null)
                    {
                        StaticPortraitGUI.Instance.BasePath = "";
                        StaticPortraitGUI.Instance.SaveConfig();
                    }
                    RefreshDirectoryStatuses();
                },
                13
            );
            RectTransform clearRt = clearBtn.GetComponent<RectTransform>();
            clearRt.anchorMin = new Vector2(1f, 1f);
            clearRt.anchorMax = new Vector2(1f, 1f);
            clearRt.pivot = new Vector2(1f, 1f);
            clearRt.anchoredPosition = new Vector2(-20f, -62f);
            clearRt.sizeDelta = new Vector2(60f, 32f);

            // 预设按钮容器 (5 Presets)
            GameObject presetsContainer = XingShuUIFactory.CreateUIObject("PresetsContainer", card);
            RectTransform pcRt = presetsContainer.GetComponent<RectTransform>();
            pcRt.anchorMin = new Vector2(0f, 1f);
            pcRt.anchorMax = new Vector2(1f, 1f);
            pcRt.pivot = new Vector2(0.5f, 1f);
            pcRt.anchoredPosition = new Vector2(0f, -102f);
            pcRt.sizeDelta = new Vector2(-40f, 28f);

            HorizontalLayoutGroup pcHlg = presetsContainer.AddComponent<HorizontalLayoutGroup>();
            pcHlg.spacing = 8f;
            pcHlg.childForceExpandWidth = true;
            pcHlg.childForceExpandHeight = true;
            pcHlg.childControlWidth = true;
            pcHlg.childControlHeight = true;

            Sprite btnSec = XingShuUIAssetService.GetSprite("button_secondary.png");
            CreatePresetBtn(presetsContainer, "Btn_P1", "[留空/默认]", "", btnSec);
            CreatePresetBtn(presetsContainer, "Btn_P2", "[持久化推荐]", SafeGetPath(() => Application.persistentDataPath), btnSec);
            CreatePresetBtn(presetsContainer, "Btn_P3", "[数据目录]", SafeGetPath(() => Application.dataPath), btnSec);
            CreatePresetBtn(presetsContainer, "Btn_P4", "[临时缓存]", SafeGetPath(() => Application.temporaryCachePath), btnSec);
            CreatePresetBtn(presetsContainer, "Btn_P5", "[只读资产]", SafeGetPath(() => Application.streamingAssetsPath), btnSec);

            // 状态指示与操作栏
            _basePathStatusText = XingShuUIFactory.CreateText(card, "Status_BasePath", "● 基准目录有效", 13, XingShuUITheme.Positive, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform bstRt = _basePathStatusText.rectTransform;
            bstRt.anchorMin = new Vector2(0f, 1f);
            bstRt.anchorMax = new Vector2(1f, 1f);
            bstRt.pivot = new Vector2(0f, 1f);
            bstRt.anchoredPosition = new Vector2(20f, -140f);
            bstRt.sizeDelta = new Vector2(-210f, 26f);

            Button openBaseBtn = XingShuUIFactory.CreateButton(card, "Btn_OpenBase", "打开基准", btnSec, Color.white, XingShuUITheme.SecondaryText, delegate
            {
                string eff = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.OpenFolderInternal(eff);
                }
            }, 12);
            RectTransform obRt = openBaseBtn.GetComponent<RectTransform>();
            obRt.anchorMin = new Vector2(1f, 1f);
            obRt.anchorMax = new Vector2(1f, 1f);
            obRt.pivot = new Vector2(1f, 1f);
            obRt.anchoredPosition = new Vector2(-104f, -140f);
            obRt.sizeDelta = new Vector2(76f, 26f);

            _createBasePathBtn = XingShuUIFactory.CreateButton(card, "Btn_CreateBase", "创建基准", btnSec, Color.white, XingShuUITheme.LimeAccent, delegate
            {
                string eff = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.CreateFolderInternal(eff);
                }
                RefreshDirectoryStatuses();
            }, 12);
            RectTransform cbRt = _createBasePathBtn.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(1f, 1f);
            cbRt.anchorMax = new Vector2(1f, 1f);
            cbRt.pivot = new Vector2(1f, 1f);
            cbRt.anchoredPosition = new Vector2(-20f, -140f);
            cbRt.sizeDelta = new Vector2(76f, 26f);

            // 解析生效路径
            _basePathResolvedText = XingShuUIFactory.CreateText(card, "Resolved_BasePath", "当前生效前缀: ...", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform bprRt = _basePathResolvedText.rectTransform;
            bprRt.anchorMin = new Vector2(0f, 1f);
            bprRt.anchorMax = new Vector2(1f, 1f);
            bprRt.pivot = new Vector2(0f, 1f);
            bprRt.anchoredPosition = new Vector2(20f, -174f);
            bprRt.sizeDelta = new Vector2(-40f, 20f);

            // Android APK 只读兼容提示 (条件显示)
            bool isAndroid = Application.platform == RuntimePlatform.Android;
            string currentEff = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
            bool isApk = currentEff.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
            if (isAndroid && isApk)
            {
                Text androidHint = XingShuUIFactory.CreateText(card, "AndroidHint", "⚠ Android 兼容提示: 当前基准路径为 APK，只读不可写入。建议选择 Application.persistentDataPath！", 11, XingShuUITheme.NumericAccent, TextAnchor.MiddleLeft);
                RectTransform ahRt = androidHint.rectTransform;
                ahRt.anchorMin = new Vector2(0f, 1f);
                ahRt.anchorMax = new Vector2(1f, 1f);
                ahRt.pivot = new Vector2(0f, 1f);
                ahRt.anchoredPosition = new Vector2(20f, -194f);
                ahRt.sizeDelta = new Vector2(-40f, 18f);
            }
        }

        private void CreatePresetBtn(GameObject parent, string name, string label, string targetPath, Sprite bg)
        {
            XingShuUIFactory.CreateButton(parent, name, label, bg, Color.white, XingShuUITheme.SecondaryText, delegate
            {
                _stagedBasePath = targetPath;
                if (_basePathInput != null)
                {
                    _basePathInput.text = targetPath;
                }
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.BasePath = targetPath;
                    StaticPortraitGUI.Instance.SaveConfig();
                }
                RefreshDirectoryStatuses();
            }, 11);
        }

        private void BuildModImgCard(GameObject parent)
        {
            GameObject card = CreateCardContainer(parent, "Card_ModImg", 154f);

            Text title = XingShuUIFactory.CreateText(card, "Title", "3. MOD 加载目录 (MODIMG)", 15, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRt = title.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(20f, -14f);
            tRt.sizeDelta = new Vector2(-40f, 22f);

            Text sub = XingShuUIFactory.CreateText(card, "Subtitle", "相对路径基于基础前缀拼接。开启 Mod 替换后，将优先读取此目录下的同名 PNG 贴图。", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform sRt = sub.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0f, 1f);
            sRt.anchoredPosition = new Vector2(20f, -36f);
            sRt.sizeDelta = new Vector2(-40f, 18f);

            Text pathLabel = XingShuUIFactory.CreateText(card, "Label_Path", "路径:", 14, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform plRt = pathLabel.rectTransform;
            plRt.anchorMin = new Vector2(0f, 1f);
            plRt.anchorMax = new Vector2(0f, 1f);
            plRt.pivot = new Vector2(0f, 1f);
            plRt.anchoredPosition = new Vector2(20f, -62f);
            plRt.sizeDelta = new Vector2(40f, 32f);

            _modImgInput = XingShuUIFactory.CreateInputField(
                card,
                "Input_ModImg",
                "MODIMG 相对或绝对路径",
                _stagedModImgPath,
                "StaticPortrait.ModImg",
                delegate(string val) { _stagedModImgPath = val; },
                delegate(string val) { _stagedModImgPath = val; RefreshDirectoryStatuses(); },
                13
            );
            RectTransform inputRt = _modImgInput.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 1f);
            inputRt.anchorMax = new Vector2(1f, 1f);
            inputRt.pivot = new Vector2(0f, 1f);
            inputRt.anchoredPosition = new Vector2(65f, -62f);
            inputRt.sizeDelta = new Vector2(-175f, 32f);

            Sprite btnSec = XingShuUIAssetService.GetSprite("button_secondary.png");
            Button resetBtn = XingShuUIFactory.CreateButton(card, "Btn_ResetModImg", "恢复默认", btnSec, Color.white, XingShuUITheme.SecondaryText, delegate
            {
                string def = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModImg : "../modimg";
                _stagedModImgPath = def;
                _modImgInput.text = def;
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.ModImgPath = def;
                    StaticPortraitGUI.Instance.SaveConfig();
                }
                RefreshDirectoryStatuses();
            }, 12);
            RectTransform rRt = resetBtn.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(1f, 1f);
            rRt.anchorMax = new Vector2(1f, 1f);
            rRt.pivot = new Vector2(1f, 1f);
            rRt.anchoredPosition = new Vector2(-20f, -62f);
            rRt.sizeDelta = new Vector2(80f, 32f);

            // 状态与操作
            _modImgStatusText = XingShuUIFactory.CreateText(card, "Status_ModImg", "● 目录有效", 13, XingShuUITheme.Positive, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform stRt = _modImgStatusText.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(1f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(20f, -100f);
            stRt.sizeDelta = new Vector2(-210f, 26f);

            Button openBtn = XingShuUIFactory.CreateButton(card, "Btn_OpenModImg", "打开目录", btnSec, Color.white, XingShuUITheme.SecondaryText, delegate
            {
                string effBase = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
                string def = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModImg : "../modimg";
                string resolved = StaticPortraitGUI.Instance != null
                    ? StaticPortraitGUI.Instance.ResolvePathInternal(_stagedModImgPath, def)
                    : Path.Combine(effBase, _stagedModImgPath).Replace('\\', '/');
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.OpenFolderInternal(resolved);
                }
            }, 12);
            RectTransform obRt = openBtn.GetComponent<RectTransform>();
            obRt.anchorMin = new Vector2(1f, 1f);
            obRt.anchorMax = new Vector2(1f, 1f);
            obRt.pivot = new Vector2(1f, 1f);
            obRt.anchoredPosition = new Vector2(-104f, -100f);
            obRt.sizeDelta = new Vector2(76f, 26f);

            _createModImgBtn = XingShuUIFactory.CreateButton(card, "Btn_CreateModImg", "创建目录", btnSec, Color.white, XingShuUITheme.LimeAccent, delegate
            {
                string effBase = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
                string def = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModImg : "../modimg";
                string resolved = StaticPortraitGUI.Instance != null
                    ? StaticPortraitGUI.Instance.ResolvePathInternal(_stagedModImgPath, def)
                    : Path.Combine(effBase, _stagedModImgPath).Replace('\\', '/');
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.CreateFolderInternal(resolved);
                }
                RefreshDirectoryStatuses();
            }, 12);
            RectTransform cbRt = _createModImgBtn.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(1f, 1f);
            cbRt.anchorMax = new Vector2(1f, 1f);
            cbRt.pivot = new Vector2(1f, 1f);
            cbRt.anchoredPosition = new Vector2(-20f, -100f);
            cbRt.sizeDelta = new Vector2(76f, 26f);

            // 解析绝对路径
            _modImgResolvedText = XingShuUIFactory.CreateText(card, "Resolved_ModImg", "解析绝对路径: ...", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform mirRt = _modImgResolvedText.rectTransform;
            mirRt.anchorMin = new Vector2(0f, 1f);
            mirRt.anchorMax = new Vector2(1f, 1f);
            mirRt.pivot = new Vector2(0f, 1f);
            mirRt.anchoredPosition = new Vector2(20f, -128f);
            mirRt.sizeDelta = new Vector2(-40f, 20f);
        }

        private void BuildModOutCard(GameObject parent)
        {
            GameObject card = CreateCardContainer(parent, "Card_ModOut", 154f);

            Text title = XingShuUIFactory.CreateText(card, "Title", "4. 原图保存转储目录 (MODOUT)", 15, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRt = title.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(20f, -14f);
            tRt.sizeDelta = new Vector2(-40f, 22f);

            Text sub = XingShuUIFactory.CreateText(card, "Subtitle", "相对路径基于基础前缀拼接。开启自动转储后，未匹配的原始立绘将保存至此目录。", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform sRt = sub.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0f, 1f);
            sRt.anchoredPosition = new Vector2(20f, -36f);
            sRt.sizeDelta = new Vector2(-40f, 18f);

            Text pathLabel = XingShuUIFactory.CreateText(card, "Label_Path", "路径:", 14, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft);
            RectTransform plRt = pathLabel.rectTransform;
            plRt.anchorMin = new Vector2(0f, 1f);
            plRt.anchorMax = new Vector2(0f, 1f);
            plRt.pivot = new Vector2(0f, 1f);
            plRt.anchoredPosition = new Vector2(20f, -62f);
            plRt.sizeDelta = new Vector2(40f, 32f);

            _modOutInput = XingShuUIFactory.CreateInputField(
                card,
                "Input_ModOut",
                "MODOUT 相对或绝对路径",
                _stagedModOutPath,
                "StaticPortrait.ModOut",
                delegate(string val) { _stagedModOutPath = val; },
                delegate(string val) { _stagedModOutPath = val; RefreshDirectoryStatuses(); },
                13
            );
            RectTransform inputRt = _modOutInput.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 1f);
            inputRt.anchorMax = new Vector2(1f, 1f);
            inputRt.pivot = new Vector2(0f, 1f);
            inputRt.anchoredPosition = new Vector2(65f, -62f);
            inputRt.sizeDelta = new Vector2(-175f, 32f);

            Sprite btnSec = XingShuUIAssetService.GetSprite("button_secondary.png");
            Button resetBtn = XingShuUIFactory.CreateButton(card, "Btn_ResetModOut", "恢复默认", btnSec, Color.white, XingShuUITheme.SecondaryText, delegate
            {
                string def = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModOut : "../outimg";
                _stagedModOutPath = def;
                _modOutInput.text = def;
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.ModOutPath = def;
                    StaticPortraitGUI.Instance.SaveConfig();
                }
                RefreshDirectoryStatuses();
            }, 12);
            RectTransform rRt = resetBtn.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(1f, 1f);
            rRt.anchorMax = new Vector2(1f, 1f);
            rRt.pivot = new Vector2(1f, 1f);
            rRt.anchoredPosition = new Vector2(-20f, -62f);
            rRt.sizeDelta = new Vector2(80f, 32f);

            // 状态与操作
            _modOutStatusText = XingShuUIFactory.CreateText(card, "Status_ModOut", "● 目录有效", 13, XingShuUITheme.Positive, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform stRt = _modOutStatusText.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(1f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(20f, -100f);
            stRt.sizeDelta = new Vector2(-210f, 26f);

            Button openBtn = XingShuUIFactory.CreateButton(card, "Btn_OpenModOut", "打开目录", btnSec, Color.white, XingShuUITheme.SecondaryText, delegate
            {
                string effBase = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
                string def = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModOut : "../outimg";
                string resolved = StaticPortraitGUI.Instance != null
                    ? StaticPortraitGUI.Instance.ResolvePathInternal(_stagedModOutPath, def)
                    : Path.Combine(effBase, _stagedModOutPath).Replace('\\', '/');
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.OpenFolderInternal(resolved);
                }
            }, 12);
            RectTransform obRt = openBtn.GetComponent<RectTransform>();
            obRt.anchorMin = new Vector2(1f, 1f);
            obRt.anchorMax = new Vector2(1f, 1f);
            obRt.pivot = new Vector2(1f, 1f);
            obRt.anchoredPosition = new Vector2(-104f, -100f);
            obRt.sizeDelta = new Vector2(76f, 26f);

            _createModOutBtn = XingShuUIFactory.CreateButton(card, "Btn_CreateModOut", "创建目录", btnSec, Color.white, XingShuUITheme.LimeAccent, delegate
            {
                string effBase = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
                string def = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModOut : "../outimg";
                string resolved = StaticPortraitGUI.Instance != null
                    ? StaticPortraitGUI.Instance.ResolvePathInternal(_stagedModOutPath, def)
                    : Path.Combine(effBase, _stagedModOutPath).Replace('\\', '/');
                if (StaticPortraitGUI.Instance != null)
                {
                    StaticPortraitGUI.Instance.CreateFolderInternal(resolved);
                }
                RefreshDirectoryStatuses();
            }, 12);
            RectTransform cbRt = _createModOutBtn.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(1f, 1f);
            cbRt.anchorMax = new Vector2(1f, 1f);
            cbRt.pivot = new Vector2(1f, 1f);
            cbRt.anchoredPosition = new Vector2(-20f, -100f);
            cbRt.sizeDelta = new Vector2(76f, 26f);

            // 解析绝对路径
            _modOutResolvedText = XingShuUIFactory.CreateText(card, "Resolved_ModOut", "解析绝对路径: ...", 12, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform morRt = _modOutResolvedText.rectTransform;
            morRt.anchorMin = new Vector2(0f, 1f);
            morRt.anchorMax = new Vector2(1f, 1f);
            morRt.pivot = new Vector2(0f, 1f);
            morRt.anchoredPosition = new Vector2(20f, -128f);
            morRt.sizeDelta = new Vector2(-40f, 20f);
        }

        private void BuildActionBarCard(GameObject parent)
        {
            GameObject card = CreateCardContainer(parent, "Card_ActionBar", 84f);

            // 保存全部配置 (主操作按钮)
            Sprite btnPri = XingShuUIAssetService.GetSprite("button_primary.png");
            Button saveBtn = XingShuUIFactory.CreateButton(
                card,
                "Btn_SaveAll",
                "保存全部配置 (立即生效)",
                btnPri,
                Color.white,
                new Color(0.06f, 0.08f, 0.12f, 1f),
                delegate
                {
                    if (StaticPortraitGUI.Instance != null)
                    {
                        StaticPortraitGUI.Instance.BasePath = _stagedBasePath;
                        StaticPortraitGUI.Instance.ModImgPath = _stagedModImgPath;
                        StaticPortraitGUI.Instance.ModOutPath = _stagedModOutPath;
                        StaticPortraitGUI.Instance.SaveConfig();
                    }
                    RefreshDirectoryStatuses();
                },
                14
            );
            RectTransform sRt = saveBtn.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0f, 1f);
            sRt.anchoredPosition = new Vector2(20f, -14f);
            sRt.sizeDelta = new Vector2(-150f, 34f);

            // 刷新状态 (次级操作按钮)
            Sprite btnSec = XingShuUIAssetService.GetSprite("button_secondary.png");
            Button refreshBtn = XingShuUIFactory.CreateButton(
                card,
                "Btn_RefreshStatus",
                "刷新状态",
                btnSec,
                Color.white,
                XingShuUITheme.SecondaryText,
                delegate
                {
                    if (StaticPortraitGUI.Instance != null)
                    {
                        StaticPortraitGUI.Instance.LoadConfig();
                        _stagedBasePath = StaticPortraitGUI.Instance.BasePath ?? "";
                        _stagedModImgPath = StaticPortraitGUI.Instance.ModImgPath ?? StaticPortraitGUI.Instance.DefaultModImg;
                        _stagedModOutPath = StaticPortraitGUI.Instance.ModOutPath ?? StaticPortraitGUI.Instance.DefaultModOut;
                        if (_basePathInput != null) _basePathInput.text = _stagedBasePath;
                        if (_modImgInput != null) _modImgInput.text = _stagedModImgPath;
                        if (_modOutInput != null) _modOutInput.text = _stagedModOutPath;
                        StaticPortraitGUI.Instance.SetStatusMessage("<color=#38bdf8>状态已刷新</color>", 2f);
                    }
                    RefreshDirectoryStatuses();
                },
                13
            );
            RectTransform rRt = refreshBtn.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(1f, 1f);
            rRt.anchorMax = new Vector2(1f, 1f);
            rRt.pivot = new Vector2(1f, 1f);
            rRt.anchoredPosition = new Vector2(-20f, -14f);
            rRt.sizeDelta = new Vector2(100f, 34f);

            // 状态反馈消息提示
            _statusMsgText = XingShuUIFactory.CreateText(card, "Text_StatusMsg", "", 13, XingShuUITheme.PrimaryText, TextAnchor.MiddleCenter);
            RectTransform smRt = _statusMsgText.rectTransform;
            smRt.anchorMin = new Vector2(0f, 1f);
            smRt.anchorMax = new Vector2(1f, 1f);
            smRt.pivot = new Vector2(0.5f, 1f);
            smRt.anchoredPosition = new Vector2(0f, -54f);
            smRt.sizeDelta = new Vector2(-40f, 22f);
        }

        private static string SafeGetPath(Func<string> getter)
        {
            try { return getter() ?? ""; } catch { return ""; }
        }

        private void UpdateStatusIndicator(bool enabled)
        {
            if (_statusIndicatorText != null)
            {
                _statusIndicatorText.text = enabled ? "● 已启用" : "○ 已暂停";
                _statusIndicatorText.color = enabled ? XingShuUITheme.Positive : XingShuUITheme.SecondaryText;
            }
        }

        private void RefreshDirectoryStatuses()
        {
            // 1. Base Path
            string effBase = StaticPortraitGUI.GetEffectiveBasePath(_stagedBasePath);
            bool customBase = !string.IsNullOrEmpty(_stagedBasePath) && !string.IsNullOrEmpty(_stagedBasePath.Trim());
            bool baseExists = false;
            try
            {
                baseExists = Directory.Exists(effBase);
            }
            catch { }

            if (_basePathStatusText != null)
            {
                _basePathStatusText.text = baseExists ? "● 基准目录有效" : "○ 基准目录不存在或为文件";
                _basePathStatusText.color = baseExists ? XingShuUITheme.Positive : XingShuUITheme.Danger;
            }
            if (_createBasePathBtn != null)
            {
                _createBasePathBtn.gameObject.SetActive(!baseExists);
            }
            if (_basePathResolvedText != null)
            {
                _basePathResolvedText.text = $"当前生效前缀: {effBase}" + (customBase ? " (自定义输入)" : " (留空默认 Application.dataPath)");
            }

            // 2. MODIMG
            string defaultImg = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModImg : "../modimg";
            string resolvedImg;
            if (StaticPortraitGUI.Instance != null)
            {
                resolvedImg = StaticPortraitGUI.Instance.ResolvePathInternal(_stagedModImgPath, defaultImg);
            }
            else
            {
                resolvedImg = Path.Combine(effBase, string.IsNullOrEmpty(_stagedModImgPath) ? defaultImg : _stagedModImgPath).Replace('\\', '/');
            }

            bool imgExists = false;
            int imgCount = 0;
            try
            {
                imgExists = Directory.Exists(resolvedImg);
                if (imgExists)
                {
                    imgCount = Directory.GetFiles(resolvedImg, "*.png").Length;
                }
            }
            catch { }

            if (_modImgStatusText != null)
            {
                _modImgStatusText.text = imgExists
                    ? $"● 目录有效 (已检测到 {imgCount} 张 PNG 贴图)"
                    : "○ 目录不存在 (不会加载任何 Mod 贴图)";
                _modImgStatusText.color = imgExists ? XingShuUITheme.Positive : XingShuUITheme.Danger;
            }
            if (_createModImgBtn != null)
            {
                _createModImgBtn.gameObject.SetActive(!imgExists);
            }
            if (_modImgResolvedText != null)
            {
                _modImgResolvedText.text = $"解析绝对路径: {resolvedImg}";
            }

            // 3. MODOUT
            string defaultOut = StaticPortraitGUI.Instance != null ? StaticPortraitGUI.Instance.DefaultModOut : "../outimg";
            string resolvedOut;
            if (StaticPortraitGUI.Instance != null)
            {
                resolvedOut = StaticPortraitGUI.Instance.ResolvePathInternal(_stagedModOutPath, defaultOut);
            }
            else
            {
                resolvedOut = Path.Combine(effBase, string.IsNullOrEmpty(_stagedModOutPath) ? defaultOut : _stagedModOutPath).Replace('\\', '/');
            }

            bool outExists = false;
            int outCount = 0;
            try
            {
                outExists = Directory.Exists(resolvedOut);
                if (outExists)
                {
                    outCount = Directory.GetFiles(resolvedOut, "*.png").Length;
                }
            }
            catch { }

            if (_modOutStatusText != null)
            {
                _modOutStatusText.text = outExists
                    ? $"● 目录有效 (已转储 {outCount} 张原图)"
                    : "○ 目录不存在 (未创建时不会执行转储)";
                _modOutStatusText.color = outExists ? XingShuUITheme.Positive : XingShuUITheme.Danger;
            }
            if (_createModOutBtn != null)
            {
                _createModOutBtn.gameObject.SetActive(!outExists);
            }
            if (_modOutResolvedText != null)
            {
                _modOutResolvedText.text = $"解析绝对路径: {resolvedOut}";
            }
        }

        /// <summary>
        /// 每帧同步外部状态变更 (热键变更、开关变更、状态反馈计时器)
        /// 无变更时 0 字符串拼接、0 GC 分配、0 UI 写入
        /// 磁盘状态检测采用 1.0 秒节流，彻底消除 I/O 顿挫
        /// </summary>
        internal void UpdateSync()
        {
            if (StaticPortraitGUI.Instance == null) return;

            // 1. 热键显示同步
            KeyCode curHotkey = StaticPortraitGUI.Instance.CurrentHotkey;
            if (curHotkey != _lastObservedHotkey)
            {
                _lastObservedHotkey = curHotkey;
                if (_hotkeyBadgeText != null)
                {
                    _hotkeyBadgeText.text = $"[{curHotkey}]";
                }
            }

            // 2. 运行时开关同步
            bool curLoad = StaticPortraitGUI.Instance.EnableLoadMod;
            if (curLoad != _lastObservedLoadMod)
            {
                _lastObservedLoadMod = curLoad;
                if (_enableLoadToggle != null && _enableLoadToggle.isOn != curLoad)
                {
                    _suppressCallbacks = true;
                    try { _enableLoadToggle.isOn = curLoad; } finally { _suppressCallbacks = false; }
                }
                UpdateStatusIndicator(curLoad);
            }

            bool curDump = StaticPortraitGUI.Instance.EnableAutoDump;
            if (curDump != _lastObservedAutoDump)
            {
                _lastObservedAutoDump = curDump;
                if (_enableDumpToggle != null && _enableDumpToggle.isOn != curDump)
                {
                    _suppressCallbacks = true;
                    try { _enableDumpToggle.isOn = curDump; } finally { _suppressCallbacks = false; }
                }
            }

            // 3. 状态反馈消息更新
            string curMsg = StaticPortraitGUI.Instance.StatusMsg ?? "";
            if (curMsg != _lastObservedStatusMsg)
            {
                _lastObservedStatusMsg = curMsg;
                if (_statusMsgText != null)
                {
                    _statusMsgText.text = curMsg;
                }
            }

            // 4. 磁盘目录状态检测节流 (1.0 秒检测一次，规避高频 I/O 顿挫)
            if (Time.unscaledTime - _lastDiskCheckTime > 1.0f)
            {
                _lastDiskCheckTime = Time.unscaledTime;
                RefreshDirectoryStatuses();
            }
        }
    }
}
