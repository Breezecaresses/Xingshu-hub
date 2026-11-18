using System;
using System.Collections.Generic;
using XingShu.Cards;
using XingShuRuntime.Core;
using XingShuRuntime.MatchPort;
using XingShuRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    // Shared pink/yellow presentation for the feature center and floating widgets.
    internal static class XingShuDetailLayout
    {
        internal static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = XingShuUIFactory.CreateUIObject(name, parent.gameObject).GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        internal static Image Panel(RectTransform rt, string sprite = "card_base.png", float alpha = 1)
        {
            var image = rt.gameObject.AddComponent<Image>(); image.sprite = XingShuUIAssetService.GetSprite(sprite);
            image.type = Image.Type.Sliced; image.color = new Color(1, 1, 1, alpha); return image;
        }
        internal static void Fill(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }
        internal static Text Text(Transform p, string value, float x, float y, float w, float h, int size = 14, bool muted = false)
        {
            var label = XingShuUIFactory.CreateText(p.gameObject, "Label", value, size, muted ? XingShuUITheme.SecondaryText : XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft);
            var rt = label.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            label.supportRichText = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; return label;
        }
        internal static Button Button(Transform p, string caption, float x, float y, float w, float h, Action action, bool selected = false)
        {
            var button = XingShuUIFactory.CreateButton(p.gameObject, "Button", caption,
                XingShuUIAssetService.GetSprite(selected ? "button_selected.png" : "button_secondary.png"), Color.white, XingShuUITheme.PrimaryText, action, 13);
            var rt = button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return button;
        }
    }

    internal sealed class XingShuMatchPlayerCard
    {
        internal static readonly string[] Labels = StatsWidgetMetricCatalog.Labels;
        private readonly bool _compact;
        private readonly bool _phone;
        private readonly int _avatarSize;
        private readonly Text _name, _character, _score, _state;
        private readonly RectTransform[] _cells = new RectTransform[StatsWidgetMetricCatalog.Count];
        private readonly Text[] _values = new Text[StatsWidgetMetricCatalog.Count];
        private readonly GameObject _root;
        private readonly RawImage _avatar;
        private string _headUrl;
        private int _heroId;
        private int _metricMask = -1;

        internal XingShuMatchPlayerCard(GameObject parent, bool compact)
        {
            _compact = compact;
            _phone = XingShuMobileUIHelper.IsMobile && !compact;
            _avatarSize = compact ? 34 : 44;
            _root = XingShuUIFactory.CreateUIObject("PlayerDetailCard", parent);
            var rt = _root.GetComponent<RectTransform>(); XingShuDetailLayout.Fill(rt);
            XingShuDetailLayout.Panel(rt).raycastTarget = false;
            _avatar = XingShuDetailLayout.Box(rt, "Avatar", compact ? 8 : 14, compact ? 8 : 16, _avatarSize, _avatarSize).gameObject.AddComponent<RawImage>();
            _avatar.raycastTarget = false; _avatar.enabled = false;
            if (compact)
            {
                _name = XingShuDetailLayout.Text(rt, "等待玩家", 48, 6, 114, 18, 12);
                _name.verticalOverflow = VerticalWrapMode.Truncate;
                _character = XingShuDetailLayout.Text(rt, "—", 48, 25, 100, 16, 10, true);
                _character.verticalOverflow = VerticalWrapMode.Truncate;
                _score = XingShuDetailLayout.Text(rt, "—", 0, 5, 72, 20, 16);
                _score.color = XingShuUITheme.NumericAccent;
                _state = XingShuDetailLayout.Text(rt, "—", 0, 25, 88, 16, 9, true);
                foreach (var label in new[] { _score, _state })
                {
                    var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                    rect.anchoredPosition = new Vector2(-8, label == _score ? -5 : -25); label.alignment = TextAnchor.MiddleRight;
                }
            }
            else
            {
                _name = XingShuDetailLayout.Text(rt, "等待玩家", 68, 12, 450, 28, 17);
                _character = XingShuDetailLayout.Text(rt, "—", 68, 42, 450, 24, 12, true);
                _score = XingShuDetailLayout.Text(rt, "—", 0, 12, 110, 32, 24);
                _score.color = XingShuUITheme.NumericAccent;
                _state = XingShuDetailLayout.Text(rt, "星枢评分 / 10", 0, 44, 130, 22, 11, true);
                foreach (var label in new[] { _score, _state })
                {
                    var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                    rect.anchoredPosition = new Vector2(-18, label == _score ? -12 : -44); label.alignment = TextAnchor.MiddleRight;
                }
                if (_phone)
                {
                    _name.fontSize = 24; _character.fontSize = 22;
                    _score.fontSize = 30; _state.fontSize = 20;
                    _name.rectTransform.anchorMax = Vector2.one;
                    _name.rectTransform.sizeDelta = new Vector2(-240, 44);
                    _character.rectTransform.anchorMax = Vector2.one;
                    _character.rectTransform.sizeDelta = new Vector2(-240, 40);
                    _character.rectTransform.anchoredPosition = new Vector2(68, -60);
                    _score.rectTransform.sizeDelta = new Vector2(140, 48);
                    _state.rectTransform.sizeDelta = new Vector2(180, 36);
                    _state.rectTransform.anchoredPosition = new Vector2(-18, -64);
                }
            }

            for (int i = 0; i < StatsWidgetMetricCatalog.Count; i++)
            {
                var cell = XingShuDetailLayout.Box(rt, "Metric" + i, 0, 0, 0, compact ? 32 : (_phone ? 88 : 44));
                var title = XingShuDetailLayout.Text(cell, Labels[i], 0, 0, 0, compact ? 14 : (_phone ? 36 : 18), compact ? 10 : (_phone ? 22 : 11), true);
                title.rectTransform.anchorMax = new Vector2(1, 1);
                title.rectTransform.sizeDelta = new Vector2(0, compact ? 14 : (_phone ? 36 : 18));
                title.alignment = TextAnchor.MiddleCenter;
                if (compact)
                {
                    title.resizeTextForBestFit = true;
                    title.resizeTextMinSize = 8;
                    title.resizeTextMaxSize = 10;
                }
                _values[i] = XingShuDetailLayout.Text(cell, "—", 0, compact ? 14 : (_phone ? 40 : 20), 0, compact ? 18 : (_phone ? 46 : 24), compact ? 13 : (_phone ? 28 : 18));
                _values[i].rectTransform.anchorMax = new Vector2(1, 1);
                _values[i].rectTransform.sizeDelta = new Vector2(0, compact ? 18 : (_phone ? 46 : 24));
                _values[i].alignment = TextAnchor.MiddleCenter;
                if (compact)
                {
                    _values[i].resizeTextForBestFit = true;
                    _values[i].resizeTextMinSize = 9;
                    _values[i].resizeTextMaxSize = 13;
                }
                _cells[i] = cell;
            }
            ApplyMetricLayout(StatsWidgetMetricCatalog.DefaultMask);
        }

        internal static float PreferredHeight(bool compact, bool mobile, int metricMask)
        {
            int count = StatsWidgetMetricCatalog.CountEnabled(metricMask);
            if (compact)
            {
                int rows = (count + 3) / 4;
                return 48f + rows * 34f + 6f;
            }
            if (mobile)
            {
                int rows = (count + 2) / 3;
                return 114f + rows * 92f + 14f;
            }
            return 142f;
        }

        private void ApplyMetricLayout(int mask)
        {
            _metricMask = mask;
            int count = StatsWidgetMetricCatalog.CountEnabled(mask);
            int columns = _compact ? Math.Min(4, count) : (_phone ? Math.Min(3, count) : count);
            float top = _compact ? 48f : (_phone ? 114f : 78f);
            float rowHeight = _compact ? 34f : (_phone ? 92f : 48f);
            float padX = _compact ? 4f : 12f;
            float gapY = _compact ? 2f : 4f;
            int pos = 0;
            for (int i = 0; i < StatsWidgetMetricCatalog.Count; i++)
            {
                var cell = _cells[i];
                if (StatsWidgetMetricCatalog.IsEnabled(mask, i))
                {
                    int col = pos % columns;
                    int row = pos / columns;
                    cell.anchorMin = new Vector2((float)col / columns, 1);
                    cell.anchorMax = new Vector2((float)(col + 1) / columns, 1);
                    cell.offsetMin = new Vector2(padX, -top - (row + 1) * rowHeight + gapY);
                    cell.offsetMax = new Vector2(-padX, -top - row * rowHeight);
                    cell.gameObject.SetActive(true);
                    pos++;
                }
                else
                {
                    cell.gameObject.SetActive(false);
                }
            }
        }

        internal void Refresh(StatsWidgetFrame.Player player, int slot, int metricMask = StatsWidgetMetricCatalog.DefaultMask)
        {
            metricMask = StatsWidgetMetricCatalog.NormalizeMask(metricMask);
            if (metricMask != _metricMask) ApplyMetricLayout(metricMask);
            if (_compact)
            {
                _name.text = player == null ? "P" + (slot + 1) + " 等待中" : "P" + (slot + 1) + " " + StatsWidgetPresenter.Name(player.Name);
                _character.text = player?.Character ?? "未入局";
                _score.text = player?.Score ?? "—";
                _state.text = player == null ? "—" : (player.Mvp ? "MVP · " : "") + player.State;
            }
            else
            {
                _name.text = player == null ? "P" + (slot + 1) + " · 等待玩家" : "P" + (slot + 1) + "  " + player.Name;
                _character.text = player?.Character ?? "进入对局后显示";
                _score.text = player?.Score ?? "—";
                _state.text = player == null ? "星枢评分 / 10" : (player.Mvp ? "MVP · " : "") + player.State;
            }
            for (int i = 0; i < StatsWidgetMetricCatalog.Count; i++)
            {
                _values[i].text = player?.Metrics[StatsWidgetMetricCatalog.FrameMetricIndices[i]] ?? "—";
            }
            int heroId = player?.HeroId ?? 0;
            string head = player?.HeadUrl ?? "";
            if (head != _headUrl || heroId != _heroId)
            {
                _headUrl = head;
                _heroId = heroId;
                _avatar.texture = null;
                var candidates = new List<string>(2);
                if (heroId > 0)
                {
                    string emote = CharacterEmoteRegistry.PickStaticEmote(heroId, slot);
                    if (!string.IsNullOrEmpty(emote)) candidates.Add(emote);
                }
                if (!string.IsNullOrEmpty(head)) candidates.Add(head);
                _avatar.enabled = candidates.Count > 0;
                _avatar.rectTransform.sizeDelta = new Vector2(_avatarSize, _avatarSize);
                if (_avatar.enabled) XingShuDetailImages.Load(_avatar, candidates, 0, () => _headUrl == head && _heroId == heroId);
            }
        }
    }

    internal sealed class XingShuStatsMetricsPanel
    {
        private readonly Toggle[] _toggles = new Toggle[StatsWidgetMetricCatalog.Count];
        private readonly Action _onChanged;
        private bool _suppress;
        private int _lastObservedMask = -1;

        internal XingShuStatsMetricsPanel(GameObject parent, float startY, Action onChanged = null)
        {
            _onChanged = onChanged;
            int mask = ReadMask();
            _lastObservedMask = mask;

            Text title = XingShuUIFactory.CreateText(parent, "MetricsTitle", "自定义显示统计项", 14, XingShuUITheme.PrimaryText, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(24f, startY);
            titleRt.sizeDelta = new Vector2(220f, 22f);

            Text hint = XingShuUIFactory.CreateText(parent, "MetricsHint", "至少保留 1 项，勾选即时同步 4 人同屏悬浮窗与对局数据页", 11, XingShuUITheme.MutedText, TextAnchor.MiddleLeft);
            RectTransform hintRt = hint.rectTransform;
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(1f, 1f);
            hintRt.pivot = new Vector2(0f, 1f);
            hintRt.anchoredPosition = new Vector2(24f, startY - 22f);
            hintRt.sizeDelta = new Vector2(-180f, 18f);

            Sprite btnSprite = XingShuUIAssetService.GetSprite("button_secondary.png");
            Button resetBtn = XingShuUIFactory.CreateButton(parent, "ResetMetricsBtn", "全选 / 恢复默认",
                btnSprite,
                XingShuUIAssetService.IsFallbackSprite(btnSprite) ? XingShuUITheme.ButtonSecondaryBase : Color.white,
                XingShuUITheme.PrimaryText,
                ResetToDefault, 12);
            RectTransform btnRt = resetBtn.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 1f);
            btnRt.anchorMax = new Vector2(1f, 1f);
            btnRt.pivot = new Vector2(1f, 1f);
            btnRt.anchoredPosition = new Vector2(-24f, startY);
            btnRt.sizeDelta = new Vector2(130f, 28f);

            for (int i = 0; i < StatsWidgetMetricCatalog.Count; i++)
            {
                int index = i;
                int col = i % 4;
                int row = i / 4;
                bool enabled = StatsWidgetMetricCatalog.IsEnabled(mask, index);
                Toggle toggle = XingShuUIFactory.CreateToggle(parent, "MetricToggle_" + index, StatsWidgetMetricCatalog.Labels[index], enabled,
                    val => OnToggleChanged(index, val));
                RectTransform rt = toggle.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(24f + col * 164f, startY - 46f - row * 36f);
                rt.sizeDelta = new Vector2(156f, 30f);
                Text label = toggle.transform.Find("Label")?.GetComponent<Text>();
                if (label != null)
                {
                    label.fontSize = 13;
                    label.fontStyle = FontStyle.Normal;
                }
                _toggles[index] = toggle;
            }
        }

        internal static int ReadMask()
        {
            return StatsWidgetMetricCatalog.NormalizeMask(
                XingShuConfigService.GetInt(XingShuConfigKeys.StatsWidgetMetrics, StatsWidgetMetricCatalog.DefaultMask));
        }

        internal void SyncFromConfig()
        {
            int mask = ReadMask();
            if (mask == _lastObservedMask) return;
            _lastObservedMask = mask;
            _suppress = true;
            try
            {
                for (int i = 0; i < _toggles.Length; i++)
                {
                    if (_toggles[i] != null)
                    {
                        XingShuUIFactory.SetToggleStateWithoutNotify(_toggles[i], StatsWidgetMetricCatalog.IsEnabled(mask, i));
                    }
                }
            }
            finally
            {
                _suppress = false;
            }
        }

        private void OnToggleChanged(int index, bool enabled)
        {
            if (_suppress) return;
            int current = ReadMask();
            int next = StatsWidgetMetricCatalog.SetEnabled(current, index, enabled);
            if (next == current)
            {
                if (!enabled && StatsWidgetMetricCatalog.IsEnabled(current, index) && _toggles[index] != null)
                {
                    _suppress = true;
                    try { XingShuUIFactory.SetToggleStateWithoutNotify(_toggles[index], true); }
                    finally { _suppress = false; }
                }
                return;
            }
            SaveAndNotify(next);
        }

        private void ResetToDefault()
        {
            SaveAndNotify(StatsWidgetMetricCatalog.DefaultMask);
        }

        private void SaveAndNotify(int mask)
        {
            mask = StatsWidgetMetricCatalog.NormalizeMask(mask);
            XingShuConfigService.SetInt(XingShuConfigKeys.StatsWidgetMetrics, mask);
            XingShuConfigService.Save();
            _lastObservedMask = -1;
            SyncFromConfig();
            BattleStatsOverlay.RefreshWidgetPreferences();
            _onChanged?.Invoke();
        }
    }
}
