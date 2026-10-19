using System;
using APXRuntime.Core;
using APXRuntime.Social;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心 - 战局守护与原生快捷通信 (ActionTimeGuard & Native Social) Modern UI 页面 (Phase 2D-5B)
    /// 遵循 Route A 迁移路线：纯 UGUI View，绝不直连网络、不创建 Socket、不直接操作 RPC 回调
    /// 彻底取代陈旧 IMGUI Window (987656)，以现代卡片化结构呈现状态监控、主开关与原生快捷通信
    /// </summary>
    internal sealed class APXActionGuardView
    {
        private readonly GameObject _root;

        // Card 1: 顶部主功能与状态卡片
        private Toggle _enableToggle;
        private Image _statusBadgeBg;
        private Text _statusBadgeText;
        private Text _statusDetailText;

        // Card 2: 监控看板指标文本
        private Text _rpcStatusText;
        private Text _battleStatusText;
        private Text _interceptCountText;
        private Text _lastInterceptTimeText;
        private Text _reconnectCountText;
        private Text _antiCheatStatusText;

        // Card 4: 原生快捷通信卡片
        private Toggle _autoThanksToggle;
        private Toggle _autoPraiseToggle;

        // 缓存观测值 (0 GC / 仅状态变化时更新 UI)
        private float _lastSyncTime;
        private bool _lastObservedEnabled;
        private bool _lastObservedBattle;
        private bool _lastObservedHook;
        private int _lastObservedInterceptCount = -1;
        private int _lastObservedReconnectCount = -1;
        private string _lastObservedInterceptTime = "";
        private long _lastObservedRoomId = -1L;
        private bool _suppressCallbacks = false;

        internal GameObject Root => _root;

        internal APXActionGuardView(Transform parent)
        {
            _root = APXUIFactory.CreateUIObject("Page_ActionGuard", parent.gameObject);
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

            // ==========================================
            // 1. 顶部主功能与状态卡片 (Card 1: 120px, y: 0)
            // ==========================================
            GameObject card1 = APXUIFactory.CreateUIObject("Card_HeaderStatus", _root);
            RectTransform c1Rt = card1.GetComponent<RectTransform>();
            c1Rt.anchorMin = new Vector2(0f, 1f);
            c1Rt.anchorMax = new Vector2(1f, 1f);
            c1Rt.pivot = new Vector2(0.5f, 1f);
            c1Rt.anchoredPosition = new Vector2(0f, 0f);
            c1Rt.sizeDelta = new Vector2(0f, 110f);

            Image c1Bg = card1.AddComponent<Image>();
            c1Bg.sprite = cfhSprite;
            c1Bg.type = Image.Type.Sliced;
            c1Bg.color = APXUIAssetService.IsFallbackSprite(cfhSprite) ? APXUITheme.CardFeatureBase : Color.white;

            // 标题 "战局守护"
            Text titleText = APXUIFactory.CreateText(card1, "Title", "战局守护", 20, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ttRt = titleText.rectTransform;
            ttRt.anchorMin = new Vector2(0f, 1f);
            ttRt.anchorMax = new Vector2(0f, 1f);
            ttRt.pivot = new Vector2(0f, 1f);
            ttRt.anchoredPosition = new Vector2(24f, -14f);
            ttRt.sizeDelta = new Vector2(240f, 26f);

            // 副标题 "超时保护 · 战局状态恢复"
            Text subText = APXUIFactory.CreateText(card1, "Subtitle", "超时保护 · 战局状态恢复", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subText.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -40f);
            stRt.sizeDelta = new Vector2(360f, 20f);

            // 状态徽章 (右上角)
            GameObject badgeGo = APXUIFactory.CreateUIObject("StatusBadge", card1);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot = new Vector2(1f, 1f);
            badgeRt.anchoredPosition = new Vector2(-24f, -14f);
            badgeRt.sizeDelta = new Vector2(180f, 26f);

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

            // 启用战局守护总开关 (Toggle, 左下角)
            _enableToggle = APXUIFactory.CreateToggle(card1, "EnableToggle", "启用战局守护", AutoResetActionTimeGUI.IsEnabled, OnToggleValueChanged);
            RectTransform toggleRt = _enableToggle.GetComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(0f, 0f);
            toggleRt.anchorMax = new Vector2(0f, 0f);
            toggleRt.pivot = new Vector2(0f, 0f);
            toggleRt.anchoredPosition = new Vector2(24f, 12f);
            toggleRt.sizeDelta = new Vector2(220f, 32f);

            // 状态说明文字 (右下角)
            _statusDetailText = APXUIFactory.CreateText(card1, "StatusDetail", "战局中自动拦截服务端超时踢出协议并恢复同步；设置自动保存", 12, APXUITheme.MutedText, TextAnchor.MiddleRight);
            RectTransform sdtRt = _statusDetailText.rectTransform;
            sdtRt.anchorMin = new Vector2(1f, 0f);
            sdtRt.anchorMax = new Vector2(1f, 0f);
            sdtRt.pivot = new Vector2(1f, 0f);
            sdtRt.anchoredPosition = new Vector2(-24f, 18f);
            sdtRt.sizeDelta = new Vector2(400f, 20f);

            // ==========================================
            // 2. 防护状态看板卡片 (Card 2: 160px, y: -118)
            // ==========================================
            GameObject card2 = APXUIFactory.CreateUIObject("Card_Dashboard", _root);
            RectTransform c2Rt = card2.GetComponent<RectTransform>();
            c2Rt.anchorMin = new Vector2(0f, 1f);
            c2Rt.anchorMax = new Vector2(1f, 1f);
            c2Rt.pivot = new Vector2(0.5f, 1f);
            c2Rt.anchoredPosition = new Vector2(0f, -118f);
            c2Rt.sizeDelta = new Vector2(0f, 160f);

            Image c2Bg = card2.AddComponent<Image>();
            c2Bg.sprite = cardBaseSprite;
            c2Bg.type = Image.Type.Sliced;
            c2Bg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 卡片标题
            Text c2Title = APXUIFactory.CreateText(card2, "Card2Title", "防护状态看板", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform c2tRt = c2Title.rectTransform;
            c2tRt.anchorMin = new Vector2(0f, 1f);
            c2tRt.anchorMax = new Vector2(0f, 1f);
            c2tRt.pivot = new Vector2(0f, 1f);
            c2tRt.anchoredPosition = new Vector2(20f, -12f);
            c2tRt.sizeDelta = new Vector2(260f, 22f);

            // 列 1 (左侧指标, x: 20, width: 380)
            BuildMetricRow(card2, "超时协议保护", out _rpcStatusText, 20f, -38f, 380f);
            BuildMetricRow(card2, "累计拦截次数", out _interceptCountText, 20f, -68f, 380f);
            BuildMetricRow(card2, "累计恢复同步", out _reconnectCountText, 20f, -98f, 380f);

            // 列 2 (右侧指标, x: 440, width: 380)
            BuildMetricRow(card2, "战局运行状态", out _battleStatusText, 440f, -38f, 380f);
            BuildMetricRow(card2, "最近拦截时间", out _lastInterceptTimeText, 440f, -68f, 380f);
            BuildMetricRow(card2, "防作弊与挂机", out _antiCheatStatusText, 440f, -98f, 380f);

            // 看板底部说明 (11px, MutedText)
            Text noteText = APXUIFactory.CreateText(card2, "DashboardNote", "核心防护钩子由本地战局守护维持，对局异常时自动执行无感状态同步", 11, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform ntRt = noteText.rectTransform;
            ntRt.anchorMin = new Vector2(0f, 0f);
            ntRt.anchorMax = new Vector2(1f, 0f);
            ntRt.pivot = new Vector2(0.5f, 0f);
            ntRt.anchoredPosition = new Vector2(0f, 8f);
            ntRt.sizeDelta = new Vector2(-40f, 18f);

            // ==========================================
            // 3. 运行机制说明卡片 (Card 3: 92px, y: -286)
            // ==========================================
            GameObject card3 = APXUIFactory.CreateUIObject("Card_Mechanism", _root);
            RectTransform c3Rt = card3.GetComponent<RectTransform>();
            c3Rt.anchorMin = new Vector2(0f, 1f);
            c3Rt.anchorMax = new Vector2(1f, 1f);
            c3Rt.pivot = new Vector2(0.5f, 1f);
            c3Rt.anchoredPosition = new Vector2(0f, -286f);
            c3Rt.sizeDelta = new Vector2(0f, 92f);

            Image c3Bg = card3.AddComponent<Image>();
            c3Bg.sprite = cardBaseSprite;
            c3Bg.type = Image.Type.Sliced;
            c3Bg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            Text c3Title = APXUIFactory.CreateText(card3, "Card3Title", "运行机制说明", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform c3tRt = c3Title.rectTransform;
            c3tRt.anchorMin = new Vector2(0f, 1f);
            c3tRt.anchorMax = new Vector2(0f, 1f);
            c3tRt.pivot = new Vector2(0f, 1f);
            c3tRt.anchoredPosition = new Vector2(20f, -12f);
            c3tRt.sizeDelta = new Vector2(260f, 22f);

            string mechanismContent =
                "• 超时协议拦截：当对局出现超时踢出指令时，战局守护按既定逻辑拦截并请求房间全量无感同步。\n" +
                "• 战局无感拉回：若底层网络中断，将自动调用游戏原生安全重连流恢复战局，杜绝误退回登录界面。\n" +
                "• 零额外网络开销：局外大厅与主菜单保持静默待机，绝不发送任何自建网络请求，保护账号安全。";

            Text mechTxt = APXUIFactory.CreateText(card3, "MechanismContent", mechanismContent, 11, APXUITheme.SecondaryText, TextAnchor.UpperLeft);
            RectTransform mtRt = mechTxt.rectTransform;
            mtRt.anchorMin = new Vector2(0f, 0f);
            mtRt.anchorMax = new Vector2(1f, 1f);
            mtRt.pivot = new Vector2(0f, 1f);
            mtRt.offsetMin = new Vector2(20f, 6f);
            mtRt.offsetMax = new Vector2(-20f, -34f);

            // ==========================================
            // 4A. 原生快捷通信 - 自动致谢独立卡片 (Card 4A: 68px, y: -386)
            // ==========================================
            GameObject cardThanks = APXUIFactory.CreateUIObject("Card_AutoThanks", _root);
            RectTransform ctRt = cardThanks.GetComponent<RectTransform>();
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(1f, 1f);
            ctRt.pivot = new Vector2(0.5f, 1f);
            ctRt.anchoredPosition = new Vector2(0f, -386f);
            ctRt.sizeDelta = new Vector2(0f, 68f);

            Image ctBg = cardThanks.AddComponent<Image>();
            ctBg.sprite = cardBaseSprite;
            ctBg.type = Image.Type.Sliced;
            ctBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            Text ctTitle = APXUIFactory.CreateText(cardThanks, "ThanksTitle", "自动致谢", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform cttRt = ctTitle.rectTransform;
            cttRt.anchorMin = new Vector2(0f, 1f);
            cttRt.anchorMax = new Vector2(0f, 1f);
            cttRt.pivot = new Vector2(0f, 1f);
            cttRt.anchoredPosition = new Vector2(20f, -12f);
            cttRt.sizeDelta = new Vector2(200f, 22f);

            Text ctDesc = APXUIFactory.CreateText(cardThanks, "ThanksDesc", "收到卡牌 / 星币 / 治疗时自动触发游戏原生致谢回复", 11, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform ctdRt = ctDesc.rectTransform;
            ctdRt.anchorMin = new Vector2(0f, 1f);
            ctdRt.anchorMax = new Vector2(0f, 1f);
            ctdRt.pivot = new Vector2(0f, 1f);
            ctdRt.anchoredPosition = new Vector2(20f, -38f);
            ctdRt.sizeDelta = new Vector2(500f, 20f);

            _autoThanksToggle = APXUIFactory.CreateToggle(cardThanks, "AutoThanksToggle", "开启", APXAutoThanks.IsEnabled, (v) => { if (!_suppressCallbacks) APXAutoThanks.IsEnabled = v; });
            RectTransform atRt = _autoThanksToggle.GetComponent<RectTransform>();
            atRt.anchorMin = new Vector2(1f, 0.5f);
            atRt.anchorMax = new Vector2(1f, 0.5f);
            atRt.pivot = new Vector2(1f, 0.5f);
            atRt.anchoredPosition = new Vector2(-20f, 0f);
            atRt.sizeDelta = new Vector2(130f, 32f);

            // ==========================================
            // 4B. 原生快捷通信 - 局末全赞独立卡片 (Card 4B: 68px, y: -464)
            // ==========================================
            GameObject cardPraise = APXUIFactory.CreateUIObject("Card_AutoPraise", _root);
            RectTransform cpRt = cardPraise.GetComponent<RectTransform>();
            cpRt.anchorMin = new Vector2(0f, 1f);
            cpRt.anchorMax = new Vector2(1f, 1f);
            cpRt.pivot = new Vector2(0.5f, 1f);
            cpRt.anchoredPosition = new Vector2(0f, -464f);
            cpRt.sizeDelta = new Vector2(0f, 68f);

            Image cpBg = cardPraise.AddComponent<Image>();
            cpBg.sprite = cardBaseSprite;
            cpBg.type = Image.Type.Sliced;
            cpBg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            Text cpTitle = APXUIFactory.CreateText(cardPraise, "PraiseTitle", "局末全赞", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform cptRt = cpTitle.rectTransform;
            cptRt.anchorMin = new Vector2(0f, 1f);
            cptRt.anchorMax = new Vector2(0f, 1f);
            cptRt.pivot = new Vector2(0f, 1f);
            cptRt.anchoredPosition = new Vector2(20f, -12f);
            cptRt.sizeDelta = new Vector2(200f, 22f);

            Text cpDesc = APXUIFactory.CreateText(cardPraise, "PraiseDesc", "对局结束时自动为真人队友点赞排除自身与人机", 11, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform cpdRt = cpDesc.rectTransform;
            cpdRt.anchorMin = new Vector2(0f, 1f);
            cpdRt.anchorMax = new Vector2(0f, 1f);
            cpdRt.pivot = new Vector2(0f, 1f);
            cpdRt.anchoredPosition = new Vector2(20f, -38f);
            cpdRt.sizeDelta = new Vector2(500f, 20f);

            _autoPraiseToggle = APXUIFactory.CreateToggle(cardPraise, "AutoPraiseToggle", "开启", APXAutoPraise.IsEnabled, (v) => { if (!_suppressCallbacks) APXAutoPraise.IsEnabled = v; });
            RectTransform apRt = _autoPraiseToggle.GetComponent<RectTransform>();
            apRt.anchorMin = new Vector2(1f, 0.5f);
            apRt.anchorMax = new Vector2(1f, 0.5f);
            apRt.pivot = new Vector2(1f, 0.5f);
            apRt.anchoredPosition = new Vector2(-20f, 0f);
            apRt.sizeDelta = new Vector2(130f, 32f);

            // 底部说明附注 (Track B 算法就绪)
            Text socialDesc = APXUIFactory.CreateText(_root, "SocialDesc", "• 怪物索敌与路径预测算法已就绪 (Track B 纯算法就绪 · 零游戏AI修改 · 零网络开销)", 11, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform sdRt = socialDesc.rectTransform;
            sdRt.anchorMin = new Vector2(0f, 1f);
            sdRt.anchorMax = new Vector2(1f, 1f);
            sdRt.pivot = new Vector2(0f, 1f);
            sdRt.anchoredPosition = new Vector2(20f, -542f);
            sdRt.sizeDelta = new Vector2(700f, 20f);
        }

        private void BuildMetricRow(GameObject parent, string label, out Text valueText, float x, float y, float width)
        {
            GameObject rowGo = APXUIFactory.CreateUIObject("MetricRow_" + label, parent);
            RectTransform rt = rowGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, 26f);

            Text lbl = APXUIFactory.CreateText(rowGo, "Label", label + "：", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform lRt = lbl.rectTransform;
            lRt.anchorMin = new Vector2(0f, 0f);
            lRt.anchorMax = new Vector2(0f, 1f);
            lRt.pivot = new Vector2(0f, 0.5f);
            lRt.anchoredPosition = Vector2.zero;
            lRt.sizeDelta = new Vector2(150f, 0f);

            valueText = APXUIFactory.CreateText(rowGo, "Value", "-", 13, APXUITheme.PrimaryText, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform vRt = valueText.rectTransform;
            vRt.anchorMin = new Vector2(1f, 0f);
            vRt.anchorMax = new Vector2(1f, 1f);
            vRt.pivot = new Vector2(1f, 0.5f);
            vRt.anchoredPosition = Vector2.zero;
            vRt.sizeDelta = new Vector2(220f, 0f);
        }

        private void OnToggleValueChanged(bool value)
        {
            if (_suppressCallbacks) return;
            AutoResetActionTimeGUI.SetEnabled(value);
        }

        internal void UpdateSync()
        {
            // 节流: 每 0.25 秒低频轮询，避免高频字符串拼接与 UI 刷新
            if (Time.unscaledTime - _lastSyncTime < 0.25f)
            {
                return;
            }
            _lastSyncTime = Time.unscaledTime;

            bool enabled = AutoResetActionTimeGUI.IsEnabled;
            bool isBattle = AutoResetActionTimeGUI.IsInBattle;
            bool hookRegistered = AutoResetActionTimeGUI.IsHookRegistered;
            int interceptCount = AutoResetActionTimeGUI.InterceptedKickCount;
            int reconnectCount = AutoResetActionTimeGUI.ReconnectSuccessCount;
            string lastIntercept = AutoResetActionTimeGUI.LastInterceptTime;
            long roomId = AutoResetActionTimeGUI.CurrentRoomId;

            // 1. 同步 Toggle 状态
            if (enabled != _lastObservedEnabled)
            {
                _lastObservedEnabled = enabled;
                if (_enableToggle != null && _enableToggle.isOn != enabled)
                {
                    _suppressCallbacks = true;
                    _enableToggle.isOn = enabled;
                    _suppressCallbacks = false;
                }
            }

            // 2. 同步状态徽章与主卡片文字 (四种离散状态)
            bool stateChanged = (enabled != _lastObservedEnabled) || (isBattle != _lastObservedBattle) || (hookRegistered != _lastObservedHook);
            if (stateChanged)
            {
                _lastObservedBattle = isBattle;
                _lastObservedHook = hookRegistered;

                if (_statusBadgeText != null)
                {
                    if (!enabled)
                    {
                        _statusBadgeText.text = "已关闭";
                        _statusBadgeText.color = APXUITheme.MutedText;
                        if (_statusBadgeBg != null) _statusBadgeBg.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);
                    }
                    else if (!isBattle)
                    {
                        _statusBadgeText.text = "已就绪 · 等待对局";
                        _statusBadgeText.color = APXUITheme.NumericAccent;
                        if (_statusBadgeBg != null) _statusBadgeBg.color = new Color(0.25f, 0.20f, 0.08f, 0.85f);
                    }
                    else
                    {
                        _statusBadgeText.text = hookRegistered ? "战局中保护生效" : "保护已启用 · 等待挂接";
                        _statusBadgeText.color = APXUITheme.Positive;
                        if (_statusBadgeBg != null) _statusBadgeBg.color = new Color(0.08f, 0.22f, 0.16f, 0.85f);
                    }
                }
            }

            // 3. 看板指标同步 (仅当值变更时更新 Text，严格 0 GC)
            if (_rpcStatusText != null)
            {
                string rpcText = hookRegistered ? "已就绪" : (enabled ? "等待就绪" : "未就绪");
                Color rpcColor = hookRegistered ? APXUITheme.Positive : (enabled ? APXUITheme.NumericAccent : APXUITheme.MutedText);
                if (_rpcStatusText.text != rpcText)
                {
                    _rpcStatusText.text = rpcText;
                    _rpcStatusText.color = rpcColor;
                }
            }

            if (_battleStatusText != null && (isBattle != _lastObservedBattle || roomId != _lastObservedRoomId))
            {
                _lastObservedRoomId = roomId;
                string battleText = isBattle ? (roomId > 0 ? $"对局中 (ID: {roomId})" : "战斗准备中") : "当前无有效战局";
                Color battleColor = isBattle ? new Color(0.22f, 0.74f, 0.97f) : APXUITheme.MutedText;
                if (_battleStatusText.text != battleText)
                {
                    _battleStatusText.text = battleText;
                    _battleStatusText.color = battleColor;
                }
            }

            if (interceptCount != _lastObservedInterceptCount)
            {
                _lastObservedInterceptCount = interceptCount;
                if (_interceptCountText != null)
                {
                    _interceptCountText.text = $"{interceptCount} 次";
                }
            }

            if (lastIntercept != _lastObservedInterceptTime)
            {
                _lastObservedInterceptTime = lastIntercept;
                if (_lastInterceptTimeText != null)
                {
                    _lastInterceptTimeText.text = string.IsNullOrEmpty(lastIntercept) ? "无" : lastIntercept;
                }
            }

            if (reconnectCount != _lastObservedReconnectCount)
            {
                _lastObservedReconnectCount = reconnectCount;
                if (_reconnectCountText != null)
                {
                    _reconnectCountText.text = $"{reconnectCount} 次";
                }
            }

            if (_antiCheatStatusText != null)
            {
                string acText = enabled ? "已接管保护" : "未启用";
                Color acColor = enabled ? APXUITheme.Positive : APXUITheme.MutedText;
                if (_antiCheatStatusText.text != acText)
                {
                    _antiCheatStatusText.text = acText;
                    _antiCheatStatusText.color = acColor;
                }
            }

            // 4. 同步原生快捷通信 Toggle 状态
            if (_autoThanksToggle != null && _autoThanksToggle.isOn != APXAutoThanks.IsEnabled)
            {
                _suppressCallbacks = true;
                _autoThanksToggle.isOn = APXAutoThanks.IsEnabled;
                _suppressCallbacks = false;
            }

            if (_autoPraiseToggle != null && _autoPraiseToggle.isOn != APXAutoPraise.IsEnabled)
            {
                _suppressCallbacks = true;
                _autoPraiseToggle.isOn = APXAutoPraise.IsEnabled;
                _suppressCallbacks = false;
            }
        }
    }
}
