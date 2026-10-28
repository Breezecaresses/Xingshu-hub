using System;
using System.Collections.Generic;
using System.Text;
using Core.Scene;
using Core.Unit;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using party.model;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities;

public sealed class MonsterKillProbability : MonoBehaviour
{
	private sealed class NameState
	{
		public string Original;

		public string Applied;

		public bool OriginalUbb;

		public GTextField WorldStatus;

		public GGraph WorldStatusBack;
	}

	private sealed class Prediction
	{
		public long Id;

		public string Name;

		public string Prefix;

		public string Detail;

		public float Probability;

		public int SkillId;

		public string SkillName;

		// A negative value means the current cooldown has not been synchronized yet.
		public int SkillCooldown = -1;
	}

	private const string ActionId = "xingshu.match.monster-kill";

	private const int PanelSortingOrder = 10000;

	private int _fightFontSize = XingShuConfigKeys.MatchKillFightSizeDefault;

	private float _fightX = XingShuConfigKeys.MatchKillFightXDefault;

	private float _fightY = XingShuConfigKeys.MatchKillFightYDefault;

	private bool _autoColor = XingShuConfigKeys.MatchKillAutoColorDefault;

	private bool _rainbow100 = XingShuConfigKeys.MatchKillRainbow100Default;

	private string _probColorHex = XingShuConfigKeys.MatchKillProbabilityColorDefault;

	private string _targetColorHex = XingShuConfigKeys.MatchKillTargetColorDefault;

	private Color _probColor = new Color(1f, 0.847f, 0.353f, 1f);

	private Color _targetColor = new Color(0.957f, 0.969f, 1f, 1f);

	private Prediction _currentFightPrediction;

	private GTextField _fightStyleSummary;

	private GButton _fontSmaller;

	private GButton _fontLarger;

	private GButton _fightReset;

	private GButton _fightLeft;

	private GButton _fightRight;

	private GButton _fightUp;

	private GButton _fightDown;

	private readonly Dictionary<GTextField, NameState> _names = new Dictionary<GTextField, NameState>();

	private readonly Dictionary<long, Prediction> _predictions = new Dictionary<long, Prediction>();

	private readonly Dictionary<long, Dictionary<int, CardInfoConfigure>> _cards = new Dictionary<long, Dictionary<int, CardInfoConfigure>>();

	private readonly List<long> _targetOrder = new List<long>();

	private readonly List<GTextField> _remove = new List<GTextField>();

	private readonly List<UIButton_MonsterItem> _items = new List<UIButton_MonsterItem>();

	private readonly Stack<GComponent> _walk = new Stack<GComponent>();

	private readonly HashSet<GTextField> _seen = new HashSet<GTextField>();

	private bool _enabled = true;

	private bool _previewCards = true;

	private float _nextTick;

	private float _nextScan;

	private float _nextError;

	private object _room;

	private long _battleId;

	private long _attackerId;

	private long _defenderId;

	private string _status = "等待进入对局";

	private string _lastError = "";

	private GTextField _fightLabel;

	private Window _panel;

	private GGraph _dragArea;

	private bool _panelPlaced;

	private bool _togglePanelRequested;

	private GComponent _pane;

	private GTextField _heading;

	private GTextField _detail;

	private GButton _toggle;

	private GButton _previewToggle;

	private GButton _previous;

	private GButton _next;

	private GButton _details;

	private GButton _close;

	private int _detailPage;

	private int _page;

	internal static bool UseModernView { get; set; } = true;

	public static MonsterKillProbability Instance { get; private set; }

	internal static bool IsEnabled
	{
		get => Instance != null ? Instance._enabled : XingShuConfigService.GetBool(XingShuConfigKeys.MatchKillEnabled, XingShuConfigKeys.MatchKillEnabledDefault);
		set
		{
			if (Instance != null)
			{
				Instance.SetEnabled(value);
			}
			else
			{
				XingShuConfigService.SetBool(XingShuConfigKeys.MatchKillEnabled, value);
				XingShuConfigService.Save();
			}
		}
	}

	internal static bool PreviewCards
	{
		get => Instance != null && Instance._previewCards;
		set
		{
			if (Instance != null)
			{
				Instance._previewCards = value;
				if (Instance.IsPanelOpen())
				{
					Instance.RefreshPanel();
				}
			}
		}
	}

	internal static int FightFontSize => Instance != null ? Instance._fightFontSize : XingShuConfigService.GetInt(XingShuConfigKeys.MatchKillFightSize, XingShuConfigKeys.MatchKillFightSizeDefault);
	internal static float FightX => Instance != null ? Instance._fightX : XingShuConfigService.GetFloat(XingShuConfigKeys.MatchKillFightX, XingShuConfigKeys.MatchKillFightXDefault, XingShuConfigKeys.MatchKillFightXMin, XingShuConfigKeys.MatchKillFightXMax);
	internal static float FightY => Instance != null ? Instance._fightY : XingShuConfigService.GetFloat(XingShuConfigKeys.MatchKillFightY, XingShuConfigKeys.MatchKillFightYDefault, XingShuConfigKeys.MatchKillFightYMin, XingShuConfigKeys.MatchKillFightYMax);

	public static bool TryParseHexColor(string input, out Color color, out string canonicalHex)
	{
		color = Color.white;
		canonicalHex = null;
		if (string.IsNullOrEmpty(input))
		{
			return false;
		}
		string text = input.Trim();
		if (text.StartsWith("#"))
		{
			text = text.Substring(1);
		}
		if (text.Length != 6)
		{
			return false;
		}
		for (int i = 0; i < 6; i++)
		{
			char c = text[i];
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
			{
				return false;
			}
		}
		try
		{
			byte r = byte.Parse(text.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
			byte g = byte.Parse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
			byte b = byte.Parse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
			color = new Color((float)(int)r / 255f, (float)(int)g / 255f, (float)(int)b / 255f, 1f);
			canonicalHex = "#" + text.ToUpperInvariant();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static Color ParseColorOrFallback(string hex, string fallbackHex)
	{
		if (TryParseHexColor(hex, out Color color, out _))
		{
			return color;
		}
		if (TryParseHexColor(fallbackHex, out Color color2, out _))
		{
			return color2;
		}
		return Color.white;
	}

	internal static bool AutoColorEnabled
	{
		get => (Instance != null) ? Instance._autoColor : XingShuConfigService.GetBool(XingShuConfigKeys.MatchKillAutoColor, XingShuConfigKeys.MatchKillAutoColorDefault);
		set
		{
			if (Instance != null)
			{
				Instance._autoColor = value;
				Instance.ApplyFightStyle();
				Instance._nextTick = 0f;
			}
			XingShuConfigService.SetBool(XingShuConfigKeys.MatchKillAutoColor, value);
			XingShuConfigService.Save();
		}
	}

	internal static bool Rainbow100Enabled
	{
		get => (Instance != null) ? Instance._rainbow100 : XingShuConfigService.GetBool(XingShuConfigKeys.MatchKillRainbow100, XingShuConfigKeys.MatchKillRainbow100Default);
		set
		{
			if (Instance != null)
			{
				Instance._rainbow100 = value;
				Instance.ApplyFightStyle();
				Instance._nextTick = 0f;
			}
			XingShuConfigService.SetBool(XingShuConfigKeys.MatchKillRainbow100, value);
			XingShuConfigService.Save();
		}
	}

	public static string GetAutoThresholdColorHex(float prob)
	{
		if (prob < 0.25f) return "#FF5A5A";
		if (prob < 0.50f) return "#FF9F43";
		if (prob < 0.75f) return "#FFD85A";
		if (prob < 0.999f) return "#67E480";
		return "#49F2C2";
	}

	public static string GetRainbowColorHex()
	{
		float hue = Mathf.Repeat(Time.unscaledTime / 3.0f, 1f);
		Color c = Color.HSVToRGB(hue, 0.75f, 1.0f);
		return $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";
	}

	public static string GetEffectiveProbabilityColorHex(float prob)
	{
		bool autoColor = AutoColorEnabled;
		if (!autoColor)
		{
			return ProbabilityColorHex;
		}

		bool is100 = prob >= 0.999f;
		if (is100 && Rainbow100Enabled)
		{
			return GetRainbowColorHex();
		}

		return GetAutoThresholdColorHex(prob);
	}

	public static string GetPreviewProbabilityColorHex(float prob)
	{
		bool autoColor = AutoColorEnabled;
		bool is100 = prob >= 0.999f;
		if (is100 && Rainbow100Enabled)
		{
			return GetRainbowColorHex();
		}
		if (!autoColor)
		{
			return ProbabilityColorHex;
		}
		return GetAutoThresholdColorHex(prob);
	}

	internal static string ProbabilityColorHex => (Instance != null) ? Instance._probColorHex : XingShuConfigService.GetString(XingShuConfigKeys.MatchKillProbabilityColor, XingShuConfigKeys.MatchKillProbabilityColorDefault);

	internal static string TargetColorHex => (Instance != null) ? Instance._targetColorHex : XingShuConfigService.GetString(XingShuConfigKeys.MatchKillTargetColor, XingShuConfigKeys.MatchKillTargetColorDefault);

	internal static Color ProbabilityColor => (Instance != null) ? Instance._probColor : ParseColorOrFallback(XingShuConfigService.GetString(XingShuConfigKeys.MatchKillProbabilityColor, XingShuConfigKeys.MatchKillProbabilityColorDefault), XingShuConfigKeys.MatchKillProbabilityColorDefault);

	internal static Color TargetColor => (Instance != null) ? Instance._targetColor : ParseColorOrFallback(XingShuConfigService.GetString(XingShuConfigKeys.MatchKillTargetColor, XingShuConfigKeys.MatchKillTargetColorDefault), XingShuConfigKeys.MatchKillTargetColorDefault);

	internal static bool SetProbabilityColor(string hex)
	{
		if (!TryParseHexColor(hex, out Color color, out string canonicalHex))
		{
			return false;
		}
		if (Instance != null)
		{
			Instance._probColorHex = canonicalHex;
			Instance._probColor = color;
			Instance.ApplyFightStyle();
			Instance._nextTick = 0f;
		}
		XingShuConfigService.SetString(XingShuConfigKeys.MatchKillProbabilityColor, canonicalHex);
		XingShuConfigService.Save();
		return true;
	}

	internal static bool SetTargetColor(string hex)
	{
		if (!TryParseHexColor(hex, out Color color, out string canonicalHex))
		{
			return false;
		}
		if (Instance != null)
		{
			Instance._targetColorHex = canonicalHex;
			Instance._targetColor = color;
			Instance.ApplyFightStyle();
			Instance._nextTick = 0f;
		}
		XingShuConfigService.SetString(XingShuConfigKeys.MatchKillTargetColor, canonicalHex);
		XingShuConfigService.Save();
		return true;
	}

	internal static string StatusText => Instance != null ? Instance._status : "等待初始化";
	internal static string LastErrorText => Instance != null ? Instance._lastError : "";

	internal static int TargetCount => Instance != null ? Instance._targetOrder.Count : 0;
	internal static int CurrentPage => Instance != null ? Instance._page : 0;
	internal static int CurrentDetailPage => Instance != null ? Instance._detailPage : 0;

	internal static void AdjustStyle(int fontDelta, float dx, float dy, bool reset = false)
	{
		if (Instance != null)
		{
			Instance.AdjustFightStyle(fontDelta, dx, dy, reset);
			if (Instance.IsPanelOpen())
			{
				Instance.RefreshPanel();
			}
		}
	}

	internal static void NextTarget()
	{
		if (Instance != null)
		{
			Instance._page++;
			Instance._detailPage = 0;
			if (Instance.IsPanelOpen())
			{
				Instance.RefreshPanel();
			}
		}
	}

	internal static void PreviousTarget()
	{
		if (Instance != null)
		{
			Instance._page--;
			Instance._detailPage = 0;
			if (Instance.IsPanelOpen())
			{
				Instance.RefreshPanel();
			}
		}
	}

	internal static void NextDetail()
	{
		if (Instance != null)
		{
			Instance._detailPage++;
			if (Instance.IsPanelOpen())
			{
				Instance.RefreshPanel();
			}
		}
	}

	internal static string GetCurrentTargetSummary()
	{
		if (Instance == null || Instance._targetOrder.Count == 0)
		{
			return "当前无交战或可见怪物目标";
		}
		int count = Instance._targetOrder.Count;
		int page = ((count != 0) ? ((Instance._page % count + count) % count) : 0);
		long id = Instance._targetOrder[page];
		if (Instance._predictions.TryGetValue(id, out var pred))
		{
			string skill = pred.SkillId > 0 ? pred.SkillName + " · " + SkillCooldownText(pred) + "\n" : "";
			return $"[{page + 1}/{count}] {pred.Prefix}{pred.Name}\n{skill}{pred.Detail}";
		}
		return $"[{page + 1}/{count}] 目标 ID: {id}";
	}

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<MonsterKillProbability>() == null)
		{
			host.AddComponent<MonsterKillProbability>();
		}
	}

	private void OnEnable()
	{
		Instance = this;
		MonsterCooldownRuntime.Install(gameObject);
		_enabled = XingShuConfigService.GetBool(XingShuConfigKeys.MatchKillEnabled, XingShuConfigKeys.MatchKillEnabledDefault);
		_fightFontSize = Mathf.Clamp(XingShuConfigService.GetInt(XingShuConfigKeys.MatchKillFightSize, XingShuConfigKeys.MatchKillFightSizeDefault), XingShuConfigKeys.MatchKillFightSizeMin, XingShuConfigKeys.MatchKillFightSizeMax);
		_fightX = XingShuConfigService.GetFloat(XingShuConfigKeys.MatchKillFightX, XingShuConfigKeys.MatchKillFightXDefault, XingShuConfigKeys.MatchKillFightXMin, XingShuConfigKeys.MatchKillFightXMax);
		_fightY = XingShuConfigService.GetFloat(XingShuConfigKeys.MatchKillFightY, XingShuConfigKeys.MatchKillFightYDefault, XingShuConfigKeys.MatchKillFightYMin, XingShuConfigKeys.MatchKillFightYMax);
		_autoColor = XingShuConfigService.GetBool(XingShuConfigKeys.MatchKillAutoColor, XingShuConfigKeys.MatchKillAutoColorDefault);
		_rainbow100 = XingShuConfigService.GetBool(XingShuConfigKeys.MatchKillRainbow100, XingShuConfigKeys.MatchKillRainbow100Default);
		string probColorStr = XingShuConfigService.GetString(XingShuConfigKeys.MatchKillProbabilityColor, XingShuConfigKeys.MatchKillProbabilityColorDefault);
		if (TryParseHexColor(probColorStr, out Color pc, out string pCanonical))
		{
			_probColorHex = pCanonical;
			_probColor = pc;
		}
		else
		{
			_probColorHex = XingShuConfigKeys.MatchKillProbabilityColorDefault;
			TryParseHexColor(_probColorHex, out _probColor, out _);
		}
		string targetColorStr = XingShuConfigService.GetString(XingShuConfigKeys.MatchKillTargetColor, XingShuConfigKeys.MatchKillTargetColorDefault);
		if (TryParseHexColor(targetColorStr, out Color tc, out string tCanonical))
		{
			_targetColorHex = tCanonical;
			_targetColor = tc;
		}
		else
		{
			_targetColorHex = XingShuConfigKeys.MatchKillTargetColorDefault;
			TryParseHexColor(_targetColorHex, out _targetColor, out _);
		}
		AssistBall.RegisterAction("xingshu.match.monster-kill", "对局增强 · 击飞概率", RequestTogglePanel, 145, "点击打开/关闭窗口；拖动标题栏移动，窗口内独立启停概率", "◇", IsFeatureEnabled);
	}

	private void OnDisable()
	{
		if (Instance == this)
		{
			Instance = null;
		}
		AssistBall.Unregister("xingshu.match.monster-kill");
		ClearSession();
		_togglePanelRequested = false;
		DisposePanel();
	}

	private void Update()
	{
		if (_rainbow100 && _fightLabel != null && !_fightLabel.isDisposed && _currentFightPrediction != null && _currentFightPrediction.Probability >= 0.999f)
		{
			ApplyFightStyle();
		}
		if (_togglePanelRequested)
		{
			_togglePanelRequested = false;
			try
			{
				TogglePanel();
			}
			catch (Exception ex)
			{
				PanelFailed(ex);
			}
		}
		if (Time.unscaledTime < _nextTick)
		{
			return;
		}
		_nextTick = Time.unscaledTime + 0.1f;
		try
		{
			Tick();
		}
		catch (Exception ex2)
		{
			RestoreNames();
			RemoveFightLabel();
			_predictions.Clear();
			_targetOrder.Clear();
			_status = "本帧数据不可用，已撤下旧概率";
			_lastError = ex2.GetType().Name + ": " + ex2.Message;
			if (Time.unscaledTime >= _nextError)
			{
				_nextError = Time.unscaledTime + 10f;
				Debug.LogWarning("[XingShu KillProbability] " + _lastError);
			}
		}
		if (!IsPanelOpen())
		{
			return;
		}
		try
		{
			RefreshPanel();
		}
		catch (Exception ex3)
		{
			PanelFailed(ex3);
		}
	}

	private void Tick()
	{
		GameLogicManager inst = SimpleSingletonProvider<GameLogicManager>.inst;
		RoomInfo roomInfo = ((inst.room == null) ? null : inst.room.curRoomInfo);
		if (_room != roomInfo)
		{
			ClearSession();
			_room = roomInfo;
		}
		if (!_enabled)
		{
			_status = "功能已停用，原始名称已恢复";
			return;
		}
		if (roomInfo == null || SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle || inst.battle == null || inst.battle.PlayerDatas == null)
		{
			ClearSession();
			_status = "等待进入对局";
			return;
		}
		long playerID = inst.account.GetPlayerID();
		BattlePlayerData battlePlayerData = FindPlayer(inst.battle.PlayerDatas, playerID);
		BattleFightData battleFightData = ((inst.fight == null) ? null : inst.fight.battleFightData);
		bool flag = battlePlayerData != null && battlePlayerData.Property != null && battlePlayerData.characterType == CharacterType.Hero && battlePlayerData.Property.HP.Value > 0 && (battleFightData?.isEnd ?? true);
		BattlePlayerData attacker;
		BattlePlayerData defender;
		bool flag2 = TryGetCombatants(battleFightData, inst.battle.PlayerDatas, out attacker, out defender) && SimpleSingletonProvider<UIManager>.inst.Fight.isShowing;
		long num = (flag2 ? battleFightData.battleId : 0);
		long num2 = (flag2 ? attacker.player.Id : 0);
		long num3 = (flag2 ? defender.player.Id : 0);
		if (_battleId != num || _attackerId != num2 || _defenderId != num3)
		{
			_battleId = num;
			_attackerId = num2;
			_defenderId = num3;
			_cards.Clear();
			_page = 0;
			_detailPage = 0;
		}
		_predictions.Clear();
		_targetOrder.Clear();
		_seen.Clear();
		if (flag2)
		{
			CacheCards(attacker);
			CacheCards(defender);
			Prediction prediction = Build(attacker, defender, battleFightData);
			_predictions[prediction.Id] = prediction;
			_targetOrder.Add(prediction.Id);
		}
		foreach (BattlePlayerData playerData in inst.battle.PlayerDatas)
		{
			if (playerData == null || playerData.player == null || playerData.characterType != CharacterType.Monster || playerData.Property == null || playerData.Property.HP.Value <= 0)
			{
				continue;
			}
			if (!_predictions.TryGetValue(playerData.player.Id, out var value))
			{
				if (flag)
				{
					value = Build(battlePlayerData, playerData, null);
					_targetOrder.Add(value.Id);
				}
				else
				{
					// Cooldowns remain visible while another combat is in progress, even
					// when a fresh pre-combat probability cannot be calculated.
					value = new Prediction { Id = playerData.player.Id, Name = CombatName(playerData), Prefix = "" };
					ReadSkillStatus(playerData, value);
				}
				_predictions[value.Id] = value;
			}
			if (playerData.CharacterInst != null && playerData.CharacterInst.window != null)
			{
				Decorate((playerData.CharacterInst.window.Com_AttrInfo == null) ? null : playerData.CharacterInst.window.Com_AttrInfo.txt_Name, value, world: true);
			}
		}
		if (Time.unscaledTime >= _nextScan)
		{
			_nextScan = Time.unscaledTime + 0.25f;
			_items.Clear();
			_walk.Clear();
			_walk.Push(GRoot.inst);
			int num4 = 6000;
			while (_walk.Count > 0 && num4-- > 0)
			{
				GComponent gComponent = _walk.Pop();
				if (gComponent.isDisposed || !gComponent.visible || gComponent == _pane)
				{
					continue;
				}
				if (gComponent is UIButton_MonsterItem item)
				{
					_items.Add(item);
				}
				for (int i = 0; i < gComponent.numChildren; i++)
				{
					if (gComponent.GetChildAt(i) is GComponent item2)
					{
						_walk.Push(item2);
					}
				}
			}
			_walk.Clear();
		}
		foreach (UIButton_MonsterItem item3 in _items)
		{
			if (!item3.isDisposed && item3.parent != null && item3.data != null && long.TryParse(item3.data.ToString(), out var result) && _predictions.TryGetValue(result, out var value2))
			{
				Decorate(item3.txt_Name, value2);
			}
		}
		if (flag2 && _predictions.TryGetValue(battleFightData.defenderInfo.PlayerId, out var value3))
		{
			UpdateFightLabel(value3);
		}
		else
		{
			RemoveFightLabel();
		}
		_remove.Clear();
		foreach (KeyValuePair<GTextField, NameState> name in _names)
		{
			if (name.Key.isDisposed || !_seen.Contains(name.Key))
			{
				_remove.Add(name.Key);
			}
		}
		foreach (GTextField item4 in _remove)
		{
			Restore(item4, _names[item4]);
			_names.Remove(item4);
		}
		_status = "目标 " + _targetOrder.Count + "  ·  " + (flag2 ? "当前战斗（全部攻防双方）" : (flag ? "本人战前估算" : "等待可见战斗数据")) + "  ·  0.1秒更新";
	}

	private static BattlePlayerData FindPlayer(List<BattlePlayerData> players, long id)
	{
		if (id == 0L)
		{
			return null;
		}
		foreach (BattlePlayerData player in players)
		{
			if (player != null && player.player != null && player.player.Id == id)
			{
				return player;
			}
		}
		return null;
	}

	private static bool TryGetCombatants(BattleFightData fight, List<BattlePlayerData> players, out BattlePlayerData attacker, out BattlePlayerData defender)
	{
		attacker = null;
		defender = null;
		if (fight == null || fight.isEnd || players == null || fight.attackerInfo == null || fight.defenderInfo == null || fight.attackerInfo.PlayerId == fight.defenderInfo.PlayerId)
		{
			return false;
		}
		BattlePlayerData battlePlayerData = FindPlayer(players, fight.attackerInfo.PlayerId);
		BattlePlayerData battlePlayerData2 = FindPlayer(players, fight.defenderInfo.PlayerId);
		if (battlePlayerData == null || battlePlayerData2 == null || battlePlayerData.Property == null || battlePlayerData2.Property == null || battlePlayerData2.Property.HP.Value <= 0)
		{
			return false;
		}
		if ((battlePlayerData.characterType != CharacterType.Hero && battlePlayerData.characterType != CharacterType.Monster) || (battlePlayerData2.characterType != CharacterType.Hero && battlePlayerData2.characterType != CharacterType.Monster))
		{
			return false;
		}
		attacker = battlePlayerData;
		defender = battlePlayerData2;
		return true;
	}

	private static string CombatName(BattlePlayerData player)
	{
		if (player.characterType == CharacterType.Hero)
		{
			string nick = player.player.GetNick();
			if (!string.IsNullOrEmpty(nick))
			{
				return nick;
			}
		}
		return CharacterHandle.GetCharacterName(player.player.characterConfig.Id, player.player.characterConfig.CharacterType);
	}

	private static string ProbabilityPrefix(bool playerDefender, KillProbabilityMath.Result probability)
	{
		if (probability == null)
		{
			return "";
		}
		return "[" + (playerDefender ? "被击飞" : "击飞") + " " + probability.Text + "] ";
	}

	private void CacheCards(BattlePlayerData player)
	{
		if (player == null || player.player == null || player.cardContainer == null || player.cardContainer._HandCardData == null)
		{
			return;
		}
		if (!_cards.TryGetValue(player.player.Id, out var value))
		{
			value = new Dictionary<int, CardInfoConfigure>();
			_cards[player.player.Id] = value;
		}
		foreach (KeyValuePair<int, HandCardData> handCardDatum in player.cardContainer._HandCardData)
		{
			if (handCardDatum.Value != null && handCardDatum.Value.Config != null)
			{
				value[handCardDatum.Key] = handCardDatum.Value.Config;
			}
		}
	}

	private KillProbabilityMath.Distribution RoleDistribution(BattleRole role, bool attack, out string note)
	{
		int num = (attack ? role.MinAtk : role.MinDef);
		int num2 = (attack ? role.MaxAtk : role.MaxDef);
		note = "服务器单值";
		if (num == num2)
		{
			return KillProbabilityMath.Fixed(num);
		}
		try
		{
			KillProbabilityMath.Distribution distribution = KillProbabilityMath.Fixed(attack ? role.InitAtk : role.InitDef);
			if (role.UseCards.Count == 0)
			{
				note = "非卡牌随机效果的权重未知，按服务器范围整数等概率估计";
				return null;
			}
			if (role.UseCards.Count > 64)
			{
				note = "卡牌数量异常，按服务器范围整数等概率估计";
				return null;
			}
			foreach (int useCard in role.UseCards)
			{
				if (!_cards.TryGetValue(role.PlayerId, out var value) || !value.TryGetValue(useCard, out var value2) || value2.EffectType != (EffectType)(attack ? 1 : 2) || value2.Params.Count < 2 || value2.Params[0] > value2.Params[1])
				{
					note = "存在未知卡牌或特殊效果，按服务器范围整数等概率估计";
					return null;
				}
				distribution = KillProbabilityMath.Add(distribution, KillProbabilityMath.Uniform(value2.Params[0], value2.Params[1]));
			}
			if (distribution.Min != num || distribution.Max != num2)
			{
				note = "逐卡范围与服务器不同（特殊增幅），按服务器范围整数等概率估计";
				return null;
			}
			note = "逐卡离散卷积（普通随机卡独立等概率假设，非整段均匀）";
			return distribution;
		}
		catch (ArgumentException)
		{
			note = "随机范围过大，按服务器范围整数等概率估计";
			return null;
		}
	}

	private Prediction Build(BattlePlayerData attacker, BattlePlayerData defender, BattleFightData fight)
	{
		int num = attacker.Property.ATK.Value;
		int num2 = num;
		int num3 = defender.Property.DEF.Value;
		int maxDef = num3;
		int value = defender.Property.HP.Value;
		int num4 = 0;
		bool estimated = false;
		KillProbabilityMath.Distribution distribution = KillProbabilityMath.Fixed(num);
		KillProbabilityMath.Distribution distribution2 = KillProbabilityMath.Fixed(num3);
		string text = "战前估算：Property.ATK / DEF（已生效属性，不重复叠加buff/芯片）";
		string note = "当前生效攻击";
		string note2 = "当前生效防御";
		int damageBonus = SyncedPveMarkDamage(attacker, defender);
		string text2 = ((fight == null) ? "未启用PVE筹码入战补算" : "战中不再触发筹码、不再增加入战攻击；标记只读当前同步层数");
		long num5 = ((fight == null) ? PveTurnContext.CurrentPlayerId() : 0);
		bool beforeOwnTurn = fight == null && PveEntryMath.ShouldForecastTurn(attacker.player.Id, num5);
		if (TryProjectPveEntry(attacker, defender, fight, out var entry, beforeOwnTurn))
		{
			distribution = entry.Attack;
			num = distribution.Min;
			num2 = distribution.Max;
			damageBonus = entry.DamageBonus;
			estimated = entry.IsEstimated;
			text += (entry.TurnForecast ? "；本人下次回合开始后预测（假设期间其他状态不变）" : "；当前状态PVE入战预测");
			if (num5 == 0L)
			{
				text += "；回合信息未知，不提前回血";
			}
			note = "当前ATK + 新满血固定加攻 + 美工刀治愈加攻" + (entry.EagleScope ? " + 鹰眼标记加攻" : "") + (entry.HammerActive ? " + 星币锤加攻" : "");
			text2 = "满血=" + entry.FullHealth + "；治愈=" + entry.Cure + "；美工刀=" + entry.Knives + "把，治愈ATK+" + entry.KnifeBonus + "；新满血固定ATK+" + entry.FixedKnifeBonus + "（已生效的固定+2/+4不重复）；普通瞄具=" + entry.OrdinaryScope + "（固定+2已在面板）；鹰眼=" + entry.EagleScope + "；目标标记=" + entry.MarkBefore + "→" + entry.MarkAfter + "（仅预测，不修改）；攻击方HP=" + entry.HpBefore + "→" + entry.HpAfter + (entry.TurnForecast ? "（下次本人回合治愈回血，不再次减半）" : "（当前状态，不重复回血）");
			if (entry.Hammer)
			{
				text2 = text2 + "；星币锤：星币=" + entry.GoldBefore + "，严格>20=" + entry.HammerActive + "，ATK+" + entry.HammerBonus + "，攻击后星币预测=" + entry.GoldAfter + "（先按扣费前余额计算，后扣6；不把5星光当成5星币，不预支轮次奖励）";
			}
			if (entry.HammerRoundingEstimated)
			{
				note += "；星币锤30%非整数，服务端取整未确认，按向下取整假设";
			}
			if (entry.OrderEstimated)
			{
				note += "；双瞄具触发顺序未知，两种先后各50%假设，非服务器权重（中间攻击值权重为0）";
			}
			else if (entry.EagleScope)
			{
				note += "（先施加1层，再按标记层数×2）";
			}
		}
		string text3 = "估";
		if (fight != null)
		{
			BattleRole attackerInfo = fight.attackerInfo;
			BattleRole defenderInfo = fight.defenderInfo;
			num = attackerInfo.MinAtk;
			num2 = attackerInfo.MaxAtk;
			num3 = defenderInfo.MinDef;
			maxDef = defenderInfo.MaxDef;
			distribution = RoleDistribution(attackerInfo, attack: true, out note);
			distribution2 = RoleDistribution(defenderInfo, attack: false, out note2);
			estimated = distribution == null || distribution2 == null;
			num4 = ((attackerInfo.Point >= 1 && attackerInfo.Point <= 6) ? attackerInfo.Point : 0);
			BattleSceneController inst = BattleSceneController.inst;
			UIFight_PlayerInfo uIFight_PlayerInfo = ((inst == null || inst.directorManager == null || inst.directorManager.attacker == null) ? null : inst.directorManager.attacker._UI);
			if (SimpleSingletonProvider<UIManager>.inst.Fight.contentPane is UIFightWindow uIFightWindow && (uIFightWindow.step.selectedIndex == 4 || uIFightWindow.step.selectedIndex == 5) && num4 == 0 && uIFight_PlayerInfo != null && !uIFight_PlayerInfo.isDisposed && uIFight_PlayerInfo.com_Attack.showPoint.selectedIndex > 0 && int.TryParse(uIFight_PlayerInfo.com_Attack.com_Point.txt_Point.text, out var result) && result >= 1 && result <= 6)
			{
				num4 = result;
			}
			text = "战斗：BattleRole.MinAtk/MaxAtk、MinDef/MaxDef（与战斗属性界面同源）";
			text3 = "";
			if (_previewCards && num4 == 0 && attackerInfo.PlayerId == SimpleSingletonProvider<GameLogicManager>.inst.account.GetPlayerID())
			{
				UIHandCard_Button_Card uIHandCard_Button_Card = GObject.draggingObject as UIHandCard_Button_Card;
				UIFightWindow uIFightWindow2 = SimpleSingletonProvider<UIManager>.inst.Fight.contentPane as UIFightWindow;
				bool flag = uIHandCard_Button_Card != null && uIHandCard_Button_Card.CardData != null && attacker.cardContainer != null && attacker.cardContainer._HandCards.Contains(uIHandCard_Button_Card.CardData);
				if (uIFightWindow2 != null && uIFightWindow2.step.selectedIndex == 3 && flag && uIHandCard_Button_Card.touchable && !attackerInfo.UseCards.Contains(uIHandCard_Button_Card.CardData.Guid) && uIHandCard_Button_Card._config.EffectType == EffectType.Attack && SimpleSingletonProvider<GameLogicManager>.inst.card.cardActions.TryGetValue(uIHandCard_Button_Card._config.Id, out var value2) && value2.GetCostValue(attacker.player.Id, uIHandCard_Button_Card.CardData) <= attackerInfo.Cost && uIHandCard_Button_Card._config.Params.Count >= 2 && uIHandCard_Button_Card._config.Params[0] <= uIHandCard_Button_Card._config.Params[1])
				{
					checked
					{
						try
						{
							KillProbabilityMath.Distribution distribution3 = KillProbabilityMath.Uniform(uIHandCard_Button_Card._config.Params[0], uIHandCard_Button_Card._config.Params[1]);
							KillProbabilityMath.Distribution distribution4 = KillProbabilityMath.Add(distribution ?? KillProbabilityMath.Uniform(num, num2), distribution3);
							int num6 = num + distribution3.Min;
							int num7 = num2 + distribution3.Max;
							distribution = distribution4;
							num = num6;
							num2 = num7;
							text3 = "预览估";
							text = text + "；拖动待出卡 #" + uIHandCard_Button_Card._config.Id + "（未发送请求）";
							note += "；待出卡特殊增幅待服务器确认";
						}
						catch (ArgumentException)
						{
							note += "；待出卡预览范围不支持";
						}
					}
				}
			}
		}
		string text4 = CombatName(defender);
		KillProbabilityMath.Result result2 = KillProbabilityMath.Evaluate(num, num2, num3, maxDef, value, distribution, distribution2, num4, estimated, damageBonus);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("攻击方：" + CombatName(attacker) + " [" + attacker.player.Id + "] → 防御方：" + text4 + " [" + defender.player.Id + "]");
		stringBuilder.AppendLine("状态：" + ((text3 == "") ? "战斗数据" : ((text3 == "估") ? "战前估算" : "待出卡预览估算")));
		stringBuilder.AppendLine(text);
		stringBuilder.AppendLine("攻击 " + num + "–" + num2 + " / 防御方DEF " + num3 + "–" + maxDef + " / HP " + value);
		stringBuilder.AppendLine("攻击骰 " + ((num4 == 0) ? "1–6 未揭示" : num4.ToString()) + " / 防御骰 1–6 / " + ((num4 == 0) ? "36种骰组合" : "6种防御骰结果") + " / 防御击飞 " + result2.Text);
		stringBuilder.AppendLine("概率口径：" + (result2.IsEstimated ? "期望估计（具体假设见下；未知卡牌权重按范围内整数等概率，不代表真实隐藏权重）" : "已知分布加权期望（普通独立1–6骰假设）"));
		stringBuilder.AppendLine("攻击分布：" + note);
		stringBuilder.AppendLine("防御分布：" + note2);
		stringBuilder.AppendLine("筹码补算：" + text2);
		stringBuilder.AppendLine("标记伤害修正：+" + damageBonus + "（本次普通防御结算，最低1点伤害之后；不是每层+1）");
		stringBuilder.AppendLine("攻击方基础Attack=" + attacker.player.Hero.Attack + "；当前有效ATK=" + attacker.Property.ATK.Value);
		stringBuilder.Append("属性来源 ");
		foreach (KeyValuePair<long, AdditionAttribute> additionAttr in attacker.player.Hero.AdditionAttrs)
		{
			stringBuilder.Append(additionAttr.Key + ":ATK" + additionAttr.Value.Attack + " ");
		}
		stringBuilder.AppendLine();
		stringBuilder.Append("buff(ID:Progress) ");
		int num8 = 0;
		if (attacker.buffContainer != null && attacker.buffContainer._buffDict != null)
		{
			foreach (KeyValuePair<long, Buff> item in attacker.buffContainer._buffDict)
			{
				if (num8++ >= 12)
				{
					stringBuilder.Append("…");
					break;
				}
				stringBuilder.Append(item.Value.BuffId + ":" + item.Value.Progress + " ");
			}
		}
		stringBuilder.AppendLine();
		stringBuilder.Append("芯片ID ");
		List<int> relicIds = attacker.GetRelicIds();
		if (relicIds != null)
		{
			foreach (int item2 in relicIds)
			{
				stringBuilder.Append(item2 + " ");
			}
		}
		stringBuilder.AppendLine();
		stringBuilder.Append("普通独立公平D6。支持上述PVE筹码与标记修正；未计护盾/免伤/复活及其他追加伤害、连击。回合预测只模拟一次治愈回血，其他属性/星币/目标标记假设不变，不预支奖励或再次减半；不是最终胜率保证。");
		Prediction prediction = new Prediction
		{
			Id = defender.player.Id,
			Name = text4,
			Prefix = ProbabilityPrefix(defender.characterType == CharacterType.Hero, result2),
			Detail = stringBuilder.ToString(),
			Probability = (float)result2.Expected
		};
		ReadSkillStatus(defender, prediction);
		return prediction;
	}

	private static void ReadSkillStatus(BattlePlayerData target, Prediction prediction)
	{
		if (target?.player == null || target.characterType != CharacterType.Monster) return;
		int id = target.player.GetBattleActiveSkillId();
		if (id <= 0) return; // Passive-only monsters do not have an active cooldown.
		prediction.SkillId = id;
		prediction.SkillName = "主动技能";
		if (StaticConfigure.Skill.InfoDict.TryGetValue(id, out var config))
		{
			string name = config.NameID.GetLocal(UIStringType.Skill);
			if (!string.IsNullOrEmpty(name)) prediction.SkillName = name;
		}
		// RoundStart applies a shared skill map to every actor and defaults a
		// missing monster skill to zero. That UI cache is not authoritative.
		prediction.SkillCooldown = MonsterCooldownRuntime.Read(target.player.Id, id);
	}

	private static string SkillCooldownText(Prediction p)
	{
		return p.SkillCooldown < 0 ? "冷却待同步" : p.SkillCooldown == 0 ? "冷却就绪" : $"冷却 {p.SkillCooldown} 回合";
	}

	private string StatusMarkup(Prediction p, bool includeCooldown = true)
	{
		string probability = string.IsNullOrEmpty(p.Prefix) ? "" :
			$"[color={GetEffectiveProbabilityColorHex(p.Probability)}]{p.Prefix.Trim().Trim('[', ']')}[/color]";
		if (!includeCooldown || p.SkillId <= 0) return probability;
		string color = p.SkillCooldown < 0 ? "#BEC7D5" : p.SkillCooldown == 0 ? "#67E480" : "#73DAFF";
		return probability + (probability.Length > 0 ? "  [color=#D1D5DF]·[/color]  " : "") +
			$"[color={color}]{SkillCooldownText(p)}[/color]";
	}

	private static bool IsPveAttack(BattlePlayerData attacker, BattlePlayerData defender)
	{
		if (attacker != null && defender != null && attacker.Property != null && defender.Property != null && attacker.characterType == CharacterType.Hero)
		{
			return defender.characterType == CharacterType.Monster;
		}
		return false;
	}

	private static int SyncedPveMarkDamage(BattlePlayerData attacker, BattlePlayerData defender)
	{
		if (!IsPveAttack(attacker, defender) || defender.Property.MarkCount.property.Value <= 0)
		{
			return 0;
		}
		return 1;
	}

	private static bool TryProjectPveEntry(BattlePlayerData attacker, BattlePlayerData defender, BattleFightData fight, out PveEntryMath.Projection entry, bool beforeOwnTurn = false)
	{
		entry = null;
		if (fight != null || !IsPveAttack(attacker, defender))
		{
			return false;
		}
		entry = PveEntryMath.Project(attacker.Property.ATK.Value, attacker.Property.HealthState == HealthState.Full, attacker.Property.CureCount.property.Value, defender.Property.MarkCount.property.Value, attacker.GetRelicIds(), attacker.Property.gold.Value, beforeOwnTurn, attacker.Property.HP.Value, attacker.Property.maxHP);
		return true;
	}

	private void Decorate(GTextField label, Prediction p, bool world = false)
	{
		if (label == null || label.isDisposed)
		{
			return;
		}
		if (string.IsNullOrEmpty(p.Prefix) && p.SkillId <= 0)
		{
			if (_names.TryGetValue(label, out var value))
			{
				Restore(label, value);
				_names.Remove(label);
			}
			return;
		}
		_seen.Add(label);
		if (!_names.TryGetValue(label, out var value2))
		{
			value2 = new NameState
			{
				Original = label.text,
				OriginalUbb = label.UBBEnabled
			};
			_names[label] = value2;
		}
		else if (!world && label.text != value2.Applied)
		{
			value2.Original = label.text;
		}
		if (world)
		{
			// Keep the native name, number, HP and guide line untouched. The extra
			// row inherits the existing annotation's camera scale automatically.
			if (label.parent == null) return;
			GTextField status = value2.WorldStatus;
			if (status == null || status.isDisposed || status.parent != label.parent)
			{
				if (status != null && !status.isDisposed) status.Dispose();
				if (value2.WorldStatusBack != null && !value2.WorldStatusBack.isDisposed) value2.WorldStatusBack.Dispose();
				value2.WorldStatusBack = new GGraph { name = "XINGSHU_MonsterStatusBack", touchable = false };
				label.parent.AddChild(value2.WorldStatusBack);
				status = Label(label.parent, 20, Color.white);
				status.name = "XINGSHU_MonsterStatus";
				status.autoSize = AutoSizeType.Both;
				status.singleLine = true;
				status.UBBEnabled = true;
				// Native name fonts can be TMP/SDF fonts: their outline is normalized,
				// unlike a DynamicFont's pixel outline. A 1.5 outline covers the face.
				// Use an unoutlined face on a separate dark plate for both font types.
				status.stroke = 0f;
				value2.WorldStatus = status;
			}
			TextFormat format = status.textFormat;
			int size = Mathf.Clamp(Mathf.RoundToInt(label.textFormat.size * 0.95f), 22, 32);
			if (format.size != size || format.font != label.textFormat.font)
			{
				format.size = size;
				format.font = label.textFormat.font;
				status.textFormat = format;
			}
			status.text = StatusMarkup(p);
			GGraph back = value2.WorldStatusBack;
			float width = status.width + 12f;
			float height = status.height + 6f;
			if (!Mathf.Approximately(back.width, width) || !Mathf.Approximately(back.height, height))
				back.DrawRoundRect(width, height, new Color(0.055f, 0.065f, 0.09f, 0.84f), new float[] { 6f, 6f, 6f, 6f });
			back.SetXY(label.x - 2f, label.y - height - 4f);
			status.SetXY(back.x + 6f, back.y + 3f);
			status.visible = label.visible;
			status.alpha = label.alpha;
			back.visible = label.visible;
			back.alpha = label.alpha;
			return;
		}
		value2.Applied = StatusMarkup(p, false) + "  " + value2.Original;
		label.UBBEnabled = true;
		if (label.text != value2.Applied)
		{
			label.text = value2.Applied;
		}
	}

	private static void Restore(GTextField label, NameState state)
	{
		if (state.WorldStatus != null)
		{
			if (!state.WorldStatus.isDisposed) state.WorldStatus.Dispose();
			if (state.WorldStatusBack != null && !state.WorldStatusBack.isDisposed) state.WorldStatusBack.Dispose();
			return;
		}
		if (!label.isDisposed && label.text == state.Applied)
		{
			label.text = state.Original;
		}
		if (!label.isDisposed) label.UBBEnabled = state.OriginalUbb;
	}

	private void RestoreNames()
	{
		foreach (KeyValuePair<GTextField, NameState> name in _names)
		{
			Restore(name.Key, name.Value);
		}
		_names.Clear();
	}

	private void UpdateFightLabel(Prediction p)
	{
		_currentFightPrediction = p;
		if (string.IsNullOrEmpty(p.Prefix))
		{
			RemoveFightLabel();
			return;
		}
		BattleSceneController inst = BattleSceneController.inst;
		UIFight_PlayerInfo uIFight_PlayerInfo = ((inst == null || inst.directorManager == null || inst.directorManager.victim == null) ? null : inst.directorManager.victim._UI);
		if (uIFight_PlayerInfo == null || uIFight_PlayerInfo.isDisposed)
		{
			RemoveFightLabel();
			return;
		}
		if (_fightLabel == null || _fightLabel.isDisposed || _fightLabel.parent != uIFight_PlayerInfo)
		{
			RemoveFightLabel();
			_fightLabel = Label(uIFight_PlayerInfo, _fightFontSize, Color.white);
			_fightLabel.name = "XINGSHU_MonsterKill_Name";
			_fightLabel.singleLine = true;
			_fightLabel.align = AlignType.Center;
			_fightLabel.UBBEnabled = true;
			_fightLabel.stroke = 1.5f;
			_fightLabel.strokeColor = new Color(0f, 0f, 0f, 0.9f);
		}
		ApplyFightStyle();
	}

	private void ApplyFightStyle()
	{
		if (_fightLabel != null && !_fightLabel.isDisposed)
		{
			TextFormat textFormat = _fightLabel.textFormat;
			if (textFormat.size != _fightFontSize)
			{
				textFormat.size = _fightFontSize;
				_fightLabel.textFormat = textFormat;
			}
			float num = (float)_fightFontSize / 18f;
			_fightLabel.SetSize(520f * num, 44f * num);
			_fightLabel.SetXY(_fightX - (520f * num - 520f) / 2f, _fightY);
			if (_currentFightPrediction != null)
			{
				int fontSize = _fightFontSize;
				string targetColorHex = _targetColorHex;
				_fightLabel.text = $"[size={fontSize}]{StatusMarkup(_currentFightPrediction, false)}  [b][color={targetColorHex}]{_currentFightPrediction.Name}[/color][/b][/size]";
			}
		}
	}

	private void AdjustFightStyle(int fontDelta, float dx, float dy, bool reset = false)
	{
		_fightFontSize = (reset ? XingShuConfigKeys.MatchKillFightSizeDefault : Mathf.Clamp(_fightFontSize + fontDelta, XingShuConfigKeys.MatchKillFightSizeMin, XingShuConfigKeys.MatchKillFightSizeMax));
		_fightX = (reset ? XingShuConfigKeys.MatchKillFightXDefault : Mathf.Clamp(_fightX + dx, XingShuConfigKeys.MatchKillFightXMin, XingShuConfigKeys.MatchKillFightXMax));
		_fightY = (reset ? XingShuConfigKeys.MatchKillFightYDefault : Mathf.Clamp(_fightY + dy, XingShuConfigKeys.MatchKillFightYMin, XingShuConfigKeys.MatchKillFightYMax));
		if (reset)
		{
			_autoColor = XingShuConfigKeys.MatchKillAutoColorDefault;
			_rainbow100 = XingShuConfigKeys.MatchKillRainbow100Default;
			_probColorHex = XingShuConfigKeys.MatchKillProbabilityColorDefault;
			TryParseHexColor(_probColorHex, out _probColor, out _);
			_targetColorHex = XingShuConfigKeys.MatchKillTargetColorDefault;
			TryParseHexColor(_targetColorHex, out _targetColor, out _);
			XingShuConfigService.SetBool(XingShuConfigKeys.MatchKillAutoColor, _autoColor);
			XingShuConfigService.SetBool(XingShuConfigKeys.MatchKillRainbow100, _rainbow100);
			XingShuConfigService.SetString(XingShuConfigKeys.MatchKillProbabilityColor, _probColorHex);
			XingShuConfigService.SetString(XingShuConfigKeys.MatchKillTargetColor, _targetColorHex);
		}
		XingShuConfigService.SetInt(XingShuConfigKeys.MatchKillFightSize, _fightFontSize);
		XingShuConfigService.SetFloat(XingShuConfigKeys.MatchKillFightX, _fightX);
		XingShuConfigService.SetFloat(XingShuConfigKeys.MatchKillFightY, _fightY);
		XingShuConfigService.Save();
		ApplyFightStyle();
		_nextTick = 0f;
	}

	private void RemoveFightLabel()
	{
		if (_fightLabel != null && !_fightLabel.isDisposed)
		{
			_fightLabel.Dispose();
		}
		_fightLabel = null;
		_currentFightPrediction = null;
	}

	private void ClearSession()
	{
		RestoreNames();
		RemoveFightLabel();
		_predictions.Clear();
		_targetOrder.Clear();
		_cards.Clear();
		_items.Clear();
		_seen.Clear();
		_walk.Clear();
		_remove.Clear();
		_battleId = 0L;
		_attackerId = 0L;
		_defenderId = 0L;
		_nextScan = 0f;
	}

	private void SetEnabled(bool value)
	{
		if (_enabled == value)
		{
			return;
		}
		_enabled = value;
		XingShuConfigService.SetBool(XingShuConfigKeys.MatchKillEnabled, value);
		XingShuConfigService.Save();
		if (!value)
		{
			ClearSession();
		}
		_nextTick = 0f;
	}

	private static GTextField Label(GComponent parent, int size, Color color)
	{
		GTextField gTextField = new GTextField();
		gTextField.touchable = false;
		TextFormat textFormat = gTextField.textFormat;
		textFormat.size = size;
		textFormat.color = color;
		gTextField.textFormat = textFormat;
		gTextField.autoSize = AutoSizeType.None;
		parent.AddChild(gTextField);
		return gTextField;
	}

	private static GButton Button(GComponent parent, string title, EventCallback0 action)
	{
		GButton gButton = new GButton();
		gButton.opaque = true;
		gButton.touchable = true;
		gButton.SetSize(120f, 40f);
		parent.AddChild(gButton);
		GGraph gGraph = new GGraph();
		gGraph.SetSize(120f, 40f);
		gGraph.DrawRect(gGraph.width, gGraph.height, 1, new Color(0.35f, 0.42f, 0.5f), new Color(0.12f, 0.16f, 0.22f));
		gGraph.touchable = false;
		gButton.AddChild(gGraph);
		gGraph.AddRelation(gButton, FairyGUI.RelationType.Size);
		gButton.rootContainer.hitArea = new ShapeHitTest(gGraph.displayObject);
		GTextField label = Label(gButton, 16, Color.white);
		label.name = "caption";
		label.SetSize(120f, 40f);
		label.align = AlignType.Center;
		label.verticalAlign = VertAlignType.Middle;
		label.text = title;
		label.AddRelation(gButton, FairyGUI.RelationType.Size);
		gButton.onClick.Add((EventCallback0)delegate
		{
			Debug.Log("[XingShu KillProbability UI v2] click: " + label.text);
			action();
		});
		return gButton;
	}

	private static void Caption(GButton button, string text)
	{
		((GTextField)button.GetChild("caption")).text = text;
	}

	private bool IsFeatureEnabled()
	{
		return _enabled;
	}

	private bool IsPanelOpen()
	{
		if (_panel != null && !_panel.isDisposed)
		{
			return _panel.isShowing;
		}
		return false;
	}

	private void RequestTogglePanel()
	{
		if (UseModernView)
		{
			try
			{
				AssistBallGUI assistBall = UnityEngine.Object.FindObjectOfType<AssistBallGUI>();
				if (assistBall != null)
				{
					assistBall.SetMenuOpen(false);
				}
			}
			catch { }
			XingShuRuntime.UI.XingShuUICenter.OpenKnockbackPage();
			return;
		}
		_togglePanelRequested = true;
	}

	private void TogglePanel()
	{
		if (IsPanelOpen())
		{
			ClosePanel();
		}
		else
		{
			OpenPanel();
		}
	}

	private void ClosePanel()
	{
		if (_panel != null && !_panel.isDisposed)
		{
			_panel.StopDrag();
			_panel.HideImmediately();
		}
		Debug.Log("[XingShu KillProbability UI] panel closed");
	}

	private void DisposePanel()
	{
		if (_panel != null && !_panel.isDisposed)
		{
			_panel.StopDrag();
			_panel.Dispose();
		}
		_panel = null;
		_pane = null;
		_dragArea = null;
		_panelPlaced = false;
	}

	private void PanelFailed(Exception ex)
	{
		_lastError = "窗口异常：" + ex.GetType().Name + ": " + ex.Message;
		Debug.LogWarning("[XingShu KillProbability UI] " + ex);
		DisposePanel();
	}

	private void OpenPanel()
	{
		if (_panel == null || _panel.isDisposed)
		{
			_panel = new Window();
			_panel.modal = false;
			_panel.opaque = true;
			XingShuOverlayInput.Protect(_panel);
			_panel.name = "XINGSHU_KillProbability_Settings";
			_panel.sortingOrder = 10000;
			_panel.touchable = true;
			_panel.onTouchBegin.Add((EventCallback0)delegate
			{
				Debug.Log("[XingShu KillProbability UI v2] panel pointer down");
			});
			_pane = new GComponent();
			_pane.opaque = true;
			_pane.SetSize(620f, 560f);
			GGraph gGraph = new GGraph();
			gGraph.SetSize(620f, 560f);
			gGraph.DrawRect(gGraph.width, gGraph.height, 1, new Color(0.3f, 0.38f, 0.45f), new Color(0.065f, 0.085f, 0.12f, 0.98f));
			gGraph.touchable = false;
			_pane.AddChild(gGraph);
			gGraph.AddRelation(_pane, FairyGUI.RelationType.Size);
			_dragArea = new GGraph();
			_dragArea.name = "XINGSHU_KillPanel_DragArea";
			_dragArea.SetSize(620f, 44f);
			_dragArea.touchable = true;
			_pane.AddChild(_dragArea);
			_heading = Label(_pane, 20, Color.white);
			_detail = Label(_pane, 15, new Color(0.78f, 0.85f, 0.9f));
			_detail.singleLine = false;
			_toggle = Button(_pane, "", delegate
			{
				SetEnabled(!_enabled);
				RefreshPanel();
			});
			_previewToggle = Button(_pane, "", delegate
			{
				_previewCards = !_previewCards;
				RefreshPanel();
			});
			_previous = Button(_pane, "上个目标", delegate
			{
				_page--;
				_detailPage = 0;
				RefreshPanel();
			});
			_next = Button(_pane, "下个目标", delegate
			{
				_page++;
				_detailPage = 0;
				RefreshPanel();
			});
			_details = Button(_pane, "详情翻页", delegate
			{
				_detailPage++;
				RefreshPanel();
			});
			_close = Button(_pane, "关闭", ClosePanel);
			_fightStyleSummary = Label(_pane, 16, new Color(1f, 0.91f, 0.55f));
			_fontSmaller = Button(_pane, "字号 −", delegate
			{
				AdjustFightStyle(-2, 0f, 0f);
			});
			_fontLarger = Button(_pane, "字号 +", delegate
			{
				AdjustFightStyle(2, 0f, 0f);
			});
			_fightReset = Button(_pane, "恢复默认", delegate
			{
				AdjustFightStyle(0, 0f, 0f, reset: true);
			});
			_fightLeft = Button(_pane, "左移", delegate
			{
				AdjustFightStyle(0, -10f, 0f);
			});
			_fightRight = Button(_pane, "右移", delegate
			{
				AdjustFightStyle(0, 10f, 0f);
			});
			_fightUp = Button(_pane, "上移", delegate
			{
				AdjustFightStyle(0, 0f, -10f);
			});
			_fightDown = Button(_pane, "下移", delegate
			{
				AdjustFightStyle(0, 0f, 10f);
			});
			_panel.contentPane = _pane;
			_panel.dragArea = _dragArea;
			_panel.onDragMove.Add((EventCallback0)delegate
			{
				LayoutPanel(center: false);
			});
			_panel.onDragEnd.Add((EventCallback0)delegate
			{
				LayoutPanel(center: false);
			});
		}
		_panel.Show();
		LayoutPanel(!_panelPlaced);
		RefreshPanel();
		Debug.Log("[XingShu KillProbability UI v2] panel opened; order=" + _panel.sortingOrder + "; rootTouchable=" + GRoot.inst.touchable + "; panelTouchable=" + _panel.touchable);
	}

	private void LayoutPanel(bool center)
	{
		if (_panel != null && !_panel.isDisposed)
		{
			Rect safeScreenRect = XingShuMobileUIHelper.SafeScreenRect;
			float num = GRoot.inst.width / (float)Mathf.Max(1, Screen.width);
			float num2 = GRoot.inst.height / (float)Mathf.Max(1, Screen.height);
			Rect bounds = new Rect(safeScreenRect.x * num + 6f, ((float)Screen.height - safeScreenRect.yMax) * num2 + 6f, Mathf.Max(1f, safeScreenRect.width * num - 12f), Mathf.Max(1f, safeScreenRect.height * num2 - 12f));
			float num3 = Mathf.Min(620f, bounds.width);
			float num4 = Mathf.Min(560f, bounds.height);
			if (_panel.width != num3 || _panel.height != num4)
			{
				_panel.SetSize(num3, num4);
			}
			Rect value = new Rect(_panel.x, _panel.y, num3, num4);
			if (center)
			{
				value.x = bounds.x + (bounds.width - num3) / 2f;
				value.y = bounds.y + (bounds.height - num4) / 2f;
			}
			Rect rect = XingShuMobileUIHelper.FitRect(value, bounds);
			if (_panel.x != rect.x || _panel.y != rect.y)
			{
				_panel.SetXY(rect.x, rect.y);
			}
			_dragArea.SetSize(num3, 44f);
			_panelPlaced = true;
		}
	}

	private void RefreshPanel()
	{
		LayoutPanel(center: false);
		float width = _panel.width;
		float height = _panel.height;
		_heading.SetXY(18f, 14f);
		_heading.SetSize(width - 36f, 28f);
		_heading.text = "对局增强 · 击飞概率 [UI v2]（拖动标题移动）";
		float num = (width - 44f) / 2f;
		_toggle.SetXY(16f, 52f);
		_toggle.SetSize(num, 40f);
		_previewToggle.SetXY(28f + num, 52f);
		_previewToggle.SetSize(num, 40f);
		Caption(_toggle, _enabled ? "已启用 · 点击停用" : "已停用 · 点击启用");
		Caption(_previewToggle, _previewCards ? "拖动卡预览：开" : "拖动卡预览：关");
		_fightStyleSummary.SetXY(18f, 102f);
		_fightStyleSummary.SetSize(width - 36f, 24f);
		_fightStyleSummary.text = "战斗文字  字号:" + _fightFontSize + "  X:" + _fightX + "  Y:" + _fightY + "  模式:" + (_autoColor ? "自动分段" : "手动") + (_rainbow100 ? "+炫彩" : "") + "  目标:" + _targetColorHex;
		float num2 = (width - 56f) / 3f;
		_fontSmaller.SetXY(16f, 130f);
		_fontSmaller.SetSize(num2, 40f);
		_fontLarger.SetXY(28f + num2, 130f);
		_fontLarger.SetSize(num2, 40f);
		_fightReset.SetXY(40f + num2 * 2f, 130f);
		_fightReset.SetSize(num2, 40f);
		float num3 = (width - 68f) / 4f;
		_fightLeft.SetXY(16f, 176f);
		_fightLeft.SetSize(num3, 40f);
		_fightRight.SetXY(28f + num3, 176f);
		_fightRight.SetSize(num3, 40f);
		_fightUp.SetXY(40f + num3 * 2f, 176f);
		_fightUp.SetSize(num3, 40f);
		_fightDown.SetXY(52f + num3 * 3f, 176f);
		_fightDown.SetSize(num3, 40f);
		_detail.SetXY(18f, 228f);
		_detail.SetSize(width - 36f, Mathf.Max(1f, height - 292f));
		int count = _targetOrder.Count;
		_page = ((count != 0) ? ((_page % count + count) % count) : 0);
		string text = _status + "\n口径：一次攻击击飞，防御方按防御计算；双方普通1–6骰。\n";
		if (count > 0)
		{
			Prediction prediction = _predictions[_targetOrder[_page]];
			text = text + "\n" + (_page + 1) + "/" + count + "  " + prediction.Prefix + prediction.Name + "\n" + prediction.Detail;
		}
		else
		{
			text += "\n显示当前战斗防御方（玩家/怪物）的风险，以及本人对其他怪物的战前估算。\n关闭面板不会停用功能。";
		}
		if (_lastError.Length > 0)
		{
			text = text + "\n最近异常：" + _lastError;
		}
		List<string> list = new List<string>();
		int num4 = Mathf.Max(8, (int)((width - 36f) / 15f));
		string[] array = text.Split('\n');
		foreach (string text2 in array)
		{
			if (text2.Length == 0)
			{
				list.Add("");
			}
			for (int j = 0; j < text2.Length; j += num4)
			{
				list.Add(text2.Substring(j, Math.Min(num4, text2.Length - j)));
			}
		}
		int num5 = Mathf.Max(1, (int)(_detail.height / 22f) - 1);
		int num6 = Math.Max(1, (list.Count + num5 - 1) / num5);
		_detailPage %= num6;
		int num7 = _detailPage * num5;
		_detail.text = "详情 " + (_detailPage + 1) + "/" + num6 + "\n" + string.Join("\n", list.GetRange(num7, Math.Min(num5, list.Count - num7)).ToArray());
		float yv = height - 52f;
		float num8 = (width - 68f) / 4f;
		_previous.SetXY(16f, yv);
		_previous.SetSize(num8, 40f);
		_next.SetXY(28f + num8, yv);
		_next.SetSize(num8, 40f);
		_details.SetXY(40f + num8 * 2f, yv);
		_details.SetSize(num8, 40f);
		_close.SetXY(52f + num8 * 3f, yv);
		_close.SetSize(num8, 40f);
	}
}
