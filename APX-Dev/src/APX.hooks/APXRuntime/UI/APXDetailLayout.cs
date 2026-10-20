using System;
using APXRuntime.MatchPort;
using APXRuntime.Utilities;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace APXRuntime.UI
{
    // Shared pink/yellow presentation for the feature center and floating widgets.
    internal static class APXDetailLayout
    {
        internal static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = APXUIFactory.CreateUIObject(name, parent.gameObject).GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        internal static Image Panel(RectTransform rt, string sprite = "card_base.png", float alpha = 1)
        {
            var image = rt.gameObject.AddComponent<Image>(); image.sprite = APXUIAssetService.GetSprite(sprite);
            image.type = Image.Type.Sliced; image.color = new Color(1, 1, 1, alpha); return image;
        }
        internal static void Fill(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }
        internal static Text Text(Transform p, string value, float x, float y, float w, float h, int size = 14, bool muted = false)
        {
            var label = APXUIFactory.CreateText(p.gameObject, "Label", value, size, muted ? APXUITheme.SecondaryText : APXUITheme.PrimaryText, TextAnchor.MiddleLeft);
            var rt = label.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            label.supportRichText = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; return label;
        }
        internal static Button Button(Transform p, string caption, float x, float y, float w, float h, Action action, bool selected = false)
        {
            var button = APXUIFactory.CreateButton(p.gameObject, "Button", caption,
                APXUIAssetService.GetSprite(selected ? "button_selected.png" : "button_secondary.png"), Color.white, APXUITheme.PrimaryText, action, 13);
            var rt = button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return button;
        }
    }

    internal sealed class APXMatchPlayerCard
    {
        internal static readonly string[] Labels = { "击杀", "死亡", "输出", "承伤", "治疗", "累计金币", "转账", "行动回合", "结算金币" };
        private readonly Text _name, _character, _score, _state;
        private readonly Text[] _values = new Text[9];
        private readonly GameObject _root;
        private readonly RawImage _avatar;
        private string _headUrl;

        internal APXMatchPlayerCard(GameObject parent, bool compact)
        {
            _root = APXUIFactory.CreateUIObject("PlayerDetailCard", parent);
            var rt = _root.GetComponent<RectTransform>(); APXDetailLayout.Fill(rt);
            APXDetailLayout.Panel(rt).raycastTarget = false;
            _avatar = APXDetailLayout.Box(rt, "Avatar", 14, 16, 44, 44).gameObject.AddComponent<RawImage>();
            _avatar.raycastTarget = false; _avatar.enabled = false;
            _name = APXDetailLayout.Text(rt, "等待玩家", 68, 12, compact ? 145 : 450, 28, compact ? 15 : 17);
            _character = APXDetailLayout.Text(rt, "—", 68, 42, compact ? 145 : 450, 24, 12, true);
            _score = APXDetailLayout.Text(rt, "—", 0, 12, 110, 32, 24);
            _score.color = APXUITheme.NumericAccent;
            _state = APXDetailLayout.Text(rt, "星枢评分 / 10", 0, 44, 130, 22, 11, true);
            foreach (var label in new[] { _score, _state })
            {
                var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-18, label == _score ? -12 : -44); label.alignment = TextAnchor.MiddleRight;
            }
            int columns = compact ? 3 : 9;
            for (int i = 0; i < 9; i++)
            {
                int col = i % columns, row = i / columns;
                var cell = APXDetailLayout.Box(rt, "Metric" + i, 0, 78 + row * 48, 0, 44);
                cell.anchorMin = new Vector2((float)col / columns, 1); cell.anchorMax = new Vector2((float)(col + 1) / columns, 1);
                cell.offsetMin = new Vector2(12, -122 - row * 48); cell.offsetMax = new Vector2(-12, -78 - row * 48);
                var title = APXDetailLayout.Text(cell, Labels[i], 0, 0, 0, 18, 11, true);
                title.rectTransform.anchorMax = new Vector2(1, 1); title.rectTransform.sizeDelta = new Vector2(0, 18); title.alignment = TextAnchor.MiddleCenter;
                _values[i] = APXDetailLayout.Text(cell, "—", 0, 20, 0, 24, compact ? 16 : 18);
                _values[i].rectTransform.anchorMax = new Vector2(1, 1); _values[i].rectTransform.sizeDelta = new Vector2(0, 24); _values[i].alignment = TextAnchor.MiddleCenter;
            }
        }

        internal void Refresh(StatsWidgetFrame.Player player, int slot)
        {
            _name.text = player == null ? "P" + (slot + 1) + " · 等待玩家" : "P" + (slot + 1) + "  " + player.Name;
            _character.text = player?.Character ?? "进入对局后显示";
            _score.text = player?.Score ?? "—";
            _state.text = player == null ? "星枢评分 / 10" : (player.Mvp ? "MVP · " : "") + player.State;
            for (int i = 0; i < 9; i++) _values[i].text = player?.Metrics[i] ?? "—";
            string head = player?.HeadUrl ?? "";
            if (head != _headUrl)
            {
                _headUrl = head;
                _avatar.texture = null; _avatar.enabled = head.Length > 0;
                _avatar.rectTransform.sizeDelta = new Vector2(44, 44);
                if (_avatar.enabled) APXDetailImages.Load(_avatar, new List<string> { head }, 0, () => _headUrl == head);
            }
        }
    }
}
