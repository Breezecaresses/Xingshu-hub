using System;
using System.Text;
using APXRuntime.Core;
using APXRuntime.MatchPort;
using APXRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心 - 对局数据 (Battle Stats) Modern UI 页面 (Phase 2D-7A)
    /// 纯 View 表现层组件：严格只读现有对局账本 (MatchLedger / StatsWidgetPresenter)，
    /// 绝不重复订阅网络报文，绝不复制统计引擎，与局内轻量悬浮挂件构成双表面 (Dual-Surface) 架构。
    /// </summary>
    internal sealed class APXBattleStatsView
    {
        private readonly GameObject _root;

        // Card 1: 顶部主功能与状态卡片
        private Image _statusBadgeBg;
        private Text _statusBadgeText;
        private Text _statusDetailText;
        private Toggle _floatingHudToggle;

        // Card 2: 对局实时数据表格卡片
        private readonly Text[,] _cellTexts = new Text[4, 5];
        private Text _matchKeyText;

        // Card 3: 统计指标口径说明卡片
        private Text _legendText;

        // 观测缓存与有界轮询 (仅状态与数值变更时触发 UI 写入)
        private float _nextRefreshTime;
        private string _lastObservedState = "";
        private bool _lastObservedHudVisible;
        private string _lastObservedKey = "";
        private readonly string[,] _lastObservedCells = new string[4, 5];

        internal GameObject Root => _root;

        internal APXBattleStatsView(Transform parent)
        {
            _root = APXUIFactory.CreateUIObject("Page_BattleStats", parent.gameObject);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildUI();
        }

        private void BuildUI()
        {
            Sprite cfhSprite = APXUIAssetService.GetSprite("card_feature_header.png");
            Sprite cardBaseSprite = APXUIAssetService.GetSprite("card_base.png");

            // ==========================================
            // 1. 顶部主功能与状态卡片 (Card 1: 115px, y: 0)
            // ==========================================
            GameObject card1 = APXUIFactory.CreateUIObject("Card_HeaderStatus", _root);
            RectTransform c1Rt = card1.GetComponent<RectTransform>();
            c1Rt.anchorMin = new Vector2(0f, 1f);
            c1Rt.anchorMax = new Vector2(1f, 1f);
            c1Rt.pivot = new Vector2(0.5f, 1f);
            c1Rt.anchoredPosition = new Vector2(0f, 0f);
            c1Rt.sizeDelta = new Vector2(0f, 115f);

            Image c1Bg = card1.AddComponent<Image>();
            c1Bg.sprite = cfhSprite;
            c1Bg.type = Image.Type.Sliced;
            c1Bg.color = APXUIAssetService.IsFallbackSprite(cfhSprite) ? APXUITheme.CardFeatureBase : Color.white;

            // 标题 "对局数据"
            Text titleText = APXUIFactory.CreateText(card1, "Title", "对局数据", 20, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ttRt = titleText.rectTransform;
            ttRt.anchorMin = new Vector2(0f, 1f);
            ttRt.anchorMax = new Vector2(0f, 1f);
            ttRt.pivot = new Vector2(0f, 1f);
            ttRt.anchoredPosition = new Vector2(24f, -16f);
            ttRt.sizeDelta = new Vector2(240f, 26f);

            // 副标题
            Text subText = APXUIFactory.CreateText(card1, "Subtitle", "当前对局实时统计 · 击倒 / 伤害 / 步数 / 转账 实时账本", 13, APXUITheme.SecondaryText, TextAnchor.MiddleLeft);
            RectTransform stRt = subText.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(24f, -44f);
            stRt.sizeDelta = new Vector2(400f, 20f);

            // 状态徽章 (右上角)
            GameObject badgeGo = APXUIFactory.CreateUIObject("StatusBadge", card1);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot = new Vector2(1f, 1f);
            badgeRt.anchoredPosition = new Vector2(-24f, -18f);
            badgeRt.sizeDelta = new Vector2(120f, 28f);

            _statusBadgeBg = badgeGo.AddComponent<Image>();
            _statusBadgeBg.sprite = cardBaseSprite;
            _statusBadgeBg.type = Image.Type.Sliced;
            _statusBadgeBg.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);

            _statusBadgeText = APXUIFactory.CreateText(badgeGo, "Text", "等待有效对局", 12, APXUITheme.SecondaryText, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform sbtRt = _statusBadgeText.rectTransform;
            sbtRt.anchorMin = Vector2.zero;
            sbtRt.anchorMax = Vector2.one;
            sbtRt.offsetMin = Vector2.zero;
            sbtRt.offsetMax = Vector2.zero;

            // 状态明细提示
            _statusDetailText = APXUIFactory.CreateText(card1, "StatusDetail", "未在战斗中，进入房间并开局后自动激活监听", 12, APXUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform sdtRt = _statusDetailText.rectTransform;
            sdtRt.anchorMin = new Vector2(0f, 0f);
            sdtRt.anchorMax = new Vector2(1f, 0f);
            sdtRt.pivot = new Vector2(0f, 0f);
            sdtRt.anchoredPosition = new Vector2(24f, 14f);
            sdtRt.sizeDelta = new Vector2(-300f, 20f);

            // 局内轻量悬浮挂件自动显示偏好开关 (Card 1 右下角)
            bool initAutoShow = BattleStatsOverlay.AutoShowPreference;
            _floatingHudToggle = APXUIFactory.CreateToggle(card1, "Toggle_FloatingHud", "进入对局自动显示挂件", initAutoShow, OnFloatingHudToggled);
            RectTransform fhtRt = _floatingHudToggle.GetComponent<RectTransform>();
            fhtRt.anchorMin = new Vector2(1f, 0f);
            fhtRt.anchorMax = new Vector2(1f, 0f);
            fhtRt.pivot = new Vector2(1f, 0f);
            fhtRt.anchoredPosition = new Vector2(-24f, 12f);
            fhtRt.sizeDelta = new Vector2(240f, 26f);

            // ==========================================
            // 2. 对局实时数据表格卡片 (Card 2: 300px, y: -130px)
            // ==========================================
            GameObject card2 = APXUIFactory.CreateUIObject("Card_StatsTable", _root);
            RectTransform c2Rt = card2.GetComponent<RectTransform>();
            c2Rt.anchorMin = new Vector2(0f, 1f);
            c2Rt.anchorMax = new Vector2(1f, 1f);
            c2Rt.pivot = new Vector2(0.5f, 1f);
            c2Rt.anchoredPosition = new Vector2(0f, -130f);
            c2Rt.sizeDelta = new Vector2(0f, 300f);

            Image c2Bg = card2.AddComponent<Image>();
            c2Bg.sprite = cardBaseSprite;
            c2Bg.type = Image.Type.Sliced;
            c2Bg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            // 表格标题与当前对局键
            Text tableHeader = APXUIFactory.CreateText(card2, "TableHeader", "实时战局统计", 15, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform thRt = tableHeader.rectTransform;
            thRt.anchorMin = new Vector2(0f, 1f);
            thRt.anchorMax = new Vector2(0f, 1f);
            thRt.pivot = new Vector2(0f, 1f);
            thRt.anchoredPosition = new Vector2(20f, -14f);
            thRt.sizeDelta = new Vector2(180f, 22f);

            _matchKeyText = APXUIFactory.CreateText(card2, "MatchKey", "对局键: —", 12, APXUITheme.MutedText, TextAnchor.MiddleRight);
            RectTransform mkRt = _matchKeyText.rectTransform;
            mkRt.anchorMin = new Vector2(1f, 1f);
            mkRt.anchorMax = new Vector2(1f, 1f);
            mkRt.pivot = new Vector2(1f, 1f);
            mkRt.anchoredPosition = new Vector2(-20f, -14f);
            mkRt.sizeDelta = new Vector2(300f, 22f);

            // 表头定义: 角色 (200px), 击倒 (100px), 伤害 (120px), 步数 (100px), 转账 (120px)
            string[] colTitles = new string[5] { "角色", "击倒", "伤害", "步数", "转账" };
            float[] colWidths = new float[5] { 220f, 110f, 130f, 110f, 130f };
            float[] colX = new float[5] { 24f, 260f, 385f, 530f, 655f };

            // 表头背景条
            GameObject headerBar = APXUIFactory.CreateUIObject("HeaderBar", card2);
            RectTransform hbRt = headerBar.GetComponent<RectTransform>();
            hbRt.anchorMin = new Vector2(0f, 1f);
            hbRt.anchorMax = new Vector2(1f, 1f);
            hbRt.pivot = new Vector2(0f, 1f);
            hbRt.anchoredPosition = new Vector2(16f, -42f);
            hbRt.sizeDelta = new Vector2(-32f, 32f);

            Image hbImg = headerBar.AddComponent<Image>();
            hbImg.sprite = cardBaseSprite;
            hbImg.type = Image.Type.Sliced;
            hbImg.color = new Color(0.10f, 0.13f, 0.18f, 0.85f);

            for (int c = 0; c < 5; c++)
            {
                Text colTxt = APXUIFactory.CreateText(headerBar, "Col_" + c, colTitles[c], 13, APXUITheme.SecondaryText,
                    (c == 0) ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, FontStyle.Bold);
                RectTransform colRt = colTxt.rectTransform;
                colRt.anchorMin = new Vector2(0f, 0f);
                colRt.anchorMax = new Vector2(0f, 1f);
                colRt.pivot = new Vector2(0f, 0.5f);
                colRt.anchoredPosition = new Vector2(colX[c] - 16f, 0f);
                colRt.sizeDelta = new Vector2(colWidths[c], 0f);
            }

            // 4 位玩家数据行 (每行 48px)
            Color[] seatColors = new Color[4]
            {
                new Color(0.43f, 0.82f, 1f),
                new Color(0.61f, 0.87f, 0.54f),
                new Color(1f, 0.84f, 0.44f),
                new Color(0.96f, 0.58f, 0.65f)
            };

            for (int r = 0; r < 4; r++)
            {
                float rowY = -80f - r * 50f;
                GameObject rowGo = APXUIFactory.CreateUIObject("Row_" + r, card2);
                RectTransform rRt = rowGo.GetComponent<RectTransform>();
                rRt.anchorMin = new Vector2(0f, 1f);
                rRt.anchorMax = new Vector2(1f, 1f);
                rRt.pivot = new Vector2(0f, 1f);
                rRt.anchoredPosition = new Vector2(16f, rowY);
                rRt.sizeDelta = new Vector2(-32f, 44f);

                Image rBg = rowGo.AddComponent<Image>();
                rBg.sprite = cardBaseSprite;
                rBg.type = Image.Type.Sliced;
                rBg.color = (r % 2 == 0) ? new Color(0.12f, 0.15f, 0.22f, 0.5f) : new Color(0.09f, 0.12f, 0.18f, 0.4f);

                for (int c = 0; c < 5; c++)
                {
                    Text cellTxt = APXUIFactory.CreateText(rowGo, $"Cell_{r}_{c}", "—", 14,
                        (c == 0) ? seatColors[r] : APXUITheme.PrimaryText,
                        (c == 0) ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                        (c == 0) ? FontStyle.Bold : FontStyle.Normal);

                    RectTransform cellRt = cellTxt.rectTransform;
                    cellRt.anchorMin = new Vector2(0f, 0f);
                    cellRt.anchorMax = new Vector2(0f, 1f);
                    cellRt.pivot = new Vector2(0f, 0.5f);
                    cellRt.anchoredPosition = new Vector2(colX[c] - 16f, 0f);
                    cellRt.sizeDelta = new Vector2(colWidths[c], 0f);

                    _cellTexts[r, c] = cellTxt;
                    _lastObservedCells[r, c] = "—";
                }
            }

            // ==========================================
            // 3. 统计指标口径说明卡片 (Card 3: 130px, y: -445px)
            // ==========================================
            GameObject card3 = APXUIFactory.CreateUIObject("Card_Legend", _root);
            RectTransform c3Rt = card3.GetComponent<RectTransform>();
            c3Rt.anchorMin = new Vector2(0f, 1f);
            c3Rt.anchorMax = new Vector2(1f, 1f);
            c3Rt.pivot = new Vector2(0.5f, 1f);
            c3Rt.anchoredPosition = new Vector2(0f, -445f);
            c3Rt.sizeDelta = new Vector2(0f, 130f);

            Image c3Bg = card3.AddComponent<Image>();
            c3Bg.sprite = cardBaseSprite;
            c3Bg.type = Image.Type.Sliced;
            c3Bg.color = APXUIAssetService.IsFallbackSprite(cardBaseSprite) ? APXUITheme.Card : Color.white;

            Text legendTitle = APXUIFactory.CreateText(card3, "LegendTitle", "权威口径与来源说明", 14, APXUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform ltRt = legendTitle.rectTransform;
            ltRt.anchorMin = new Vector2(0f, 1f);
            ltRt.anchorMax = new Vector2(0f, 1f);
            ltRt.pivot = new Vector2(0f, 1f);
            ltRt.anchoredPosition = new Vector2(20f, -12f);
            ltRt.sizeDelta = new Vector2(220f, 20f);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("• 击倒：角色血量 (HP) 归零计数，来源 HeroHpChangeS2C 权威报文。");
            sb.AppendLine("• 伤害：造成对手扣减的真实绝对血量累计；步数：由移动桥接确认的已行进地块总数。");
            sb.AppendLine("• 转账：玩家间点对点移交金币总数；数据源与轻量对局挂件完全一致，单局结算后自动重置。");

            _legendText = APXUIFactory.CreateText(card3, "LegendContent", sb.ToString().TrimEnd(), 12, APXUITheme.SecondaryText, TextAnchor.UpperLeft);
            RectTransform lcRt = _legendText.rectTransform;
            lcRt.anchorMin = new Vector2(0f, 0f);
            lcRt.anchorMax = new Vector2(1f, 1f);
            lcRt.offsetMin = new Vector2(20f, 10f);
            lcRt.offsetMax = new Vector2(-20f, -36f);
        }

        private void OnFloatingHudToggled(bool value)
        {
            BattleStatsOverlay.AutoShowPreference = value;
        }

        /// <summary>
        /// 轮询同步：纯只读读取当前对局账本 (约 4Hz 有界刷新 / 0 重复协议注册)
        /// </summary>
        internal void UpdateSync()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }
            _nextRefreshTime = Time.unscaledTime + 0.25f;

            try
            {
                bool isObserving = BattleStatsOverlay.IsObserving;
                bool isBattleActive = isObserving && BattleStatsOverlay.HasActiveBattleContext;

                // 1. 同步进入对局自动显示偏好开关状态
                bool autoShow = BattleStatsOverlay.AutoShowPreference;
                if (_lastObservedHudVisible != autoShow && _floatingHudToggle != null)
                {
                    _lastObservedHudVisible = autoShow;
                    _floatingHudToggle.SetIsOnWithoutNotify(autoShow);
                }

                // 2. 同步状态徽章与明细
                // 监听已关闭: when IsObserving == false
                // 等待有效对局: when observing but no valid battle
                // 对局统计中: only when authoritative context is active
                string stateText;
                string detailText;
                Color badgeTextColor;
                Color badgeBgColor;

                if (!isObserving)
                {
                    stateText = "监听已关闭";
                    detailText = "对局数据监听已关闭，可在高级设置中开启独立采集";
                    badgeTextColor = APXUITheme.MutedText;
                    badgeBgColor = new Color(0.18f, 0.2f, 0.25f, 0.9f);
                }
                else if (isBattleActive)
                {
                    stateText = "对局统计中";
                    detailText = "正在实时统计当前战斗数据，单局结束自动清空";
                    badgeTextColor = new Color(0.32f, 0.86f, 0.76f, 1f);
                    badgeBgColor = new Color(0.12f, 0.32f, 0.28f, 0.9f);
                }
                else
                {
                    stateText = "等待有效对局";
                    detailText = "未在战斗中，进入房间并开局后自动激活监听";
                    badgeTextColor = APXUITheme.SecondaryText;
                    badgeBgColor = new Color(0.2f, 0.25f, 0.35f, 0.9f);
                }

                if (_lastObservedState != stateText)
                {
                    _lastObservedState = stateText;
                    if (_statusBadgeText != null)
                    {
                        _statusBadgeText.text = stateText;
                        _statusBadgeText.color = badgeTextColor;
                    }
                    if (_statusBadgeBg != null)
                    {
                        _statusBadgeBg.color = badgeBgColor;
                    }
                    if (_statusDetailText != null)
                    {
                        _statusDetailText.text = detailText;
                    }
                }

                // 3. 同步表格单元格数据 (若不在有效对局中，严禁显示陈旧行，全部置为 "—")
                StatsWidgetFrame frame = isBattleActive ? BattleStatsOverlay.GetCurrentFrame() : null;
                for (int r = 0; r < 4; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        string val = (isBattleActive && frame != null && frame.Cells != null) ? frame.Cells[r, c] : "—";
                        if (string.IsNullOrEmpty(val))
                        {
                            val = "—";
                        }

                        if (_lastObservedCells[r, c] != val)
                        {
                            _lastObservedCells[r, c] = val;
                            if (_cellTexts[r, c] != null)
                            {
                                _cellTexts[r, c].text = val;
                            }
                        }
                    }
                }

                // 4. 同步对局键 (非战斗或无键时固定为 "对局键: —")
                string curKey = (isBattleActive && BattleStatsOverlay.Instance != null && BattleStatsOverlay.Instance.Ledger != null && !string.IsNullOrEmpty(BattleStatsOverlay.Instance.Ledger.Key))
                    ? ("对局键: " + BattleStatsOverlay.Instance.Ledger.Key)
                    : "对局键: —";

                if (_lastObservedKey != curKey)
                {
                    _lastObservedKey = curKey;
                    if (_matchKeyText != null)
                    {
                        _matchKeyText.text = curKey;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[APX BattleStatsView] UpdateSync 异常: " + ex.Message);
            }
        }
    }
}
