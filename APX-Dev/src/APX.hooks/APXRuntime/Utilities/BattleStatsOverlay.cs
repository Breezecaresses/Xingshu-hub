using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using APXRuntime.Core;
using APXRuntime.MatchPort;
using Core.Scene;
using GameLogic;
using Tools;
using UnityEngine;
using party.model;
using party.protocol;

namespace APXRuntime.Utilities;

public sealed class BattleStatsOverlay : MonoBehaviour
{
	private const string EnabledKey = APXConfigKeys.MatchObserveEnabled;

	private readonly MatchLedger _ledger = new MatchLedger();

	internal readonly HashSet<long> _live = new HashSet<long>();

	private BattleLogic _battle;

	private Signal<HeroHpChangeS2C, bool> _hp;

	private Action<HeroHpChangeS2C, bool> _hpListener;

	private long _generation;

	private bool _enabled;

	private bool _userWantsWidget;

	private bool _isWidgetShowing;

	private bool _lastHadBattleContext;

	private bool _diagnostics;

	private bool _hpAttached;

	private float _nextBind;

	private Rect _windowRect = new Rect(100f, 80f, 640f, 450f);

	private Vector2 _scroll;

	private DateTime _observedSince;

	private string _state = "等待有效对局";

	private StatsWidgetView _widget;

	private float _nextWidgetRefresh;

	private float _retryWidgetAt;

	public static BattleStatsOverlay Instance { get; private set; }

	internal MatchLedger Ledger => _ledger;

	internal static StatsWidgetFrame GetCurrentFrame()
	{
		if (Instance == null)
		{
			return null;
		}
		return StatsWidgetPresenter.Build(Instance._ledger, Instance._live, Instance._enabled, Instance._observedSince, Instance.WidgetDetails());
	}

	internal static bool IsObserving
	{
		get => Instance != null && Instance._enabled;
		set
		{
			if (Instance != null)
			{
				Instance.SetObserving(value);
			}
		}
	}

	internal static bool HasActiveBattleContext
	{
		get
		{
			if (Instance == null || !Instance._enabled)
			{
				return false;
			}
			if (!Context(out RoomInfo room, out BattleLogic battle, out string key))
			{
				return false;
			}
			if (Instance._ledger == null || Instance._ledger.Key == null || Instance._ledger.Key != key || Instance._battle != battle)
			{
				return false;
			}
			return true;
		}
	}

	internal static bool AutoShowPreference
	{
		get
		{
			if (Instance != null)
			{
				return Instance._userWantsWidget;
			}
			return APXConfigService.GetBool(APXConfigKeys.StatsWidgetAutoShow, APXConfigKeys.StatsWidgetAutoShowDefault);
		}
		set
		{
			if (Instance != null)
			{
				Instance._userWantsWidget = value;
				if (HasActiveBattleContext)
				{
					Instance._isWidgetShowing = value;
				}
				else
				{
					Instance._isWidgetShowing = false;
				}
			}
			APXConfigService.SetBool(APXConfigKeys.StatsWidgetAutoShow, value);
			APXConfigService.Save();
		}
	}

	internal static bool UserWantsWidget
	{
		get => AutoShowPreference;
		set => AutoShowPreference = value;
	}

	internal static bool IsFloatingHudVisible
	{
		get => AutoShowPreference;
		set => AutoShowPreference = value;
	}

	internal static bool IsWidgetActuallyShowing
	{
		get => Instance != null && Instance._isWidgetShowing && HasActiveBattleContext;
	}

	internal static void RefreshWidgetPreferences()
	{
		if (Instance != null && Instance._widget != null)
		{
			Instance._widget.ReloadPreferences();
		}
	}

	private bool WidgetReady
	{
		get
		{
			if (_widget != null)
			{
				return _widget.Ready;
			}
			return false;
		}
	}

	private string WidgetDetails()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(_state).Append("\n对局键：").Append(_ledger.Key ?? "—");
		stringBuilder.Append("\nHP：").Append(_hpAttached ? "已连接" : "未连接");
		stringBuilder.Append("\n步数：").Append(MovementBridge.Status).Append("；未确认 ")
			.Append(MovementBridge.Unconfirmed);
		stringBuilder.Append("\n转账：").Append(TransferBridge.Status);
		stringBuilder.Append("\n击倒指 HP 归零，不代表永久消灭。\n— 是未知来源，不是 0；大数仅在挂件中缩写。");
		foreach (string issue in _ledger.Issues)
		{
			stringBuilder.Append("\n· ").Append(issue);
		}
		return stringBuilder.ToString();
	}

	private void LateUpdate()
	{
		bool shouldShow = _isWidgetShowing && HasActiveBattleContext;
		if (!shouldShow)
		{
			if (_widget != null)
			{
				_widget.Show(show: false);
			}
			return;
		}
		if (!WidgetReady && Time.unscaledTime >= _retryWidgetAt)
		{
			_retryWidgetAt = Time.unscaledTime + 5f;
			try
			{
				if (_widget != null)
				{
					_widget.Dispose();
				}
				_widget = new StatsWidgetView(delegate
				{
					_isWidgetShowing = false;
				}, SetObserving);
				_widget.Build();
				_nextWidgetRefresh = 0f;
			}
			catch (Exception ex)
			{
				if (_widget != null)
				{
					_widget.Dispose();
				}
				_widget = null;
				Debug.LogWarning("[APX StatsWidget] uGUI initialization failed; temporary IMGUI fallback: " + ex);
			}
		}
		if (WidgetReady)
		{
			_widget.Show(show: true);
			_widget.Tick();
			if (Time.unscaledTime >= _nextWidgetRefresh)
			{
				_nextWidgetRefresh = Time.unscaledTime + 0.25f;
				_widget.Refresh(StatsWidgetPresenter.Build(_ledger, _live, _enabled, _observedSince, WidgetDetails()));
			}
		}
	}

	private void OnApplicationFocus(bool focused)
	{
		if (!focused && _widget != null)
		{
			_widget.SuspendInteraction();
		}
	}

	private void OnApplicationPause(bool paused)
	{
		if (paused && _widget != null)
		{
			_widget.SuspendInteraction();
		}
	}

	public static void Install(GameObject host)
	{
		if (!(host == null))
		{
			if (host.GetComponent<BattleStatsOverlay>() == null)
			{
				host.AddComponent<BattleStatsOverlay>();
			}
			AssistBall.RegisterAction("battle_stats", "对局统计", ToggleWindow, 30, "轻量统计挂件 · 大小/透明度/位置设置 · 独立采集开关", "统计", () => Instance != null && Instance._enabled);
		}
	}

	public static void ToggleWindow()
	{
		if (Instance != null)
		{
			if (HasActiveBattleContext)
			{
				Instance._isWidgetShowing = !Instance._isWidgetShowing;
			}
			else
			{
				APXRuntime.UI.APXUICenter.OpenBattleStatsPage();
			}
		}
		else
		{
			APXRuntime.UI.APXUICenter.OpenBattleStatsPage();
		}
	}

	private void Awake()
	{
		Instance = this;
		_enabled = APXConfigService.GetBool(APXConfigKeys.MatchObserveEnabled, APXConfigKeys.MatchObserveEnabledDefault);
		_userWantsWidget = APXConfigService.GetBool(APXConfigKeys.StatsWidgetAutoShow, APXConfigKeys.StatsWidgetAutoShowDefault);
		APXOverlayInput.Register(this, () => Instance != null && Instance._isWidgetShowing && HasActiveBattleContext, () => (!WidgetReady) ? APXOverlayInput.ScaledRect(_windowRect, APXMobileUIHelper.ScaleFactor) : _widget.WidgetBounds);
		APXOverlayInput.Register(this, () => Instance != null && Instance._isWidgetShowing && HasActiveBattleContext && WidgetReady && _widget.SettingsVisible, () => (!WidgetReady) ? default(Rect) : _widget.SettingsBounds);
	}

	internal static bool Context(out RoomInfo room, out BattleLogic battle, out string key)
	{
		room = null;
		battle = null;
		key = null;
		if (!SimpleSingletonProvider<GameLogicManager>.hasInstance || SimpleSingletonProvider<GameLogicManager>.inst == null)
		{
			return false;
		}
		if (!SimpleSingletonProvider<SceneManager>.hasInstance || SimpleSingletonProvider<SceneManager>.inst == null || SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
		{
			return false;
		}
		GameLogicManager inst = SimpleSingletonProvider<GameLogicManager>.inst;
		room = ((inst.room == null) ? null : inst.room.curRoomInfo);
		battle = inst.battle;
		if (room == null || room.info == null || room.State != Room.Types.State.Running || room.info.Id <= 0 || room.info.StartTime <= 0 || battle == null)
		{
			return false;
		}
		key = room.info.Id.ToString(CultureInfo.InvariantCulture) + "/" + room.info.StartTime.ToString(CultureInfo.InvariantCulture);
		return true;
	}

	private bool SameContext(BattleLogic battle, string key, long generation)
	{
		if (_enabled && generation == _generation && Context(out var _, out var battle2, out var key2) && battle == battle2)
		{
			return key == key2;
		}
		return false;
	}

	private void Update()
	{
		bool hasBattle = HasActiveBattleContext;
		if (hasBattle != _lastHadBattleContext)
		{
			if (hasBattle)
			{
				if (_userWantsWidget)
				{
					_isWidgetShowing = true;
				}
			}
			else
			{
				_isWidgetShowing = false;
			}
			_lastHadBattleContext = hasBattle;
		}

		if (!_enabled)
		{
			return;
		}
		try
		{
			if (!Context(out var room, out var battle, out var key))
			{
				if (_ledger.Key != null)
				{
					ResetObservation();
				}
				_state = "等待有效对局（不读取旧缓存或离线历史）";
				return;
			}
			if (_ledger.Key != key || _battle != battle)
			{
				bool num = _ledger.Key == key;
				ResetObservation();
				_battle = battle;
				_ledger.Clear(key);
				_observedSince = DateTime.Now;
				_nextBind = 0f;
				if (num)
				{
					_ledger.Issue("同局采集对象更换：已重新开始观察，不拼接缺口");
				}
			}
			MovementBridge.Pump();
			if (Time.unscaledTime >= _nextBind)
			{
				_nextBind = Time.unscaledTime + 0.5f;
				ReadRoster(room);
				BindHp(battle, key);
				bool num2 = MovementBridge.Attach();
				bool flag = TransferBridge.Attach();
				if (!num2)
				{
					_ledger.Issue("步数来源不可用");
				}
				if (!flag)
				{
					_ledger.Issue("转账来源不可用");
				}
				foreach (MatchLedger.Row value in _ledger.Rows.Values)
				{
					value.HpReady |= _hpAttached;
					value.TransferReady |= flag;
				}
			}
			if (MovementBridge.Available)
			{
				foreach (KeyValuePair<long, long> total in MovementBridge.Totals)
				{
					if (_live.Contains(total.Key))
					{
						_ledger.Movement(key, total.Key, total.Value);
					}
				}
			}
			if (MovementBridge.Incomplete)
			{
				_ledger.Issue("部分移动未确认");
			}
			TransferBridge.Drain(delegate(string eventKey, string id, long sender, long recipient, long amount)
			{
				if (_live.Contains(sender) && _live.Contains(recipient))
				{
					_ledger.Transfer(eventKey, id, sender, recipient, amount);
				}
			});
			if (TransferBridge.Incomplete)
			{
				_ledger.Issue("部分转账无法唯一确认");
			}
			_state = "仅统计开启监听后已确认事件；非服务器整局结算";
		}
		catch (Exception ex)
		{
			_ledger.Issue("来源异常: " + ex.GetType().Name);
			_state = "统计不完整，详见来源诊断";
		}
	}

	private void ReadRoster(RoomInfo room)
	{
		_live.Clear();
		HashSet<long> hashSet = new HashSet<long>();
		HashSet<int> hashSet2 = new HashSet<int>();
		foreach (RoomPlayer player in room.Players)
		{
			if (player == null || player.Slot < 0 || player.Slot > 3)
			{
				continue;
			}
			bool flag = false;
			foreach (RoomPlayer player2 in room.Players)
			{
				if (player2 != null && player != player2 && (player2.Id == player.Id || player2.Slot == player.Slot))
				{
					flag = true;
				}
			}
			if (flag || !hashSet.Add(player.Id) || !hashSet2.Add(player.Slot))
			{
				_ledger.Issue("重复身份/席位已排除");
				continue;
			}
			string text = "未知角色";
			if (player.characterConfig != null)
			{
				text = CharacterHandle.GetCharacterName(player.characterConfig.Id, player.characterConfig.CharacterType);
			}
			_ledger.Register(player.Id, player.Slot, text);
			_live.Add(player.Id);
		}
		if (_live.Count != 4)
		{
			_ledger.Issue("四人席位信息不完整");
		}
	}

	private void BindHp(BattleLogic battle, string key)
	{
		Signal<HeroHpChangeS2C, bool> signal = ((battle.signal == null) ? null : battle.signal.playerHpChange);
		if (_hp != signal)
		{
			if (_hp != null)
			{
				_ledger.Issue("HP 来源已更换");
			}
			DetachHp();
			_hp = signal;
		}
		if (_hp == null)
		{
			_ledger.Issue("HP 来源不可用");
			return;
		}
		if (_hpListener == null)
		{
			long generation = _generation;
			_hpListener = delegate(HeroHpChangeS2C message, bool inFight)
			{
				try
				{
					if (message != null && SameContext(battle, key, generation) && _live.Contains(message.Killer))
					{
						int? currentHp = null;
						foreach (BattlePlayerData playerData in battle.PlayerDatas)
						{
							if (playerData != null && playerData.player != null && playerData.player.Id == message.PlayerId && playerData.Property != null)
							{
								currentHp = playerData.Property.HP.Value;
								break;
							}
						}
						_ledger.Hp(key, message, message.PlayerId, message.Killer, message.RealChangeHp, message.OriHp, currentHp);
					}
				}
				catch (Exception ex)
				{
					_ledger.Issue("HP 事件不完整: " + ex.GetType().Name);
				}
			};
		}
		_hpAttached = false;
		_hp.AddListener(_hpListener);
		_hpAttached = true;
	}

	private void DetachHp()
	{
		_generation++;
		Signal<HeroHpChangeS2C, bool> hp = _hp;
		Action<HeroHpChangeS2C, bool> hpListener = _hpListener;
		_hp = null;
		_hpListener = null;
		_hpAttached = false;
		if (hp != null && hpListener != null)
		{
			try
			{
				hp.RemoveListener(hpListener);
			}
			catch (Exception ex)
			{
				_ledger.Issue("移除 HP 监听失败: " + ex.GetType().Name);
			}
		}
	}

	private void ResetObservation()
	{
		_generation++;
		DetachHp();
		MovementBridge.Reset();
		TransferBridge.Reset();
		_battle = null;
		_ledger.Clear(null);
		_live.Clear();
		_nextBind = 0f;
	}

	private void SetObserving(bool value)
	{
		if (_enabled != value)
		{
			_enabled = value;
			ResetObservation();
			_nextWidgetRefresh = 0f;
			APXConfigService.SetBool(APXConfigKeys.MatchObserveEnabled, value);
			APXConfigService.Save();
			_state = (value ? "等待有效对局；重新开启从零观察" : "已关闭监听并清空本次观察");
		}
	}

	private void OnDisable()
	{
		ResetObservation();
		if (_widget != null)
		{
			_widget.SuspendInteraction();
			_widget.Show(show: false);
		}
	}

	private void OnDestroy()
	{
		ResetObservation();
		if (_widget != null)
		{
			_widget.Dispose();
			_widget = null;
		}
		if (Instance == this)
		{
			Instance = null;
			AssistBall.Unregister("battle_stats");
		}
	}

	private void OnGUI()
	{
		if (!(_isWidgetShowing && HasActiveBattleContext) || WidgetReady)
		{
			return;
		}
		Color color = GUI.color;
		Color backgroundColor = GUI.backgroundColor;
		int depth = GUI.depth;
		try
		{
			GUI.depth = -1000;
			GUI.color = Color.white;
			GUI.backgroundColor = new Color(0.1f, 0.15f, 0.22f, 1f);
			using (new APXMobileUIHelper.MatrixScope(0f))
			{
				_windowRect = APXMobileUIHelper.FitRect(_windowRect, APXMobileUIHelper.SafeGUIRect(APXMobileUIHelper.ScaleFactor));
				_windowRect = GUI.Window(88577, _windowRect, DrawWindow, "对局统计 · 备用界面");
			}
		}
		finally
		{
			GUI.color = color;
			GUI.backgroundColor = backgroundColor;
			GUI.depth = depth;
		}
	}

	private static string Metric(long value, bool ready, bool known)
	{
		if (!(ready && known))
		{
			return "—";
		}
		return value.ToString(CultureInfo.InvariantCulture);
	}

	private void DrawWindow(int id)
	{
		Color color = GUI.color;
		GUI.color = new Color(0.075f, 0.1f, 0.15f, 1f);
		GUI.DrawTexture(new Rect(0f, 20f, _windowRect.width, Mathf.Max(0f, _windowRect.height - 20f)), Texture2D.whiteTexture);
		GUI.color = color;
		GUILayout.BeginHorizontal();
		if (GUILayout.Button(_enabled ? "统计：开启（点击关闭并清空）" : "统计：关闭（点击开启）", GUILayout.Height(30f)))
		{
			SetObserving(!_enabled);
		}
		if (GUILayout.Button("隐藏窗口", GUILayout.Width(90f), GUILayout.Height(30f)))
		{
			_isWidgetShowing = false;
		}
		GUILayout.EndHorizontal();
		_scroll = GUILayout.BeginScrollView(_scroll);
		GUILayout.Label(_state);
		if (_ledger.Key != null)
		{
			GUILayout.Label("观察起点：" + _observedSince.ToString("HH:mm:ss") + "（切局/重连重建/关闭后重开会清零）");
		}
		GUILayout.Label("击倒 / 实际伤害 / 确认步数 / 累计转出（不含收款）");
		GUILayout.Label("击倒指 HP 归零，不代表永久消灭；— 表示来源未知。隐藏窗口不关闭统计。");
		for (int i = 0; i < 4; i++)
		{
			MatchLedger.Row row = null;
			foreach (MatchLedger.Row value in _ledger.Rows.Values)
			{
				if (value.Slot == i && _live.Contains(value.Id))
				{
					row = value;
					break;
				}
			}
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("P" + (i + 1) + "  " + ((row == null) ? "—" : row.Name));
			GUILayout.Label((row == null) ? "— / — / — / —" : (Metric(row.Kills, row.HpReady, row.KillsKnown) + " / " + Metric(row.Damage, row.HpReady, row.DamageKnown) + " / " + Metric(row.Movement, row.MovementReady, row.MovementKnown) + " / " + Metric(row.Transferred, row.TransferReady, row.TransferKnown)));
			GUILayout.EndVertical();
		}
		_diagnostics = GUILayout.Toggle(_diagnostics, "来源诊断" + ((_ledger.Issues.Count > 0) ? "（部分/不完整）" : "（观察累计）"));
		if (_diagnostics)
		{
			GUILayout.Label("对局键：" + (_ledger.Key ?? "—"));
			GUILayout.Label("HP：" + (_hpAttached ? "已连接" : "未连接"));
			GUILayout.Label("步数：" + MovementBridge.Status + "；未确认 " + MovementBridge.Unconfirmed);
			GUILayout.Label("转账：" + TransferBridge.Status);
			foreach (string issue in _ledger.Issues)
			{
				GUILayout.Label("· " + issue);
			}
		}
		GUILayout.EndScrollView();
		GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 20f));
	}
}
