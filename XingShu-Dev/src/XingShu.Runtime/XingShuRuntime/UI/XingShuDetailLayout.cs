using System;
using XingShuRuntime.MatchPort;
using XingShuRuntime.Utilities;
using System.Collections.Generic;
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
        internal static readonly string[] Labels = { "击杀", "死亡", "输出", "承伤", "治疗", "累计金币", "转账", "行动回合", "移动步数" };
        private readonly Text _name, _character, _score, _state;
        private readonly Text[] _values = new Text[Labels.Length];
        private readonly GameObject _root;
        private readonly RawImage _avatar;
        private string _headUrl;

        internal XingShuMatchPlayerCard(GameObject parent, bool compact)
        {
            bool phone = XingShuMobileUIHelper.IsMobile && !compact;
            _root = XingShuUIFactory.CreateUIObject("PlayerDetailCard", parent);
            var rt = _root.GetComponent<RectTransform>(); XingShuDetailLayout.Fill(rt);
            XingShuDetailLayout.Panel(rt).raycastTarget = false;
            _avatar = XingShuDetailLayout.Box(rt, "Avatar", 14, 16, 44, 44).gameObject.AddComponent<RawImage>();
            _avatar.raycastTarget = false; _avatar.enabled = false;
            _name = XingShuDetailLayout.Text(rt, "等待玩家", 68, 12, compact ? 145 : 450, 28, compact ? 15 : 17);
            _character = XingShuDetailLayout.Text(rt, "—", 68, 42, compact ? 145 : 450, 24, 12, true);
            _score = XingShuDetailLayout.Text(rt, "—", 0, 12, 110, 32, 24);
            _score.color = XingShuUITheme.NumericAccent;
            _state = XingShuDetailLayout.Text(rt, "星枢评分 / 10", 0, 44, 130, 22, 11, true);
            foreach (var label in new[] { _score, _state })
            {
                var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-18, label == _score ? -12 : -44); label.alignment = TextAnchor.MiddleRight;
            }
            if (phone)
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
            int columns = compact || phone ? 3 : Labels.Length;
            float top = phone ? 114 : 78, rowHeight = phone ? 92 : 48;
            for (int i = 0; i < Labels.Length; i++)
            {
                int col = i % columns, row = i / columns;
                var cell = XingShuDetailLayout.Box(rt, "Metric" + i, 0, top + row * rowHeight, 0, rowHeight - 4);
                cell.anchorMin = new Vector2((float)col / columns, 1); cell.anchorMax = new Vector2((float)(col + 1) / columns, 1);
                cell.offsetMin = new Vector2(12, -top - (row + 1) * rowHeight + 4); cell.offsetMax = new Vector2(-12, -top - row * rowHeight);
                var title = XingShuDetailLayout.Text(cell, Labels[i], 0, 0, 0, phone ? 36 : 18, phone ? 22 : 11, true);
                title.rectTransform.anchorMax = new Vector2(1, 1); title.rectTransform.sizeDelta = new Vector2(0, phone ? 36 : 18); title.alignment = TextAnchor.MiddleCenter;
                _values[i] = XingShuDetailLayout.Text(cell, "—", 0, phone ? 40 : 20, 0, phone ? 46 : 24, phone ? 28 : compact ? 16 : 18);
                _values[i].rectTransform.anchorMax = new Vector2(1, 1); _values[i].rectTransform.sizeDelta = new Vector2(0, phone ? 46 : 24); _values[i].alignment = TextAnchor.MiddleCenter;
            }
        }

        internal void Refresh(StatsWidgetFrame.Player player, int slot)
        {
            _name.text = player == null ? "P" + (slot + 1) + " · 等待玩家" : "P" + (slot + 1) + "  " + player.Name;
            _character.text = player?.Character ?? "进入对局后显示";
            _score.text = player?.Score ?? "—";
            _state.text = player == null ? "星枢评分 / 10" : (player.Mvp ? "MVP · " : "") + player.State;
            for (int i = 0; i < Labels.Length; i++) _values[i].text = player?.Metrics[i] ?? "—";
            string head = player?.HeadUrl ?? "";
            if (head != _headUrl)
            {
                _headUrl = head;
                _avatar.texture = null; _avatar.enabled = head.Length > 0;
                _avatar.rectTransform.sizeDelta = new Vector2(44, 44);
                if (_avatar.enabled) XingShuDetailImages.Load(_avatar, new List<string> { head }, 0, () => _headUrl == head);
            }
        }
    }
}
