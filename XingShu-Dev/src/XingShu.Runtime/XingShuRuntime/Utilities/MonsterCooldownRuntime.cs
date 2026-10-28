using System;
using System.Collections.Generic;
using Core.Scene;
using GameLogic;
using Tools;
using UnityEngine;
using party.model;
using party.protocol;

namespace XingShuRuntime.Utilities;

// NPC hero-info requests did not supply an attributable CD in the live test.
// Keep actor snapshots/cast notifications, project across real round numbers,
// and disclose every projected value. Never modify the game's skill state.
public sealed class MonsterCooldownRuntime : MonoBehaviour
{
    private static MonsterCooldownRuntime _instance;
    private readonly MonsterCooldownLedger _ledger = new MonsterCooldownLedger();
    private readonly Dictionary<long, BattlePlayerData> _players = new Dictionary<long, BattlePlayerData>();
    private readonly List<long> _remove = new List<long>();
    private readonly HashSet<long> _seen = new HashSet<long>();
    private object _room;
    private long _roomId, _matchStarted;
    private BattleLogic _battle;
    private double _nextTick, _nextError;
    private double _lastClock = ThinkingClock.RealSeconds(), _clock;

    internal static void Install(GameObject host)
    {
        if (host != null && host.GetComponent<MonsterCooldownRuntime>() == null) host.AddComponent<MonsterCooldownRuntime>();
    }

    internal static int Read(long monster, int skill, out bool estimated)
    {
        estimated = false;
        return _instance == null ? -1 : _instance._ledger.Read(monster, skill, out estimated);
    }

    private void Awake()
    {
        _instance = this;
        Log("Monitor installed; event tracking, disclosed round projections, no NPC hero-info polling");
    }
    private void OnDestroy() { Reset(); if (_instance == this) _instance = null; }

    private void Reset()
    {
        _ledger.Reset(); _players.Clear(); _seen.Clear();
        _room = null; _roomId = _matchStarted = 0; _battle = null;
    }

    private static void Log(string message) { XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[MonsterCD] " + message); }

    private bool Context()
    {
        var logic = SimpleSingletonProvider<GameLogicManager>.inst;
        var room = logic?.room?.curRoomInfo;
        var battle = logic?.battle;
        if (room?.info == null || room.info.Id <= 0 || room.info.StartTime <= 0 || battle?.PlayerDatas == null ||
            logic.room.roomController == null || logic.room.roomController.roomStateType != RoomStateType.RUNNING ||
            SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
        { Reset(); return false; }
        if (!ReferenceEquals(_room, room) || !ReferenceEquals(_battle, battle) ||
            _roomId != room.info.Id || _matchStarted != room.info.StartTime)
        { Reset(); _room = room; _battle = battle; _roomId = room.info.Id; _matchStarted = room.info.StartTime; }
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
        double clock = ThinkingClock.RealSeconds();
        _clock += Math.Max(0d, clock - _lastClock); _lastClock = Math.Max(_lastClock, clock);
        if (_clock < _nextTick) return;
        _nextTick = _clock + 0.1d;
        try
        {
            if (!Context()) return;
            _seen.Clear();
            foreach (var player in _battle.PlayerDatas)
            {
                if (player?.player == null || Monster(player.player.Id) == null) continue;
                long id = player.player.Id;
                BattlePlayerData previous;
                if (_players.TryGetValue(id, out previous) && !ReferenceEquals(previous, player)) _ledger.Remove(id);
                _players[id] = player; _seen.Add(id);
                int skill = player.player.GetBattleActiveSkillId();
                if (_ledger.Contains(id, skill)) continue; // Never overwrite a preceding cast notification.
                int cd;
                bool explicitValue = player.player.Hero.SkillCds.TryGetValue(skill, out cd) && cd >= 0;
                if (explicitValue) _ledger.ObserveServer(id, skill, cd, _clock);
                else _ledger.ObserveEstimated(id, skill, 0, _clock);
                // Official InitSkill uses this actor's Hero.SkillCds with a 0
                // default. Only use that default at first observation, marked
                // estimated; a missing entry in a shared round map is ignored.
                Log("Initial state: monster=" + id + ", skill=" + skill + ", round=" + _ledger.Round +
                    ", cd=" + _ledger.Read(id, skill) + ", estimated=" + !explicitValue);
            }
            _remove.Clear();
            foreach (long id in _players.Keys) if (!_seen.Contains(id)) _remove.Add(id);
            foreach (long id in _remove) { _players.Remove(id); _ledger.Remove(id); }
        }
        catch (Exception ex)
        {
            Reset();
            if (_clock >= _nextError)
            { _nextError = _clock + 10d; Log("Read unavailable: " + ex.GetType().Name); }
        }
    }

    // Guarded protocol observers preserve the original official handlers.
    public static void ObserveHeroInfo(GetHeroInfoS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || self == null || !self.Context() || model.Cd < 0) return;
            var monster = self.Monster(model.PlayerId);
            if (monster == null) return; // A zero/mismatched ID is not an NPC CD.
            int skill = monster.player.GetBattleActiveSkillId();
            self._ledger.ObserveServer(model.PlayerId, skill, model.Cd, self._clock);
            Log("Official info: monster=" + model.PlayerId + ", skill=" + skill + ", round=" + self._ledger.Round + ", cd=" + model.Cd);
        }
        catch (Exception) { }
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
                if (cd == null || cd.Cd < 0) continue;
                if (cd.PlayerId != 0 && effect.PlayerId != 0 && cd.PlayerId != effect.PlayerId) continue;
                long id = cd.PlayerId != 0 ? cd.PlayerId : effect.PlayerId;
                var monster = self.Monster(id);
                if (monster == null) continue;
                int skill = monster.player.GetBattleActiveSkillId();
                self._ledger.ObserveServer(id, skill, cd.Cd, self._clock);
                Log("Server push: monster=" + id + ", skill=" + skill + ", round=" + self._ledger.Round + ", cd=" + cd.Cd);
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
            if (monster == null) return;
            int skill = monster.player.GetBattleActiveSkillId(), cd;
            bool hasCooldown = model.SkillCds.TryGetValue(skill, out cd);
            if (!self._ledger.ObserveSkillUse(model.PlayerId, skill, model.SkillId, hasCooldown, cd, self._clock)) return;
            if ((!hasCooldown || cd < 0) && StaticConfigure.Skill.InfoDict.TryGetValue(skill, out var config))
                self._ledger.ObserveEstimated(model.PlayerId, skill, config.Round, self._clock);
            bool estimated;
            int value = self._ledger.Read(model.PlayerId, skill, out estimated);
            Log("Skill observed: monster=" + model.PlayerId + ", skill=" + skill + ", usedSkill=" + model.SkillId +
                ", round=" + self._ledger.Round + ", targetCount=" + model.TargetIds.Count + ", cd=" + value + ", estimated=" + estimated);
        }
        catch (Exception) { }
    }

    public static void ObserveRound(RoundStartS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || self == null || !self.Context()) return;
            int before = self._ledger.Round;
            self._ledger.ObserveRound(model.Round);
            // The packet's PlayerId scopes the map. Never apply a player's map
            // or its missing-key default to all monsters.
            var monster = self.Monster(model.PlayerId);
            int cd;
            if (monster != null)
            {
                int skill = monster.player.GetBattleActiveSkillId();
                if (model.SkillCds.TryGetValue(skill, out cd)) self._ledger.ObserveServer(model.PlayerId, skill, cd, self._clock);
            }
            if (self._ledger.Round != before) Log("Round observed: round=" + self._ledger.Round + ", actorScopedMonster=" + (monster != null));
        }
        catch (Exception) { }
    }

    public static void ObserveAction(ActionStartNotifyS2C model)
    {
        try
        {
            var self = _instance;
            if (model == null || self == null || !self.Context() || self._ledger.CurrentActor == model.PlayerId) return;
            long previous = self._ledger.ObserveAction(model.PlayerId);
            var monster = self.Monster(previous);
            if (monster == null) return;
            bool estimated;
            int skill = monster.player.GetBattleActiveSkillId();
            int cd = self._ledger.Read(previous, skill, out estimated);
            Log("Action ended; CD retained: monster=" + previous + ", skill=" + skill + ", round=" + self._ledger.Round +
                ", cd=" + cd + ", estimated=" + estimated);
        }
        catch (Exception) { }
    }
}
