using System;
using System.Collections.Generic;
using System.Text;
using XingShuRuntime.Core;
using XingShuRuntime.Utilities;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using party.model;

namespace XingShuRuntime.Targeting
{
    /// <summary>Read-only, maximum-dice preview. Unmapped forks remain candidates.</summary>
    public sealed class MonsterTargetRuntime : MonoBehaviour
    {
        private sealed class Row
        {
            public GTextField Anchor;
            public GTextField Text;
            public GGraph Back;
        }

        private static MonsterTargetRuntime _instance;
        private readonly Dictionary<GTextField, Row> _rows = new Dictionary<GTextField, Row>();
        private readonly HashSet<GTextField> _seen = new HashSet<GTextField>();
        private readonly List<GTextField> _remove = new List<GTextField>();
        private readonly List<string> _details = new List<string>();
        private bool _enabled;
        private object _room;
        private float _nextTick;
        private float _nextError;
        private int _selected;
        private string _status = "等待进入 PVE 对局";
        private long _turnId;
        private long _attackedMonsterId;
        private int _attackedRound;

        internal static bool IsEnabled => _instance != null ? _instance._enabled :
            XingShuConfigService.GetBool(XingShuConfigKeys.MonsterTargetEnabled, XingShuConfigKeys.MonsterTargetEnabledDefault);

        internal static string Summary => _instance == null ? "等待索敌模块初始化" : _instance.CurrentSummary();

        internal static void SetEnabled(bool value)
        {
            XingShuConfigService.SetBool(XingShuConfigKeys.MonsterTargetEnabled, value);
            if (_instance == null) return;
            _instance._enabled = value;
            _instance._nextTick = 0f;
            if (!value) _instance.ClearSession();
        }

        internal static void Select(int delta)
        {
            if (_instance == null || _instance._details.Count == 0) return;
            int count = _instance._details.Count;
            _instance._selected = (_instance._selected + delta % count + count) % count;
        }

        public static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<MonsterTargetRuntime>() == null)
                host.AddComponent<MonsterTargetRuntime>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
            _enabled = XingShuConfigService.GetBool(XingShuConfigKeys.MonsterTargetEnabled, XingShuConfigKeys.MonsterTargetEnabledDefault);
            Debug.Log("[XingShu Targeting] 怪物索敌已接入：最大10格推演，未确认岔路保留候选");
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.25f;
            try { Refresh(); }
            catch (Exception ex)
            {
                ClearRows();
                _details.Clear();
                _status = "索敌数据暂未就绪";
                if (Time.unscaledTime >= _nextError)
                {
                    _nextError = Time.unscaledTime + 10f;
                    XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[XingShu Targeting] " + ex);
                }
            }
        }

        private void LateUpdate()
        {
            foreach (var row in _rows.Values) Place(row);
        }

        private void Refresh()
        {
            var logic = SimpleSingletonProvider<GameLogicManager>.inst;
            var room = logic?.room?.curRoomInfo;
            if (!ReferenceEquals(_room, room)) { ClearSession(); _room = room; }
            if (!_enabled) { _status = "索敌显示已关闭"; return; }
            if (room == null || !HandCardDisplayManager.IsPVEMode() || logic.battle?.PlayerDatas == null ||
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
            {
                ClearSession();
                _status = "等待进入 PVE 对局";
                return;
            }

            long turn = PveTurnContext.CurrentPlayerId();
            if (turn != _turnId)
            {
                _turnId = turn;
                _attackedMonsterId = 0;
            }
            var fight = logic.fight?.battleFightData;
            var data = logic.battle.PlayerDatas;
            if (fight != null && !fight.isEnd && fight.attackerInfo != null && fight.attackerInfo.PlayerId == turn)
            {
                foreach (var monster in data)
                    if (monster?.player != null && monster.player.Id == turn && monster.characterType == CharacterType.Monster)
                    {
                        _attackedMonsterId = turn;
                        _attackedRound = monster.player.Hero.Round;
                        break;
                    }
            }

            // Build the shared graph/player snapshot once, not once per monster.
            var snapshot = MonsterTargetContext.ExtractInput();
            _seen.Clear();
            _details.Clear();
            bool combat = (fight != null && !fight.isEnd) || SimpleSingletonProvider<UIManager>.inst.Fight.isShowing;
            foreach (var monster in data)
            {
                if (monster?.player == null || monster.characterType != CharacterType.Monster ||
                    monster.player.Hero == null || monster.Property?.HP == null || monster.Property.HP.Value <= 0 ||
                    monster.player.Hero.MonsterType == Hero.Types.MonsterType.PveAllyMonster) continue;
                var input = snapshot;
                input.MonsterId = monster.player.Id;
                input.CurrentNode = monster.CharacterInst?.standLand != null ? monster.CharacterInst.standLand.Id : monster.player.NodeId;
                input.MonsterNodeKnown = input.CurrentNode > 0;
                input.HasAttackedKnown = _attackedMonsterId == input.MonsterId && turn == input.MonsterId &&
                    _attackedRound == monster.player.Hero.Round;
                input.HasAttackedThisTurn = input.HasAttackedKnown;
                string directionSource;
                var steps = MonsterTargetContext.ExtractForwardSteps(monster, input.GraphEdges, out directionSource);
                var preview = MonsterTargetPreview.Calculate(input, steps);
                string compact;
                string reason;
                if (preview.AlreadyAttacked)
                {
                    compact = "本回合已攻击 · 继续原路线";
                    reason = "已观察到本回合主动战斗；本回合不再选择追击目标。";
                }
                else if (!preview.IsValid)
                {
                    compact = steps == null ? "索敌：等待箭头同步" : "索敌：等待路径数据";
                    reason = steps == null ? directionSource + "，暂不推测目标。" : "地图连接或玩家位置尚未同步完整，暂不推测目标。";
                }
                else if (preview.Candidates.Count == 0)
                {
                    compact = "10格推演：无候选 · 沿原路线";
                    reason = "仅沿箭头前进，每步排除来路；先行一格后，剩余9格内没有可达的存活玩家。";
                }
                else
                {
                    bool uncertain = preview.AmbiguousFirstStep || preview.TiedTargets || preview.SomeRoutesHaveNoTarget;
                    var text = new StringBuilder(uncertain ? "10格候选 → " : "10格推演 → ");
                    foreach (var candidate in preview.Candidates)
                    {
                        if (text[text.Length - 1] != ' ') text.Append(" / ");
                        text.Append(PlayerTag(data, candidate.TargetPlayerId, preview.Candidates.Count == 1));
                    }
                    if (preview.Candidates.Count == 1) text.Append(" · ").Append(preview.Candidates[0].Distance).Append("格");
                    compact = text.ToString();
                    reason = uncertain ? "仅沿箭头前进；多个箭头出口或同距离先后未确认，保留可能目标。" : "仅沿箭头前进，不掉头；按未攻击、最大骰10格推演，实际骰点可能不足。";
                    if (preview.SomeRoutesHaveNoTarget) reason += " 部分出口无可达目标。";
                }

                var detail = new StringBuilder();
                detail.Append(monster.player.GetNick()).Append("  ·  地图格 #").Append(input.CurrentNode).Append('\n');
                detail.Append("当前箭头：");
                if (steps == null) detail.Append("等待同步");
                else for (int i = 0; i < steps.Count; i++)
                {
                    if (i > 0) detail.Append(" / ");
                    detail.Append('#').Append(steps[i]);
                }
                detail.Append('\n');
                detail.Append("方向来源：").Append(directionSource).Append('\n');
                detail.Append(compact).Append('\n').Append(reason);
                if (preview.Candidates.Count > 0)
                {
                    detail.Append("\n路径示例：");
                    var path = preview.Candidates[0].Path;
                    for (int i = 0; i < path.Count; i++)
                    {
                        if (i > 0) detail.Append(" → ");
                        detail.Append(path[i]);
                    }
                }
                _details.Add(detail.ToString());
                // Targeting is a map annotation only; never add it to the combat screen.
                if (!combat)
                {
                    var label = monster.CharacterInst?.window?.Com_AttrInfo?.txt_Name;
                    if (label != null && !label.isDisposed && label.parent != null)
                        Show(label, compact, preview.Candidates.Count == 1 && !preview.AmbiguousFirstStep && !preview.TiedTargets && !preview.SomeRoutesHaveNoTarget);
                }
            }
            PruneRows();
            _status = _details.Count == 0 ? "当前没有可推演的怪物" : "沿箭头前进 · 最大10格（含先行1格）；暂不计技能加步。";
            if (_selected >= _details.Count) _selected = 0;
        }

        private static string PlayerTag(List<BattlePlayerData> players, long id, bool includeName)
        {
            foreach (var player in players)
                if (player?.player != null && player.player.Id == id)
                {
                    string slot = "P" + (player.player.Slot + 1);
                    if (!includeName) return slot;
                    string name = player.player.GetNick().Replace('[', '［').Replace(']', '］').Replace('\n', ' ');
                    return slot + " " + (name.Length > 8 ? name.Substring(0, 8) + "…" : name);
                }
            return "未知玩家";
        }

        private void Show(GTextField anchor, string text, bool single)
        {
            _seen.Add(anchor);
            Row row;
            if (!_rows.TryGetValue(anchor, out row) || row.Text.isDisposed || row.Text.parent != anchor.parent)
            {
                if (row != null) Dispose(row);
                row = new Row { Anchor = anchor };
                row.Back = new GGraph { name = "XINGSHU_MonsterTargetBack", touchable = false };
                anchor.parent.AddChild(row.Back);
                row.Text = new GTextField
                {
                    name = "XINGSHU_MonsterTarget", touchable = false, singleLine = true,
                    autoSize = AutoSizeType.Both, stroke = 0f
                };
                anchor.parent.AddChild(row.Text);
                _rows[anchor] = row;
            }
            var format = row.Text.textFormat;
            format.font = anchor.textFormat.font;
            format.size = Mathf.Clamp(Mathf.RoundToInt(anchor.textFormat.size * 0.9f), 22, 30);
            format.color = new Color(0.16f, 0.13f, 0.18f);
            row.Text.textFormat = format;
            row.Text.text = text;
            float width = row.Text.width + 12f, height = row.Text.height + 6f;
            row.Back.DrawRoundRect(width, height, single ? new Color(1f, 0.94f, 0.76f, 0.94f) :
                new Color(1f, 0.87f, 0.93f, 0.94f), new[] { 6f, 6f, 6f, 6f });
            Place(row);
        }

        private static void Place(Row row)
        {
            if (row.Anchor.isDisposed || row.Text.isDisposed || row.Back.isDisposed || row.Anchor.parent == null) return;
            var kill = row.Anchor.parent.GetChild("XINGSHU_MonsterStatusBack");
            float bottom = kill != null && kill.visible ? kill.y : row.Anchor.y;
            row.Back.SetXY(row.Anchor.x - 2f, bottom - row.Back.height - 4f);
            row.Text.SetXY(row.Back.x + 6f, row.Back.y + 3f);
            row.Text.visible = row.Back.visible = row.Anchor.visible;
            row.Text.alpha = row.Back.alpha = row.Anchor.alpha;
        }

        private string CurrentSummary()
        {
            if (!_enabled) return "索敌显示已关闭";
            if (_details.Count == 0) return _status;
            return "[" + (_selected + 1) + "/" + _details.Count + "]  " + _status + "\n" + _details[_selected];
        }

        private void PruneRows()
        {
            _remove.Clear();
            foreach (var pair in _rows) if (!_seen.Contains(pair.Key) || pair.Key.isDisposed) _remove.Add(pair.Key);
            foreach (var anchor in _remove) { Dispose(_rows[anchor]); _rows.Remove(anchor); }
        }

        private static void Dispose(Row row)
        {
            if (row.Text != null && !row.Text.isDisposed) row.Text.Dispose();
            if (row.Back != null && !row.Back.isDisposed) row.Back.Dispose();
        }

        private void ClearRows()
        {
            foreach (var row in _rows.Values) Dispose(row);
            _rows.Clear();
            _seen.Clear();
            _remove.Clear();
        }

        private void ClearSession()
        {
            ClearRows();
            _details.Clear();
            _selected = 0;
            _turnId = _attackedMonsterId = 0;
            _attackedRound = 0;
        }

        private void OnDisable() { ClearSession(); }
        private void OnDestroy() { ClearSession(); if (_instance == this) _instance = null; }
    }
}
