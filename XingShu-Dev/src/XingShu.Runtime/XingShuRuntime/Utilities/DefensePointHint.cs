using System;
using Core.Scene;
using Core.Unit;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities;

/// <summary>Read-only thresholds from the native battle snapshot; never sends a combat action.</summary>
internal sealed class DefensePointHint : MonoBehaviour
{
    private static DefensePointHint _instance;
    private const string ConfigKey = "XINGSHU_DEFENSE_POINT_HINT";
    private bool _enabled;
    private float _nextTick;
    private GComponent _panel;
    private GGraph _background;
    private GTextField _label;
    private GTextField _warning;
    internal static bool IsEnabled => _instance != null && _instance._enabled;
    internal static void SetEnabled(bool value)
    {
        if (_instance == null) return;
        _instance._enabled = value;
        XingShuConfigService.SetBool(ConfigKey, value);
        XingShuConfigService.Save();
        if (!value) _instance.RemoveLabel();
    }
    public static void Install(GameObject host)
    {
        if (_instance != null) return;
        _instance = host.AddComponent<DefensePointHint>();
        _instance._enabled = XingShuConfigService.GetBool(ConfigKey, true);
    }
    private void Update()
    {
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 0.1f;
        try { Tick(); }
        catch (Exception) { RemoveLabel(); }
    }
    private void Tick()
    {
        if (!_enabled || !SimpleSingletonProvider<SceneManager>.hasInstance ||
            SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle ||
            !SimpleSingletonProvider<GameLogicManager>.hasInstance ||
            !SimpleSingletonProvider<UIManager>.hasInstance) { RemoveLabel(); return; }
        var logic = SimpleSingletonProvider<GameLogicManager>.inst;
        var fight = logic.fight?.battleFightData;
        if (fight == null || fight.isEnd || fight.attackerInfo == null || fight.defenderInfo == null ||
            logic.account == null ||
            fight.defenderInfo.PlayerId != logic.account.GetPlayerID() ||
            logic.battle?.PlayerDatas == null)
        { RemoveLabel(); return; }
        // UIManager.Fight is a factory. Constructing it during login leaves CardPool
        // uninitialized and breaks the game's next scene unload in FightWindow.Dispose.
        var window = ShownNativeWindows.Find<FightWindow>(SimpleSingletonProvider<UIManager>.inst);
        var view = window?.contentPane as UIFightWindow;
        if (view == null || view.isDisposed || view.step == null ||
            (view.step.selectedIndex != 3 && view.step.selectedIndex != 5))
        { RemoveLabel(); return; }
        BattlePlayerData attacker = null, defender = null;
        foreach (var player in logic.battle.PlayerDatas)
        {
            if (player?.player == null) continue;
            if (player.player.Id == fight.attackerInfo.PlayerId) attacker = player;
            if (player.player.Id == fight.defenderInfo.PlayerId) defender = player;
        }
        if (attacker?.Property == null || defender?.Property == null || defender.Property.HP.Value <= 0)
        { RemoveLabel(); return; }
        int point = fight.attackerInfo.Point;
        var director = BattleSceneController.inst?.directorManager;
        var attackerUI = director?.attacker?._UI;
        if ((point < 1 || point > 6) && attackerUI != null && !attackerUI.isDisposed &&
            attackerUI.com_Attack.showPoint.selectedIndex > 0)
            int.TryParse(attackerUI.com_Attack.com_Point.txt_Point.text, out point);
        bool revealed = point >= 1 && point <= 6;
        long attack = (long)fight.attackerInfo.MaxAtk + (revealed ? point : 6);
        long defense = fight.defenderInfo.MinDef;
        long hp = defender.Property.HP.Value;
        if (attack < 0 || defense < 0 || fight.attackerInfo.MinAtk > fight.attackerInfo.MaxAtk ||
            fight.defenderInfo.MinDef > fight.defenderInfo.MaxDef) { RemoveLabel(); return; }
        long bonus = MonsterKillProbability.SyncedPveMarkDamage(attacker, defender);
        long floor = DefensePointMath.MinimumDamageDefense(attack);
        long survive = DefensePointMath.SurviveDefense(attack, hp, bonus);
        bool estimated = !revealed || fight.attackerInfo.MinAtk != fight.attackerInfo.MaxAtk ||
            fight.defenderInfo.MinDef != fight.defenderInfo.MaxDef;
        string advice = DefensePointMath.DodgeAdvice(hp, survive, fight.defenderInfo.MaxDef, estimated);
        string prefix = estimated ? "防御参考 · 预计上限" : "防御参考";
        string text = prefix + "\n最低 " + DefensePointMath.MinimumDamage(bonus) + " 伤害：总防御 ≥ " + floor +
            "\n" + DefensePointMath.Dice(floor, defense) + "\n" +
            (survive < 0 ? "当前生命无法承受最低伤害" :
                "避免击飞：总防御 ≥ " + survive + "\n" + DefensePointMath.Dice(survive, defense)) +
            "\n已计同步牌；特殊效果以结算为准";
        if (window.width < 480f || window.height < 400f) { RemoveLabel(); return; }
        if (_panel == null || _panel.isDisposed || _panel.parent != window || _label == null || _label.isDisposed ||
            _warning == null || _warning.isDisposed)
        {
            RemoveLabel();
            // Use the full-screen native fight window, not the animated defender
            // nameplate: that parent moves this text under the native top banner.
            _panel = new GComponent { name = "XINGSHU_DefensePointHintPanel", touchable = false, opaque = false };
            _background = new GGraph { touchable = false };
            _panel.AddChild(_background);
            _warning = new GTextField { name = "XINGSHU_DefenseDodgeAdvice", touchable = false,
                autoSize = AutoSizeType.Height, align = AlignType.Left, stroke = 1f,
                strokeColor = new Color(0f, 0f, 0f, 0.9f) };
            var warningFormat = _warning.textFormat;
            warningFormat.size = Application.isMobilePlatform ? 28 : 24;
            warningFormat.lineSpacing = 5;
            warningFormat.bold = true;
            warningFormat.color = new Color(1f, 0.58f, 0.34f);
            _warning.textFormat = warningFormat;
            _panel.AddChild(_warning);
            _label = new GTextField { name = "XINGSHU_DefensePointHint", touchable = false,
                autoSize = AutoSizeType.Height, align = AlignType.Left, stroke = 1f,
                strokeColor = new Color(0f, 0f, 0f, 0.9f) };
            var format = _label.textFormat;
            format.size = Application.isMobilePlatform ? 26 : 22;
            format.lineSpacing = 5;
            format.color = new Color(0.95f, 1f, 0.82f);
            _label.textFormat = format;
            _panel.AddChild(_label);
            window.AddChild(_panel);
        }
        float width = Mathf.Min(440f, window.width * 0.25f);
        _warning.SetSize(width - 28f, _warning.height);
        _warning.SetXY(14f, 12f);
        if (_warning.text != advice) _warning.text = advice;
        _warning.visible = advice.Length > 0;
        float warningHeight = _warning.visible ? _warning.height + 8f : 0f;
        _label.SetSize(width - 28f, _label.height);
        _label.SetXY(14f, 12f + warningHeight);
        if (_label.text != text) _label.text = text;
        float height = _label.height + warningHeight + 24f;
        _panel.SetSize(width, height);
        _background.DrawRoundRect(width, height, new Color(0.055f, 0.065f, 0.09f, 0.86f), new float[] { 10f, 10f, 10f, 10f });
        // Left arena margin requested by the player; stay off the fighters,
        // native top banner, and the right-side action buttons on both platforms.
        _panel.SetXY(window.width * 0.135f,
            Mathf.Clamp(window.height * 0.37f, 160f, Mathf.Max(160f, window.height - height - 160f)));
    }
    private void RemoveLabel()
    {
        if (_panel != null && !_panel.isDisposed) _panel.Dispose();
        _panel = null;
        _background = null;
        _label = null;
        _warning = null;
    }
    private void OnDestroy() { RemoveLabel(); if (_instance == this) _instance = null; }
}
