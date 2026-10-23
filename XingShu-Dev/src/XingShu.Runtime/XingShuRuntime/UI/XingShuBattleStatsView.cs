using XingShuRuntime.MatchPort;
using XingShuRuntime.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace XingShuRuntime.UI
{
    internal sealed class XingShuBattleStatsView
    {
        private readonly GameObject _root;
        private readonly XingShuMatchPlayerCard[] _players = new XingShuMatchPlayerCard[4];
        private readonly Text _status;
        private readonly Toggle _floating;
        private float _next;
        internal GameObject Root => _root;

        internal XingShuBattleStatsView(Transform parent)
        {
            _root = XingShuUIFactory.CreateUIObject("Page_BattleStats", parent.gameObject);
            XingShuDetailLayout.Fill(_root.GetComponent<RectTransform>());
            XingShuUIFactory.CreateVerticalScrollView(_root, "MatchDetails", out GameObject content);
            var header = Card(content, "MatchHeader", 112);
            XingShuDetailLayout.Text(header.transform, "对局数据 · 星枢评分 v2", 20, 12, 480, 30, 18);
            _status = XingShuDetailLayout.Text(header.transform, "等待有效对局", 20, 44, 700, 24, 12, true);
            _floating = XingShuUIFactory.CreateToggle(header, "Floating", "显示统计挂件", BattleStatsOverlay.AutoShowPreference,
                value => BattleStatsOverlay.AutoShowPreference = value);
            var toggleRt = _floating.GetComponent<RectTransform>();
            toggleRt.anchorMin = toggleRt.anchorMax = toggleRt.pivot = new Vector2(0, 1);
            toggleRt.anchoredPosition = new Vector2(20, -76); toggleRt.sizeDelta = new Vector2(280, 28);
            for (int i = 0; i < 4; i++) _players[i] = new XingShuMatchPlayerCard(Card(content, "Player" + i, 142), false);
            var note = Card(content, "RatingSource", 124);
            var text = XingShuDetailLayout.Text(note.transform,
                "评分与 Hub 相同：取综合、输出、辅助三项最高分，再按死亡次数扣分。\n局内为开启观察后的实时参考；收到服务器结算后替换为整局数据。缺失指标显示 —，不补成 0。\n辅助转账达到 20 时有基础贡献；星枢评分属于项目参考值。",
                20, 14, 0, 0, 12, true);
            XingShuDetailLayout.Fill(text.rectTransform, 20, 14, 20, 14);
        }

        private static GameObject Card(GameObject parent, string name, float height)
        {
            var go = XingShuUIFactory.CreateUIObject(name, parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            XingShuDetailLayout.Panel(go.GetComponent<RectTransform>());
            return go;
        }

        internal void UpdateSync()
        {
            if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + .25f;
            var frame = BattleStatsOverlay.GetCurrentFrame();
            _floating.SetIsOnWithoutNotify(BattleStatsOverlay.AutoShowPreference);
            _status.text = frame == null ? "等待有效对局" : frame.Status + " · " + frame.Footer;
            for (int i = 0; i < 4; i++) _players[i].Refresh(frame?.Players[i], i);
        }
    }
}
