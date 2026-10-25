using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Net;
using XingShuRuntime.Core;
using XingShuRuntime.MatchPort;
using Core.Scene;
using GameLogic;
using Tools;
using UnityEngine;
using party.model;
using party.protocol;

namespace XingShuRuntime.Utilities;

public sealed class BattleStatsOverlay : MonoBehaviour
{
	private const string EnabledKey = XingShuConfigKeys.MatchObserveEnabled;

	private readonly MatchLedger _ledger = new MatchLedger();
	private readonly LastSettlementCache _lastSettlement = new LastSettlementCache();
	private readonly HealingSourceMap _healingSources = new HealingSourceMap();

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
	private bool _settled;
	private readonly Queue<object> _goldSeen = new Queue<object>();
	private RPCAsyncResult _healingRpc;
	private Action<RPCAsyncResult> _healingListener;
	private double _nextHealingSync, _healingDeadline;
	private double _lastHealingClock = ThinkingClock.RealSeconds(), _healingClock;
	private int _healingSlot;
	private readonly HashSet<long> _healingConfirmed = new HashSet<long>();
	private readonly Queue<long> _healingRefresh = new Queue<long>();
	private readonly HashSet<long> _healingQueued = new HashSet<long>();
	private readonly Dictionary<long, int> _healingLastValues = new Dictionary<long, int>();
	private GetHeroInfoS2C _healingReply;
	private long _healingRequestPlayer, _healingRequestGeneration;
	private string _healingRequestKey;
	private bool _healingEffectRefresh;

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
		bool hasContext = Context(out _, out _, out var key, true);
		var retained = Instance._lastSettlement.Read(CurrentAccountId(), hasContext ? key : null);
		if (!HasActiveBattleContext && retained != null) return retained;
		return StatsWidgetPresenter.Build(Instance._ledger, Instance._live, Instance._enabled, Instance._observedSince, Instance.WidgetDetails());
	}

	private static long CurrentAccountId()
	{
		try
		{
			var account = SimpleSingletonProvider<GameLogicManager>.hasInstance ? SimpleSingletonProvider<GameLogicManager>.inst.account : null;
			return account != null && account.online ? account.GetPlayerID() : 0;
		}
		catch { return 0; }
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
				return Instance._settled && Context(out _, out var settledBattle, out var settledKey, true) &&
					Instance._ledger.Key == settledKey && Instance._battle == settledBattle;
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
			return XingShuConfigService.GetBool(XingShuConfigKeys.StatsWidgetAutoShow, XingShuConfigKeys.StatsWidgetAutoShowDefault);
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
			XingShuConfigService.SetBool(XingShuConfigKeys.StatsWidgetAutoShow, value);
			XingShuConfigService.Save();
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
		stringBuilder.Append("\n治疗：合作模式累计已确认有效回血；来源不全显示 ≥、评分显示 ≈；结算按服务器校准");
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
				Debug.LogWarning("[XingShu StatsWidget] uGUI initialization failed; temporary IMGUI fallback: " + ex);
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
				XingShuRuntime.UI.XingShuUICenter.OpenBattleStatsPage();
			}
		}
		else
		{
			XingShuRuntime.UI.XingShuUICenter.OpenBattleStatsPage();
		}
	}

	private void Awake()
	{
		Instance = this;
		_enabled = XingShuConfigService.GetBool(XingShuConfigKeys.MatchObserveEnabled, XingShuConfigKeys.MatchObserveEnabledDefault);
		_userWantsWidget = XingShuConfigService.GetBool(XingShuConfigKeys.StatsWidgetAutoShow, XingShuConfigKeys.StatsWidgetAutoShowDefault);
		XingShuOverlayInput.Register(this, () => Instance != null && Instance._isWidgetShowing && HasActiveBattleContext, () => (!WidgetReady) ? XingShuOverlayInput.ScaledRect(_windowRect, XingShuMobileUIHelper.ScaleFactor) : _widget.WidgetBounds);
		XingShuOverlayInput.Register(this, () => Instance != null && Instance._isWidgetShowing && HasActiveBattleContext && WidgetReady && _widget.SettingsVisible, () => (!WidgetReady) ? default(Rect) : _widget.SettingsBounds);
	}

	internal static bool Context(out RoomInfo room, out BattleLogic battle, out string key, bool includeSettlement = false)
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
		if (room == null || room.info == null || (!includeSettlement && room.State != Room.Types.State.Running) || room.info.Id <= 0 || room.info.StartTime <= 0 || battle == null)
		{
			return false;
		}
		key = room.info.Id.ToString(CultureInfo.InvariantCulture) + "/" + room.info.StartTime.ToString(CultureInfo.InvariantCulture);
		return true;
	}

	private bool SameContext(BattleLogic battle, string key, long generation)
	{
		if (_enabled && !_settled && generation == _generation && Context(out var _, out var battle2, out var key2) && battle == battle2)
		{
			return key == key2;
		}
		return false;
	}

	private void Update()
	{
		// Invalidate on logout/account changes even while the statistics page is
		// closed, so signing back in cannot resurrect another session's snapshot.
		_lastSettlement.Read(CurrentAccountId(), null);
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
				if (_settled && HasActiveBattleContext) return;
				if (_ledger.Key != null)
				{
					ResetObservation();
				}
				_state = "等待有效对局（不读取旧缓存或离线历史）";
				return;
			}
			if (_ledger.Key != key || _battle != battle)
			{
				_lastSettlement.Read(CurrentAccountId(), key);
				bool num = _ledger.Key == key;
				ResetObservation();
				_battle = battle;
				_ledger.Clear(key);
				_healingSources.Clear(key);
				_observedSince = DateTime.Now;
				_nextBind = 0f;
				if (num)
				{
					_ledger.Issue("同局采集对象更换：已重新开始观察，不拼接缺口");
				}
			}
			if (_settled) return;
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
					value.MetricsReady = _hpAttached && flag;
				}
			}
			SyncHealing(battle, key);
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

	private void SyncHealing(BattleLogic battle, string key)
	{
		// The game's hero-info reply supplies TreatmentScore directly. Stagger the
		// four public participant reads: at most one request every three real seconds.
		// Unity's unscaled clock and Stopwatch can both be accelerated on Windows.
		double clock = ThinkingClock.RealSeconds();
		_healingClock += Math.Max(0d, clock - _lastHealingClock);
		_lastHealingClock = Math.Max(_lastHealingClock, clock);
		double now = _healingClock;
		if (_healingRpc != null)
		{
			if (_healingRpc.keepWaiting && now < _healingDeadline) return;
			DetachHealing();
		}
		if (now < _nextHealingSync || !MonoSingletonProvider<NetManager>.hasInstance ||
			!MonoSingletonProvider<NetManager>.inst.IsConnected) return;
		MatchLedger.Row selected = null;
		while (_healingRefresh.Count > 0 && selected == null)
		{
			long pending = _healingRefresh.Dequeue();
			_healingQueued.Remove(pending);
			MatchLedger.Row row;
			if (_live.Contains(pending) && _ledger.Rows.TryGetValue(pending, out row)) selected = row;
		}
		bool effectRefresh = selected != null;
		for (int offset = 0; offset < 4 && selected == null; offset++)
		{
			int slot = (_healingSlot + offset) % 4;
			foreach (var row in _ledger.Rows.Values)
				if (row.Slot == slot && _live.Contains(row.Id)) { selected = row; break; }
		}
		_nextHealingSync = now + 3d;
		if (selected == null) return;
		_healingSlot = (selected.Slot + 1) % 4;
		long playerId = selected.Id, generation = _generation;
		var playerData = battle.GetPlayerDataById(playerId);
		if (playerData == null) return;
		_healingReply = null;
		_healingRequestPlayer = playerId;
		_healingRequestGeneration = generation;
		_healingRequestKey = key;
		_healingEffectRefresh = effectRefresh;
		var rpc = battle.RequestGetHeroInfoC2S(playerId);
		if (rpc == null) { _ledger.Issue("服务器治疗统计请求暂不可用"); return; }
		_healingRpc = rpc;
		_healingDeadline = now + 12d;
		_healingListener = result => {
			try
			{
				if (!ReferenceEquals(_healingRpc, result) || !SameContext(battle, key, generation)) return;
				// Read the typed reply observed at the official handler for this in-flight
				// participant request. Completion alone does not prove the cached field
				// was populated; a default zero must not masquerade as a fresh reply.
				var reply = _healingReply;
				if (result.errId == 0 && reply != null && reply.PlayerId == playerId &&
					_healingRequestKey == key && _healingRequestGeneration == generation &&
					ReferenceEquals(battle.GetPlayerDataById(playerId), playerData) &&
					SimpleSingletonProvider<GameLogicManager>.inst.room.roomController != null &&
					SimpleSingletonProvider<GameLogicManager>.inst.room.roomController.roomStateType == RoomStateType.RUNNING)
				{
					bool authoritative = !BattleConfig.IsPVE(SimpleSingletonProvider<GameLogicManager>.inst.room.curRoomInfo.MapType);
					_ledger.ServerHealing(key, playerId, reply.TreatmentScore, authoritative);
					int previous;
					bool first = _healingConfirmed.Add(playerId);
					if (first || effectRefresh || !_healingLastValues.TryGetValue(playerId, out previous) || previous != reply.TreatmentScore)
						XingShu.Runtime.XingShuRuntimeHost.LogLifecycle(
							"[Stats] Typed hero-info healing observed: slot=" + (selected.Slot + 1) + ", healing=" + reply.TreatmentScore + ", authoritative=" + authoritative +
							", effectRefresh=" + effectRefresh + ", cached=" + playerData.TreatmentScore);
					_healingLastValues[playerId] = reply.TreatmentScore;
				}
				else _ledger.Issue("服务器治疗统计暂未返回，保留已确认数据");
			}
			catch (Exception ex) { _ledger.Issue("治疗统计回包暂不可用: " + ex.GetType().Name); }
		};
		rpc.OnFinished.AddOnce(_healingListener);
		if (!rpc.keepWaiting) { rpc.OnFinished.RemoveListener(_healingListener); _healingListener(rpc); }
	}

	private void QueueHealingRefresh(long id)
	{
		if (_live.Contains(id) && _healingQueued.Add(id)) _healingRefresh.Enqueue(id);
	}

	private void DetachHealing()
	{
		if (_healingRpc != null && _healingListener != null) _healingRpc.OnFinished.RemoveListener(_healingListener);
		_healingRpc = null;
		_healingListener = null;
		_healingReply = null;
		_healingRequestPlayer = 0;
		_healingRequestKey = null;
		_healingEffectRefresh = false;
	}

	// Observes the decoded protocol message before the official handler applies it.
	// The patch checks errId == 0 and leaves the original handler unchanged.
	public static void ObserveHeroInfo(GetHeroInfoS2C message)
	{
		try
		{
			var instance = Instance;
			if (message == null || instance == null || instance._healingRequestPlayer != message.PlayerId ||
				!instance.SameContext(instance._battle, instance._healingRequestKey, instance._healingRequestGeneration)) return;
			instance._healingReply = message;
			MatchLedger.Row row;
			if (!instance._ledger.Rows.TryGetValue(message.PlayerId, out row)) return;
			int previous;
			if (instance._healingEffectRefresh || !instance._healingLastValues.TryGetValue(message.PlayerId, out previous) || previous != message.TreatmentScore)
				XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[Stats] Typed hero-info reply: slot=" + (row.Slot + 1) +
					", healing=" + message.TreatmentScore + ", damage=" + message.TotalDamage + ", kills=" + message.KillCount +
					", deaths=" + message.TotalDie + ", taken=" + message.TotalInjured);
		}
		catch (Exception ex) { Debug.LogWarning("[XingShu Stats] Hero-info observation: " + ex.GetType().Name); }
	}

	public static void ObserveHealingSources(UpdateHeroAttrS2C message)
	{
		try
		{
			var instance = Instance;
			if (instance == null || message == null || message.Cause == null || instance._settled ||
				!Context(out _, out var battle, out var key) || instance._battle != battle || instance._ledger.Key != key ||
				!instance._enabled || !instance._live.Contains(message.PlayerId)) return;
			// Card/skill show actions identify PlayerId as the user. Buff, terrain
			// and relic notifications do not establish a healing source.
			if (message.Cause.S != CauseOrigin.Types.source.Card && message.Cause.S != CauseOrigin.Types.source.Skill) return;
			foreach (var effect in message.EffectDatas)
				if (effect != null && effect.Hp != null && effect.Hp.Killer == 0 && effect.Hp.PlayerId == effect.PlayerId &&
					(effect.Hp.ChangeHp > 0 || effect.Hp.RealChangeHp > 0) && instance._live.Contains(effect.PlayerId))
					instance._healingSources.Remember(key, effect.Hp, effect.PlayerId, message.PlayerId);
		}
		catch (Exception ex) { Debug.LogWarning("[XingShu Stats] Healing source: " + ex.GetType().Name); }
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
			_ledger.Register(player.Id, player.Slot, player.GetNick());
			var ledgerRow = _ledger.Rows[player.Id];
			ledgerRow.Character = text;
			// Hero.Round is not maintained by the client round notification.
			ledgerRow.Turns = Math.Max(0, room.Round);
			ledgerRow.Gold = Math.Max(0, player.Hero?.Gold ?? 0);
			try { ledgerRow.HeadUrl = _battle.GetPlayerDataById(player.Id)?.GetCharacterHeadUrl(); } catch { }
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
					if (message != null && SameContext(battle, key, generation))
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
						long healingSource = _healingSources.Resolve(key, message, message.PlayerId, message.Killer);
						_ledger.Hp(key, message, message.PlayerId, healingSource, message.RealChangeHp, message.OriHp, currentHp);
						if (message.ChangeHp > 0 || message.RealChangeHp > 0)
						{
							// Refresh official totals after any healing effect, including cards,
							// chips and full-HP uses. An absent Killer is not proof of self-healing.
							QueueHealingRefresh(healingSource);
							QueueHealingRefresh(message.PlayerId);
							if (!_live.Contains(healingSource))
								foreach (var id in _live) QueueHealingRefresh(id);
							MatchLedger.Row target, source;
							_ledger.Rows.TryGetValue(message.PlayerId, out target);
							_ledger.Rows.TryGetValue(healingSource, out source);
							XingShu.Runtime.XingShuRuntimeHost.LogLifecycle(
								"[Stats] Healing effect observed: targetSlot=" + (target == null ? 0 : target.Slot + 1) +
								", sourceSlot=" + (source == null ? 0 : source.Slot + 1) +
								", effect=" + message.ChangeHp + ", reportedChange=" + message.RealChangeHp +
								", before=" + message.OriHp + ", after=" + message.CurrHp + ", max=" + message.MaxHp);
						}
					}
				}
				catch (Exception ex)
				{
					_ledger.Issue("HP 事件不完整: " + ex.GetType().Name);
				}
			};
		}
		if (!_hpAttached)
		{
			_hp.AddListener(_hpListener);
			_hpAttached = true;
		}
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
		_settled = false;
		DetachHealing();
		_nextHealingSync = 0;
		_healingSlot = 0;
		_healingConfirmed.Clear();
		_healingRefresh.Clear();
		_healingQueued.Clear();
		_healingLastValues.Clear();
		_goldSeen.Clear();
		_generation++;
		DetachHp();
		MovementBridge.Reset();
		TransferBridge.Reset();
		_battle = null;
		_ledger.Clear(null);
		_healingSources.Clear(null);
		_live.Clear();
		_nextBind = 0f;
	}

	// Invoked at the verified game protocol handlers; never sends or changes a message.
	public static void ObserveGold(HeroGoldChangeS2C message)
	{
		try
		{
			var instance = Instance;
			if (message == null || instance == null || !instance._enabled || instance._settled || !HasActiveBattleContext) return;
			// Protobuf messages compare their values; two equal gold awards are still two events.
			foreach (var seen in instance._goldSeen) if (ReferenceEquals(seen, message)) return;
			instance._goldSeen.Enqueue(message);
			if (instance._goldSeen.Count > 128) instance._goldSeen.Dequeue();
			MatchLedger.Row row;
			if (instance._ledger.Rows.TryGetValue(message.PlayerId, out row) && message.ChangeGold > 0) row.Coins += message.ChangeGold;
		}
		catch (Exception ex) { Debug.LogWarning("[XingShu Stats] Gold: " + ex.GetType().Name); }
	}

	public static void ObserveSettlement(GameFinishS2C message)
	{
		try
		{
			// Capture history even when the optional live statistics widget is disabled.
			if (message != null) XingShu.Runtime.XingShuRuntimeHost.LogLifecycle(
				"[Stats] Official settlement observed; achievements=" + message.NewAchieve.Count +
				"; replayIdentity=" + !string.IsNullOrEmpty(message.ReplayId));
			XingShu.Runtime.GameLibraryExporter.ObserveSettlement(message);
			var instance = Instance;
			if (message == null || instance == null || !instance._enabled || instance._settled ||
				!Context(out var room, out var battle, out var key, true) ||
				SimpleSingletonProvider<GameLogicManager>.inst.replay?.Session?.IsReplay == true) return;
			if (room.Id != message.RoomId) return;
			if (instance._ledger.Key != key || instance._battle != battle)
			{
				instance.ResetObservation(); instance._battle = battle; instance._ledger.Clear(key);
			}
			instance.ReadRoster(room);
			foreach (var row in instance._ledger.Rows.Values)
			{
				PlayerFinishAchieve achieve;
				if (!message.NewAchieve.TryGetValue(row.Id, out achieve)) { row.MetricsReady = false; continue; }
				instance._ledger.Settlement(row.Id, achieve.KillCount, achieve.TotalDie, achieve.TotalDamage,
					achieve.TotalInjured, achieve.TreatmentScore, achieve.TotalGold, achieve.PveTransferGold, achieve.MovePoint);
				XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[Stats] Settlement metrics: slot=" + (row.Slot + 1) +
					", kills=" + achieve.KillCount + ", deaths=" + achieve.TotalDie + ", damage=" + achieve.TotalDamage +
					", taken=" + achieve.TotalInjured + ", healing=" + achieve.TreatmentScore + ", coins=" + achieve.TotalGold +
					", transfer=" + achieve.PveTransferGold + ", movement=" + achieve.MovePoint + ", turns=" + room.Round);
			}
			instance._settled = true;
			instance._state = "服务器结算 · 星枢评分 v2";
			instance.DetachHealing();
			instance._lastSettlement.Capture(CurrentAccountId(), key,
				StatsWidgetPresenter.Build(instance._ledger, instance._live, instance._enabled, instance._observedSince, instance.WidgetDetails()));
			XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[Stats] Official settlement applied to widget");
			instance._nextWidgetRefresh = 0;
		}
		catch (Exception ex) { Debug.LogWarning("[XingShu Stats] Settlement: " + ex.GetType().Name); }
	}

	private void SetObserving(bool value)
	{
		if (_enabled != value)
		{
			_enabled = value;
			if (!value) _lastSettlement.Clear();
			ResetObservation();
			_nextWidgetRefresh = 0f;
			XingShuConfigService.SetBool(XingShuConfigKeys.MatchObserveEnabled, value);
			XingShuConfigService.Save();
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
			using (new XingShuMobileUIHelper.MatrixScope(0f))
			{
				_windowRect = XingShuMobileUIHelper.FitRect(_windowRect, XingShuMobileUIHelper.SafeGUIRect(XingShuMobileUIHelper.ScaleFactor));
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
