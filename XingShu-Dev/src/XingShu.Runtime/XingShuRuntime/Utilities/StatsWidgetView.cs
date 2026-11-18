using System;
using System.Collections.Generic;
using System.Globalization;
using XingShu.Cards;
using XingShuRuntime.Core;
using XingShuRuntime.MatchPort;
using XingShuRuntime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.Utilities
{
    internal sealed class StatsWidgetView : IDisposable
    {
        private const int PlayerCount = 4;
        private const float PadX = 10f;
        private const float PlayerColWidth = 140f;
        private const float ColGap = 6f;
        private const float RowTopY = 68f;
        private const float RowHeight = 32f;
        private const float RowStep = 35f;

        private static readonly Color PanelTint = new Color(0.45f, 0.54f, 0.70f, 1f);
        private static readonly Color HeaderTint = new Color(0.50f, 0.58f, 0.74f, 0.48f);
        private static readonly Color HeaderAccentColor = new Color(0.24f, 0.85f, 0.92f, 1f);
        private static readonly Color StatusGoldColor = new Color(0.96f, 0.78f, 0.35f, 1f);
        private static readonly Color HeaderBtnBg = new Color(0.24f, 0.30f, 0.42f, 0.90f);
        private static readonly Color ColPillBg = new Color(0.05f, 0.08f, 0.14f, 0.82f);
        private static readonly Color RowBgEven = new Color(0.14f, 0.19f, 0.28f, 0.62f);
        private static readonly Color RowBgOdd = new Color(0.11f, 0.15f, 0.23f, 0.46f);
        private static readonly Color RowLeaderFill = new Color(0.14f, 0.52f, 0.46f, 0.36f);
        private static readonly Color TextPrimaryLight = new Color(0.96f, 0.98f, 1f, 1f);
        private static readonly Color TextSubtleLight = new Color(0.70f, 0.77f, 0.86f, 1f);
        private static readonly Color TextLeaderMint = new Color(0.32f, 0.95f, 0.70f, 1f);

        private static Sprite _solidSprite;

        private readonly Action _hide;
        internal readonly StatsWidgetPreferences Preferences = new StatsWidgetPreferences();
        private GameObject _root, _body;
        private RectTransform _widget, _heading, _dragHandle, _playerColLabel;
        private Image _background;
        private Text _title, _status, _footer;
        private Button _btnCollapse, _btnSettings, _btnClose;
        private StatsWidgetDrag _drag;

        private readonly RectTransform[] _metricHeaders = new RectTransform[StatsWidgetMetricCatalog.Count];
        private readonly RectTransform[] _rowRects = new RectTransform[PlayerCount];
        private readonly RectTransform[] _rowFillRects = new RectTransform[PlayerCount];
        private readonly Image[] _rowFillImages = new Image[PlayerCount];
        private readonly RawImage[] _avatars = new RawImage[PlayerCount];
        private readonly string[] _loadedHeads = new string[PlayerCount];
        private readonly int[] _loadedHeroIds = new int[PlayerCount];
        private readonly Text[] _names = new Text[PlayerCount];
        private readonly Text[] _subs = new Text[PlayerCount];
        private readonly Text[,] _cells = new Text[PlayerCount, StatsWidgetMetricCatalog.Count];

        private StatsWidgetFrame _frame;
        private int _metricMask = StatsWidgetMetricCatalog.DefaultMask;
        private bool _collapsed;

        internal bool Ready => _root != null;
        internal bool SettingsVisible => false;
        internal Rect WidgetBounds => XingShuOverlayInput.ScreenRect(_widget);
        internal Rect SettingsBounds => default(Rect);

        internal StatsWidgetView(Action hide, Action<bool> observe) { _hide = hide; Load(); }

        private static Sprite GetSolidSprite()
        {
            if (_solidSprite != null) return _solidSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false, true);
            _solidSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            return _solidSprite;
        }

        private static Image AddSolidRect(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rt = XingShuDetailLayout.Box(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = GetSolidSprite();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Button CreateHeaderPillButton(Transform parent, string caption, float w, Action action)
        {
            var sprite = XingShuUIAssetService.GetSprite("hotkey_badge.png");
            var btn = XingShuUIFactory.CreateButton(parent.gameObject, "Btn_" + caption, caption,
                sprite, XingShuUIAssetService.IsFallbackSprite(sprite) ? HeaderBtnBg : HeaderBtnBg, TextPrimaryLight, action, 11);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, 22f);
            var label = btn.GetComponentInChildren<Text>();
            if (label != null) label.color = TextPrimaryLight;
            return btn;
        }

        internal void Build()
        {
            XingShuCentralInputManager.EnsureEventSystemAvailable("StatsWidget");
            _root = new GameObject("XINGSHU_StatsWidget", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30020;

            float width = CurrentPanelWidth;
            float height = CurrentPanelHeight;
            _widget = XingShuDetailLayout.Box(_root.transform, "Widget", 0, 0, width, height);
            _background = XingShuDetailLayout.Panel(_widget, "window_frame.png", Preferences.Background);
            _background.color = new Color(PanelTint.r, PanelTint.g, PanelTint.b, Preferences.Background);

            _heading = XingShuDetailLayout.Box(_widget, "Header", 0, 0, width, StatsWidgetPreferences.HeaderHeight);
            var headImg = XingShuDetailLayout.Panel(_heading, "header_bar.png", HeaderTint.a);
            headImg.color = HeaderTint;

            AddSolidRect(_heading, "Accent", 11, 11, 3, 16, HeaderAccentColor);
            _title = XingShuDetailLayout.Text(_heading, "对局统计", 20, 4, 68, 30, 14);
            _title.color = TextPrimaryLight;
            _title.fontStyle = FontStyle.Bold;

            _status = XingShuDetailLayout.Text(_heading, "实时参考", 90, 5, 110, 28, 10);
            _status.color = StatusGoldColor;

            var handle = XingShuDetailLayout.Box(_heading, "Drag", 0, 0, width - 120f, StatsWidgetPreferences.HeaderHeight);
            handle.gameObject.AddComponent<Image>().color = Color.clear;
            _dragHandle = handle;
            _drag = handle.gameObject.AddComponent<StatsWidgetDrag>();
            _drag.View = this;
            _drag.BindEvents();

            _btnCollapse = CreateHeaderPillButton(_heading, "−", 26f, ToggleCollapse);
            _btnSettings = CreateHeaderPillButton(_heading, "设置", 44f, () => XingShuUICenter.OpenAdvancedSettings("BattleStats"));
            _btnClose = CreateHeaderPillButton(_heading, "×", 26f, () => { Show(false); _hide(); });

            _body = XingShuUIFactory.CreateUIObject("Body", _widget.gameObject);
            XingShuDetailLayout.Fill(_body.GetComponent<RectTransform>());

            _playerColLabel = XingShuDetailLayout.Box(_body.transform, "ColPlayer", PadX + 2f, 42f, PlayerColWidth, 22f);
            var playerColText = XingShuDetailLayout.Text(_playerColLabel, "玩家 / 角色", 2, 0, PlayerColWidth - 4f, 22f, 10);
            playerColText.color = TextSubtleLight;

            Sprite pillSprite = XingShuUIAssetService.GetSprite("hotkey_badge.png");
            for (int c = 0; c < StatsWidgetMetricCatalog.Count; c++)
            {
                var pillRt = XingShuDetailLayout.Box(_body.transform, "Col_" + c, 0, 42f, 44f, 21f);
                var pillImg = pillRt.gameObject.AddComponent<Image>();
                pillImg.sprite = pillSprite != null ? pillSprite : GetSolidSprite();
                pillImg.type = pillSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                pillImg.color = ColPillBg;
                pillImg.raycastTarget = false;

                var colLabel = XingShuDetailLayout.Text(pillRt, StatsWidgetMetricCatalog.Labels[c], 2, 0, 40f, 21f, 10);
                colLabel.rectTransform.anchorMin = Vector2.zero;
                colLabel.rectTransform.anchorMax = Vector2.one;
                colLabel.rectTransform.offsetMin = new Vector2(2f, 0f);
                colLabel.rectTransform.offsetMax = new Vector2(-2f, 0f);
                colLabel.alignment = TextAnchor.MiddleCenter;
                colLabel.color = TextPrimaryLight;
                colLabel.resizeTextForBestFit = true;
                colLabel.resizeTextMinSize = 8;
                colLabel.resizeTextMaxSize = 10;
                _metricHeaders[c] = pillRt;
            }

            for (int row = 0; row < PlayerCount; row++)
            {
                var rowRt = XingShuDetailLayout.Box(_body.transform, "Row_" + row, PadX, RowTopY + row * RowStep, width - 2f * PadX, RowHeight);
                var bg = rowRt.gameObject.AddComponent<Image>();
                bg.sprite = GetSolidSprite();
                bg.color = row % 2 == 0 ? RowBgEven : RowBgOdd;
                bg.raycastTarget = false;
                _rowRects[row] = rowRt;

                var fillImg = AddSolidRect(rowRt, "Fill", 0, 0, 0, RowHeight, RowLeaderFill);
                _rowFillRects[row] = fillImg.rectTransform;
                _rowFillImages[row] = fillImg;

                var avatarRt = XingShuDetailLayout.Box(rowRt, "Avatar", 6f, 4f, 24f, 24f);
                var avatar = avatarRt.gameObject.AddComponent<RawImage>();
                avatar.raycastTarget = false;
                avatar.enabled = false;
                _avatars[row] = avatar;

                var nameText = XingShuDetailLayout.Text(rowRt, "P" + (row + 1) + " 等待中", 35f, 2f, 104f, 16f, 11);
                nameText.color = TextPrimaryLight;
                nameText.verticalOverflow = VerticalWrapMode.Truncate;
                nameText.fontStyle = FontStyle.Bold;
                _names[row] = nameText;

                var subText = XingShuDetailLayout.Text(rowRt, "P" + (row + 1) + " · 未入局", 35f, 17f, 104f, 13f, 9);
                subText.color = TextSubtleLight;
                subText.verticalOverflow = VerticalWrapMode.Truncate;
                _subs[row] = subText;

                for (int c = 0; c < StatsWidgetMetricCatalog.Count; c++)
                {
                    var cellText = XingShuDetailLayout.Text(rowRt, "—", 0, 0, 44f, RowHeight, 13);
                    cellText.alignment = TextAnchor.MiddleCenter;
                    cellText.color = TextPrimaryLight;
                    cellText.resizeTextForBestFit = true;
                    cellText.resizeTextMinSize = 9;
                    cellText.resizeTextMaxSize = 13;
                    _cells[row, c] = cellText;
                }
            }

            _footer = XingShuDetailLayout.Text(_body.transform, "4人同屏 · 实时参考", PadX + 2f, 210f, width - 2f * PadX - 4f, 16f, 9);
            _footer.color = TextSubtleLight;

            ReloadPreferences();
            Show(false);
        }

        private float CurrentPanelWidth => StatsWidgetPreferences.PanelWidth(StatsWidgetMetricCatalog.CountEnabled(_metricMask));
        private float CurrentPanelHeight => StatsWidgetPreferences.PanelHeight(StatsWidgetMetricCatalog.CountEnabled(_metricMask), _collapsed);

        internal void Show(bool show) { if (Ready) _root.SetActive(show); }

        internal void Refresh(StatsWidgetFrame frame)
        {
            if (!Ready || frame == null) return;
            _frame = frame;

            bool settled = false;
            for (int i = 0; i < frame.Players.Length; i++)
            {
                if (frame.Players[i] != null && frame.Players[i].State == "已结算") { settled = true; break; }
            }
            _status.text = !frame.Enabled ? "已关闭" : settled ? "已结算" : (frame.Partial ? "部分数据" : "实时参考");

            // Compute column max numeric value per metric to highlight leaders cleanly in mint green.
            double[] colMax = new double[StatsWidgetMetricCatalog.Count];
            double[,] parsed = new double[PlayerCount, StatsWidgetMetricCatalog.Count];
            for (int row = 0; row < PlayerCount; row++)
            {
                var p = frame.Players[row];
                for (int c = 0; c < StatsWidgetMetricCatalog.Count; c++)
                {
                    double val = ParseNumeric(p?.Metrics[StatsWidgetMetricCatalog.FrameMetricIndices[c]]);
                    parsed[row, c] = val;
                    if (val > colMax[c]) colMax[c] = val;
                }
            }

            // Pick a representative score/metric ratio for the subtle horizontal bar fill per row (Score -> Damage -> Kills -> Coins -> Steps).
            double bestRowStrength = 0d;
            double[] rowStrength = new double[PlayerCount];
            for (int row = 0; row < PlayerCount; row++)
            {
                var p = frame.Players[row];
                if (p == null) continue;
                double s = ParseNumeric(p.Score);
                if (s <= 0d) s = parsed[row, 2] > 0d ? parsed[row, 2] : (parsed[row, 7] > 0d ? parsed[row, 7] : parsed[row, 5]);
                rowStrength[row] = s;
                if (s > bestRowStrength) bestRowStrength = s;
            }

            float usableRowW = CurrentPanelWidth - 2f * PadX;
            for (int row = 0; row < PlayerCount; row++)
            {
                var p = frame.Players[row];
                _names[row].text = p == null ? "等待玩家" : StatsWidgetPresenter.Name(p.Name);
                string charName = p?.Character ?? "未入局";
                string prefix = (p != null && p.Mvp) ? "MVP · " : "";
                _subs[row].text = prefix + "P" + (row + 1) + " · " + charName;
                _subs[row].color = (p != null && p.Mvp)
                    ? new Color(StatusGoldColor.r, StatusGoldColor.g, StatusGoldColor.b, Preferences.TextAlpha)
                    : new Color(TextSubtleLight.r, TextSubtleLight.g, TextSubtleLight.b, Preferences.TextAlpha);

                float fillW = (bestRowStrength > 0d && rowStrength[row] > 0d)
                    ? usableRowW * Mathf.Clamp01((float)(rowStrength[row] / bestRowStrength))
                    : 0f;
                _rowFillRects[row].sizeDelta = new Vector2(fillW, RowHeight);

                for (int c = 0; c < StatsWidgetMetricCatalog.Count; c++)
                {
                    string text = p?.Metrics[StatsWidgetMetricCatalog.FrameMetricIndices[c]] ?? "—";
                    var cell = _cells[row, c];
                    cell.text = text;
                    bool isLeader = colMax[c] > 0d && parsed[row, c] > 0d && Math.Abs(parsed[row, c] - colMax[c]) < 0.0001d;
                    Color baseColor = isLeader ? TextLeaderMint : TextPrimaryLight;
                    cell.color = new Color(baseColor.r, baseColor.g, baseColor.b, Preferences.TextAlpha);
                }

                RefreshAvatar(row, p);
            }

            _footer.text = frame.Footer;
            Layout();
        }

        private void RefreshAvatar(int slot, StatsWidgetFrame.Player player)
        {
            int heroId = player?.HeroId ?? 0;
            string head = player?.HeadUrl ?? "";
            if (head == _loadedHeads[slot] && heroId == _loadedHeroIds[slot]) return;

            _loadedHeads[slot] = head;
            _loadedHeroIds[slot] = heroId;
            var avatar = _avatars[slot];
            avatar.texture = null;

            var candidates = new List<string>(2);
            if (heroId > 0)
            {
                string emote = CharacterEmoteRegistry.PickStaticEmote(heroId, slot);
                if (!string.IsNullOrEmpty(emote)) candidates.Add(emote);
            }
            if (!string.IsNullOrEmpty(head)) candidates.Add(head);

            avatar.enabled = candidates.Count > 0;
            if (avatar.enabled)
            {
                XingShuDetailImages.Load(avatar, candidates, 0, () => _loadedHeads[slot] == head && _loadedHeroIds[slot] == heroId);
            }
        }

        private static double ParseNumeric(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return 0d;
            string s = raw.Trim();
            if (s == "—" || s == "-") return 0d;
            if (s.StartsWith("≥") || s.StartsWith("≈")) s = s.Substring(1).Trim();
            double mul = 1d;
            if (s.EndsWith("万亿")) { mul = 1e12; s = s.Substring(0, s.Length - 2); }
            else if (s.EndsWith("亿")) { mul = 1e8; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("万")) { mul = 1e4; s = s.Substring(0, s.Length - 1); }
            s = s.Replace(",", "");
            double value;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value * mul : 0d;
        }

        private void ToggleCollapse()
        {
            if (!Ready) return;
            Rect safe = XingShuMobileUIHelper.SafeScreenRect;
            float safeTop = Screen.height - safe.yMax;
            var oldBounds = Preferences.Layout(safe.x, safeTop, safe.width, safe.height, BaseScale, CurrentPanelWidth, CurrentPanelHeight);
            _collapsed = !_collapsed;
            var collapseLabel = _btnCollapse != null ? _btnCollapse.GetComponentInChildren<Text>() : null;
            if (collapseLabel != null) collapseLabel.text = _collapsed ? "+" : "−";
            var newBounds = Preferences.Layout(safe.x, safeTop, safe.width, safe.height, BaseScale, CurrentPanelWidth, CurrentPanelHeight);
            float margin = Math.Min(12f, Math.Min(safe.width, safe.height) * 0.025f);
            float availW = Math.Max(1f, safe.width - 2f * margin);
            float availH = Math.Max(1f, safe.height - 2f * margin);
            float newRangeX = Math.Max(0f, availW - newBounds.Width);
            float newRangeY = Math.Max(0f, availH - newBounds.Height);
            if (newRangeX > 0.01f)
            {
                Preferences.X = StatsWidgetPreferences.Limit((oldBounds.X - (safe.x + margin)) / newRangeX, 0f, 1f, Preferences.X);
            }
            if (newRangeY > 0.01f)
            {
                Preferences.Y = StatsWidgetPreferences.Limit((oldBounds.Y - (safeTop + margin)) / newRangeY, 0f, 1f, Preferences.Y);
            }
            Layout();
        }

        private void Layout()
        {
            if (!Ready) return;
            float panelWidth = CurrentPanelWidth;
            float panelHeight = CurrentPanelHeight;
            _body.SetActive(!_collapsed);
            _widget.sizeDelta = new Vector2(panelWidth, panelHeight);
            _heading.sizeDelta = new Vector2(panelWidth, StatsWidgetPreferences.HeaderHeight);
            _dragHandle.sizeDelta = new Vector2(Math.Max(120f, panelWidth - 118f), StatsWidgetPreferences.HeaderHeight);

            _btnCollapse.GetComponent<RectTransform>().anchoredPosition = new Vector2(panelWidth - 112f, -8f);
            _btnSettings.GetComponent<RectTransform>().anchoredPosition = new Vector2(panelWidth - 82f, -8f);
            _btnClose.GetComponent<RectTransform>().anchoredPosition = new Vector2(panelWidth - 34f, -8f);

            int enabledCount = StatsWidgetMetricCatalog.CountEnabled(_metricMask);
            float colWidth = enabledCount <= 4 ? 56f : 46f;
            float rowW = panelWidth - 2f * PadX;
            float playerColW = Math.Max(PlayerColWidth, rowW - ColGap - enabledCount * colWidth);
            float metricStartX = PadX + playerColW + ColGap;

            _playerColLabel.sizeDelta = new Vector2(playerColW, 22f);
            float textBoxW = Math.Max(68f, playerColW - 38f);

            int pos = 0;
            for (int c = 0; c < StatsWidgetMetricCatalog.Count; c++)
            {
                bool on = StatsWidgetMetricCatalog.IsEnabled(_metricMask, c);
                _metricHeaders[c].gameObject.SetActive(on);
                for (int row = 0; row < PlayerCount; row++) _cells[row, c].gameObject.SetActive(on);
                if (!on) continue;

                float headerX = metricStartX + pos * colWidth;
                _metricHeaders[c].anchoredPosition = new Vector2(headerX + 2f, -42f);
                _metricHeaders[c].sizeDelta = new Vector2(colWidth - 4f, 21f);

                float cellX = playerColW + ColGap + pos * colWidth;
                for (int row = 0; row < PlayerCount; row++)
                {
                    var cellRt = _cells[row, c].rectTransform;
                    cellRt.anchoredPosition = new Vector2(cellX + 2f, 0f);
                    cellRt.sizeDelta = new Vector2(colWidth - 4f, RowHeight);
                }
                pos++;
            }

            for (int row = 0; row < PlayerCount; row++)
            {
                _rowRects[row].anchoredPosition = new Vector2(PadX, -(RowTopY + row * RowStep));
                _rowRects[row].sizeDelta = new Vector2(rowW, RowHeight);
                _names[row].rectTransform.sizeDelta = new Vector2(textBoxW, 16f);
                _subs[row].rectTransform.sizeDelta = new Vector2(textBoxW, 13f);
            }

            _footer.rectTransform.anchoredPosition = new Vector2(PadX + 2f, -210f);
            _footer.rectTransform.sizeDelta = new Vector2(rowW - 4f, 16f);

            Rect safe = XingShuMobileUIHelper.SafeScreenRect;
            var bounds = Preferences.Layout(safe.x, Screen.height - safe.yMax, safe.width, safe.height, BaseScale, panelWidth, panelHeight);
            _widget.anchoredPosition = new Vector2(bounds.X, -bounds.Y);
            _widget.localScale = new Vector3(bounds.Scale, bounds.Scale, 1);
        }

        internal void Tick() { Layout(); }

        internal void Drag(Vector2 delta)
        {
            var safe = XingShuMobileUIHelper.SafeScreenRect;
            Preferences.Move(delta.x, -delta.y, safe.width, safe.height, BaseScale, CurrentPanelWidth, CurrentPanelHeight);
            Layout();
        }

        private static float BaseScale => XingShuMobileUIHelper.WidgetScaleFactor;
        internal void SuspendInteraction() { if (_drag != null) _drag.Cancel(); }
        private static string Key(string pc, string mobile) => XingShuMobileUIHelper.IsMobile ? mobile : pc;

        private void Load()
        {
            _metricMask = StatsWidgetMetricCatalog.NormalizeMask(XingShuConfigService.GetInt(XingShuConfigKeys.StatsWidgetMetrics, StatsWidgetMetricCatalog.DefaultMask));
            Preferences.Background = XingShuConfigService.GetFloat(Key(XingShuConfigKeys.StatsWidgetPcBackground, XingShuConfigKeys.StatsWidgetAndroidBackground), .78f);
            Preferences.TextAlpha = XingShuConfigService.GetFloat(Key(XingShuConfigKeys.StatsWidgetPcText, XingShuConfigKeys.StatsWidgetAndroidText), .96f);
            Preferences.Scale = XingShuConfigService.GetFloat(Key(XingShuConfigKeys.StatsWidgetPcScale, XingShuConfigKeys.StatsWidgetAndroidScale),
                XingShuMobileUIHelper.IsMobile ? XingShuConfigKeys.StatsWidgetAndroidScaleDefault : XingShuConfigKeys.StatsWidgetScaleDefault);
            Preferences.X = XingShuConfigService.GetFloat(Key(XingShuConfigKeys.StatsWidgetPcX, XingShuConfigKeys.StatsWidgetAndroidX), 1);
            Preferences.Y = XingShuConfigService.GetFloat(Key(XingShuConfigKeys.StatsWidgetPcY, XingShuConfigKeys.StatsWidgetAndroidY), .12f);
            Preferences.Locked = XingShuConfigService.GetBool(Key(XingShuConfigKeys.StatsWidgetPcLock, XingShuConfigKeys.StatsWidgetAndroidLock), false);
            Preferences.Normalize();
        }

        internal void ReloadPreferences()
        {
            Load();
            if (!Ready) return;
            _background.color = new Color(PanelTint.r, PanelTint.g, PanelTint.b, Preferences.Background);
            foreach (var text in _body.GetComponentsInChildren<Text>(true))
            {
                var color = text.color;
                color.a = Preferences.TextAlpha;
                text.color = color;
            }
            if (_frame != null) Refresh(_frame);
            XingShuUITheme.RefreshFonts(_root);
            Layout();
        }

        internal void Save()
        {
            XingShuConfigService.SetFloat(Key(XingShuConfigKeys.StatsWidgetPcX, XingShuConfigKeys.StatsWidgetAndroidX), Preferences.X);
            XingShuConfigService.SetFloat(Key(XingShuConfigKeys.StatsWidgetPcY, XingShuConfigKeys.StatsWidgetAndroidY), Preferences.Y);
            XingShuConfigService.Save();
        }

        public void Dispose()
        {
            if (!Ready) return;
            Save();
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
