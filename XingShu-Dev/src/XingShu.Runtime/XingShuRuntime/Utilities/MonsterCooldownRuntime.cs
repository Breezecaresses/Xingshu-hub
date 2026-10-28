using System;
using System.Collections.Generic;
using Core.Net;
using Core.Scene;
using GameLogic;
using Tools;
using UnityEngine;
using party.model;
using party.protocol;

namespace XingShuRuntime.Utilities;

// Reads the same per-player server Cd as the official PVE detail window.
// Empty casts are observed before animation/target filtering. Shared round
// skill maps and the local reactive skill panel are deliberately not sources.
public sealed class MonsterCooldownRuntime : MonoBehaviour
{
    private static MonsterCooldownRuntime _instance;
    private readonly MonsterCooldownLedger _ledger = new MonsterCooldownLedger();
    private readonly Dictionary<long, BattlePlayerData> _players = new Dictionary<long, BattlePlayerData>();
    private readonly List<BattlePlayerData> _live = new List<BattlePlayerData>();
    private readonly List<long> _remove = new List<long>();
    private readonly HashSet<long> _seen = new HashSet<long>();
    private object _room;
    private BattleLogic _battle;
    private RPCAsyncResult _request;
    private MonsterCooldownLedger.Request _token;
    private BattlePlayerData _requestPlayer;
    private double _deadline, _nextRequest, _nextTick, _nextError;
    private int _cursor;
    private long _lastActor, _priority;

    internal static void Install(GameObject host)
    {
        if (host != null && host.GetComponent<MonsterCooldownRuntime>() == null) host.AddComponent<MonsterCooldownRuntime>();
    }

    internal static int Read(long monster, int skill)
    {
        return _instance == null ? -1 : _instance._ledger.Read(monster, skill);
    }

    private void Awake() { _instance = this; }
    private void OnDestroy() { Reset(); if (_instance == this) _instance = null; }

    private void Reset()
    {
        _request = null; _requestPlayer = null; _token = default(MonsterCooldownLedger.Request);
        _ledger.Reset(); _players.Clear(); _live.Clear(); _seen.Clear();
        _room = null; _battle = null; _lastActor = _priority = 0; _cursor = 0; _nextRequest = 0d;
    }

    private bool Context()
    {
        var logic = SimpleSingletonProvider<GameLogicManager>.inst;
        var room = logic?.room?.curRoomInfo;
        var battle = logic?.battle;
        if (room == null || battle?.PlayerDatas == null || !HandCardDisplayManager.IsPVEMode() ||
            logic.room.roomController == null || logic.room.roomController.roomStateType != RoomStateType.RUNNING ||
            SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
        { Reset(); return false; }
        if (!ReferenceEquals(_room, room) || !ReferenceEquals(_battle, battle))
        { Reset(); _room = room; _battle = battle; }
        _ledger.ObserveRound(room.Round);
        return true;
    }

    private BattlePlayerData Monster(long id)
    {
        var data = _battle?.GetPlayerDataById(id);
        return data?.player?.Hero != null && data.characterType == CharacterType.Monster && data.Property?.HP != null &&
            data.Property.HP.Value > 0 && data.player.GetBattleActiveSkillId() > 0 ? data : null;
    }

    private void Update()
    {
        double now = Time.realtimeSinceStartup;
        if (now < _nextTick) return;
        _nextTick = now + 0.1d;
        try { Tick(now); }
        catch (Exception ex)
        {
            Reset();
            if (now >= _nextError)
            {
                _nextError = now + 10d;
                XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[MonsterCD] Read unavailable: " + ex.GetType().Name);
            }
        }
    }

    private void Tick(double now)
    {
        if (!MonsterKillProbability.IsEnabled || !Context()) { Reset(); return; }
        _live.Clear(); _seen.Clear();
        foreach (var player in _battle.PlayerDatas)
        {
            if (player?.player == null || Monster(player.player.Id) == null) continue;
            BattlePlayerData previous;
            if (_players.TryGetValue(player.player.Id, out previous) && !ReferenceEquals(previous, player))
                _ledger.Remove(player.player.Id);
            _players[player.player.Id] = player; _seen.Add(player.player.Id); _live.Add(player);
        }
        _remove.Clear();
        foreach (long id in _players.Keys) if (!_seen.Contains(id)) _remove.Add(id);
        foreach (long id in _remove) { _players.Remove(id); _ledger.Remove(id); }
        if (_request != null)
        {
            if (_request.keepWaiting && now < _deadline) return;
            _request = null; _requestPlayer = null;
        }
        if (_live.Count == 0 || now < _nextRequest || !MonoSingletonProvider<NetManager>.hasInstance ||
            !MonoSingletonProvider<NetManager>.inst.IsConnected) return;
        BattlePlayerData selected = null;
        foreach (var player in _live)
            if (player.player.Id == _priority && _ledger.NeedsRefresh(player.player.Id, player.player.GetBattleActiveSkillId(), now))
            { selected = player; break; }
        for (int i = 0; i < _live.Count && selected == null; i++)
        {
            var player = _live[_cursor++ % _live.Count];
            if (_ledger.NeedsRefresh(player.player.Id, player.player.GetBattleActiveSkillId(), now)) selected = player;
        }
        if (selected == null) return;
        _priority = 0;
        _requestPlayer = selected;
        _token = _ledger.Begin(selected.player.Id, selected.player.GetBattleActiveSkillId(), now);
        _nextRequest = now + 1d; _deadline = now + 10d;
        _request = _battle.RequestGetHeroInfoC2S(selected.player.Id);
    }

    // Called by guarded, read-only protocol observers; none replace official handlers.
    public static void ObserveHeroInfo(GetHeroInfoS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || self == null || self._requestPlayer == null || model.PlayerId != self._token.Monster ||
                !self.Context() || !ReferenceEquals(self.Monster(model.PlayerId), self._requestPlayer)) return;
            int before = self._ledger.Read(model.PlayerId, self._token.Skill);
            if (self._ledger.Reply(self._token, model.Cd, Time.realtimeSinceStartup) && before != model.Cd)
                XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[MonsterCD] Server reply: monster=" + model.PlayerId +
                    ", skill=" + self._token.Skill + ", round=" + self._ledger.Round + ", cd=" + model.Cd);
        }
        catch (Exception) { /* Invalid/late data never establishes readiness. */ }
    }

    public static void ObserveAttributes(UpdateHeroAttrS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || self == null || !self.Context()) return;
            foreach (var effect in model.EffectDatas)
            {
                var cd = effect?.Cd;
                if (cd == null) continue;
                if (cd.PlayerId != 0 && effect.PlayerId != 0 && cd.PlayerId != effect.PlayerId) continue;
                long id = cd.PlayerId != 0 ? cd.PlayerId : effect.PlayerId;
                var monster = self.Monster(id);
                if (monster == null) continue;
                int before = self._ledger.Read(id, monster.player.GetBattleActiveSkillId());
                self._ledger.ObserveServer(id, monster.player.GetBattleActiveSkillId(), cd.Cd, Time.realtimeSinceStartup);
                if (before != cd.Cd)
                    XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[MonsterCD] Server push: monster=" + id +
                        ", skill=" + monster.player.GetBattleActiveSkillId() + ", round=" + self._ledger.Round + ", cd=" + cd.Cd);
            }
        }
        catch (Exception) { }
    }

    public static void ObserveUse(UseEffectCardS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || !model.UseSkill || self == null || !self.Context()) return;
            var monster = self.Monster(model.PlayerId);
            if (monster == null || monster.player.GetBattleActiveSkillId() != model.SkillId) return;
            int cd;
            if (model.SkillCds.TryGetValue(model.SkillId, out cd))
                self._ledger.ObserveServer(model.PlayerId, model.SkillId, cd, Time.realtimeSinceStartup);
            else
                self._ledger.Invalidate(model.PlayerId, model.SkillId);
            // TargetIds may be empty: use attempts/empty casts still need the
            // server's updated CD. Never require an animation or a hit effect.
            self._priority = model.PlayerId;
            XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[MonsterCD] Skill observed: monster=" + model.PlayerId +
                ", skill=" + model.SkillId + ", round=" + self._ledger.Round + ", targetCount=" + model.TargetIds.Count +
                ", cd=" + self._ledger.Read(model.PlayerId, model.SkillId));
        }
        catch (Exception) { }
    }

    public static void ObserveRound(RoundStartS2C model)
    {
        try { var self = _instance; if (model != null && self != null && self.Context()) self._ledger.ObserveRound(model.Round); }
        catch (Exception) { }
    }

    public static void ObserveAction(ActionStartNotifyS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || self == null || !self.Context() || self._lastActor == model.PlayerId) return;
            var previous = self.Monster(self._lastActor);
            if (previous != null)
            {
                self._ledger.Invalidate(previous.player.Id, previous.player.GetBattleActiveSkillId());
                self._priority = previous.player.Id;
            }
            self._lastActor = model.PlayerId;
        }
        catch (Exception) { }
    }
}
