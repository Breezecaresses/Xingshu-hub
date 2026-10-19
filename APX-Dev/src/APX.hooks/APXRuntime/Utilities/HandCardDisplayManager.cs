using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Core;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameLogic;
using Google.Protobuf.Collections;
using Tools;
using UI;
using UnityEngine;
using UnityEngine.Networking;
using party.model;
using APXRuntime.Core;
using APXRuntime.Features;
using XingShu.Cards;

namespace APXRuntime.Utilities;

public sealed class HandCardDisplayManager : MonoBehaviour
{
	private const int REQUEST_TIMEOUT_SECONDS = 5;
	private const int MAX_COMPENSATED_TIMEOUT_SECONDS = 30;

	/// <summary>
	/// 计算低层时钟变速补偿后的请求超时时间 (秒) (Phase 2D-2B3-R4)
	/// 仅针对 Native Engine 进行现实时间预算补偿: timeout = ceil(5.0 * max(1.0, NativeEffectiveSpeed))，上限 30s
	/// Managed 模式或 Native 停用/未就绪时严格保持基线 5s，慢动作 (0.25~1.0x) 下亦维持基线 5s
	/// </summary>
	internal static int GetCompensatedRequestTimeout()
	{
		try
		{
			float nativeEff = APXSpeedEngineCoordinator.GetNativeEffectiveSpeedForTiming();
			float factor = Mathf.Max(1.0f, nativeEff);
			int compensated = Mathf.CeilToInt((float)REQUEST_TIMEOUT_SECONDS * factor);
			return Mathf.Clamp(compensated, REQUEST_TIMEOUT_SECONDS, MAX_COMPENSATED_TIMEOUT_SECONDS);
		}
		catch
		{
			return REQUEST_TIMEOUT_SECONDS;
		}
	}

	private const int MAX_RETRIES = 3;

	private const float RETRY_DELAY_SECONDS = 0.5f;

	private const float DEBOUNCE_INTERVAL = 0.5f;

	private static bool _isEnabled = true;

	private static bool _spectatorAutoOpen = true;

	private static float _cardScale = 1f;

	private static float _uiScale = 1f;

	private static float _playBtnSize = 24f;

	private static bool _btnSizeInitialized;

	private static float SafeClamp(float val, float min, float max)
	{
		if (val < min) return min;
		if (val > max) return max;
		return val;
	}

	internal static float GetEffectivePlayBtnSize()
	{
		if (!_btnSizeInitialized)
		{
			float defaultBtnSize = APXMobileUIHelper.IsMobile ? APXConfigKeys.HandCardBtnSizeMobileDefault : APXConfigKeys.HandCardBtnSizeDesktopDefault;
			try
			{
				_playBtnSize = SafeClamp(APXConfigService.GetFloat(APXConfigKeys.HandCardBtnSize, defaultBtnSize), APXConfigKeys.HandCardBtnSizeMin, APXConfigKeys.HandCardBtnSizeMax);
			}
			catch
			{
				_playBtnSize = defaultBtnSize;
			}
			_btnSizeInitialized = true;
		}
		return _playBtnSize;
	}

	private static readonly Dictionary<long, TrackedPlayerHand> _playerHands = new Dictionary<long, TrackedPlayerHand>();

	private static bool _needFetchFromServer = false;

	private static long _firstRoundFetchedRoomId = 0L;

	private static long _lastActiveRoomId = 0L;

	private static int _lastSpectatorRoomId = 0;

	private static long _lastSpectatorEpoch = 0;

	private static bool _roundListenerRegistered = false;

	public static bool OverlayRenderDirty { get; set; } = true;

	private static float _nextReconcileTime = 0f;

	private static int _reconcileRetryCount = 0;

	internal const float RECONCILE_RETRY_INTERVAL = 0.25f;

	internal const int MAX_AGGRESSIVE_RETRIES = 12;

	internal const float STEADY_RECONCILE_INTERVAL = 1.0f;

#pragma warning disable CS0414
	private static bool _lastBattleUiReady = false;

	private static bool _lastBattleRunning = false;

	private static object _lastContainerInstance = null;

	private static int _lastKnownRosterCount = 0;
#pragma warning restore CS0414

	internal static Func<long, int, object> MockHeadButtonResolver { get; set; }

	private static string _lastServerResponseText = "";

	private static string _logFilePath = "";

	private static readonly object _logLock = new object();

	private bool _showDebugUI;

	private Rect _debugWinRect = new Rect(80f, 80f, 520f, 500f);

	private Vector2 _debugScroll = Vector2.zero;

	internal static bool UseModernView { get; set; } = true;

	internal static string LastServerResponseText => _lastServerResponseText;

	private static readonly object _widgetLock = new object();
	private static readonly Dictionary<long, HandCardPresentationWidgetContract> _presentationWidgets = new Dictionary<long, HandCardPresentationWidgetContract>();

	public static int WidgetCreateAttempts { get; set; }
	public static int WidgetCreateSuccess { get; set; }

	public static int ActiveWidgetCount
	{
		get
		{
			lock (_widgetLock)
			{
				int count = 0;
				foreach (var kvp in _presentationWidgets)
				{
					var contract = kvp.Value;
					if (contract == null || contract.IsDisposed) continue;

					if (contract.Parent is GObject gObj)
					{
						if (gObj.isDisposed || !gObj.onStage)
						{
							contract.IsDisposed = true;
							contract.Visible = false;
							continue;
						}
					}
					count++;
				}
				return count;
			}
		}
	}

	public static int VisibleWidgetCount
	{
		get
		{
			if (!_isEnabled) return 0;
			lock (_widgetLock)
			{
				int count = 0;
				foreach (var kvp in _presentationWidgets)
				{
					var contract = kvp.Value;
					if (contract == null || contract.IsDisposed || !contract.Visible) continue;

					if (contract.Parent is GObject gObj)
					{
						if (gObj.isDisposed || !gObj.onStage)
						{
							contract.IsDisposed = true;
							contract.Visible = false;
							continue;
						}
					}
					count++;
				}
				return count;
			}
		}
	}

	public static HandCardPresentationWidgetContract GetWidgetPresentation(long playerId)
	{
		lock (_widgetLock)
		{
			_presentationWidgets.TryGetValue(playerId, out var w);
			return w;
		}
	}

	public static IReadOnlyDictionary<long, HandCardPresentationWidgetContract> GetAllWidgetPresentations()
	{
		lock (_widgetLock)
		{
			return new Dictionary<long, HandCardPresentationWidgetContract>(_presentationWidgets);
		}
	}

	public static void RegisterPresentationWidget(long playerId, object parent, float x, float y, float width, float height, float scale, float alpha = 1.0f)
	{
		WidgetCreateAttempts++;
		lock (_widgetLock)
		{
			var widget = new HandCardPresentationWidgetContract
			{
				PlayerId = playerId,
				Parent = parent,
				Visible = _isEnabled,
				Alpha = alpha,
				Width = width,
				Height = height,
				Scale = scale,
				X = x,
				Y = y,
				IsDisposed = false
			};
			_presentationWidgets[playerId] = widget;
			WidgetCreateSuccess++;
		}
	}

	public static int ExpectedVisibleWidgetCount
	{
		get
		{
			if (!_isEnabled) return 0;
			XingShu.Truth.HandSessionLifecycle.Observe();
            bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
			if (inSpectator)
			{
				var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();
				var allHands = XingShu.Spectator.SpectatorStateStore.GetAllPlayerHands();
				if (validPids != null && validPids.Count > 0)
				{
					int count = 0;
					foreach (var pid in validPids)
					{
						if (allHands.ContainsKey(pid)) count++;
					}
					return count;
				}
				return allHands.Count;
			}
			else if (IsPVEMode())
			{
				return _playerHands.Count;
			}
			return 0;
		}
	}

	public static void ResetPresentationWidgets()
	{
		lock (_widgetLock)
		{
			_presentationWidgets.Clear();
			WidgetCreateAttempts = 0;
			WidgetCreateSuccess = 0;
		}
		OverlayRenderDirty = true;
		_reconcileRetryCount = 0;
		_nextReconcileTime = 0f;
	}

	private static float GetCurrentTime()
	{
		try
		{
			return Time.realtimeSinceStartup;
		}
		catch
		{
			return (float)(DateTime.UtcNow.Ticks / 10000000.0);
		}
	}

	public static void MarkOverlayDirty(string reason)
	{
		OverlayRenderDirty = true;
		_reconcileRetryCount = 0;
		_nextReconcileTime = 0f;
		Log("RECONCILE_DIRTY", $"Overlay marked dirty: {reason}");
	}

	public static void PerformReconciliationTick(float? mockCurrentTime = null)
	{
		if (!_isEnabled) return;

		int expected = ExpectedVisibleWidgetCount;
		int actual = VisibleWidgetCount;

		if (actual >= expected && expected > 0)
		{
			if (OverlayRenderDirty)
			{
				OverlayRenderDirty = false;
				Log("RECONCILE_CONVERGED", $"Overlay presentation fully converged: Visible={actual}/{expected} in {_reconcileRetryCount} retries.");
			}
			return;
		}

		if (expected == 0)
		{
			return;
		}

		float now = mockCurrentTime ?? GetCurrentTime();
		if (now >= _nextReconcileTime)
		{
			float interval = (_reconcileRetryCount < MAX_AGGRESSIVE_RETRIES)
				? RECONCILE_RETRY_INTERVAL
				: STEADY_RECONCILE_INTERVAL;

			_nextReconcileTime = now + interval;
			_reconcileRetryCount++;

			ReconcileOverlayWidgets();
		}
	}

	public static void ReconcileOverlayWidgets()
	{
		if (!_isEnabled) return;
		bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
		if (!inSpectator && !IsPVEMode() && MockHeadButtonResolver == null) return;

		if (inSpectator)
		{
			var allStoreHands = XingShu.Spectator.SpectatorStateStore.GetAllPlayerHands();
			var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();

			foreach (var kvp in allStoreHands)
			{
				long pid = kvp.Key;
				if (validPids.Count > 0 && !validPids.Contains(pid)) continue;

				TrackedPlayerHand hand = GetOrCreateHand(pid);
				PopulatePlayerMetadataIfMissing(hand, pid);

				ReconcileSingleHandPresentation(hand);
			}
		}
		else
		{
			foreach (var kvp in _playerHands)
			{
				ReconcileSingleHandPresentation(kvp.Value);
			}
		}

		int actual = VisibleWidgetCount;
		int expected = ExpectedVisibleWidgetCount;
		if (actual >= expected && expected > 0)
		{
			OverlayRenderDirty = false;
			Log("RECONCILE_CONVERGED", $"Overlay presentation converged: Visible={actual}/{expected} in {_reconcileRetryCount} retries.");
		}
	}

	private static void ReconcileSingleHandPresentation(TrackedPlayerHand hand)
	{
		if (hand == null) return;

		var contract = GetWidgetPresentation(hand.PlayerId);
		bool contractValid = contract != null && !contract.IsDisposed && contract.Visible;

		if (MockHeadButtonResolver != null)
		{
			object mockParent = MockHeadButtonResolver(hand.PlayerId, hand.Slot);
			if (mockParent != null)
			{
				if (!contractValid)
				{
					RegisterPresentationWidget(hand.PlayerId, mockParent, 120f, 65f, 24f, 24f, _uiScale, 1.0f);
				}
				return;
			}
			else
			{
				return;
			}
		}

		UIBattleInfo_Button_PlayerInfo headBtn = null;
		bool headBtnValid = false;

		if (hand.HeadButtonRef != null && hand.HeadButtonRef.TryGetTarget(out var cached) && cached != null && !cached.isDisposed && cached.onStage)
		{
			headBtn = cached;
			var gComp = headBtn.GetChild("__apx_handcard_widget") as GComponent;
			if (gComp != null && !gComp.isDisposed && gComp.visible)
			{
				headBtnValid = true;
			}
		}

		if (contractValid && headBtnValid)
		{
			return;
		}

		headBtn = FindHeadButtonForPlayer(hand.PlayerId, hand.Slot);
		if (headBtn != null && !headBtn.isDisposed && headBtn.onStage)
		{
			hand.HeadButtonRef = new WeakReference<UIBattleInfo_Button_PlayerInfo>(headBtn);
			AttachOrUpdateWidget(headBtn, hand);
		}
	}

	static HandCardDisplayManager()
	{
		XingShu.Spectator.SpectatorStateStore.OnPlayerHandUpdated += OnSpectatorStorePlayerHandUpdated;
		XingShu.Spectator.SpectatorStateStore.OnSessionReset += OnSpectatorStoreSessionReset;
		XingShu.Spectator.SpectatorStateStore.OnSpectatorJoined += OnSpectatorJoined;
		XingShu.Spectator.SpectatorStateStore.OnPlayerRosterUpdated += OnSpectatorStoreRosterUpdated;
	}

	public static IReadOnlyDictionary<long, TrackedPlayerHand> PlayerHands
	{
		get
		{
			if (XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession())
			{
				var allStoreHands = XingShu.Spectator.SpectatorStateStore.GetAllPlayerHands();
				List<long> stalePids = null;
				foreach (var kvp in _playerHands)
				{
					if (!allStoreHands.ContainsKey(kvp.Key))
					{
						if (stalePids == null) stalePids = new List<long>();
						stalePids.Add(kvp.Key);
					}
				}
				if (stalePids != null)
				{
					foreach (var stalePid in stalePids)
					{
						_playerHands.Remove(stalePid);
					}
				}
				foreach (var kvp in allStoreHands)
				{
					if (!_playerHands.TryGetValue(kvp.Key, out var hand) || hand == null)
					{
						hand = GetOrCreateHand(kvp.Key);
						ApplyCardIdsToHand(hand, kvp.Value);
					}
				}
				var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();
				lock (_widgetLock)
				{
					List<long> stale = null;
					foreach (var kvp in _presentationWidgets)
					{
						if (!validPids.Contains(kvp.Key))
						{
							if (stale == null) stale = new List<long>();
							stale.Add(kvp.Key);
						}
					}
					if (stale != null)
					{
						foreach (var pid in stale)
						{
							_presentationWidgets[pid].Visible = false;
							_presentationWidgets[pid].IsDisposed = true;
							_presentationWidgets.Remove(pid);
						}
					}
				}
				var dict = new Dictionary<long, TrackedPlayerHand>();
				foreach (var kvp in _playerHands)
				{
					if (validPids.Contains(kvp.Key))
					{
						dict[kvp.Key] = kvp.Value;
					}
				}
				return dict;
			}
			return _playerHands;
		}
	}

	public static HandCardDisplayManager Instance { get; private set; }

	public static bool IsEnabled
	{
		get
		{
			return _isEnabled;
		}
		set
		{
			if (_isEnabled != value)
			{
				_isEnabled = value;
				try
				{
					APXConfigService.SetBool(APXConfigKeys.HandCardEnabled, _isEnabled);
					APXConfigService.Save();
				}
				catch { }
				Log("SET_ENABLED", $"实时手牌悬浮窗开关切换: IsEnabled={_isEnabled}");
				if (!_isEnabled)
				{
					ClearAllWidgets();
				}
				else
				{
					MarkOverlayDirty("ToggleON");
					RefreshAllWidgets();
					if (_needFetchFromServer && HasAnyTeammateActiveDisplay())
					{
						TriggerFetchCards("EnableToggle: Active Display & Pending Fetch");
					}
				}
			}
		}
	}

	public static bool SpectatorAutoOpen
	{
		get
		{
			return _spectatorAutoOpen;
		}
		set
		{
			if (_spectatorAutoOpen != value)
			{
				_spectatorAutoOpen = value;
				try
				{
					APXConfigService.SetBool(APXConfigKeys.HandCardSpectatorAutoOpen, _spectatorAutoOpen);
					APXConfigService.Save();
				}
				catch { }
				Log("SET_AUTO_OPEN", $"进入观战自动开启偏好切换: SpectatorAutoOpen={_spectatorAutoOpen}");
			}
		}
	}

	public static float UIScale
	{
		get
		{
			return _uiScale;
		}
		set
		{
			float clamped = SafeClamp(value, APXConfigKeys.HandCardUIScaleMin, APXConfigKeys.HandCardUIScaleMax);
			if (Math.Abs(_uiScale - clamped) > 0.01f)
			{
				_uiScale = clamped;
				try
				{
					APXConfigService.SetFloat(APXConfigKeys.HandCardUIScale, _uiScale);
					APXConfigService.Save();
				}
				catch { }
				RefreshAllWidgets();
			}
		}
	}

	public static float CardScale
	{
		get
		{
			return _cardScale;
		}
		set
		{
			_cardScale = SafeClamp(value, APXConfigKeys.HandCardScaleMin, APXConfigKeys.HandCardScaleMax);
			try
			{
				APXConfigService.SetFloat(APXConfigKeys.HandCardScale, _cardScale);
				APXConfigService.Save();
			}
			catch { }
			RefreshAllWidgets();
		}
	}

	public static float PlayBtnSize
	{
		get
		{
			return GetEffectivePlayBtnSize();
		}
		set
		{
			float num = SafeClamp(value, APXConfigKeys.HandCardBtnSizeMin, APXConfigKeys.HandCardBtnSizeMax);
			_btnSizeInitialized = true;
			if (Math.Abs(_playBtnSize - num) > 0.1f)
			{
				_playBtnSize = num;
				try
				{
					APXConfigService.SetFloat(APXConfigKeys.HandCardBtnSize, _playBtnSize);
					APXConfigService.Save();
				}
				catch { }
				RefreshAllWidgets();
			}
		}
	}

	public static bool NeedFetchFromServer => _needFetchFromServer;

	public static bool HasAnyTeammateActiveDisplay()
	{
		foreach (KeyValuePair<long, TrackedPlayerHand> playerHand in _playerHands)
		{
			TrackedPlayerHand value = playerHand.Value;
			if (value != null)
			{
				bool flag = false;
				try
				{
					flag = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(value.PlayerId);
				}
				catch
				{
				}
				if (!flag && (value.IsPinned || value.IsHovered))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<HandCardDisplayManager>() == null)
		{
			host.AddComponent<HandCardDisplayManager>();
			Log("INSTALL", "HandCardDisplayManager 局内手牌插件已挂载 (服务端驱动版)");
		}
		try
		{
			AssistBall.RegisterAction("hand_card_display", "局内手牌监视与设置", ToggleWindow, 25, "服务端实时同步真实手牌，支持卡牌缩放、防抖探测与总开关切换", "\ud83c\udfb4", () => IsEnabled && (XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession() || IsPVEMode()));
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[APX AssistBall] 注册手牌显示插件异常: " + ex.Message);
		}
	}

	private void Awake()
	{
		APXOverlayInput.Register(this, () => _showDebugUI, () => APXOverlayInput.ScaledRect(_debugWinRect, APXMobileUIHelper.ScaleFactor));
		if (Instance == null)
		{
			Instance = this;
		}
		UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
		_isEnabled = APXConfigService.GetBool(APXConfigKeys.HandCardEnabled, APXConfigKeys.HandCardEnabledDefault);
		_spectatorAutoOpen = APXConfigService.GetBool(APXConfigKeys.HandCardSpectatorAutoOpen, APXConfigKeys.HandCardSpectatorAutoOpenDefault);
		_uiScale = Mathf.Clamp(APXConfigService.GetFloat(APXConfigKeys.HandCardUIScale, APXConfigKeys.HandCardUIScaleDefault), APXConfigKeys.HandCardUIScaleMin, APXConfigKeys.HandCardUIScaleMax);
		float defaultScale = APXMobileUIHelper.IsMobile ? APXConfigKeys.HandCardScaleMobileDefault : APXConfigKeys.HandCardScaleDesktopDefault;
		_cardScale = Mathf.Clamp(APXConfigService.GetFloat(APXConfigKeys.HandCardScale, defaultScale), APXConfigKeys.HandCardScaleMin, APXConfigKeys.HandCardScaleMax);
		GetEffectivePlayBtnSize();

		// Ensure single subscription to canonical SpectatorStateStore events
		XingShu.Spectator.SpectatorStateStore.OnPlayerHandUpdated -= OnSpectatorStorePlayerHandUpdated;
		XingShu.Spectator.SpectatorStateStore.OnSessionReset -= OnSpectatorStoreSessionReset;
		XingShu.Spectator.SpectatorStateStore.OnSpectatorJoined -= OnSpectatorJoined;
		XingShu.Spectator.SpectatorStateStore.OnPlayerRosterUpdated -= OnSpectatorStoreRosterUpdated;
		XingShu.Spectator.SpectatorStateStore.OnPlayerHandUpdated += OnSpectatorStorePlayerHandUpdated;
		XingShu.Spectator.SpectatorStateStore.OnSessionReset += OnSpectatorStoreSessionReset;
		XingShu.Spectator.SpectatorStateStore.OnSpectatorJoined += OnSpectatorJoined;
		XingShu.Spectator.SpectatorStateStore.OnPlayerRosterUpdated += OnSpectatorStoreRosterUpdated;

		try
		{
			APXSceneWatcher.SceneLoaded -= OnSceneLoaded;
			APXSceneWatcher.SceneLoaded += OnSceneLoaded;
		}
		catch { }

		MarkOverlayDirty("Awake");

		try
		{
			_logFilePath = Path.Combine(Application.persistentDataPath, "apx_hand_cards.log");
			File.WriteAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INIT] HandCardDisplayManager 初始化\n");
		}
		catch
		{
		}
	}

	private void OnDestroy()
	{
		XingShu.Spectator.SpectatorStateStore.OnPlayerHandUpdated -= OnSpectatorStorePlayerHandUpdated;
		XingShu.Spectator.SpectatorStateStore.OnSessionReset -= OnSpectatorStoreSessionReset;
		XingShu.Spectator.SpectatorStateStore.OnSpectatorJoined -= OnSpectatorJoined;
		XingShu.Spectator.SpectatorStateStore.OnPlayerRosterUpdated -= OnSpectatorStoreRosterUpdated;
		try
		{
			APXSceneWatcher.SceneLoaded -= OnSceneLoaded;
		}
		catch { }
	}

	private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
	{
		try
		{
			MarkOverlayDirty($"SceneLoaded:{scene.name}");
		}
		catch { }
	}

	private void Update()
	{
		if (APXCentralInputManager.TryConsumeHotkeyChord(APXHotkeyAction.HandCardDebug, "HandCardDisplayManager", KeyCode.H, APXHotkeyModifiers.Ctrl | APXHotkeyModifiers.Shift, exactModifiers: true, allowDuringTextEntry: false))
		{
			if (UseModernView)
			{
				APXRuntime.UI.APXUICenter.ToggleHandCardPage();
				return;
			}
			_showDebugUI = !_showDebugUI;
		}
		try
		{
			RoomInfo roomInfo = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
			bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();

			if (!inSpectator && (XingShu.Truth.HandSessionLifecycle.ActiveRoomId() <= 0 || roomInfo == null || roomInfo.Id <= 0 || !IsPVEMode()))
			{
				if (_playerHands.Count > 0 || _needFetchFromServer || _firstRoundFetchedRoomId > 0 || _roundListenerRegistered || _lastActiveRoomId > 0)
				{
					ClearAllWidgets();
					_playerHands.Clear();
					_needFetchFromServer = false;
					_firstRoundFetchedRoomId = 0L;
					_lastActiveRoomId = 0L;
					RemoveRoundListener();
					XingShu.Truth.HandTruthSource.ResetSession("ExitBattle");
					Log("CLEAR", "已退出战局，清空手牌数据缓存、待拉取标记与第一回合防重记录");
				}
				return;
			}

			if (inSpectator)
			{
				int currentRoomId = XingShu.Spectator.SpectatorStateStore.RoomId;
				long currentEpoch = XingShu.Spectator.SpectatorStateStore.SpectatorSessionEpoch;

				if (_lastSpectatorRoomId != currentRoomId || _lastSpectatorEpoch != currentEpoch)
				{
					_lastSpectatorRoomId = currentRoomId;
					_lastSpectatorEpoch = currentEpoch;
					ClearAllWidgets();
					_playerHands.Clear();
					ResetPresentationWidgets();
					MarkOverlayDirty($"SpectatorSessionTransition:Room={currentRoomId},Epoch={currentEpoch}");
					RefreshAllWidgets();
					Log("SPECTATOR_EPOCH_OR_ROOM_RESET", $"观战房间或 Epoch 变迁 (Room={currentRoomId}, Epoch={currentEpoch})，清空旧手牌缓存");

					if (SpectatorAutoOpen && !_isEnabled && currentRoomId > 0)
					{
						IsEnabled = true;
						Log("SPECTATOR_AUTO_ENABLE_POLL", "观战状态变迁检测: 自动启用实时手牌悬浮窗");
					}
				}

				// Lifecycle Re-arm check: Battle UI container appearance & rebuild
				bool curBattleUiReady = false;
				GComponent curContainer = null;
				try
				{
					var battleInfo = SimpleSingletonProvider<GameLogicManager>.inst?.battle?.battleInfo;
					curContainer = battleInfo?.UIObject?.com_BattlePlayer?.com_Container;
					curBattleUiReady = curContainer != null && !curContainer.isDisposed && curContainer.onStage;
				}
				catch { }

				if (curBattleUiReady != _lastBattleUiReady || (curContainer != null && curContainer != _lastContainerInstance))
				{
					_lastBattleUiReady = curBattleUiReady;
					_lastContainerInstance = curContainer;
					if (curBattleUiReady)
					{
						MarkOverlayDirty("BattleUIAppeared:ContainerReady");
						RefreshAllWidgets();
					}
				}

				// Lifecycle Re-arm check: Battle start transition
				bool curBattleRunning = false;
				try
				{
					curBattleRunning = IsBattleRunning(roomInfo);
				}
				catch { }

				if (curBattleRunning != _lastBattleRunning)
				{
					_lastBattleRunning = curBattleRunning;
					if (curBattleRunning)
					{
						MarkOverlayDirty("BattleStart");
						RefreshAllWidgets();
					}
				}

				// Lifecycle Re-arm check: Player roster count change
				var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();
				int curRosterCount = validPids != null ? validPids.Count : 0;
				if (_lastKnownRosterCount != curRosterCount)
				{
					_lastKnownRosterCount = curRosterCount;
					MarkOverlayDirty($"PlayerRosterCountChanged:{curRosterCount}");
					RefreshAllWidgets();
				}

				var allStoreHands = XingShu.Spectator.SpectatorStateStore.GetAllPlayerHands();

				// Stale Player Defense: remove any player from _playerHands that is NOT in current room store hands
				List<long> stalePids = null;
				foreach (var kvp in _playerHands)
				{
					if (!allStoreHands.ContainsKey(kvp.Key))
					{
						if (stalePids == null) stalePids = new List<long>();
						stalePids.Add(kvp.Key);
					}
				}
				if (stalePids != null)
				{
					foreach (var spid in stalePids)
					{
						if (_playerHands.TryGetValue(spid, out var staleHand) && staleHand?.HeadButtonRef != null && staleHand.HeadButtonRef.TryGetTarget(out var target) && target != null && !target.isDisposed)
						{
							if (target.GetChild("__apx_handcard_widget") is GComponent staleWidget && !staleWidget.isDisposed)
							{
								staleWidget.visible = false;
								HideHoverBar(staleWidget);
								if (staleWidget.GetChild("card_list") is GComponent staleCardList && !staleCardList.isDisposed)
								{
									staleCardList.RemoveChildren(0, -1, dispose: true);
								}
							}
						}
						_playerHands.Remove(spid);
					}
					MarkOverlayDirty("StalePlayersRemoved");
				}

				foreach (var kvp in allStoreHands)
				{
					long pid = kvp.Key;
					var cardIds = kvp.Value;
					TrackedPlayerHand hand = GetOrCreateHand(pid);
					PopulatePlayerMetadataIfMissing(hand, pid);
					if (hand.SyncState != HandSyncState.Latest || !CardListsMatch(hand.Cards, cardIds))
					{
						ApplyCardIdsToHand(hand, cardIds);
						MarkOverlayDirty($"CardsUpdated:Player={pid}");
						RefreshHandWidget(hand);
					}
				}

				PerformReconciliationTick();
				return;
			}

			if (_lastActiveRoomId != roomInfo.Id)
			{
				_lastActiveRoomId = roomInfo.Id;
				ClearAllWidgets();
				_playerHands.Clear();
				_needFetchFromServer = false;
				_firstRoundFetchedRoomId = 0L;
				RemoveRoundListener();
				XingShu.Truth.HandTruthSource.ResetSession($"PVERoomChanged:Room={roomInfo.Id}");
				MarkOverlayDirty($"PVERoomChanged:Room={roomInfo.Id}");
				Log("ROOM_CHANGED", $"检测到进入新战局 (RoomID={roomInfo.Id})，重置状态缓存");
			}

			bool pveBattleUiReady = false;
			try
			{
				var battleInfo = SimpleSingletonProvider<GameLogicManager>.inst?.battle?.battleInfo;
				var container = battleInfo?.UIObject?.com_BattlePlayer?.com_Container;
				pveBattleUiReady = container != null && !container.isDisposed && container.onStage;
			}
			catch { }

			if (pveBattleUiReady != _lastBattleUiReady)
			{
				_lastBattleUiReady = pveBattleUiReady;
				if (pveBattleUiReady)
				{
					MarkOverlayDirty("PVEBattleUIAppeared:ContainerReady");
					RefreshAllWidgets();
				}
			}

			SyncBattlePlayers(roomInfo);
			if (roomInfo.Round == 1 && _firstRoundFetchedRoomId != roomInfo.Id && IsBattleRunning(roomInfo))
			{
				EnsureRoundListener();
				CheckAndFetchFirstRound("UpdateLoop: Round=1");
			}

			PerformReconciliationTick();
		}
		catch
		{
		}
	}

	public static bool IsPVEMode()
	{
		try
		{
			return CheckPVEModeInternal();
		}
		catch
		{
			return false;
		}
	}

	private static bool CheckPVEModeInternal()
	{
		try
		{
			RoomInfo roomInfo = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
			if (roomInfo != null && roomInfo.Id > 0)
			{
				try
				{
					if (roomInfo.IsPVE())
					{
						return true;
					}
					if (roomInfo.IsPVP())
					{
						return false;
					}
				}
				catch
				{
				}
				try
				{
					if (BattleConfig.IsPVE(roomInfo.MapType))
					{
						return true;
					}
					if (BattleConfig.IsPVP(roomInfo.MapType))
					{
						return false;
					}
				}
				catch
				{
				}
				switch (roomInfo.MapType)
				{
				case 4:
				case 6:
				case 9:
				case 10:
				case 12:
					return true;
				case 1:
				case 3:
				case 5:
				case 8:
					return false;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public static bool IsBattleRunning(RoomInfo curRoom)
	{
		if (curRoom == null || curRoom.Id <= 0)
		{
			return false;
		}
		try
		{
			if (curRoom.State == Room.Types.State.Running)
			{
				return true;
			}
			if (curRoom.info != null && curRoom.info.State == Room.Types.State.Running)
			{
				return true;
			}
			BattleLogic battleLogic = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
			if (battleLogic != null && battleLogic.PlayerDatas != null && battleLogic.PlayerDatas.Count > 0)
			{
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	public static void ToggleWindow()
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
			APXRuntime.UI.APXUICenter.OpenHandCardPage();
			return;
		}

		if (Instance != null)
		{
			Instance._showDebugUI = !Instance._showDebugUI;
		}
	}

	private static void EnsureRoundListener()
	{
		if (_roundListenerRegistered)
		{
			return;
		}
		try
		{
			RoomLogic roomLogic = SimpleSingletonProvider<GameLogicManager>.inst?.room;
			if (roomLogic != null && roomLogic.signal != null && roomLogic.signal.roundChange != null)
			{
				roomLogic.signal.roundChange.RemoveListener(OnRoundChanged);
				roomLogic.signal.roundChange.AddListener(OnRoundChanged);
				_roundListenerRegistered = true;
				Log("LISTENER", "已成功注册 room.signal.roundChange 官方回合监听");
			}
		}
		catch (Exception ex)
		{
			Log("LISTENER_ERR", "注册官方回合监听异常: " + ex.Message);
		}
	}

	private static void RemoveRoundListener()
	{
		if (!_roundListenerRegistered)
		{
			return;
		}
		try
		{
			RoomLogic roomLogic = SimpleSingletonProvider<GameLogicManager>.inst?.room;
			if (roomLogic != null && roomLogic.signal != null && roomLogic.signal.roundChange != null)
			{
				roomLogic.signal.roundChange.RemoveListener(OnRoundChanged);
				Log("LISTENER_REMOVE", "已安全注销官方回合监听");
			}
		}
		catch
		{
		}
		finally
		{
			_roundListenerRegistered = false;
		}
	}

	private static void OnRoundChanged(int round)
	{
		Log("ROUND_CHANGE", $"收到官方回合变更信号: Round={round}");
		if (round == 1)
		{
			CheckAndFetchFirstRound("RoundChangeSignal: Round=1");
		}
		else if (IsEnabled && _needFetchFromServer && HasAnyTeammateActiveDisplay())
		{
			TriggerFetchCards($"RoundChangeSignal: Round={round}");
		}
	}

	public static void CheckAndFetchFirstRound(string reason)
	{
		if (!IsEnabled || !IsPVEMode())
		{
			return;
		}
		try
		{
			RoomInfo roomInfo = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
			if (roomInfo != null && roomInfo.Id > 0 && IsBattleRunning(roomInfo) && roomInfo.Round == 1 && _firstRoundFetchedRoomId != roomInfo.Id)
			{
				_firstRoundFetchedRoomId = roomInfo.Id;
				Log("FIRST_ROUND_FETCH", $"第一回合激活状态触发手牌拉取: RoomID={roomInfo.Id}, 原因={reason}");
				TriggerFetchCards("FirstRoundActiveFetch: " + reason);
			}
		}
		catch (Exception ex)
		{
			Log("FIRST_ROUND_ERR", "第一回合手牌检测异常: " + ex.Message);
		}
	}

	public static void OnHandCardsChanged(BattlePlayerData playerData, RepeatedField<CardInfo> attrCards)
	{
		if (!IsEnabled || playerData == null || playerData.player == null)
		{
			return;
		}
		bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
		if (!inSpectator && !IsPVEMode())
		{
			return;
		}
		if (attrCards == null && !inSpectator)
		{
			return;
		}
		long id = playerData.player.Id;
		string nick = playerData.player.GetNick();
		TrackedPlayerHand orCreateHand = GetOrCreateHand(id);
		orCreateHand.Slot = playerData.player.Slot;
		orCreateHand.PlayerName = nick;
		try
		{
			orCreateHand.IsBot = playerData.player != null && playerData.player.IsBot;
		}
		catch
		{
		}

		// Spectator Truth Store precedence check:
		if (inSpectator || XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(id, out _))
		{
			if (XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(id, out var spectatorCards))
			{
				ApplyCardIdsToHand(orCreateHand, spectatorCards);
				orCreateHand.SyncState = HandSyncState.Latest;
				Log("SPECTATOR_HAND_CHANGED", $"玩家 {id} ({nick}) 处于观战会话，权威手牌来自 SpectatorStateStore (Count={spectatorCards.Count})，保持最新且不生成掩码卡");
				RefreshHandWidget(orCreateHand);
				return;
			}
		}
		bool flag = false;
		try
		{
			flag = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(id);
		}
		catch
		{
		}
		if (flag)
		{
			orCreateHand.SyncState = HandSyncState.Latest;
			if (!SyncSelfCardsFromContainer(orCreateHand, playerData))
			{
				if (attrCards != null && attrCards.Count > 0 && attrCards[0].CardId > 0)
				{
					UpdateSelfCardsFromAttr(orCreateHand, attrCards);
					Log("SELF_UPDATE", $"己方玩家 {id} 手牌从有效 attrCards 更新，当前手牌数={orCreateHand.Cards.Count}");
				}
				else if (attrCards != null && attrCards.Count == 0)
				{
					orCreateHand.Cards.Clear();
					Log("SELF_EMPTY", $"己方玩家 {id} 手牌清空(0张)");
				}
				else
				{
					Log("SELF_MASK_PRESERVE", $"己方手牌变动通知中携带掩码(CardId<=0)，保留本地已有手牌={orCreateHand.Cards.Count}，拒绝覆盖！");
				}
			}
			else
			{
				Log("SELF_UPDATE", $"己方玩家 {id} 手牌原生更新，当前手牌数={orCreateHand.Cards.Count}");
			}
			RefreshHandWidget(orCreateHand);
			return;
		}
		if (orCreateHand.IsBot)
		{
			string text2 = BuildAttrSignature(attrCards);
			bool signatureChanged = (orCreateHand.LastAttrSignature != text2);
			orCreateHand.LastAttrSignature = text2;
			if (attrCards.Count == 0)
			{
				orCreateHand.Cards.Clear();
				orCreateHand.SyncState = HandSyncState.Latest;
				Log("BOT_EMPTY", $"玩家 {id} ({nick}) Bot 手牌清空(0张)");
				RefreshHandWidget(orCreateHand);
				return;
			}
			bool hasValidServerCards = orCreateHand.Cards.Count > 0 && !orCreateHand.Cards.Exists((TrackedCard c) => c.CardId <= 0);
			if (hasValidServerCards)
			{
				if (signatureChanged || orCreateHand.Cards.Count != attrCards.Count)
				{
					orCreateHand.SyncState = HandSyncState.Pending;
					_needFetchFromServer = true;
					if (IsEnabled && HasAnyTeammateActiveDisplay())
					{
						Log("FETCH_TRIGGER", $"检测到 Bot 手牌变动且已激活手牌显示，请求服务端最新手牌: Bot={id}");
						TriggerFetchCards("OnHandCardsChanged: Bot=" + id + " (Active Display)");
					}
				}
				Log("BOT_ATTR_MASKED", $"UID={id} Slot={orCreateHand.Slot} AttrCount={attrCards.Count} ValidCardIds={orCreateHand.Cards.Count} Action=PreserveServerCards");
				RefreshHandWidget(orCreateHand);
				return;
			}
			orCreateHand.Cards.Clear();
			for (int k = 0; k < attrCards.Count; k++)
			{
				CardInfo cardInfo = attrCards[k];
				if (cardInfo != null)
				{
					orCreateHand.Cards.Add(new TrackedCard
					{
						UniqueId = cardInfo.UniqueId,
						CardId = ((cardInfo.CardId > 0) ? cardInfo.CardId : (-1)),
						BattleCost = cardInfo.BattleCost,
						IsTemp = cardInfo.IsTemp,
						PurifyNum = cardInfo.PurifyNum
					});
				}
			}
			orCreateHand.SyncState = HandSyncState.Pending;
			_needFetchFromServer = true;
			Log("BOT_ATTR_MASKED", $"UID={id} Slot={orCreateHand.Slot} AttrCount={attrCards.Count} ValidCardIds=0 Action=CreateUnknownPlaceholders");
			if (IsEnabled && HasAnyTeammateActiveDisplay())
			{
				Log("FETCH_TRIGGER", $"Bot 尚无服务端手牌，立即请求服务器: Bot={id}");
				TriggerFetchCards("OnHandCardsChanged: Bot=" + id + " (Initial Placeholders)");
			}
			RefreshHandWidget(orCreateHand);
			return;
		}
		string text = BuildAttrSignature(attrCards);
		if (orCreateHand.LastAttrSignature == text)
		{
			Log("TEAMMATE_UNCHANGED", $"队友玩家 {id} ({nick}) 手牌特征与上次一致({text})，无手牌变化，跳过请求服务器");
			return;
		}
		orCreateHand.LastAttrSignature = text;
		if (attrCards.Count == 0)
		{
			orCreateHand.Cards.Clear();
			orCreateHand.SyncState = HandSyncState.Latest;
			Log("TEAMMATE_EMPTY", $"队友玩家 {id} ({nick}) 手牌清空(0张)，直接更新本地最新，无需请求服务器");
			RefreshHandWidget(orCreateHand);
			return;
		}
		orCreateHand.SyncState = HandSyncState.Pending;
		_needFetchFromServer = true;
		RefreshHandWidget(orCreateHand);
		Log("TEAMMATE_CHANGED", $"队友玩家 {id} ({nick}) 手牌发生真实变动 (特征: {text})，已标记待更新(浅黄)");
		if (IsEnabled && HasAnyTeammateActiveDisplay())
		{
			Log("FETCH_TRIGGER", $"检测到有队友播放标识激活且已启用手牌，立即请求服务器手牌: Teammate={id}");
			TriggerFetchCards("OnHandCardsChanged: Teammate=" + id + " (Active Display)");
		}
		else
		{
			Log("FETCH_DEFERRED", "当前未启用手牌或无激活播放标识(全折叠)，暂不请求服务器，等待用户真正需要显示时再按需拉取");
		}
	}

	private static string BuildAttrSignature(RepeatedField<CardInfo> attrCards)
	{
		if (attrCards == null)
		{
			return "null";
		}
		if (attrCards.Count == 0)
		{
			return "0:";
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(attrCards.Count).Append(':');
		for (int i = 0; i < attrCards.Count; i++)
		{
			CardInfo cardInfo = attrCards[i];
			if (cardInfo != null)
			{
				stringBuilder.Append(cardInfo.UniqueId).Append(',');
			}
		}
		return stringBuilder.ToString();
	}

	public static void OnPlayerInfoInit(UIBattleInfo_Button_PlayerInfo headBtn)
	{
		MarkOverlayDirty("OnPlayerInfoInit");
		if (headBtn == null || headBtn.isDisposed || headBtn.PlayerData == null || headBtn.PlayerData.player == null)
		{
			return;
		}
		bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
		if (!inSpectator && !IsPVEMode())
		{
			return;
		}
		long id = headBtn.PlayerData.player.Id;
		TrackedPlayerHand orCreateHand = GetOrCreateHand(id);
		orCreateHand.Slot = headBtn.PlayerData.player.Slot;
		orCreateHand.PlayerName = headBtn.PlayerData.player.GetNick();
		try
		{
			orCreateHand.IsBot = headBtn.PlayerData.player != null && headBtn.PlayerData.player.IsBot;
		}
		catch
		{
		}
		orCreateHand.HeadButtonRef = new WeakReference<UIBattleInfo_Button_PlayerInfo>(headBtn);

		if (!IsEnabled)
		{
			// 实时手牌悬浮窗已关闭：保留头像按钮弱引用供 F1 手牌监控或稍后开启，不挂载悬浮控件
			return;
		}

		// Spectator Truth Store precedence check:
		if (inSpectator || XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(id, out _))
		{
			if (XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(id, out var spectatorCards))
			{
				ApplyCardIdsToHand(orCreateHand, spectatorCards);
				orCreateHand.SyncState = HandSyncState.Latest;
				Log("SPECTATOR_INFO_INIT", $"观战头像初始化: 玩家 {id} 从 SpectatorStateStore 权威加载手牌 (Count={spectatorCards.Count})");
				AttachOrUpdateWidget(headBtn, orCreateHand);
				return;
			}
		}
			bool flag = false;
			try
			{
				flag = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(id);
			}
			catch
			{
			}
			if (flag)
			{
				orCreateHand.SyncState = HandSyncState.Latest;
			}
			else if (orCreateHand.IsBot)
			{
				if (orCreateHand.Cards.Count > 0 && !orCreateHand.Cards.Exists((TrackedCard c) => c.CardId <= 0))
				{
					orCreateHand.SyncState = HandSyncState.Latest;
				}
				else
				{
					orCreateHand.SyncState = HandSyncState.Pending;
				}
			}
			if (flag)
			{
				SyncSelfCardsFromContainer(orCreateHand, headBtn.PlayerData);
			}
			else
			{
				if (orCreateHand.SyncState != HandSyncState.Latest)
				{
					int cardCount = (headBtn.PlayerData?.cardContainer != null) ? headBtn.PlayerData.cardContainer.CardCount : 0;
					EnsureUnknownCards(orCreateHand, cardCount);
				}
			}
			AttachOrUpdateWidget(headBtn, orCreateHand);
			EnsureRoundListener();
			CheckAndFetchFirstRound("OnPlayerInfoInit: Player=" + id);
	}

	public static void EnsureUnknownCards(TrackedPlayerHand hand, int cardCount)
	{
		if (hand == null || hand.Cards == null)
		{
			return;
		}
		if (hand.SyncState == HandSyncState.Latest)
		{
			return;
		}
		if (XingShu.Truth.HandTruthSource.TryGetPlayerHand(hand.PlayerId, out var truthCards) && truthCards != null && truthCards.Count > 0)
		{
			ApplyCardIdsToHand(hand, truthCards);
			hand.SyncState = HandSyncState.Latest;
			hand.IsAuthoritative = true;
			hand.MaskedHandCount = truthCards.Count;
			return;
		}
		if ((XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession() || XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(hand.PlayerId, out _)) &&
			XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(hand.PlayerId, out var storeCards) && storeCards != null && storeCards.Count > 0)
		{
			ApplyCardIdsToHand(hand, storeCards);
			hand.SyncState = HandSyncState.Latest;
			hand.IsAuthoritative = true;
			hand.MaskedHandCount = storeCards.Count;
			return;
		}

		// HS4-P1.6R2 Decision B: DO NOT FABRICATE fake unknown cards (CardId = -1)!
		// Truthfully record the masked hand count while keeping hand cards list empty.
		hand.Cards.Clear();
		hand.MaskedHandCount = cardCount;
		hand.IsAuthoritative = false;
		hand.SyncState = HandSyncState.Pending;
	}

	internal static void SyncBattlePlayers(RoomInfo roomInfo)
	{
		if (XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession() && !IsPVEMode())
		{
			return; // Pure spectator session is handled authoritatively by SpectatorStateStore
		}
		if (roomInfo == null || roomInfo.Id <= 0 || !IsPVEMode() || !IsBattleRunning(roomInfo))
		{
			return;
		}
		try
		{
			BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
			if (battle == null || battle.PlayerDatas == null)
			{
				return;
			}

			// 1. Collect authoritative active room hero IDs
			HashSet<long> activeHeroIds = new HashSet<long>();
			if (roomInfo.Players != null && roomInfo.Players.Count > 0)
			{
				foreach (var p in roomInfo.Players)
				{
					if (p != null && p.Id > 0) activeHeroIds.Add(p.Id);
				}
			}
			if (activeHeroIds.Count == 0)
			{
				foreach (BattlePlayerData bpd in battle.PlayerDatas)
				{
					if (bpd?.player != null && bpd.player.Id > 0 && bpd.characterType == CharacterType.Hero)
					{
						activeHeroIds.Add(bpd.player.Id);
					}
				}
			}

			// 2. Strict Invariant: Purge any non-room player / ghost / monster rows from _playerHands
			if (activeHeroIds.Count > 0)
			{
				List<long> staleKeys = null;
				foreach (var kvp in _playerHands)
				{
					if (!activeHeroIds.Contains(kvp.Key))
					{
						if (staleKeys == null) staleKeys = new List<long>();
						staleKeys.Add(kvp.Key);
					}
				}
				if (staleKeys != null)
				{
					foreach (var k in staleKeys)
					{
						_playerHands.Remove(k);
						Log("PURGE_GHOST", $"已清洗旧房间/非房间实体手牌行: PlayerId={k}");
					}
					MarkOverlayDirty("PurgedGhostRows");
				}
			}

			AccountLogic account = SimpleSingletonProvider<GameLogicManager>.inst?.account;
			foreach (BattlePlayerData playerData in battle.PlayerDatas)
			{
				if (playerData == null || playerData.player == null)
				{
					continue;
				}

				// CRITICAL DEFECT FIX: Strictly filter out non-Hero entities (Monsters, Bosses, Event triggers)
				if (playerData.characterType != CharacterType.Hero)
				{
					continue;
				}

				long id = playerData.player.Id;
				if (activeHeroIds.Count > 0 && !activeHeroIds.Contains(id))
				{
					continue;
				}

				TrackedPlayerHand orCreateHand = GetOrCreateHand(id);
				orCreateHand.Slot = playerData.player.Slot;
				orCreateHand.PlayerName = playerData.player.GetNick();
				try
				{
					orCreateHand.IsBot = playerData.player.IsBot;
				}
				catch
				{
				}

				bool isSelf = (account != null && account.IsSelf(id));
				if (isSelf)
				{
					orCreateHand.SyncState = HandSyncState.Latest;
					orCreateHand.IsAuthoritative = true;
					SyncSelfCardsFromContainer(orCreateHand, playerData);
					orCreateHand.MaskedHandCount = orCreateHand.Cards.Count;
				}
				else
				{
					// DECISION A: Check if local cardContainer._HandCards contains valid CardId > 0 for all cards
					bool decisionASucceeded = false;
					CardContainer container = playerData.cardContainer;
					if (container != null && container._HandCards != null && container._HandCards.Count > 0)
					{
						bool allValid = true;
						for (int i = 0; i < container._HandCards.Count; i++)
						{
							var hc = container._HandCards[i];
							int cid = hc != null ? ((hc.CardId > 0) ? hc.CardId : (hc.Config != null ? hc.Config.Id : 0)) : 0;
							if (cid <= 0)
							{
								allValid = false;
								break;
							}
						}
						if (allValid)
						{
							decisionASucceeded = true;
							orCreateHand.Cards.Clear();
							foreach (var hc in container._HandCards)
							{
								int cid = (hc.CardId > 0) ? hc.CardId : (hc.Config != null ? hc.Config.Id : 0);
								orCreateHand.Cards.Add(new TrackedCard
								{
									UniqueId = hc.Guid,
									CardId = cid,
									BattleCost = hc.BattleCost,
									IsTemp = hc.IsTemp,
									PurifyNum = hc.PurifyNum
								});
							}
							orCreateHand.SyncState = HandSyncState.Latest;
							orCreateHand.IsAuthoritative = true;
							orCreateHand.MaskedHandCount = orCreateHand.Cards.Count;
							Log("DECISION_A_SYNC", $"Bot 玩家 {id} 从本地 cardContainer 权威加载手牌 (Count={orCreateHand.Cards.Count})");
						}
					}

					// DECISION B: Remote cards are masked in player client -> DO NOT FABRICATE fake cards!
					if (!decisionASucceeded)
					{
						if (XingShu.Truth.HandTruthSource.TryGetPlayerHand(id, out var truthCards) && truthCards != null && truthCards.Count > 0)
						{
							ApplyCardIdsToHand(orCreateHand, truthCards);
							orCreateHand.SyncState = HandSyncState.Latest;
							orCreateHand.IsAuthoritative = true;
							orCreateHand.MaskedHandCount = truthCards.Count;
							Log("DECISION_B_TRUTH_SYNC", $"Bot 玩家 {id} 从 HandTruthSource 获取权威手牌 (Count={truthCards.Count})");
						}
						else if (XingShu.Spectator.SpectatorStateStore.TryGetPlayerHand(id, out var storeCards) && storeCards != null && storeCards.Count > 0)
						{
							ApplyCardIdsToHand(orCreateHand, storeCards);
							orCreateHand.SyncState = HandSyncState.Latest;
							orCreateHand.IsAuthoritative = true;
							orCreateHand.MaskedHandCount = storeCards.Count;
							Log("DECISION_B_STORE_SYNC", $"Bot 玩家 {id} 从 SpectatorStateStore 获取权威手牌 (Count={storeCards.Count})");
						}
						else
						{
							// Cards are masked and spectator stream is absent
							int heldCount = (container != null) ? container.CardCount : 0;
							orCreateHand.Cards.Clear();
							orCreateHand.MaskedHandCount = heldCount;
							orCreateHand.IsAuthoritative = false;
							orCreateHand.SyncState = HandSyncState.Pending;
						}
					}
				}
			}

			// Synchronize into HandTruthSource
			XingShu.Truth.HandTruthSource.SyncFromLocalPve(roomInfo, battle, account, _playerHands);
		}
		catch (Exception ex)
		{
			Log("SYNC_PLAYERS_ERR", "SyncBattlePlayers 异常: " + ex.Message);
		}
	}

	private static bool SyncSelfCardsFromContainer(TrackedPlayerHand hand, BattlePlayerData playerData)
	{
		if (hand == null)
		{
			return false;
		}
		try
		{
			CardContainer cardContainer = playerData?.cardContainer ?? SimpleSingletonProvider<GameLogicManager>.inst?.battle?.GetSelfPlayerData()?.cardContainer;
			if (cardContainer != null && cardContainer._HandCards != null && cardContainer._HandCards.Count > 0)
			{
				bool flag = false;
				for (int i = 0; i < cardContainer._HandCards.Count; i++)
				{
					if (cardContainer._HandCards[i] != null && cardContainer._HandCards[i].CardId > 0)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					hand.Cards.Clear();
					foreach (HandCardData handCard in cardContainer._HandCards)
					{
						if (handCard != null)
						{
							int cardId = ((handCard.CardId > 0) ? handCard.CardId : ((handCard.Config != null) ? handCard.Config.Id : handCard.CardId));
							hand.Cards.Add(new TrackedCard
							{
								UniqueId = handCard.Guid,
								CardId = cardId,
								BattleCost = handCard.BattleCost,
								IsTemp = handCard.IsTemp,
								PurifyNum = handCard.PurifyNum
							});
						}
					}
					Log("SELF_CONTAINER_SYNC", $"己方手牌已从官方 cardContainer 权威同步，卡牌数={hand.Cards.Count}");
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			Log("SELF_CONTAINER_ERR", "从 cardContainer 同步己方手牌异常: " + ex.Message);
		}
		return false;
	}

	private static void UpdateSelfCardsFromAttr(TrackedPlayerHand hand, RepeatedField<CardInfo> attrCards)
	{
		if (hand == null || attrCards == null)
		{
			return;
		}
		bool flag = false;
		for (int i = 0; i < attrCards.Count; i++)
		{
			if (attrCards[i] != null && attrCards[i].CardId <= 0)
			{
				flag = true;
				break;
			}
		}
		if (flag && hand.Cards.Count > 0)
		{
			Log("SELF_ATTR_MASKED", $"己方 attrCards 携带掩码卡牌(CardId<=0)，跳过直接覆盖，保留本地已有手牌={hand.Cards.Count}");
			return;
		}
		hand.Cards.Clear();
		for (int j = 0; j < attrCards.Count; j++)
		{
			CardInfo cardInfo = attrCards[j];
			if (cardInfo != null)
			{
				hand.Cards.Add(new TrackedCard
				{
					UniqueId = cardInfo.UniqueId,
					CardId = cardInfo.CardId,
					BattleCost = cardInfo.BattleCost,
					IsTemp = cardInfo.IsTemp,
					PurifyNum = cardInfo.PurifyNum
				});
			}
		}
	}

	public static void TriggerFetchCards(string reason)
	{
		// RC1 Note: Remote hand-card network sync is disabled for public release.
		// Player discovery and local authoritative hand tracking remain operational.
		return;
	}

	private IEnumerator FetchRoomCardsCoroutine(long roomId, int roomServerId, string triggerReason)
	{
		yield break;
	}

	private static void ApplyServerResponse(string responseText)
	{
		try
		{
			_lastServerResponseText = responseText;
			Log("SERVER_RAW", "服务端原始响应内容: " + responseText);
			MatchCollection matchCollection = Regex.Matches(responseText, "\"(\\d+)\"\\s*:\\s*\\[");
			if (matchCollection.Count == 0)
			{
				Log("PARSE_WARN", "未能从服务端响应中匹配到玩家键名 (格式不匹配)");
				return;
			}
			int num = 0;
			for (int i = 0; i < matchCollection.Count; i++)
			{
				Match match = matchCollection[i];
				if (!long.TryParse(match.Groups[1].Value, out var result) || result <= 0)
				{
					continue;
				}
				int num2 = match.Index + match.Length;
				int num3 = 1;
				int num4 = -1;
				for (int j = num2; j < responseText.Length; j++)
				{
					switch (responseText[j])
					{
					case '[':
						num3++;
						continue;
					case ']':
						num3--;
						if (num3 != 0)
						{
							continue;
						}
						break;
					default:
						continue;
					}
					num4 = j;
					break;
				}
				if (num4 <= num2)
				{
					continue;
				}
				string input = responseText.Substring(num2, num4 - num2);
				bool flag = false;
				try
				{
					flag = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(result);
				}
				catch
				{
				}
				TrackedPlayerHand orCreateHand = GetOrCreateHand(result);
				MatchCollection matchCollection2 = Regex.Matches(input, "\\[\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\]");
				if (flag)
				{
					if (orCreateHand.Cards.Count != 0 && !orCreateHand.Cards.Exists((TrackedCard c) => c.CardId <= 0) && orCreateHand.Cards.Count == matchCollection2.Count)
					{
						continue;
					}
					Log("SELF_SERVER_REPAIR", $"利用服务端权威数据修复己方手牌 (原手牌数={orCreateHand.Cards.Count}, 修复后={matchCollection2.Count})");
				}
				orCreateHand.Cards.Clear();
				StringBuilder stringBuilder = new StringBuilder();
				foreach (Match item in matchCollection2)
				{
					if (int.TryParse(item.Groups[1].Value, out var result2) && result2 > 0)
					{
						int result3 = 0;
						if (item.Groups.Count > 2)
						{
							int.TryParse(item.Groups[2].Value, out result3);
						}
						orCreateHand.Cards.Add(new TrackedCard
						{
							UniqueId = -1,
							CardId = result2,
							BattleCost = result3
						});
						stringBuilder.Append($"[{result2}:{result3}] ");
					}
				}
				orCreateHand.SyncState = HandSyncState.Latest;
				num++;
				if (orCreateHand.IsBot)
				{
					List<int> list = new List<int>();
					foreach (TrackedCard card in orCreateHand.Cards)
					{
						list.Add(card.CardId);
					}
					Log("BOT_SERVER_APPLY", $"UID={result} Slot={orCreateHand.Slot} Cards=[{string.Join(",", list)}] Source=RoomCardsServer Result=Applied");
				}
				Log("APPLY_PLAYER", $"玩家 {result} ({orCreateHand.PlayerName}) 成功解析 {orCreateHand.Cards.Count} 张手牌: {stringBuilder}");
			}
			Log("APPLY_SERVER", $"已成功应用服务端真实手牌，共更新 {num} 名玩家");
			RefreshAllWidgets();
		}
		catch (Exception ex)
		{
			Log("PARSE_ERR", "解析服务端手牌响应异常: " + ex);
		}
	}

	public static TrackedPlayerHand GetOrCreateHand(long playerId)
	{
		if (!_playerHands.TryGetValue(playerId, out var value))
		{
			value = new TrackedPlayerHand
			{
				PlayerId = playerId
			};
			_playerHands[playerId] = value;
		}
		return value;
	}

	internal static bool TryGetAuthoritativeRemoteHand(long expectedRoomId, long playerId, out List<TrackedCard> cards, out HandSyncState syncState)
	{
		cards = null;
		syncState = HandSyncState.Pending;
		if (_lastActiveRoomId <= 0 || _lastActiveRoomId != expectedRoomId)
		{
			return false;
		}
		if (_playerHands.TryGetValue(playerId, out TrackedPlayerHand hand) && hand != null)
		{
			syncState = hand.SyncState;
			if (hand.SyncState == HandSyncState.Latest && hand.Cards != null && hand.Cards.Count > 0)
			{
				cards = new List<TrackedCard>(hand.Cards);
				return true;
			}
		}
		return false;
	}

	private static bool IsInGameEnvironment()
	{
		try
		{
			return Instance != null;
		}
		catch
		{
			return false;
		}
	}

	private static UIBattleInfo_Button_PlayerInfo FindHeadButtonForPlayer(long playerId, int slot = -1)
	{
		if (!IsInGameEnvironment()) return null;
		try
		{
			if (!SimpleSingletonProvider<GameLogicManager>.hasInstance) return null;
			var battleInfo = SimpleSingletonProvider<GameLogicManager>.inst.battle?.battleInfo;
			var ui = battleInfo?.UIObject;
			var container = ui?.com_BattlePlayer?.com_Container;
			if (container == null || container.isDisposed || !container.onStage) return null;

			UIBattleInfo_Button_PlayerInfo[] players = new UIBattleInfo_Button_PlayerInfo[]
			{
				container.com_Player_1,
				container.com_Player_2,
				container.com_Player_3,
				container.com_Player_4
			};

			for (int i = 0; i < players.Length; i++)
			{
				var btn = players[i];
				if (btn != null && !btn.isDisposed && btn.onStage && btn.PlayerData?.player != null)
				{
					if (btn.PlayerData.player.Id == playerId || (slot >= 0 && btn.PlayerData.player.Slot == slot))
					{
						return btn;
					}
				}
			}

			if (slot >= 0 && slot < players.Length)
			{
				var btn = players[slot];
				if (btn != null && !btn.isDisposed && btn.onStage) return btn;
			}

			if (slot >= 1 && slot <= players.Length)
			{
				var btn = players[slot - 1];
				if (btn != null && !btn.isDisposed && btn.onStage) return btn;
			}
		}
		catch { }
		return null;
	}

	private static void CleanWidgetFromHeadButton(UIBattleInfo_Button_PlayerInfo headBtn)
	{
		if (headBtn == null || headBtn.isDisposed) return;
		for (int i = headBtn.numChildren - 1; i >= 0; i--)
		{
			var child = headBtn.GetChildAt(i);
			if (child != null && child.name == "__apx_handcard_widget")
			{
				if (child is GComponent gComp)
				{
					gComp.visible = false;
					HideHoverBar(gComp);
					if (gComp.GetChild("card_list") is GComponent cardList && !cardList.isDisposed)
					{
						cardList.RemoveChildren(0, -1, dispose: true);
					}
				}
				headBtn.RemoveChild(child, dispose: true);
			}
		}
	}

	public static void RefreshAllWidgets()
	{
		bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
		if (!IsEnabled || (!inSpectator && !IsPVEMode()))
		{
			ClearAllWidgets();
			return;
		}
		if (_isEnabled)
		{
			lock (_widgetLock)
			{
				foreach (var kvp in _presentationWidgets)
				{
					if (!kvp.Value.IsDisposed)
					{
						kvp.Value.Visible = true;
					}
				}
			}
		}

		if (inSpectator)
		{
			var allStoreHands = XingShu.Spectator.SpectatorStateStore.GetAllPlayerHands();
			var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();

			// Clean stale players not in current room
			List<long> stalePids = null;
			foreach (var kvp in _playerHands)
			{
				if (!allStoreHands.ContainsKey(kvp.Key) || (validPids.Count > 0 && !validPids.Contains(kvp.Key)))
				{
					if (stalePids == null) stalePids = new List<long>();
					stalePids.Add(kvp.Key);
				}
			}
			if (stalePids != null)
			{
				foreach (var spid in stalePids)
				{
					if (_playerHands.TryGetValue(spid, out var staleHand) && staleHand?.HeadButtonRef != null && staleHand.HeadButtonRef.TryGetTarget(out var target) && target != null && !target.isDisposed)
					{
						CleanWidgetFromHeadButton(target);
					}
					_playerHands.Remove(spid);
				}
			}

			// Apply store snapshot to _playerHands and render immediately
			foreach (var kvp in allStoreHands)
			{
				long pid = kvp.Key;
				if (validPids.Count > 0 && !validPids.Contains(pid)) continue;
				var cardIds = kvp.Value;
				TrackedPlayerHand hand = GetOrCreateHand(pid);
				try
				{
					PopulatePlayerMetadataIfMissing(hand, pid);
				}
				catch { }
				ApplyCardIdsToHand(hand, cardIds);
				hand.SyncState = HandSyncState.Latest;
				hand.IsPinned = true;
				RefreshHandWidget(hand);
			}
			ReconcileOverlayWidgets();
			return;
		}

		foreach (KeyValuePair<long, TrackedPlayerHand> playerHand2 in _playerHands)
		{
			RefreshHandWidget(playerHand2.Value);
		}
		ReconcileOverlayWidgets();
	}

	public static void RefreshHandWidget(TrackedPlayerHand hand)
	{
		if (hand == null) return;
		UIBattleInfo_Button_PlayerInfo target = null;
		if (hand.HeadButtonRef != null && hand.HeadButtonRef.TryGetTarget(out var cached) && cached != null && !cached.isDisposed && cached.onStage)
		{
			target = cached;
		}
		else
		{
			try
			{
				target = FindHeadButtonForPlayer(hand.PlayerId, hand.Slot);
				if (target != null && target.onStage)
				{
					hand.HeadButtonRef = new WeakReference<UIBattleInfo_Button_PlayerInfo>(target);
				}
			}
			catch { }
		}

		if (target != null && !target.isDisposed && target.onStage)
		{
			AttachOrUpdateWidget(target, hand);
		}
	}

	private static void AttachOrUpdateWidget(UIBattleInfo_Button_PlayerInfo headBtn, TrackedPlayerHand hand)
	{
		if (headBtn == null || headBtn.isDisposed)
		{
			return;
		}
		try
		{
			GComponent gComponent = headBtn.GetChild("__apx_handcard_widget") as GComponent;
			bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
			if (!IsEnabled || (!inSpectator && !IsPVEMode()))
			{
				if (gComponent != null && !gComponent.isDisposed)
				{
					gComponent.visible = false;
					HideHoverBar(gComponent);
				}
				return;
			}
			float cardScale = _cardScale;
			float playBtnSize = _playBtnSize;
			float num = 52f * cardScale;
			if (gComponent == null || gComponent.isDisposed)
			{
				for (int num2 = headBtn.numChildren - 1; num2 >= 0; num2--)
				{
					GObject childAt = headBtn.GetChildAt(num2);
					if (childAt != null && childAt.name == "__apx_handcard_widget")
					{
						headBtn.RemoveChild(childAt, dispose: true);
					}
				}
				gComponent = new GComponent();
				gComponent.name = "__apx_handcard_widget";
				GComponent gComponent2 = new GComponent();
				APXOverlayInput.Protect(gComponent2);
				gComponent2.name = "btn_play";
				gComponent2.SetSize(playBtnSize, playBtnSize);
				GGraph gGraph = new GGraph();
				gGraph.name = "bg";
				gComponent2.AddChild(gGraph);
				GTextField gTextField = new GTextField();
				gTextField.name = "txt";
				gTextField.autoSize = AutoSizeType.None;
				gTextField.text = "▶";
				gTextField.textFormat.align = AlignType.Center;
				gTextField.verticalAlign = VertAlignType.Middle;
				gComponent2.AddChild(gTextField);
				GComponent gComponent3 = new GComponent();
				gComponent3.name = "pause_icon";
				gComponent3.SetSize(playBtnSize, playBtnSize);
				gComponent3.touchable = false;
				GGraph gGraph2 = new GGraph();
				gGraph2.name = "bar_l";
				gComponent3.AddChild(gGraph2);
				GGraph gGraph3 = new GGraph();
				gGraph3.name = "bar_r";
				gComponent3.AddChild(gGraph3);
				gComponent2.AddChild(gComponent3);
				gComponent.AddChild(gComponent2);
				GComponent gComponent4 = new GComponent();
				APXOverlayInput.Protect(gComponent4);
				gComponent4.name = "card_list";
				gComponent4.visible = false;
				gComponent.AddChild(gComponent4);
				gComponent2.onRollOver.Add((EventCallback0)delegate
				{
					hand.IsHovered = true;
					bool flag2 = false;
					try
					{
						flag2 = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(hand.PlayerId);
					}
					catch
					{
					}
					if (flag2 && (hand.Cards.Count == 0 || hand.Cards.Exists((TrackedCard c) => c.CardId <= 0)))
					{
						SyncSelfCardsFromContainer(hand, headBtn.PlayerData);
						RenderHandCards(headBtn, hand);
					}
					UpdateWidgetVisuals(headBtn, hand);
					if (IsEnabled && !flag2 && HasAnyTeammateActiveDisplay() && (_needFetchFromServer || hand.Cards.Count == 0 || hand.Cards.Exists((TrackedCard c) => c.CardId <= 0)))
					{
						TriggerFetchCards("UserHoverDisplay: Player=" + hand.PlayerId);
					}
				});
				gComponent2.onRollOut.Add((EventCallback0)delegate
				{
					hand.IsHovered = false;
					UpdateWidgetVisuals(headBtn, hand);
				});
				gComponent4.onRollOver.Add((EventCallback0)delegate
				{
					hand.IsHovered = true;
					UpdateWidgetVisuals(headBtn, hand);
				});
				gComponent4.onRollOut.Add((EventCallback0)delegate
				{
					hand.IsHovered = false;
					UpdateWidgetVisuals(headBtn, hand);
				});
				gComponent2.onClick.Add((EventCallback0)delegate
				{
					hand.IsPinned = !hand.IsPinned;
					Log("UI_PIN", $"玩家 {hand.PlayerId} 播放按钮点击锁定切换: IsPinned={hand.IsPinned}");
					bool flag = false;
					try
					{
						flag = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(hand.PlayerId);
					}
					catch
					{
					}
					if (flag && (hand.Cards.Count == 0 || hand.Cards.Exists((TrackedCard c) => c.CardId <= 0)))
					{
						SyncSelfCardsFromContainer(hand, headBtn.PlayerData);
						RenderHandCards(headBtn, hand);
					}
					UpdateWidgetVisuals(headBtn, hand);
					if (hand.IsPinned && IsEnabled && !flag && (_needFetchFromServer || hand.Cards.Count == 0 || hand.Cards.Exists((TrackedCard c) => c.CardId <= 0)))
					{
						TriggerFetchCards("UserActivatedDisplay: Player=" + hand.PlayerId);
					}
				});
				headBtn.AddChild(gComponent);
			}
			gComponent.visible = true;
			float num3 = 0f;
			if (headBtn.width > 120f)
			{
				num3 = Mathf.Max(num3, headBtn.width);
			}
			if (headBtn.com_player != null && !headBtn.com_player.isDisposed && headBtn.com_player.width > 50f)
			{
				num3 = Mathf.Max(num3, headBtn.com_player.x + headBtn.com_player.width);
			}
			if (headBtn.btn_Label != null && !headBtn.btn_Label.isDisposed && headBtn.btn_Label.width > 50f)
			{
				num3 = Mathf.Max(num3, headBtn.btn_Label.x + headBtn.btn_Label.width);
			}
			if (num3 < 220f)
			{
				num3 = 246f;
			}
			float num4 = num3 + 16f;
			float value = 65f;
			try
			{
				if (headBtn.com_player is UICom_PlayerInfo { list_Level: not null } uICom_PlayerInfo && !uICom_PlayerInfo.list_Level.isDisposed && uICom_PlayerInfo.list_Level.height > 5f)
				{
					value = headBtn.com_player.y + uICom_PlayerInfo.list_Level.y + uICom_PlayerInfo.list_Level.height * 0.5f;
				}
			}
			catch
			{
			}
			value = Mathf.Clamp(value, 40f, 90f);
			float xv = num4 - playBtnSize * 0.5f;
			float yv = value - playBtnSize * 0.5f;
			gComponent.SetXY(xv, yv);
			gComponent.SetScale(_uiScale, _uiScale);
			RegisterPresentationWidget(hand.PlayerId, headBtn, xv, yv, playBtnSize, playBtnSize, _uiScale, 1.0f);
			if (gComponent.GetChild("btn_play") is GComponent gComponent5)
			{
				gComponent5.SetXY(0f, 0f);
				gComponent5.SetSize(playBtnSize, playBtnSize);
			}
			if (gComponent.GetChild("card_list") is GComponent gComponent6)
			{
				gComponent6.SetXY(playBtnSize + 6f, (playBtnSize - num) * 0.5f);
			}
			if (!(gComponent.GetChild("hover_bar") is GComponent { isDisposed: false }))
			{
				GComponent gComponent8 = new GComponent();
				APXOverlayInput.Protect(gComponent8);
				gComponent8.name = "hover_bar";
				gComponent8.touchable = true;
				gComponent8.visible = false;
				GGraph gGraph4 = new GGraph();
				gGraph4.name = "bg";
				gComponent8.AddChild(gGraph4);
				GRichTextField gRichTextField = new GRichTextField();
				gRichTextField.name = "txt";
				gRichTextField.UBBEnabled = true;
				gRichTextField.color = Color.white;
				gRichTextField.stroke = 1.5f;
				gRichTextField.strokeColor = new Color(0f, 0f, 0f, 0.95f);
				TextFormat textFormat = gRichTextField.textFormat;
				textFormat.color = Color.white;
				textFormat.bold = true;
				textFormat.size = 14;
				gRichTextField.textFormat = textFormat;
				gRichTextField.autoSize = AutoSizeType.Both;
				gComponent8.AddChild(gRichTextField);
				gComponent.AddChild(gComponent8);
			}
			RenderHandCards(headBtn, hand);
			UpdateWidgetVisuals(headBtn, hand);
		}
		catch (Exception ex)
		{
			Log("ATTACH_WIDGET_ERROR", "挂载控件失败: " + ex.Message);
		}
	}

	private static void ShowHoverBar(GComponent widgetRoot, string content, float cardX, float cardW)
	{
		if (widgetRoot == null || widgetRoot.isDisposed || string.IsNullOrEmpty(content) || !(widgetRoot.GetChild("hover_bar") is GComponent { isDisposed: false } gComponent))
		{
			return;
		}
		GRichTextField gRichTextField = gComponent.GetChild("txt") as GRichTextField;
		GGraph gGraph = gComponent.GetChild("bg") as GGraph;
		if (gRichTextField != null && gGraph != null)
		{
			gRichTextField.UBBEnabled = true;
			gRichTextField.color = Color.white;
			gRichTextField.stroke = 1.5f;
			gRichTextField.strokeColor = new Color(0f, 0f, 0f, 0.95f);
			TextFormat textFormat = gRichTextField.textFormat;
			textFormat.color = Color.white;
			textFormat.bold = true;
			textFormat.size = Mathf.RoundToInt(14f * Mathf.Clamp(_cardScale, 1f, 1.8f));
			if (APXMobileUIHelper.IsMobile)
			{
				textFormat.size = Mathf.RoundToInt((float)textFormat.size * 1.25f);
			}
			gRichTextField.textFormat = textFormat;
			gRichTextField.text = content;
			float num = 12f;
			float num2 = gRichTextField.textWidth + num * 2f + 4f;
			float num3 = Mathf.Max(28f, gRichTextField.textHeight + 10f);
			gComponent.SetSize(num2, num3);
			gRichTextField.SetXY(num, (num3 - gRichTextField.textHeight) * 0.5f);
			gGraph.DrawRect(num2, num3, 1, new Color(0.35f, 0.65f, 1f, 0.95f), new Color(0.08f, 0.1f, 0.15f, 0.98f));
			GComponent gComponent2 = widgetRoot.GetChild("card_list") as GComponent;
			float num4 = gComponent2?.x ?? 30f;
			float num5 = gComponent2?.y ?? 0f;
			float xv = num4 + cardX + (cardW - num2) * 0.5f;
			float num6 = num5 - num3 - 4f;
			if (num6 < -10f)
			{
				num6 = num5 + cardW + 4f;
			}

			// Screen Boundary Protection (1920x1080 and 1366x768 resolutions):
			float canvasWidth = GRoot.inst != null && GRoot.inst.width > 0 ? GRoot.inst.width : (float)Screen.width;
			float canvasHeight = GRoot.inst != null && GRoot.inst.height > 0 ? GRoot.inst.height : (float)Screen.height;
			Vector2 globalWidgetPos = Vector2.zero;
			try
			{
				globalWidgetPos = widgetRoot.LocalToGlobal(Vector2.zero);
			}
			catch { }

			float globalHoverX = globalWidgetPos.x + xv * _uiScale;
			float effectiveHoverW = num2 * _uiScale;
			if (globalHoverX < 10f)
			{
				xv += (10f - globalHoverX) / Mathf.Max(0.5f, _uiScale);
			}
			else if (globalHoverX + effectiveHoverW > canvasWidth - 10f)
			{
				xv -= (globalHoverX + effectiveHoverW - (canvasWidth - 10f)) / Mathf.Max(0.5f, _uiScale);
			}

			float globalHoverY = globalWidgetPos.y + num6 * _uiScale;
			float effectiveHoverH = num3 * _uiScale;
			if (globalHoverY < 10f)
			{
				num6 = num5 + cardW + 4f;
			}
			else if (globalHoverY + effectiveHoverH > canvasHeight - 10f)
			{
				num6 = num5 - num3 - 4f;
			}
			gComponent.SetXY(xv, num6);
			gComponent.sortingOrder = 9999;
			widgetRoot.AddChild(gComponent);
			gComponent.visible = true;
		}
	}

	private static void HideHoverBar(GComponent widgetRoot)
	{
		if (widgetRoot != null && !widgetRoot.isDisposed && widgetRoot.GetChild("hover_bar") is GComponent { isDisposed: false } gComponent)
		{
			gComponent.visible = false;
		}
	}

	private static void UpdateWidgetVisuals(UIBattleInfo_Button_PlayerInfo headBtn, TrackedPlayerHand hand)
	{
		if (headBtn == null || headBtn.isDisposed || !(headBtn.GetChild("__apx_handcard_widget") is GComponent { isDisposed: false } gComponent))
		{
			return;
		}
		GComponent gComponent2 = gComponent.GetChild("btn_play") as GComponent;
		GComponent gComponent3 = gComponent.GetChild("card_list") as GComponent;
		if (gComponent2 == null || gComponent3 == null || gComponent2.isDisposed || gComponent3.isDisposed)
		{
			return;
		}
		GGraph gGraph = gComponent2.GetChild("bg") as GGraph;
		GTextField gTextField = gComponent2.GetChild("txt") as GTextField;
		GComponent gComponent4 = gComponent2.GetChild("pause_icon") as GComponent;
		float width = gComponent2.width;
		try
		{
			Color lineColor;
			Color fillColor;
			Color color;
			switch (hand.SyncState)
			{
			case HandSyncState.Pending:
				lineColor = new Color(0.95f, 0.82f, 0.25f, 0.95f);
				fillColor = (hand.IsPinned ? new Color(0.38f, 0.3f, 0.08f, 0.98f) : new Color(0.2f, 0.16f, 0.05f, 0.9f));
				color = new Color(1f, 0.92f, 0.4f, 1f);
				break;
			case HandSyncState.Failed:
				lineColor = new Color(0.85f, 0.25f, 0.28f, 0.95f);
				fillColor = (hand.IsPinned ? new Color(0.35f, 0.08f, 0.1f, 0.98f) : new Color(0.18f, 0.05f, 0.06f, 0.9f));
				color = new Color(1f, 0.45f, 0.48f, 1f);
				break;
			default:
				lineColor = new Color(0.35f, 0.85f, 0.45f, 0.95f);
				fillColor = (hand.IsPinned ? new Color(0.12f, 0.35f, 0.2f, 0.98f) : new Color(0.08f, 0.2f, 0.12f, 0.9f));
				color = new Color(0.55f, 1f, 0.65f, 1f);
				break;
			}
			if (hand.IsHovered)
			{
				fillColor = new Color(Mathf.Min(1f, fillColor.r + 0.12f), Mathf.Min(1f, fillColor.g + 0.12f), Mathf.Min(1f, fillColor.b + 0.12f), fillColor.a);
			}
			if (gGraph != null && !gGraph.isDisposed)
			{
				gGraph.DrawRect(width, width, 1, lineColor, fillColor);
			}
			if (hand.IsPinned)
			{
				if (gTextField != null && !gTextField.isDisposed)
				{
					gTextField.visible = false;
				}
				if (gComponent4 != null && !gComponent4.isDisposed)
				{
					gComponent4.visible = true;
					gComponent4.SetSize(width, width);
					float num = Mathf.Round(width * 0.44f);
					float num2 = Mathf.Max(2.5f, Mathf.Round(width * 0.13f));
					float num3 = Mathf.Max(2.5f, Mathf.Round(width * 0.15f));
					float num4 = num2 * 2f + num3;
					float num5 = Mathf.Round((width - num4) * 0.5f);
					float yv = Mathf.Round((width - num) * 0.5f);
					if (gComponent4.GetChild("bar_l") is GGraph gGraph2)
					{
						gGraph2.DrawRect(num2, num, 0, Color.clear, color);
						gGraph2.SetXY(num5, yv);
					}
					if (gComponent4.GetChild("bar_r") is GGraph gGraph3)
					{
						gGraph3.DrawRect(num2, num, 0, Color.clear, color);
						gGraph3.SetXY(num5 + num2 + num3, yv);
					}
				}
				gComponent3.visible = true;
			}
			else
			{
				if (gTextField != null && !gTextField.isDisposed)
				{
					gTextField.visible = true;
					gTextField.SetXY(0f, 0f);
					gTextField.SetSize(width, width);
					gTextField.textFormat.size = Mathf.RoundToInt(width * 0.55f);
					gTextField.textFormat.color = color;
					gTextField.text = "▶";
					gTextField.textFormat.align = AlignType.Center;
					gTextField.verticalAlign = VertAlignType.Middle;
				}
				if (gComponent4 != null && !gComponent4.isDisposed)
				{
					gComponent4.visible = false;
				}
				gComponent3.visible = hand.IsHovered;
				if (!hand.IsHovered)
				{
					HideHoverBar(gComponent);
				}
			}
		}
		catch
		{
		}
	}

	private static void RenderHandCards(UIBattleInfo_Button_PlayerInfo headBtn, TrackedPlayerHand hand)
	{
		if (headBtn == null || headBtn.isDisposed)
		{
			return;
		}
		GComponent widgetRoot = headBtn.GetChild("__apx_handcard_widget") as GComponent;
		if (widgetRoot == null || widgetRoot.isDisposed || !(widgetRoot.GetChild("card_list") is GComponent { isDisposed: false } gComponent))
		{
			return;
		}
		try
		{
			bool flag = false;
			try
			{
				flag = SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(hand.PlayerId);
			}
			catch
			{
			}
			if (flag && (hand.Cards.Count == 0 || hand.Cards.Exists((TrackedCard c) => c.CardId <= 0)))
			{
				SyncSelfCardsFromContainer(hand, headBtn.PlayerData);
			}
			gComponent.RemoveChildren(0, -1, dispose: true);
			List<TrackedCard> knownCards = new List<TrackedCard>();
			int unknownCount = 0;
			for (int j = 0; j < hand.Cards.Count; j++)
			{
				TrackedCard tc = hand.Cards[j];
				if (tc != null && tc.CardId > 0)
				{
					knownCards.Add(tc);
				}
				else
				{
					unknownCount++;
				}
			}

			if (hand.Cards.Count == 0 && headBtn.PlayerData != null && headBtn.PlayerData.cardContainer != null && !XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession())
			{
				int containerCount = headBtn.PlayerData.cardContainer.CardCount;
				if (containerCount > 0)
				{
					unknownCount = containerCount;
				}
			}

			int totalItems = knownCards.Count + (unknownCount > 0 ? 1 : 0);
			if (totalItems == 0)
			{
				return;
			}

			float cardScale = _cardScale;
			float cardWidth = 52f * cardScale;
			float num = 52f * cardScale;
			float num2 = 360f * cardScale;

			// Safe Bounds & Critical Controls Overlap Prevention (1920x1080 and 1366x768 resolutions):
			float canvasWidth = GRoot.inst != null && GRoot.inst.width > 0 ? GRoot.inst.width : (float)Screen.width;
			Vector2 globalHeadPos = Vector2.zero;
			try
			{
				globalHeadPos = headBtn.LocalToGlobal(Vector2.zero);
			}
			catch { }

			float widgetStartX = globalHeadPos.x + (widgetRoot.x + gComponent.x) * _uiScale;
			// Critical Game Controls Protection:
			// Right-side controls (dice roll, action buttons, surrender) occupy the right ~38-42% of the screen.
			// On 1920x1080 (canvasWidth ~1920): leaves safe boundary at 1150px.
			// On 1366x768 (canvasWidth ~1366): leaves safe boundary at ~819px.
			float maxSafeX = Mathf.Min(canvasWidth - 280f, canvasWidth * 0.60f);
			if (maxSafeX > widgetStartX + 120f)
			{
				float maxAvailableWidth = (maxSafeX - widgetStartX) / Mathf.Max(0.5f, _uiScale);
				num2 = Mathf.Min(num2, maxAvailableWidth);
			}

			float num3 = 4f * cardScale;
			float num4 = cardWidth + num3;
			if (totalItems > 1 && (float)(totalItems - 1) * num4 + cardWidth > num2)
			{
				num4 = (num2 - cardWidth) / (float)(totalItems - 1);
			}

			// 1. 渲染所有已识别卡牌 (CardId > 0)
			for (int i = 0; i < knownCards.Count; i++)
			{
				TrackedCard trackedCard = knownCards[i];
				if (trackedCard == null)
				{
					continue;
				}
				GComponent cardItem = new GComponent();
				cardItem.SetSize(cardWidth, num);
				float curX = (float)i * num4;
				cardItem.SetXY(curX, 0f);
				cardItem.sortingOrder = i;
				cardItem.opaque = true;
				bool isAltArt = false;
				string altArtTitle = "";
				List<string> cardImageCandidates = GetCardImageCandidates(trackedCard.Config, hand, flag, out isAltArt, out altArtTitle);
				if (cardImageCandidates.Count == 0 && CardRegistryGenerated.TryGetCard(trackedCard.CardId, out var cardDef) && !string.IsNullOrEmpty(cardDef.ImageKey))
				{
					cardImageCandidates.Add(cardDef.ImageKey);
				}
				GGraph gGraph = new GGraph();
				gGraph.touchable = false;
				Color lineColor = (isAltArt ? new Color(0.9f, 0.4f, 0.78f, 0.95f) : new Color(0.4f, 0.45f, 0.55f, 0.9f));
				gGraph.DrawRect(cardWidth, num, 1, lineColor, new Color(0.1f, 0.11f, 0.15f, 0.95f));
				cardItem.AddChild(gGraph);
				GImage gImage = new GImage();
				gImage.touchable = false;
				float num5 = 2f * cardScale;
				gImage.SetXY(num5, num5);
				gImage.SetSize(cardWidth - num5 * 2f, num - num5 * 2f);
				cardItem.AddChild(gImage);
				if (cardImageCandidates.Count > 0)
				{
					SafeLoadCardTexture(cardImageCandidates, gImage, cardItem);
				}
				else
				{
					SafeLoadCardTexture("UT_CardBack_75010", gImage, cardItem);
				}
				if (trackedCard.BattleCost > 0)
				{
					float num6 = Mathf.Clamp(18f * cardScale, 14f, 26f);
					GComponent gComponent2 = new GComponent();
					gComponent2.touchable = false;
					gComponent2.SetSize(num6, num6);
					gComponent2.SetXY(1f, 1f);
					GGraph gGraph2 = new GGraph();
					gGraph2.DrawRect(num6, num6, 1, new Color(0.1f, 0.4f, 0.8f, 0.95f), new Color(0.05f, 0.15f, 0.35f, 0.98f));
					gComponent2.AddChild(gGraph2);
					GTextField gTextField2 = new GTextField();
					gTextField2.autoSize = AutoSizeType.None;
					gTextField2.SetSize(num6, num6);
					gTextField2.text = trackedCard.BattleCost.ToString();
					gTextField2.textFormat.size = Mathf.RoundToInt(num6 * 0.72f);
					gTextField2.textFormat.color = Color.white;
					gTextField2.textFormat.bold = true;
					gTextField2.textFormat.align = AlignType.Center;
					gTextField2.verticalAlign = VertAlignType.Middle;
					gComponent2.AddChild(gTextField2);
					cardItem.AddChild(gComponent2);
				}
				int capturedIndex = i;
				string cardDisplay = CardRegistryGenerated.FormatCardDisplay(trackedCard.CardId);
				string hoverText = "[color=#ffd700][b]【" + cardDisplay + "】[/b][/color]";
				if (trackedCard.BattleCost > 0)
				{
					hoverText += $"  [color=#38bdf8][b]费用: {trackedCard.BattleCost}[/b][/color]";
				}
				cardItem.onRollOver.Add((EventCallback0)delegate
				{
					cardItem.sortingOrder = 100;
					ShowHoverBar(widgetRoot, hoverText, curX, cardWidth);
				});
				cardItem.onRollOut.Add((EventCallback0)delegate
				{
					cardItem.sortingOrder = capturedIndex;
					HideHoverBar(widgetRoot);
				});
				cardItem.onClick.Add((EventCallback0)delegate
				{
					if (widgetRoot.GetChild("hover_bar") is GComponent { visible: not false })
					{
						cardItem.sortingOrder = capturedIndex;
						HideHoverBar(widgetRoot);
					}
					else
					{
						cardItem.sortingOrder = 100;
						ShowHoverBar(widgetRoot, hoverText, curX, cardWidth);
					}
				});
				gComponent.AddChild(cardItem);
			}

			// 2. 渲染未知手牌摘要块 (若存在未知卡牌，仅占用 1 个槽位展示未知摘要)
			if (unknownCount > 0)
			{
				int summaryIndex = knownCards.Count;
				GComponent summaryItem = new GComponent();
				summaryItem.SetSize(cardWidth, num);
				float curX = (float)summaryIndex * num4;
				summaryItem.SetXY(curX, 0f);
				summaryItem.sortingOrder = summaryIndex;
				summaryItem.opaque = true;

				GGraph gGraph = new GGraph();
				gGraph.touchable = false;
				Color lineColor = new Color(0.45f, 0.4f, 0.25f, 0.9f);
				gGraph.DrawRect(cardWidth, num, 1, lineColor, new Color(0.12f, 0.12f, 0.16f, 0.95f));
				summaryItem.AddChild(gGraph);

				GImage gImage = new GImage();
				gImage.touchable = false;
				float num5 = 2f * cardScale;
				gImage.SetXY(num5, num5);
				gImage.SetSize(cardWidth - num5 * 2f, num - num5 * 2f);
				summaryItem.AddChild(gImage);
				SafeLoadCardTexture("UT_CardBack_75010", gImage, summaryItem);

				// 醒目金色未知数量摘要标签 "×N"
				GTextField gTextField = new GTextField();
				gTextField.touchable = false;
				gTextField.autoSize = AutoSizeType.None;
				gTextField.SetSize(cardWidth, num);
				gTextField.text = $"×{unknownCount}";
				gTextField.textFormat.size = Mathf.RoundToInt(20f * cardScale);
				gTextField.textFormat.color = new Color(1f, 0.85f, 0.2f);
				gTextField.textFormat.bold = true;
				gTextField.textFormat.align = AlignType.Center;
				gTextField.verticalAlign = VertAlignType.Middle;
				summaryItem.AddChild(gTextField);

				int capturedIndex = summaryIndex;
				string hoverText = $"[color=#ffd700][b]【未知手牌】[/b][/color]  [color=#94a3b8]数量: {unknownCount} 张 (服务端遮蔽)[/color]";
				summaryItem.onRollOver.Add((EventCallback0)delegate
				{
					summaryItem.sortingOrder = 100;
					ShowHoverBar(widgetRoot, hoverText, curX, cardWidth);
				});
				summaryItem.onRollOut.Add((EventCallback0)delegate
				{
					summaryItem.sortingOrder = capturedIndex;
					HideHoverBar(widgetRoot);
				});
				summaryItem.onClick.Add((EventCallback0)delegate
				{
					if (widgetRoot.GetChild("hover_bar") is GComponent { visible: not false })
					{
						summaryItem.sortingOrder = capturedIndex;
						HideHoverBar(widgetRoot);
					}
					else
					{
						summaryItem.sortingOrder = 100;
						ShowHoverBar(widgetRoot, hoverText, curX, cardWidth);
					}
				});
				gComponent.AddChild(summaryItem);
			}
		}
		catch (Exception ex)
		{
			Log("RENDER_CARDS_ERROR", "渲染卡牌列表失败: " + ex.Message);
		}
	}

	private static List<string> GetCardImageCandidates(CardInfoConfigure config, TrackedPlayerHand hand, bool isSelf, out bool isAltArt, out string altArtTitle)
	{
		isAltArt = false;
		altArtTitle = "";
		List<string> list = new List<string>();
		if (config == null)
		{
			return list;
		}
		int altArtCardId = 0;
		CardView cardView = null;
		try
		{
			if (hand != null && hand.PlayerId != 0L)
			{
				cardView = config.GetBattlePlayerCardView(hand.PlayerId);
				if (cardView != null && cardView.IsAltArtCard && cardView.AltArtCardId != 0)
				{
					altArtCardId = cardView.AltArtCardId;
				}
			}
		}
		catch
		{
		}
		if (altArtCardId == 0 && isSelf)
		{
			try
			{
				cardView = config.GetMainPlayerCardView();
				if (cardView != null && cardView.IsAltArtCard && cardView.AltArtCardId != 0)
				{
					altArtCardId = cardView.AltArtCardId;
				}
			}
			catch
			{
			}
		}
		if (altArtCardId == 0)
		{
			try
			{
				if (hand != null && hand.PlayerId != 0L)
				{
					((SimpleSingletonProvider<GameLogicManager>.inst?.battle)?.GetPlayerDataById(hand.PlayerId))?.TryGetAltArtCardId(config.Id, out altArtCardId);
				}
			}
			catch
			{
			}
		}
		if (altArtCardId == 0 && isSelf)
		{
			try
			{
				SimpleSingletonProvider<GameLogicManager>.inst?.altArtCardLogic?.TryGetAltArtCardId(config.Id, out altArtCardId);
			}
			catch
			{
			}
		}
		if (altArtCardId != 0 && altArtCardId != config.Id)
		{
			isAltArt = true;
			try
			{
				if (StaticConfigure.Card.AltArtDict.TryGetValue(config.Id, out var value) && value != null)
				{
					foreach (CardAltArtConfigureItem cardAltArtConfigureItem in value.CardAltArtConfigureItems)
					{
						if (cardAltArtConfigureItem.AltArtId == altArtCardId)
						{
							if (GameSettings.angelMode && !string.IsNullOrEmpty(cardAltArtConfigureItem.AccountBackgroundSfw))
							{
								list.Add(cardAltArtConfigureItem.AccountBackgroundSfw);
							}
							if (!string.IsNullOrEmpty(cardAltArtConfigureItem.AccountBackground))
							{
								list.Add(cardAltArtConfigureItem.AccountBackground);
							}
							break;
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				if (StaticConfigure.Item.InfoDict.TryGetValue(altArtCardId, out var value2) && value2 != null)
				{
					if (!string.IsNullOrEmpty(value2.ShowIcon))
					{
						list.Add(value2.ShowIcon);
					}
					try
					{
						string local = value2.NameID.GetLocal(UIStringType.Item);
						if (!string.IsNullOrEmpty(local))
						{
							altArtTitle = local;
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			if (cardView != null && !cardView.IsVideo && !string.IsNullOrEmpty(cardView.Key))
			{
				list.Add(cardView.Key);
			}
		}
		else if (cardView != null && !cardView.IsVideo && !string.IsNullOrEmpty(cardView.Key))
		{
			list.Add(cardView.Key);
		}
		try
		{
			string image = config.GetImage();
			if (!string.IsNullOrEmpty(image))
			{
				list.Add(image);
			}
		}
		catch
		{
		}
		try
		{
			if (!string.IsNullOrEmpty(config.Image))
			{
				list.Add(config.Image);
			}
		}
		catch
		{
		}
		List<string> list2 = new List<string>();
		foreach (string item in list)
		{
			if (!string.IsNullOrEmpty(item) && !list2.Contains(item))
			{
				list2.Add(item);
			}
		}
		return list2;
	}

	private static void SafeLoadCardTexture(List<string> candidateUrls, GImage targetImg, GComponent parent, int attemptIndex = 0)
	{
		if (candidateUrls == null || attemptIndex >= candidateUrls.Count || targetImg == null || parent == null)
		{
			return;
		}
		string text = candidateUrls[attemptIndex];
		if (string.IsNullOrEmpty(text))
		{
			SafeLoadCardTexture(candidateUrls, targetImg, parent, attemptIndex + 1);
			return;
		}
		try
		{
			TextureManager inst = SimpleSingletonProvider<TextureManager>.inst;
			if (inst != null)
			{
				inst.AsyncLoad(text, delegate(NTexture ntex)
				{
					if (parent != null && targetImg != null && !parent.isDisposed && !targetImg.isDisposed)
					{
						if (ntex != null)
						{
							NTexture texture = ntex;
							try
							{
								int width = ntex.width;
								int height = ntex.height;
								if (width > 0 && height > 0 && width != height)
								{
									if (height > width)
									{
										Rect region = new Rect(0f, 0f, width, width);
										texture = new NTexture(ntex, region, rotated: false);
									}
									else
									{
										float x = (float)(width - height) * 0.5f;
										Rect region2 = new Rect(x, 0f, height, height);
										texture = new NTexture(ntex, region2, rotated: false);
									}
								}
							}
							catch
							{
							}
							targetImg.texture = texture;
							targetImg.visible = true;
						}
						else
						{
							SafeLoadCardTexture(candidateUrls, targetImg, parent, attemptIndex + 1);
						}
					}
				}, null).Forget();
			}
			else
			{
				SafeLoadCardTexture(candidateUrls, targetImg, parent, attemptIndex + 1);
			}
		}
		catch
		{
			SafeLoadCardTexture(candidateUrls, targetImg, parent, attemptIndex + 1);
		}
	}

	private static void SafeLoadCardTexture(string url, GImage targetImg, GComponent parent)
	{
		if (!string.IsNullOrEmpty(url))
		{
			SafeLoadCardTexture(new List<string> { url }, targetImg, parent);
		}
	}

	private void OnGUI()
	{
		if (UseModernView || !_showDebugUI)
		{
			return;
		}
		using (new APXMobileUIHelper.MatrixScope(0f))
		{
			GUI.skin.window.fontSize = (APXMobileUIHelper.IsMobile ? 14 : 13);
			_debugWinRect = GUI.Window(9984, _debugWinRect, DrawDebugWindow, "\ud83c\udfb4 星枢 局内手牌监视设置");
		}
	}

	private void DrawDebugWindow(int windowId)
	{
		GUILayout.BeginVertical();
		GUILayout.BeginHorizontal();
		GUILayout.Label("实时手牌悬浮窗: ", GUILayout.Width(150f));
		bool flag = GUILayout.Toggle(IsEnabled, IsEnabled ? "\ud83d\udfe2 已启用" : "⚪ 已停用");
		if (flag != IsEnabled)
		{
			IsEnabled = flag;
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		GUILayout.Label("进入观战自动开启: ", GUILayout.Width(150f));
		bool flagAuto = GUILayout.Toggle(SpectatorAutoOpen, SpectatorAutoOpen ? "\ud83d\udfe2 自动开启" : "⚪ 手动控制");
		if (flagAuto != SpectatorAutoOpen)
		{
			SpectatorAutoOpen = flagAuto;
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);
		GUILayout.BeginHorizontal();
		GUILayout.Label($"卡牌尺寸缩放 ({CardScale:F2}x): ", GUILayout.Width(150f));
		float num = GUILayout.HorizontalSlider(CardScale, 0.6f, 3f);
		if (Mathf.Abs(num - CardScale) > 0.01f)
		{
			CardScale = num;
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("0.8x (紧凑)"))
		{
			CardScale = 0.8f;
		}
		if (GUILayout.Button("1.0x (推荐)"))
		{
			CardScale = 1f;
		}
		if (GUILayout.Button("1.6x (放大)"))
		{
			CardScale = 1.6f;
		}
		if (GUILayout.Button("2.0x (特大)"))
		{
			CardScale = 2f;
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(4f);
		GUILayout.BeginHorizontal();
		GUILayout.Label($"播放标识大小 ({PlayBtnSize:F0} px): ", GUILayout.Width(150f));
		float num2 = GUILayout.HorizontalSlider(PlayBtnSize, 16f, 200f);
		if (Mathf.Abs(num2 - PlayBtnSize) > 0.5f)
		{
			PlayBtnSize = num2;
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("20px (紧凑)"))
		{
			PlayBtnSize = 20f;
		}
		if (GUILayout.Button("24px (标准)"))
		{
			PlayBtnSize = 24f;
		}
		if (GUILayout.Button("36px (清晰)"))
		{
			PlayBtnSize = 36f;
		}
		if (GUILayout.Button("60px (特大)"))
		{
			PlayBtnSize = 60f;
		}
		if (GUILayout.Button("100px (醒目)"))
		{
			PlayBtnSize = 100f;
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		IsPVEMode();
		GUILayout.Label("状态: [待拉取: " + (_needFetchFromServer ? "\ud83d\udfe1 存在变动" : "\ud83d\udfe2 最新") + "] [播放标识: " + (HasAnyTeammateActiveDisplay() ? "▶ 激活显示中" : "⏸ 全折叠") + "]", GUI.skin.box);
		if (!string.IsNullOrEmpty(_lastServerResponseText))
		{
			GUILayout.Label("<b>服务端最近报文:</b> " + _lastServerResponseText, GUI.skin.box);
		}
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("\ud83d\udd04 手动同步房间手牌") && IsPVEMode())
		{
			TriggerFetchCards("ManualGUI");
		}
		if (GUILayout.Button("✕ 关闭面板"))
		{
			_showDebugUI = false;
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);
		bool inSpectator = XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
		List<TrackedPlayerHand> displayHands = new List<TrackedPlayerHand>();
		if (inSpectator)
		{
			var validPids = XingShu.Spectator.SpectatorStateStore.GetCurrentRoomPlayerIds();
			foreach (KeyValuePair<long, TrackedPlayerHand> kvp in _playerHands)
			{
				if (validPids.Contains(kvp.Key))
				{
					displayHands.Add(kvp.Value);
				}
			}
		}
		else
		{
			foreach (KeyValuePair<long, TrackedPlayerHand> kvp in _playerHands)
			{
				displayHands.Add(kvp.Value);
			}
		}

		GUILayout.Label($"<b>当前战局玩家手牌状态 ({displayHands.Count} 人):</b>");
		_debugScroll = GUILayout.BeginScrollView(_debugScroll, GUILayout.Height(180f));
		foreach (TrackedPlayerHand value in displayHands)
		{
			string text = "";
			for (int i = 0; i < value.Cards.Count; i++)
			{
				TrackedCard trackedCard = value.Cards[i];
				if (trackedCard != null && trackedCard.CardId > 0)
				{
					string cardDisplay = CardRegistryGenerated.FormatCardDisplay(trackedCard.CardId);
					text += (trackedCard.BattleCost > 0) ? $"[{cardDisplay} 费:{trackedCard.BattleCost}] " : $"[{cardDisplay}] ";
				}
				else
				{
					text += "[未知手牌] ";
				}
			}
			if (string.IsNullOrEmpty(text))
			{
				text = "(无手牌, Cards=0, 特征=" + value.LastAttrSignature + ")";
			}
			string text2 = (value.IsBot ? "[\ud83e\udd16Bot]" : ((value.SyncState == HandSyncState.Latest) ? "[\ud83d\udfe2最新]" : ((value.SyncState == HandSyncState.Pending) ? "[\ud83d\udfe1待更新]" : "[\ud83d\udd34失败]")));
			GUILayout.Label($"• {text2} 玩家 {value.PlayerName} / {value.PlayerId} (Slot={value.Slot}) [手牌: {value.Cards.Count}张]: {text}");
		}
		GUILayout.EndScrollView();
		GUILayout.EndVertical();
		GUI.DragWindow();
	}

	private static void OnSpectatorStorePlayerHandUpdated(long playerId, IReadOnlyList<int> cardIds)
	{
		try
		{
			TrackedPlayerHand hand = GetOrCreateHand(playerId);
			ApplyCardIdsToHand(hand, cardIds);
			PopulatePlayerMetadataIfMissing(hand, playerId);
			MarkOverlayDirty($"HandSnapshotUpdated:Player={playerId}");
			RefreshHandWidget(hand);
			Log("SPECTATOR_STORE_UPDATE", $"观战状态仓同步玩家 {playerId} 手牌，数量={cardIds.Count}, CardIds=[{string.Join(",", cardIds)}]");
		}
		catch (Exception ex)
		{
			Log("SPECTATOR_STORE_ERR", $"OnSpectatorStorePlayerHandUpdated 异常: {ex.Message}");
		}
	}

	public static void ClearAllWidgets()
	{
		try
		{
			lock (_widgetLock)
			{
				foreach (var kvp in _presentationWidgets)
				{
					kvp.Value.Visible = false;
				}
			}
			foreach (KeyValuePair<long, TrackedPlayerHand> kvp in _playerHands)
			{
				TrackedPlayerHand hand = kvp.Value;
				if (hand?.HeadButtonRef != null && hand.HeadButtonRef.TryGetTarget(out var target) && target != null && !target.isDisposed)
				{
					CleanWidgetFromHeadButton(target);
				}
			}

			if (IsInGameEnvironment())
			{
				try
				{
					if (SimpleSingletonProvider<GameLogicManager>.hasInstance)
					{
						var battleInfo = SimpleSingletonProvider<GameLogicManager>.inst.battle?.battleInfo;
						var container = battleInfo?.UIObject?.com_BattlePlayer?.com_Container;
						if (container != null && !container.isDisposed)
						{
							CleanWidgetFromHeadButton(container.com_Player_1);
							CleanWidgetFromHeadButton(container.com_Player_2);
							CleanWidgetFromHeadButton(container.com_Player_3);
							CleanWidgetFromHeadButton(container.com_Player_4);
						}
					}
				}
				catch { }
			}
		}
		catch (Exception ex)
		{
			Log("CLEAR_WIDGETS_ERR", $"ClearAllWidgets 异常: {ex.Message}");
		}
	}

	private static void OnSpectatorJoined(int roomId, long epoch)
	{
		try
		{
			Log("SPECTATOR_JOIN", $"检测到进入观战房间 (RoomID={roomId}, Epoch={epoch})");
			if (SpectatorAutoOpen && !_isEnabled)
			{
				IsEnabled = true;
				Log("SPECTATOR_AUTO_ENABLE", "进入观战自动开启偏好生效: 自动启用实时手牌悬浮窗");
			}
			else if (_isEnabled)
			{
				MarkOverlayDirty($"SpectatorJoined:Room={roomId},Epoch={epoch}");
				RefreshAllWidgets();
				Log("SPECTATOR_JOIN_REFRESH", "已处于开启状态，进入新观战房间触发全量渲染");
			}
		}
		catch (Exception ex)
		{
			Log("SPECTATOR_JOIN_ERR", $"OnSpectatorJoined 处理异常: {ex.Message}");
		}
	}

	private static void OnSpectatorStoreSessionReset()
	{
		try
		{
			Log("SPECTATOR_STORE_RESET", "观战状态仓会话重置，清空手牌监视");
			lock (_widgetLock)
			{
				foreach (var kvp in _presentationWidgets)
				{
					kvp.Value.Visible = false;
					kvp.Value.IsDisposed = true;
				}
				_presentationWidgets.Clear();
			}
			ClearAllWidgets();
			_playerHands.Clear();
			_lastSpectatorRoomId = 0;
			_lastSpectatorEpoch = 0;
			_lastActiveRoomId = 0;
			_firstRoundFetchedRoomId = 0;
			_needFetchFromServer = false;
			RemoveRoundListener();
			MarkOverlayDirty("SpectatorStoreSessionReset");
			RefreshAllWidgets();
		}
		catch (Exception ex)
		{
			Log("SPECTATOR_STORE_RESET_ERR", $"OnSpectatorStoreSessionReset 异常: {ex.Message}");
		}
	}

	private static void OnSpectatorStoreRosterUpdated()
	{
		try
		{
			MarkOverlayDirty("PlayerRosterUpdated");
			RefreshAllWidgets();
		}
		catch (Exception ex)
		{
			Log("SPECTATOR_ROSTER_ERR", $"OnSpectatorStoreRosterUpdated 异常: {ex.Message}");
		}
	}

	public static void ApplyCardIdsToHand(TrackedPlayerHand hand, IEnumerable<int> cardIds)
	{
		if (hand == null) return;
		hand.Cards.Clear();
		if (cardIds != null)
		{
			int fakeUid = -1;
			foreach (int cid in cardIds)
			{
				if (cid <= 0) continue;
				int cost = 0;
				try
				{
					if (CardRegistryGenerated.TryGetCard(cid, out var cardDef))
					{
						cost = cardDef.Cost;
					}
					else
					{
						cost = cid.GetCardConfigure()?.Cost ?? 0;
					}
				}
				catch { }

				hand.Cards.Add(new TrackedCard
				{
					UniqueId = fakeUid--,
					CardId = cid,
					BattleCost = cost
				});
			}
		}
		hand.SyncState = HandSyncState.Latest;
	}

	private static bool CardListsMatch(List<TrackedCard> cards, IReadOnlyList<int> cardIds)
	{
		if (cards == null && cardIds == null) return true;
		if (cards == null || cardIds == null) return false;
		if (cards.Count != cardIds.Count) return false;
		for (int i = 0; i < cards.Count; i++)
		{
			if (cards[i] == null || cards[i].CardId != cardIds[i]) return false;
		}
		return true;
	}

	private static void PopulatePlayerMetadataIfMissing(TrackedPlayerHand hand, long playerId)
	{
		if (hand == null || playerId <= 0 || !IsInGameEnvironment()) return;
		try
		{
			if (string.IsNullOrEmpty(hand.PlayerName) || hand.Slot < 0)
			{
				var room = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
				if (room?.Players != null)
				{
					foreach (var p in room.Players)
					{
						if (p != null && p.Id == playerId)
						{
							hand.Slot = p.Slot;
							hand.PlayerName = p.GetNick();
							hand.IsBot = p.IsBot;
							break;
						}
					}
				}
			}
			if (string.IsNullOrEmpty(hand.PlayerName) || hand.Slot < 0)
			{
				var battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
				if (battle?.PlayerDatas != null)
				{
					foreach (var pd in battle.PlayerDatas)
					{
						if (pd?.player != null && pd.player.Id == playerId)
						{
							hand.Slot = pd.player.Slot;
							hand.PlayerName = pd.player.GetNick();
							hand.IsBot = pd.player.IsBot;
							break;
						}
					}
				}
			}
		}
		catch { }
	}

	public static void Log(string tag, string message)
	{
		string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {message}";
		try
		{
			lock (_logLock)
			{
				if (!string.IsNullOrEmpty(_logFilePath))
				{
					File.AppendAllText(_logFilePath, text + "\n");
				}
			}
		}
		catch
		{
		}
	}
}

public sealed class HandCardPresentationWidgetContract
{
	public long PlayerId { get; set; }
	public object Parent { get; set; }
	public bool Visible { get; set; }
	public float Alpha { get; set; }
	public float Width { get; set; }
	public float Height { get; set; }
	public float Scale { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public bool IsDisposed { get; set; }

	public bool IntersectsScreenBounds(float screenWidth = 1920f, float screenHeight = 1080f)
	{
		if (!Visible || IsDisposed) return false;
		float effectiveScale = Scale > 0f ? Scale : 1f;
		float effectiveW = Width * effectiveScale;
		float effectiveH = Height * effectiveScale;
		return (X + effectiveW > 0f) && (X < screenWidth) && (Y + effectiveH > 0f) && (Y < screenHeight);
	}
}

