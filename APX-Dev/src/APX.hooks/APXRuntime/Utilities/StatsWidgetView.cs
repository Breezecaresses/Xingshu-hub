using System;
using APXRuntime.Core;
using APXRuntime.MatchPort;
using APXRuntime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.Utilities
{
    internal sealed class StatsWidgetView : IDisposable
    {
        private readonly Action _hide;
        internal readonly StatsWidgetPreferences Preferences = new StatsWidgetPreferences();
        private GameObject _root, _body;
        private RectTransform _widget;
        private Image _background;
        private Text _status, _footer;
        private StatsWidgetDrag _drag;
        private APXMatchPlayerCard _card;
        private readonly Button[] _tabs = new Button[4];
        private StatsWidgetFrame _frame;
        private int _selected;
        private bool _collapsed;
        internal bool Ready => _root != null;
        internal bool SettingsVisible => false;
        internal Rect WidgetBounds => APXOverlayInput.ScreenRect(_widget);
        internal Rect SettingsBounds => default(Rect);

        internal StatsWidgetView(Action hide, Action<bool> observe) { _hide = hide; Load(); }
        internal void Build()
        {
            APXCentralInputManager.EnsureEventSystemAvailable("StatsWidget");
            _root = new GameObject("APX_StatsWidget", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30020;
            _widget = APXDetailLayout.Box(_root.transform, "Widget", 0, 0, 360, 328);
            _background = APXDetailLayout.Panel(_widget, "window_frame.png", Preferences.Background);
            var heading = APXDetailLayout.Box(_widget, "Header", 0, 0, 360, 42);
            APXDetailLayout.Panel(heading, "header_bar.png");
            var handle = APXDetailLayout.Box(heading, "Drag", 0, 0, 210, 42);
            handle.gameObject.AddComponent<Image>().color = Color.clear;
            _drag = handle.gameObject.AddComponent<StatsWidgetDrag>(); _drag.View = this;
            _status = APXDetailLayout.Text(heading, "对局统计", 14, 6, 192, 30, 15); _status.color = Color.white;
            APXDetailLayout.Button(heading, "−", 218, 8, 34, 26, () => { _collapsed = !_collapsed; Layout(); });
            APXDetailLayout.Button(heading, "设置", 258, 8, 54, 26, () => APXUICenter.OpenAdvancedSettings("BattleStats"));
            APXDetailLayout.Button(heading, "×", 318, 8, 30, 26, () => { Show(false); _hide(); });
            _body = APXUIFactory.CreateUIObject("Body", _widget.gameObject); APXDetailLayout.Fill(_body.GetComponent<RectTransform>());
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                _tabs[i] = APXDetailLayout.Button(_body.transform, "P" + (i + 1), 12 + i * 84, 48, 78, 28, () => { _selected = slot; Refresh(_frame); });
            }
            var detail = APXDetailLayout.Box(_body.transform, "Detail", 10, 84, 340, 226);
            _card = new APXMatchPlayerCard(detail.gameObject, true);
            _footer = APXDetailLayout.Text(_body.transform, "实时参考 · 点 P1–P4 切换", 14, 310, 334, 18, 10);
            ReloadPreferences(); Show(false);
        }

        internal void Show(bool show) { if (Ready) _root.SetActive(show); }
        internal void Refresh(StatsWidgetFrame frame)
        {
            if (!Ready || frame == null) return; _frame = frame;
            _status.text = frame.Players[_selected]?.State == "已结算" ? "对局统计 · 已结算" : "对局统计 · 实时参考";
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].image.sprite = APXUIAssetService.GetSprite(i == _selected ? "button_selected.png" : "button_secondary.png");
            _card.Refresh(frame.Players[_selected], _selected);
            _footer.text = frame.Players[_selected]?.State == "已结算" ? "服务器结算 · 与 Hub 同一评分规则" : "实时参考 · 累计金币计分，结算金币仅展示";
        }
        private void Layout()
        {
            if (!Ready) return;
            _body.SetActive(!_collapsed); _widget.sizeDelta = new Vector2(360, _collapsed ? 42 : 328);
            Rect safe = APXMobileUIHelper.SafeScreenRect;
            var bounds = Preferences.Layout(safe.x, Screen.height - safe.yMax, safe.width, safe.height, 1);
            _widget.anchoredPosition = new Vector2(bounds.X, -bounds.Y);
            _widget.localScale = new Vector3(bounds.Scale, bounds.Scale, 1);
        }
        internal void Tick() { Layout(); }
        internal void Drag(Vector2 delta)
        {
            var safe = APXMobileUIHelper.SafeScreenRect;
            Preferences.Move(delta.x, -delta.y, safe.width, safe.height, 1); Layout();
        }
        internal void SuspendInteraction() { if (_drag != null) _drag.Cancel(); }
        private static string Key(string pc, string mobile) => APXMobileUIHelper.IsMobile ? mobile : pc;
        private void Load()
        {
            Preferences.Background = APXConfigService.GetFloat(Key(APXConfigKeys.StatsWidgetPcBackground, APXConfigKeys.StatsWidgetAndroidBackground), .78f);
            Preferences.TextAlpha = APXConfigService.GetFloat(Key(APXConfigKeys.StatsWidgetPcText, APXConfigKeys.StatsWidgetAndroidText), .96f);
            Preferences.Scale = APXConfigService.GetFloat(Key(APXConfigKeys.StatsWidgetPcScale, APXConfigKeys.StatsWidgetAndroidScale), 1);
            Preferences.X = APXConfigService.GetFloat(Key(APXConfigKeys.StatsWidgetPcX, APXConfigKeys.StatsWidgetAndroidX), 1);
            Preferences.Y = APXConfigService.GetFloat(Key(APXConfigKeys.StatsWidgetPcY, APXConfigKeys.StatsWidgetAndroidY), .12f);
            Preferences.Locked = APXConfigService.GetBool(Key(APXConfigKeys.StatsWidgetPcLock, APXConfigKeys.StatsWidgetAndroidLock), false);
            Preferences.Normalize();
        }
        internal void ReloadPreferences()
        {
            Load(); if (!Ready) return;
            _background.color = new Color(1, 1, 1, Preferences.Background);
            foreach (var text in _body.GetComponentsInChildren<Text>(true)) { var color = text.color; color.a = Preferences.TextAlpha; text.color = color; }
            APXUITheme.RefreshFonts(_root); Layout();
        }
        internal void Save()
        {
            APXConfigService.SetFloat(Key(APXConfigKeys.StatsWidgetPcX, APXConfigKeys.StatsWidgetAndroidX), Preferences.X);
            APXConfigService.SetFloat(Key(APXConfigKeys.StatsWidgetPcY, APXConfigKeys.StatsWidgetAndroidY), Preferences.Y);
            APXConfigService.Save();
        }
        public void Dispose() { if (!Ready) return; Save(); UnityEngine.Object.Destroy(_root); _root = null; }
    }
}
