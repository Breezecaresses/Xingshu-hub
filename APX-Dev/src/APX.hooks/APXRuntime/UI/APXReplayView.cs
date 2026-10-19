using System;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心 - 战绩回放快捷播放器 Modern UI 页面 (Phase 2C-2A)
    /// 纯粹的 View 层，100% 委托 ReplayPlayerGUI 底层解析与播放调度
    /// 遵循 R3.2 / R4 纯色半透拟态磨砂玻璃规范 (4 张紧凑卡片，无滚动条)
    /// </summary>
    internal sealed class APXReplayView
    {
        private readonly GameObject _root;
        private Text _statusIndicatorText;
        private Text _hotkeyBadgeText;

        // Card 2: 录像代码输入卡片
        private InputField _replayIdInput;
        private Button _pasteBtn;
        private Button _clearBtn;

        // Card 3: 播放控制卡片
        private Button _playStandardBtn;
        private Button _playDirectBtn;

        // Card 4: 状态反馈卡片
        private Text _statusMsgText;

        private bool _suppressCallbacks;
        private string _lastObservedReplayId = "";
        private string _lastObservedStatusMsg = "";
        private bool _lastObservedIsBusy;
        private KeyCode _lastObservedHotkey = KeyCode.None;

        internal GameObject Root => _root;

        internal APXReplayView(Transform parent)
        {
            _root = APXUIFactory.CreateUIObject("Page_Replay", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildUI();
        }

        private void BuildUI()
        {
            // 1. 特色功能卡片 (Feature Header Card: 76px, y: 0)
            BuildHeaderCard();

            // 2. 录像识别码输入卡片 (88px, y: -90px)
            BuildInputCard();

            // 3. 播放控制卡片 (72px, y: -192px)
            BuildControlCard();

            // 4. 状态反馈卡片 (56px, y: -278px)
            BuildStatusCard();

            // 初始状态同步
            UpdateSync(force: true);
        }

        private void BuildHeaderCard()
        {
            GameObject headerGo = APXUIFactory.CreateUIObject("FeatureHeaderCard", _root);
            RectTransform headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, 0f);
            headerRt.sizeDelta = new Vector2(0f, 76f);

            Sprite cfhSprite = APXUIAssetService.GetSprite("card_feature_header.png");
            Image headerBg = headerGo.AddComponent<Image>();
            headerBg.sprite = cfhSprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = APXUIAssetService.IsFallbackSprite(cfhSprite) ? APXUITheme.CardFeatureBase : Color.white;

            // 标题: 录像回放 (20px Bold, #F4F7FA)
            Text titleTxt = APXUIFactory.CreateText(headerGo, "Title", "录像回放", 20, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = titleTxt.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 10f);
            titleRt.sizeDelta = new Vector2(300f, 26f);

            // 说明文案 (13px, #B9C2CF)
            Text subTxt = APXUIFactory.CreateText(headerGo, "Subtitle", "本地 / 在线录像文件解析与复盘回放", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform subRt = subTxt.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(24f, -14f);
            subRt.sizeDelta = new Vector2(480f, 22f);

            // 状态指示文本 (● 准备就绪 / ○ 处理中...)
            _statusIndicatorText = APXUIFactory.CreateText(headerGo, "StatusIndicator", "● 准备就绪", 15, APXUITheme.Positive, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform statusRt = _statusIndicatorText.rectTransform;
            statusRt.anchorMin = new Vector2(1f, 0.5f);
            statusRt.anchorMax = new Vector2(1f, 0.5f);
            statusRt.pivot = new Vector2(1f, 0.5f);
            statusRt.anchoredPosition = new Vector2(-88f, 0f);
            statusRt.sizeDelta = new Vector2(110f, 26f);

            // 快捷键徽章 (绑定 ReplayPlayerGUI.CurrentHotkey, 圆角 8px)
            _lastObservedHotkey = ReplayPlayerGUI.CurrentHotkey;
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
        }

        private void BuildInputCard()
        {
            GameObject cardGo = APXUIFactory.CreateUIObject("Card_ReplayId", _root);
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0f, 1f);
            cardRt.anchorMax = new Vector2(1f, 1f);
            cardRt.pivot = new Vector2(0.5f, 1f);
            cardRt.anchoredPosition = new Vector2(0f, -90f);
            cardRt.sizeDelta = new Vector2(0f, 88f);

            Sprite cardBaseSprite = APXUIAssetService.GetSprite("card_base.png");
            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = cardBaseSprite;
            cardBg.type = Image.Type.Sliced;
            cardBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 标签: 对战识别码 / 录像文件名 (14px Bold, #F4F7FA)
            Text labelTxt = APXUIFactory.CreateText(cardGo, "Label", "对战识别码 / 录像文件名", 14, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform lRt = labelTxt.rectTransform;
            lRt.anchorMin = new Vector2(0f, 1f);
            lRt.anchorMax = new Vector2(1f, 1f);
            lRt.pivot = new Vector2(0f, 1f);
            lRt.anchoredPosition = new Vector2(24f, -14f);
            lRt.sizeDelta = new Vector2(-280f, 20f);

            // 辅助提示 (12px, #7F8998)
            Text tipTxt = APXUIFactory.CreateText(cardGo, "Tip", "(支持粘贴对战结算ID或本地录像文件名)", 12, APXUITheme.MutedText, TextAnchor.MiddleRight);
            RectTransform tipRt = tipTxt.rectTransform;
            tipRt.anchorMin = new Vector2(1f, 1f);
            tipRt.anchorMax = new Vector2(1f, 1f);
            tipRt.pivot = new Vector2(1f, 1f);
            tipRt.anchoredPosition = new Vector2(-24f, -14f);
            tipRt.sizeDelta = new Vector2(250f, 20f);

            // 输入框
            _replayIdInput = APXUIFactory.CreateInputField(
                cardGo,
                "Input_ReplayId",
                "请输入或粘贴录像识别码...",
                ReplayPlayerGUI.ReplayId,
                "ReplayPlayerGUI.ReplayId",
                delegate(string val)
                {
                    if (_suppressCallbacks) return;
                    ReplayPlayerGUI.ReplayId = val;
                },
                delegate(string val)
                {
                    if (_suppressCallbacks) return;
                    ReplayPlayerGUI.ReplayId = val;
                },
                13
            );
            RectTransform inputRt = _replayIdInput.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 1f);
            inputRt.anchorMax = new Vector2(1f, 1f);
            inputRt.pivot = new Vector2(0f, 1f);
            inputRt.anchoredPosition = new Vector2(24f, -42f);
            inputRt.sizeDelta = new Vector2(-172f, 34f);

            Sprite btnSec = APXUIAssetService.GetSprite("button_secondary.png");

            // 粘贴按钮
            _pasteBtn = APXUIFactory.CreateButton(
                cardGo,
                "Btn_Paste",
                "粘贴",
                btnSec,
                Color.white,
                APXUITheme.PrimaryText,
                delegate
                {
                    ReplayPlayerGUI.PasteReplayId();
                    if (_replayIdInput != null)
                    {
                        _replayIdInput.text = ReplayPlayerGUI.ReplayId;
                    }
                },
                13
            );
            RectTransform pasteRt = _pasteBtn.GetComponent<RectTransform>();
            pasteRt.anchorMin = new Vector2(1f, 1f);
            pasteRt.anchorMax = new Vector2(1f, 1f);
            pasteRt.pivot = new Vector2(1f, 1f);
            pasteRt.anchoredPosition = new Vector2(-88f, -42f);
            pasteRt.sizeDelta = new Vector2(60f, 34f);

            // 清空按钮
            _clearBtn = APXUIFactory.CreateButton(
                cardGo,
                "Btn_Clear",
                "清空",
                btnSec,
                Color.white,
                APXUITheme.SecondaryText,
                delegate
                {
                    ReplayPlayerGUI.ClearReplayId();
                    if (_replayIdInput != null)
                    {
                        _replayIdInput.text = "";
                    }
                },
                13
            );
            RectTransform clearRt = _clearBtn.GetComponent<RectTransform>();
            clearRt.anchorMin = new Vector2(1f, 1f);
            clearRt.anchorMax = new Vector2(1f, 1f);
            clearRt.pivot = new Vector2(1f, 1f);
            clearRt.anchoredPosition = new Vector2(-24f, -42f);
            clearRt.sizeDelta = new Vector2(56f, 34f);
        }

        private void BuildControlCard()
        {
            GameObject cardGo = APXUIFactory.CreateUIObject("Card_PlayActions", _root);
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0f, 1f);
            cardRt.anchorMax = new Vector2(1f, 1f);
            cardRt.pivot = new Vector2(0.5f, 1f);
            cardRt.anchoredPosition = new Vector2(0f, -192f);
            cardRt.sizeDelta = new Vector2(0f, 72f);

            Sprite cardBaseSprite = APXUIAssetService.GetSprite("card_base.png");
            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = cardBaseSprite;
            cardBg.type = Image.Type.Sliced;
            cardBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            Sprite btnPri = APXUIAssetService.GetSprite("button_primary.png");
            Sprite btnSec = APXUIAssetService.GetSprite("button_secondary.png");

            // 主按钮: 播放回放 (标准流程)
            _playStandardBtn = APXUIFactory.CreateButton(
                cardGo,
                "Btn_PlayStandard",
                "播放回放 (标准流程)",
                btnPri,
                Color.white,
                Color.white,
                delegate
                {
                    ReplayPlayerGUI.RequestPlayStandard();
                },
                14
            );
            RectTransform psRt = _playStandardBtn.GetComponent<RectTransform>();
            psRt.anchorMin = new Vector2(0f, 0.5f);
            psRt.anchorMax = new Vector2(0.5f, 0.5f);
            psRt.pivot = new Vector2(0.5f, 0.5f);
            psRt.offsetMin = new Vector2(24f, -20f);
            psRt.offsetMax = new Vector2(-8f, 20f);

            // 次按钮: 直接进入 (跳过结算)
            _playDirectBtn = APXUIFactory.CreateButton(
                cardGo,
                "Btn_PlayDirect",
                "直接进入 (跳过结算)",
                btnSec,
                Color.white,
                APXUITheme.PrimaryText,
                delegate
                {
                    ReplayPlayerGUI.RequestPlayDirect();
                },
                14
            );
            RectTransform pdRt = _playDirectBtn.GetComponent<RectTransform>();
            pdRt.anchorMin = new Vector2(0.5f, 0.5f);
            pdRt.anchorMax = new Vector2(1f, 0.5f);
            pdRt.pivot = new Vector2(0.5f, 0.5f);
            pdRt.offsetMin = new Vector2(8f, -20f);
            pdRt.offsetMax = new Vector2(-24f, 20f);
        }

        private void BuildStatusCard()
        {
            GameObject cardGo = APXUIFactory.CreateUIObject("Card_StatusFeedback", _root);
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0f, 1f);
            cardRt.anchorMax = new Vector2(1f, 1f);
            cardRt.pivot = new Vector2(0.5f, 1f);
            cardRt.anchoredPosition = new Vector2(0f, -278f);
            cardRt.sizeDelta = new Vector2(0f, 56f);

            Sprite cardBaseSprite = APXUIAssetService.GetSprite("card_base.png");
            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = cardBaseSprite;
            cardBg.type = Image.Type.Sliced;
            cardBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 状态标签
            Text labelTxt = APXUIFactory.CreateText(cardGo, "Label", "状态:", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform lRt = labelTxt.rectTransform;
            lRt.anchorMin = new Vector2(0f, 0.5f);
            lRt.anchorMax = new Vector2(0f, 0.5f);
            lRt.pivot = new Vector2(0f, 0.5f);
            lRt.anchoredPosition = new Vector2(24f, 0f);
            lRt.sizeDelta = new Vector2(45f, 24f);

            // 状态文本
            _statusMsgText = APXUIFactory.CreateText(cardGo, "StatusMessage", "<color=#7F8998>等待输入录像代码并开始播放...</color>", 13, APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            _statusMsgText.supportRichText = true;
            RectTransform smRt = _statusMsgText.rectTransform;
            smRt.anchorMin = new Vector2(0f, 0.5f);
            smRt.anchorMax = new Vector2(1f, 0.5f);
            smRt.pivot = new Vector2(0f, 0.5f);
            smRt.offsetMin = new Vector2(72f, -14f);
            smRt.offsetMax = new Vector2(-24f, 14f);
        }

        internal void UpdateSync(bool force = false)
        {
            string curId = ReplayPlayerGUI.ReplayId;
            string curMsg = ReplayPlayerGUI.StatusMsg;
            bool curBusy = ReplayPlayerGUI.IsBusy;
            KeyCode curHotkey = ReplayPlayerGUI.CurrentHotkey;

            // 1. 同步热键徽章
            if (force || curHotkey != _lastObservedHotkey)
            {
                _lastObservedHotkey = curHotkey;
                if (_hotkeyBadgeText != null)
                {
                    _hotkeyBadgeText.text = $"[{curHotkey}]";
                }
            }

            // 2. 同步繁忙状态与指示器
            if (force || curBusy != _lastObservedIsBusy)
            {
                _lastObservedIsBusy = curBusy;
                if (_statusIndicatorText != null)
                {
                    _statusIndicatorText.text = curBusy ? "○ 处理中..." : "● 准备就绪";
                    _statusIndicatorText.color = curBusy ? APXUITheme.SecondaryText : APXUITheme.Positive;
                }

                if (_replayIdInput != null)
                {
                    _replayIdInput.interactable = !curBusy;
                }
                if (_pasteBtn != null)
                {
                    _pasteBtn.interactable = !curBusy;
                }
                if (_clearBtn != null)
                {
                    _clearBtn.interactable = !curBusy;
                }
            }

            // 3. 播放按钮可交互性门禁 (非繁忙且有非空 ID，对齐 Legacy: !_isBusy && !string.IsNullOrEmpty(_replayId))
            bool canPlay = !curBusy && !string.IsNullOrEmpty(curId);
            if (_playStandardBtn != null && _playStandardBtn.interactable != canPlay)
            {
                _playStandardBtn.interactable = canPlay;
            }
            if (_playDirectBtn != null && _playDirectBtn.interactable != canPlay)
            {
                _playDirectBtn.interactable = canPlay;
            }

            // 4. 输入框外部文本同步 (来自粘贴或清空操作)
            if (force || curId != _lastObservedReplayId)
            {
                _lastObservedReplayId = curId;
                if (_replayIdInput != null && _replayIdInput.text != curId)
                {
                    _suppressCallbacks = true;
                    _replayIdInput.text = curId;
                    _suppressCallbacks = false;
                }
            }

            // 5. 状态反馈消息同步
            if (force || curMsg != _lastObservedStatusMsg)
            {
                _lastObservedStatusMsg = curMsg;
                if (_statusMsgText != null)
                {
                    if (string.IsNullOrEmpty(curMsg))
                    {
                        _statusMsgText.text = "<color=#7F8998>等待输入录像代码并开始播放...</color>";
                    }
                    else
                    {
                        _statusMsgText.text = curMsg;
                    }
                }
            }
        }
    }
}
