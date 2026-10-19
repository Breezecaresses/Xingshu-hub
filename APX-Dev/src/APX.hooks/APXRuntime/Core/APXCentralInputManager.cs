using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using APXRuntime.Utilities;
using UnityEngine.UI;

namespace APXRuntime.Core;

/// <summary>
/// APX 集中输入管理与应急 EventSystem 托管服务 (Phase 1B.2B.3A)
/// 核心职责：
/// 1. 原生优先（Native First）：若游戏存在任何可用的 External EventSystem，全生命周期使用 Native，绝不创建 APX EventSystem。
/// 2. 中央受控应急兜底（Centralized Emergency Fallback）：仅在 External EventSystem 完全缺失时，在 Utility Runtime 节点下创建唯一受控的临时兜底。
/// 3. 自动让位（Auto Reconcile & Release）：一旦 External EventSystem 出现或恢复，临时兜底立即自动让位并销毁，彻底消除多 EventSystem 冲突。
/// 4. 集中热键协调与文本输入门禁（Hotkey Coordination & Text Entry Gate）：单帧懒清理按键防重，统一文本输入焦点探测，杜绝热键穿透打字。
/// </summary>
[DisallowMultipleComponent]
internal sealed class APXCentralInputManager : MonoBehaviour
{
	/// <summary>
	/// 全局单例访问器
	/// </summary>
	public static APXCentralInputManager Instance { get; private set; }

	private bool _sceneSubscribed;

	// Hotkey Coordination & Text Entry Gate 状态 (Phase 1B.2B.3A)
	private int _lastHotkeyFrame = -1;
	private readonly HashSet<KeyCode> _consumedKeysThisFrame = new HashSet<KeyCode>();
	private APXInputCaptureMode _captureMode = APXInputCaptureMode.None;
	private string _captureOwner;

	// Left Ctrl Standalone Tap 状态机 (Phase 2D-8C-R6B-P1B)
	private bool _leftCtrlDown = false;
	private bool _leftCtrlChordUsed = false;


	// 唯一受控的临时兜底 EventSystem 及其 GameObject
	private EventSystem _temporaryFallback;
	private GameObject _temporaryFallbackObject;
	private Coroutine _fallbackMonitorCoroutine;

	/// <summary>
	/// 当前是否存在由 CentralInputManager 持有的临时兜底 EventSystem
	/// </summary>
	public bool OwnedFallbackPresent => _temporaryFallback != null && _temporaryFallbackObject != null;

	/// <summary>
	/// 临时兜底 GameObject 的 InstanceID (若不存在则返回 0)
	/// </summary>
	public int OwnedFallbackInstanceId => (_temporaryFallbackObject != null) ? _temporaryFallbackObject.GetInstanceID() : 0;

	/// <summary>
	/// 当前 EventSystem.current 是否指向我们的临时兜底
	/// </summary>
	public bool CurrentIsOwnedFallback => EventSystem.current != null && _temporaryFallback != null && EventSystem.current == _temporaryFallback;

	/// <summary>
	/// 获取当前可用的 EventSystem 引用（只读）
	/// </summary>
	public EventSystem CurrentAvailableEventSystem
	{
		get
		{
			EventSystem current = EventSystem.current;
			if (current != null && current.isActiveAndEnabled && current != _temporaryFallback)
			{
				return current;
			}
			return _temporaryFallback;
		}
	}

	/// <summary>
	/// 当前捕获模式
	/// </summary>
	public APXInputCaptureMode CurrentCaptureMode => _captureMode;

	/// <summary>
	/// 当前键盘/输入捕获所有者标识
	/// </summary>
	public string CurrentCaptureOwner => _captureOwner;

	/// <summary>
	/// 当前是否处于文本输入活跃状态 (显式 TextEntry 捕获或 UGUI InputField 聚焦)
	/// </summary>
	public bool IsTextEntryActive
	{
		get
		{
			if (_captureMode == APXInputCaptureMode.TextEntry)
			{
				return true;
			}

			try
			{
				EventSystem currentEs = CurrentAvailableEventSystem;
				if (currentEs != null && currentEs.currentSelectedGameObject != null)
				{
					UnityEngine.UI.InputField inputField = currentEs.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
					if (inputField != null && inputField.isFocused)
					{
						return true;
					}
				}
			}
			catch
			{
			}

			return false;
		}
	}

	private void Awake()
	{
		if (Instance == null)
		{
			Instance = this;
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CentralInput.Awake] 集中输入观察宿主已就绪; GameObject='{base.gameObject.name}', InstanceId={base.gameObject.GetInstanceID()}");
			
			// 挂接场景生命周期信号
			SubscribeSceneWatcher();

			// 首次启动执行拓扑快照扫描
			EventSystemDiagnostics.DumpCurrentTopology("Startup");
		}
		else if (Instance == this)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CentralInput.Awake] Instance == this 重复唤醒检测");
		}
		else if (Instance.gameObject == base.gameObject)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CentralInput.Awake] 检测到同宿主重复挂载 APXCentralInputManager 组件，安全销毁重复组件 (ComponentId={base.GetInstanceID()})");
			UnityEngine.Object.Destroy(this);
		}
		else
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CentralInput.Awake] 检测到异地重复 GameObject 尝试挂载输入宿主 (InstanceId={base.gameObject.GetInstanceID()})，安全销毁重复 GameObject");
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private void OnEnable()
	{
		if (Instance == this)
		{
			SubscribeSceneWatcher();
		}
	}

	private void OnDisable()
	{
		if (Instance == this)
		{
			UnsubscribeSceneWatcher();
			StopFallbackMonitor();
			_leftCtrlDown = false;
			_leftCtrlChordUsed = false;
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			UnsubscribeSceneWatcher();
			ReleaseTemporaryFallback("HostDestroyed");
			ResetTransientStateInternal("HostDestroyed");
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CentralInput.OnDestroy] 输入宿主正在注销 (InstanceId={base.gameObject.GetInstanceID()})");
			Instance = null;
		}
	}

	#region 集中式 EventSystem Ownership 托管接口

	/// <summary>
	/// Legacy 模块统一请求 EventSystem 保证接口。
	/// 替代 AssistBallGUI 与 StatsWidgetView 各自随地 new EventSystem 的分散行为。
	/// 严格遵循 Native First 策略：只要存在 External EventSystem，绝不创建 Fallback。
	/// </summary>
	/// <param name="requester">调用方模块标识 (如 AssistBallGUI, StatsWidgetView)</param>
	/// <returns>当前可用 EventSystem 实例</returns>
	internal static EventSystem EnsureEventSystemAvailable(string requester)
	{
		try
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.Ensure] Requester={requester}");

			APXCentralInputManager manager = Instance;
			if (manager == null)
			{
				if (APXRuntimeHost.Instance != null)
				{
					EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.Ensure] Instance is null, mounting APXCentralInputManager on APXRuntimeHost");
					manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
				}
				else
				{
					EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.EnsureError] Requester='{requester}': Both APXCentralInputManager.Instance and APXRuntimeHost.Instance are null!");
					return EventSystem.current;
				}
			}

			return manager.EnsureAvailableInternal(requester);
		}
		catch (Exception ex)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.EnsureError] Requester='{requester}', Exception: {ex.Message}");
			return EventSystem.current;
		}
	}

	private EventSystem EnsureAvailableInternal(string requester)
	{
		// 步骤 1：先检查 EventSystem.current 是否有效且不是我们的临时兜底
		EventSystem current = EventSystem.current;
		if (current != null && current.isActiveAndEnabled && current != _temporaryFallback)
		{
			// 若之前曾存在临时兜底，此时发现 Native Current 有效，立即让位销毁
			if (_temporaryFallback != null)
			{
				ReconcileEventSystemOwnership($"EnsureRequest_NativeCurrentActive (Requester={requester})");
			}

			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.NativeUse] Requester={requester}, Name='{current.gameObject.name}', InstanceId={current.gameObject.GetInstanceID()}");
			return current;
		}

		// 步骤 2：若 current 不可用（为 null 或 disabled 或等于 temporary fallback），扫描场景中所有 Active EventSystem
		EventSystem[] all = UnityEngine.Object.FindObjectsOfType<EventSystem>();
		EventSystem externalCandidate = null;
		int externalCount = 0;
		if (all != null)
		{
			for (int i = 0; i < all.Length; i++)
			{
				EventSystem es = all[i];
				if (es != null && es.isActiveAndEnabled && es != _temporaryFallback)
				{
					externalCount++;
					if (externalCandidate == null)
					{
						externalCandidate = es;
					}
				}
			}
		}

		if (externalCount > 1 && externalCandidate != null)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.MultipleExternal] Count={externalCount}, FirstSelected='{externalCandidate.gameObject.name}'");
		}

		if (externalCandidate != null)
		{
			// 找到外部可用 EventSystem，若存在临时兜底则让位销毁
			if (_temporaryFallback != null)
			{
				ReconcileEventSystemOwnership($"EnsureRequest_ExternalCandidateFound (Requester={requester})");
			}

			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.NativeUse] Requester={requester}, Name='{externalCandidate.gameObject.name}', InstanceId={externalCandidate.gameObject.GetInstanceID()}");
			return externalCandidate;
		}

		// 步骤 3：完全找不到任何外部 EventSystem，进入应急模式
		if (_temporaryFallback != null && _temporaryFallback.gameObject != null && _temporaryFallback.isActiveAndEnabled)
		{
			return _temporaryFallback;
		}

		return CreateTemporaryFallback(requester);
	}

	/// <summary>
	/// 仅在外部 EventSystem 数量严格为 0 时创建唯一受控的临时兜底
	/// </summary>
	private EventSystem CreateTemporaryFallback(string requester)
	{
		if (_temporaryFallback != null && _temporaryFallbackObject != null)
		{
			return _temporaryFallback;
		}

		string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		_temporaryFallbackObject = new GameObject("[APX] Temporary EventSystem");
		_temporaryFallbackObject.transform.SetParent(base.transform, false);

		_temporaryFallback = _temporaryFallbackObject.AddComponent<EventSystem>();
		_temporaryFallbackObject.AddComponent<StandaloneInputModule>();

		int goId = _temporaryFallbackObject.GetInstanceID();
		int compId = _temporaryFallback.GetInstanceID();

		EventSystemDiagnostics.LogDirect($"[{timestamp}] [Fallback.Create] Requester={requester}, GameObjectId={goId}, EventSystemId={compId}, Reason=NoExternalEventSystem");

		StartFallbackMonitor();
		return _temporaryFallback;
	}

	/// <summary>
	/// 协调 EventSystem 归属权：检测到外部 EventSystem 存在时，销毁受控临时兜底并让位
	/// </summary>
	internal void ReconcileEventSystemOwnership(string reason)
	{
		try
		{
			if (_temporaryFallback == null && _temporaryFallbackObject == null)
			{
				return;
			}

			EventSystem[] all = UnityEngine.Object.FindObjectsOfType<EventSystem>();
			EventSystem externalFound = null;
			int externalCount = 0;
			if (all != null)
			{
				for (int i = 0; i < all.Length; i++)
				{
					EventSystem es = all[i];
					if (es != null && es.isActiveAndEnabled && es != _temporaryFallback)
					{
						externalCount++;
						if (externalFound == null)
						{
							externalFound = es;
						}
					}
				}
			}

			if (externalCount > 1 && externalFound != null)
			{
				EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EventSystem.MultipleExternal] Count={externalCount}, FirstSelected='{externalFound.gameObject.name}'");
			}

			if (externalFound != null)
			{
				ReleaseTemporaryFallback($"ExternalEventSystemAvailable (Reason={reason}, External='{externalFound.gameObject.name}', InstanceId={externalFound.gameObject.GetInstanceID()})");
			}
		}
		catch (Exception ex)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Fallback.ReconcileError] Reason='{reason}', Exception: {ex.Message}");
		}
	}

	/// <summary>
	/// 安全释放销毁临时兜底，严禁使用 DestroyImmediate
	/// </summary>
	private void ReleaseTemporaryFallback(string releaseReason)
	{
		StopFallbackMonitor();

		if (_temporaryFallbackObject != null)
		{
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			EventSystemDiagnostics.LogDirect($"[{timestamp}] [Fallback.Release] Reason={releaseReason}");
			UnityEngine.Object.Destroy(_temporaryFallbackObject);
		}

		_temporaryFallback = null;
		_temporaryFallbackObject = null;
	}

	#endregion

	#region 低频 Fallback 让位监听协程

	private void StartFallbackMonitor()
	{
		if (_fallbackMonitorCoroutine != null)
		{
			return;
		}
		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Fallback.MonitorStart] 启动低频 Fallback 让位监听协程 (Interval=0.8s)");
		_fallbackMonitorCoroutine = StartCoroutine(FallbackMonitorRoutine());
	}

	private void StopFallbackMonitor()
	{
		if (_fallbackMonitorCoroutine != null)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Fallback.MonitorStop] 停止 Fallback 让位监听协程");
			StopCoroutine(_fallbackMonitorCoroutine);
			_fallbackMonitorCoroutine = null;
		}
	}

	private IEnumerator FallbackMonitorRoutine()
	{
		WaitForSeconds wait = new WaitForSeconds(0.8f);
		while (_temporaryFallback != null && _temporaryFallbackObject != null)
		{
			yield return wait;
			ReconcileEventSystemOwnership("MonitorPolling");
		}
		_fallbackMonitorCoroutine = null;
	}

	#endregion

	#region 场景生命周期监听

	private void SubscribeSceneWatcher()
	{
		if (_sceneSubscribed)
		{
			return;
		}
		try
		{
			APXSceneWatcher.SceneLoaded += OnSceneLoaded;
			APXSceneWatcher.ActiveSceneChanged += OnActiveSceneChanged;
			_sceneSubscribed = true;
		}
		catch (Exception ex)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CentralInput.SubscribeError] 挂接场景生命周期信号异常: {ex.Message}");
		}
	}

	private void UnsubscribeSceneWatcher()
	{
		if (!_sceneSubscribed)
		{
			return;
		}
		try
		{
			APXSceneWatcher.SceneLoaded -= OnSceneLoaded;
			APXSceneWatcher.ActiveSceneChanged -= OnActiveSceneChanged;
			_sceneSubscribed = false;
		}
		catch
		{
		}
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		string sceneName = scene.name ?? "<null>";
		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SceneSignal] SceneLoaded Name={sceneName}, Mode={mode}");
		ResetTransientInputState($"SceneLoaded_{sceneName}");
		ReconcileEventSystemOwnership($"SceneLoaded_{sceneName}");
		EventSystemDiagnostics.DumpCurrentTopology($"SceneLoaded_{sceneName}");
	}

	private void OnActiveSceneChanged(Scene from, Scene to)
	{
		string fromName = from.name ?? "<null>";
		string toName = to.name ?? "<null>";
		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SceneSignal] ActiveSceneChanged From='{fromName}' To='{toName}'");
		ResetTransientInputState($"ActiveSceneChanged_{toName}");
		ReconcileEventSystemOwnership($"ActiveSceneChanged_{toName}");
		EventSystemDiagnostics.DumpCurrentTopology($"ActiveSceneChanged_{toName}");
	}

	#endregion

	#region 热键协调与文本输入门禁 (Hotkey Coordination & Text Entry Gate, Phase 1B.2B.3A)

	/// <summary>
	/// 懒清理单帧热键消费状态 (零 Update 轮询开销，仅在按键查询时惰性更新)
	/// </summary>
	private void EnsureFrameState()
	{
		int currentFrame = Time.frameCount;
		if (_lastHotkeyFrame != currentFrame)
		{
			_lastHotkeyFrame = currentFrame;
			_consumedKeysThisFrame.Clear();
		}
	}

	/// <summary>
	/// 兼容既有签名：快捷键权限查询接口 (默认不允许在文本输入期间触发)
	/// </summary>
	public bool CanProcessHotkey(string featureId, KeyCode key)
	{
		return CanProcessHotkeyInternal(featureId, false);
	}

	/// <summary>
	/// 热键权限综合门禁判定
	/// </summary>
	/// <param name="owner">请求方功能标识</param>
	/// <param name="allowDuringTextEntry">是否允许在文本输入激活期间穿透</param>
	/// <returns>是否允许处理热键</returns>
	internal static bool CanProcessHotkey(string owner, bool allowDuringTextEntry = false)
	{
		APXCentralInputManager manager = Instance;
		if (manager != null)
		{
			return manager.CanProcessHotkeyInternal(owner, allowDuringTextEntry);
		}
		return true;
	}

	internal bool CanProcessHotkeyInternal(string owner, bool allowDuringTextEntry = false)
	{
		switch (_captureMode)
		{
			case APXInputCaptureMode.None:
			case APXInputCaptureMode.PointerOnly:
				break;
			case APXInputCaptureMode.KeyboardOnly:
			case APXInputCaptureMode.PointerAndKeyboard:
				return false;
			case APXInputCaptureMode.TextEntry:
				if (!allowDuringTextEntry)
				{
					return false;
				}
				break;
			case APXInputCaptureMode.Modal:
				if (!allowDuringTextEntry)
				{
					return false;
				}
				break;
		}

		if (!allowDuringTextEntry && IsTextEntryActive)
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// 尝试消费离散按键按下事件 (按键级别单帧防重与冲突协调)
	/// </summary>
	/// <param name="owner">功能归属方标识</param>
	/// <param name="key">目标 KeyCode</param>
	/// <param name="allowDuringTextEntry">是否允许在打字输入时穿透</param>
	/// <returns>若本帧首次命中 GetKeyDown 且通过门禁则返回 true (成功消费)</returns>
	internal static bool TryConsumeHotkey(string owner, KeyCode key, bool allowDuringTextEntry = false)
	{
		return TryConsumeHotkey(APXHotkeyAction.None, owner, key, allowDuringTextEntry);
	}

	/// <summary>
	/// 尝试消费离散按键按下事件 (支持语义动作映射)
	/// </summary>
	internal static bool TryConsumeHotkey(APXHotkeyAction action, string owner, KeyCode key, bool allowDuringTextEntry = false)
	{
		if (key == KeyCode.None)
		{
			return false;
		}

		APXCentralInputManager manager = Instance;
		if (manager == null)
		{
			if (APXRuntimeHost.Instance != null)
			{
				manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
			}
		}

		if (manager != null)
		{
			return manager.TryConsumeHotkeyInternal(action, owner, key, allowDuringTextEntry);
		}

		return Input.GetKeyDown(key);
	}

	private bool TryConsumeHotkeyInternal(APXHotkeyAction action, string owner, KeyCode key, bool allowDuringTextEntry)
	{
		EnsureFrameState();

		if (!Input.GetKeyDown(key))
		{
			return false;
		}

		string ownerTag = !string.IsNullOrEmpty(owner) ? owner : action.ToString();

		if (!CanProcessHotkeyInternal(ownerTag, allowDuringTextEntry))
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Hotkey.Blocked] Owner='{ownerTag}', Key={key}, Action={action}, Reason=GatedByCaptureMode(Mode={_captureMode},TextActive={IsTextEntryActive})");
			return false;
		}

		if (_consumedKeysThisFrame.Contains(key))
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Hotkey.Blocked] Owner='{ownerTag}', Key={key}, Action={action}, Reason=AlreadyConsumedThisFrame");
			return false;
		}

		_consumedKeysThisFrame.Add(key);
		_leftCtrlChordUsed = true;
		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Hotkey.Consumed] Owner='{ownerTag}', Key={key}, Action={action}");
		return true;
	}

	/// <summary>
	/// 获取当前物理键盘处于按住状态的修饰键掩码 (支持左右修饰键, Phase 1B.2B.3B)
	/// </summary>
	internal static APXHotkeyModifiers GetCurrentModifiers()
	{
		APXHotkeyModifiers mods = APXHotkeyModifiers.None;
		if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
		{
			mods |= APXHotkeyModifiers.Ctrl;
		}
		if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
		{
			mods |= APXHotkeyModifiers.Shift;
		}
		if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
		{
			mods |= APXHotkeyModifiers.Alt;
		}
		return mods;
	}

	/// <summary>
	/// 尝试消费组合按键 (Chord Hotkey, Phase 1B.2B.3B)
	/// </summary>
	/// <param name="owner">功能归属方标识</param>
	/// <param name="key">主按键 KeyCode</param>
	/// <param name="modifiers">所需修饰键掩码 (Ctrl / Shift / Alt)</param>
	/// <param name="exactModifiers">是否严格匹配修饰键 (若为 true 则禁止多余修饰键)</param>
	/// <param name="allowDuringTextEntry">是否允许在文本输入激活期间穿透</param>
	/// <returns>若本帧成功命中组合键且通过门禁则返回 true</returns>
	internal static bool TryConsumeHotkeyChord(
		string owner,
		KeyCode key,
		APXHotkeyModifiers modifiers,
		bool exactModifiers = true,
		bool allowDuringTextEntry = false)
	{
		return TryConsumeHotkeyChord(APXHotkeyAction.None, owner, key, modifiers, exactModifiers, allowDuringTextEntry);
	}

	/// <summary>
	/// 尝试消费组合按键 (Chord Hotkey，支持动作意图, Phase 1B.2B.3B)
	/// </summary>
	internal static bool TryConsumeHotkeyChord(
		APXHotkeyAction action,
		string owner,
		KeyCode key,
		APXHotkeyModifiers modifiers,
		bool exactModifiers = true,
		bool allowDuringTextEntry = false)
	{
		if (key == KeyCode.None)
		{
			return false;
		}

		APXCentralInputManager manager = Instance;
		if (manager == null)
		{
			if (APXRuntimeHost.Instance != null)
			{
				manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
			}
		}

		if (manager != null)
		{
			return manager.TryConsumeHotkeyChordInternal(action, owner, key, modifiers, exactModifiers, allowDuringTextEntry);
		}

		// 安全回退 (若 Instance 暂不可用)
		if (!Input.GetKeyDown(key))
		{
			return false;
		}
		APXHotkeyModifiers currentMods = GetCurrentModifiers();
		bool match = exactModifiers ? (currentMods == modifiers) : ((currentMods & modifiers) == modifiers);
		return match;
	}

	private bool TryConsumeHotkeyChordInternal(
		APXHotkeyAction action,
		string owner,
		KeyCode key,
		APXHotkeyModifiers modifiers,
		bool exactModifiers,
		bool allowDuringTextEntry)
	{
		EnsureFrameState();

		if (!Input.GetKeyDown(key))
		{
			return false;
		}

		APXHotkeyModifiers currentMods = GetCurrentModifiers();
		bool modifiersMatch = exactModifiers ? (currentMods == modifiers) : ((currentMods & modifiers) == modifiers);
		if (!modifiersMatch)
		{
			return false;
		}

		string ownerTag = !string.IsNullOrEmpty(owner) ? owner : action.ToString();

		if (!CanProcessHotkeyInternal(ownerTag, allowDuringTextEntry))
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Hotkey.Blocked] Owner='{ownerTag}', Key={key}, Modifiers={modifiers}, Action={action}, Reason=GatedByCaptureMode(Mode={_captureMode},TextActive={IsTextEntryActive})");
			return false;
		}

		if (_consumedKeysThisFrame.Contains(key))
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Hotkey.Blocked] Owner='{ownerTag}', Key={key}, Modifiers={modifiers}, Action={action}, Reason=AlreadyConsumedThisFrame");
			return false;
		}

		_consumedKeysThisFrame.Add(key);
		_leftCtrlChordUsed = true;
		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Hotkey.Consumed] Owner='{ownerTag}', Key={key}, Modifiers={modifiers}, Action={action}");
		return true;
	}

	/// <summary>
	/// 尝试消费 Left Ctrl 独立单击 (Standalone Tap) 事件 (Phase 2D-8C-R6B-P1B)
	/// 严格保护组合键 (如 Ctrl+Shift+H 或任意其它按键同按时均视作 Chord，决不触发)
	/// 严格遵循 CentralInput 门禁 (TextEntry 与 Modal 期间绝对屏蔽)
	/// </summary>
	internal static bool TryConsumeLeftCtrlTap(string owner = "GameSpeedControllerGUI")
	{
		APXCentralInputManager manager = Instance;
		if (manager == null && APXRuntimeHost.Instance != null)
		{
			manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
		}

		if (manager != null)
		{
			return manager.TryConsumeLeftCtrlTapInternal(owner);
		}

		return false;
	}

	internal bool TryConsumeLeftCtrlTapInternal(string owner)
	{
		// 1. KeyDown on LeftControl
		if (Input.GetKeyDown(KeyCode.LeftControl))
		{
			_leftCtrlDown = true;
			_leftCtrlChordUsed = false;

			// 若按下 LeftControl 时已按下了其它修饰键，则判定为组合键序，直接标记破坏
			if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
			    Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
			    Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows))
			{
				_leftCtrlChordUsed = true;
			}
		}

		// 2. 在 LeftControl 按住期间，若检测到任何其它按键按下，标记破坏独立单击
		if (_leftCtrlDown)
		{
			if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.LeftControl))
			{
				_leftCtrlChordUsed = true;
			}
		}

		// 3. KeyUp on LeftControl - 独立释放时判定消费
		if (Input.GetKeyUp(KeyCode.LeftControl))
		{
			bool wasStandalone = _leftCtrlDown && !_leftCtrlChordUsed;
			_leftCtrlDown = false;
			_leftCtrlChordUsed = false;

			if (wasStandalone)
			{
				if (CanProcessHotkeyInternal(owner, allowDuringTextEntry: false))
				{
					EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [LeftCtrl.Consumed] Standalone tap consumed by Owner='{owner}'");
					return true;
				}
				else
				{
					EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [LeftCtrl.Blocked] Owner='{owner}', Reason=GatedByCaptureMode(Mode={_captureMode},TextActive={IsTextEntryActive})");
				}
			}
		}

		return false;
	}

	/// <summary>
	/// 申请独占或限制性键盘输入捕获
	/// </summary>
	internal static bool AcquireKeyboardCapture(string owner, APXInputCaptureMode mode)
	{
		APXCentralInputManager manager = Instance;
		if (manager == null)
		{
			if (APXRuntimeHost.Instance != null)
			{
				manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
			}
		}

		if (manager != null)
		{
			return manager.AcquireCaptureInternal(owner, mode);
		}

		return false;
	}

	private bool AcquireCaptureInternal(string owner, APXInputCaptureMode mode)
	{
		if (mode == APXInputCaptureMode.None)
		{
			return ReleaseCaptureInternal(owner);
		}

		if (_captureMode != APXInputCaptureMode.None && !string.IsNullOrEmpty(_captureOwner) && _captureOwner != owner)
		{
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [InputCapture.AcquireBlocked] Requester='{owner}', Mode={mode}, CurrentOwner='{_captureOwner}', CurrentMode={_captureMode}");
			return false;
		}

		_captureMode = mode;
		_captureOwner = owner;
		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [InputCapture.Acquire] Owner='{owner}', Mode={mode}");
		return true;
	}

	/// <summary>
	/// 释放键盘输入捕获
	/// </summary>
	internal static bool ReleaseKeyboardCapture(string owner)
	{
		APXCentralInputManager manager = Instance;
		if (manager != null)
		{
			return manager.ReleaseCaptureInternal(owner);
		}

		return false;
	}

	private bool ReleaseCaptureInternal(string owner)
	{
		if (_captureMode == APXInputCaptureMode.None && string.IsNullOrEmpty(_captureOwner))
		{
			return true;
		}

		if (string.IsNullOrEmpty(_captureOwner) || _captureOwner == owner)
		{
			APXInputCaptureMode oldMode = _captureMode;
			_captureMode = APXInputCaptureMode.None;
			_captureOwner = null;
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [InputCapture.Release] Owner='{owner}', ReleasedMode={oldMode}");
			return true;
		}

		EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [InputCapture.ReleaseRejected] Requester='{owner}', ActualOwner='{_captureOwner}', CurrentMode={_captureMode}");
		return false;
	}

	/// <summary>
	/// 重置临时输入捕获与单帧状态 (场景加载/卸载或宿主重置时触发，防止锁死)
	/// </summary>
	internal static void ResetTransientInputState(string reason)
	{
		APXCentralInputManager manager = Instance;
		if (manager != null)
		{
			manager.ResetTransientStateInternal(reason);
		}
	}

	private void ResetTransientStateInternal(string reason)
	{
		if (_captureMode != APXInputCaptureMode.None || !string.IsNullOrEmpty(_captureOwner))
		{
			string oldOwner = _captureOwner;
			APXInputCaptureMode oldMode = _captureMode;
			_captureMode = APXInputCaptureMode.None;
			_captureOwner = null;
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [InputCapture.Reset] Reason='{reason}', OldOwner='{oldOwner}', OldMode={oldMode}");
		}

		_consumedKeysThisFrame.Clear();
		_lastHotkeyFrame = -1;
	}

	/// <summary>
	/// 显式开启文本输入捕获 (门禁上游所有非穿透热键)
	/// </summary>
	internal static bool BeginTextEntry(string owner)
	{
		return AcquireKeyboardCapture(owner, APXInputCaptureMode.TextEntry);
	}

	/// <summary>
	/// 显式结束文本输入捕获
	/// </summary>
	internal static bool EndTextEntry(string owner)
	{
		return ReleaseKeyboardCapture(owner);
	}

	/// <summary>
	/// 静态文本输入状态检测探针
	/// </summary>
	internal static bool IsTextEntryActiveStatic()
	{
		APXCentralInputManager manager = Instance;
		if (manager != null)
		{
			return manager.IsTextEntryActive;
		}

		try
		{
			EventSystem es = EventSystem.current;
			if (es != null && es.currentSelectedGameObject != null)
			{
				UnityEngine.UI.InputField inputField = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
				if (inputField != null && inputField.isFocused)
				{
					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	#endregion

	#region 指针权限查询门禁 (Pointer Permission Foundation, Phase 1B.2B.4B-R2)

	private static bool _overlayExceptionLogged;
	private static bool _uguiExceptionLogged;

	private static EventSystem _cachedRaycastEventSystem;
	private static PointerEventData _cachedPointerEventData;
	private static readonly List<RaycastResult> _scratchRaycastHits = new List<RaycastResult>(16);

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	private static void LogOverlayExceptionOnce(string consumer, Exception ex)
	{
		if (!_overlayExceptionLogged)
		{
			_overlayExceptionLogged = true;
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Pointer.OverlayException] Consumer='{consumer}', Msg={ex.Message}");
		}
	}

	private static void LogUguiExceptionOnce(string consumer, Exception ex)
	{
		if (!_uguiExceptionLogged)
		{
			_uguiExceptionLogged = true;
			EventSystemDiagnostics.LogDirect($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Pointer.UguiException] Consumer='{consumer}', Msg={ex.Message}");
		}
	}

	/// <summary>
	/// 显式屏幕坐标 UGUI 射线检测 (Phase 1B.2B.4B-R2, 对齐旧 MapCameraNative.TouchBlocked: 仅当命中美工射线检测器 GraphicRaycaster 时才视作 UGUI 阻断)
	/// 纯临时 Scratch Buffer 复用，0 堆内存分配，不维护任何指针生命周期状态机
	/// </summary>
	private static bool CheckGraphicRaycastBlocked(EventSystem currentEs, int pointerId, Vector2 screenPosition)
	{
		if (currentEs == null)
		{
			return false;
		}

		if (_cachedRaycastEventSystem != currentEs || _cachedPointerEventData == null)
		{
			_cachedRaycastEventSystem = currentEs;
			_cachedPointerEventData = new PointerEventData(currentEs);
		}

		_cachedPointerEventData.Reset();
		_cachedPointerEventData.pointerId = pointerId;
		_cachedPointerEventData.position = screenPosition;

		try
		{
			_scratchRaycastHits.Clear();
			currentEs.RaycastAll(_cachedPointerEventData, _scratchRaycastHits);
			for (int i = 0; i < _scratchRaycastHits.Count; i++)
			{
				if (_scratchRaycastHits[i].module is GraphicRaycaster)
				{
					return true;
				}
			}
			return false;
		}
		finally
		{
			_scratchRaycastHits.Clear();
		}
	}

	/// <summary>
	/// 快捷鼠标指针权限查询接口 (Phase 1B.2B.4B-R2)
	/// 默认读取当前 Input.mousePosition 与鼠标标识 (-1)，使用 EventSystem.IsPointerOverGameObject() 查询
	/// 适用目标：Underlying / Non-APX-Surface Consumers (如 MapCamera)
	/// </summary>
	/// <param name="consumer">请求方标识 (如 "MapCamera")</param>
	/// <param name="action">指针语义动作 (None, Hover, Click, Drag, Scroll)</param>
	/// <returns>若当前允许该 Consumer 处理指针则返回 true；若非法坐标、被遮挡或应让出则返回 false</returns>
	internal static bool CanProcessPointer(string consumer, APXPointerAction action = APXPointerAction.None)
	{
		Vector3 mousePos = Input.mousePosition;
		if (!IsFinite(mousePos.x) || !IsFinite(mousePos.y))
		{
			return false;
		}

		APXCentralInputManager manager = Instance;
		if (manager == null)
		{
			if (APXRuntimeHost.Instance != null)
			{
				manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
			}
		}

		if (manager != null)
		{
			return manager.CanProcessPointerInternal(consumer, action, -1, new Vector2(mousePos.x, mousePos.y), explicitRaycast: false);
		}

		// 宿主未就绪时的安全降级判定 (Fail-Closed 策略)
		float fallbackFlippedY = (Screen.height > 0) ? ((float)Screen.height - mousePos.y) : mousePos.y;
		try
		{
			if (APXOverlayInput.IsBlocked(mousePos.x, fallbackFlippedY, -1))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogOverlayExceptionOnce(consumer, ex);
			return false;
		}

		try
		{
			EventSystem currentEs = EventSystem.current;
			if (currentEs != null && currentEs.IsPointerOverGameObject())
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogUguiExceptionOnce(consumer, ex);
			return false;
		}

		return true;
	}

	/// <summary>
	/// 显式屏幕坐标鼠标指针权限查询接口 (Phase 1B.2B.4B-R2, Explicit Coordinate Query)
	/// 对传入 screenPosition 执行显式 APX Overlay 判定与 UGUI GraphicRaycaster 射线检测
	/// 适用目标：Underlying / Non-APX-Surface Consumers (如 MapCamera)
	/// </summary>
	/// <param name="consumer">请求方标识 (如 "MapCamera")</param>
	/// <param name="action">指针语义动作 (None, Hover, Click, Drag, Scroll)</param>
	/// <param name="screenPosition">Unity 屏幕坐标 (左下角原点)</param>
	/// <returns>若当前允许该 Consumer 处理指针则返回 true；若非法坐标、被遮挡或应让出则返回 false</returns>
	internal static bool CanProcessPointer(string consumer, APXPointerAction action, Vector2 screenPosition)
	{
		if (!IsFinite(screenPosition.x) || !IsFinite(screenPosition.y))
		{
			return false;
		}

		APXCentralInputManager manager = Instance;
		if (manager == null)
		{
			if (APXRuntimeHost.Instance != null)
			{
				manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
			}
		}

		if (manager != null)
		{
			return manager.CanProcessPointerInternal(consumer, action, -1, screenPosition, explicitRaycast: true);
		}

		// 宿主未就绪时的安全降级判定 (Fail-Closed 策略, 显式 GraphicRaycaster 射线检测)
		float fallbackFlippedY = (Screen.height > 0) ? ((float)Screen.height - screenPosition.y) : screenPosition.y;
		try
		{
			if (APXOverlayInput.IsBlocked(screenPosition.x, fallbackFlippedY, -1))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogOverlayExceptionOnce(consumer, ex);
			return false;
		}

		try
		{
			EventSystem currentEs = EventSystem.current;
			if (CheckGraphicRaycastBlocked(currentEs, -1, screenPosition))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogUguiExceptionOnce(consumer, ex);
			return false;
		}

		return true;
	}

	/// <summary>
	/// 触控完整上下文指针权限查询接口 (Phase 1B.2B.4B-R2, Explicit Raycast Parity)
	/// 强制执行触控合法性校验：pointerId (fingerId) 必须 >= 0，且坐标必须有限，非法输入直接 fail-closed 返回 false
	/// 严禁非法 fingerId 自动退化为鼠标查询；对 screenPosition 执行显式 RaycastAll 并严格过滤 GraphicRaycaster
	/// 适用目标：Underlying / Non-APX-Surface Consumers (如 MapCamera Touch)
	/// </summary>
	/// <param name="consumer">请求方标识 (如 "MapCamera")</param>
	/// <param name="action">指针语义动作 (None, Hover, Click, Drag, Scroll)</param>
	/// <param name="pointerId">触摸 fingerId (必须 >= 0)</param>
	/// <param name="screenPosition">触摸屏幕坐标 (左下角原点，必须有限值)</param>
	/// <returns>若合法且允许处理返回 true；若非法输入、被遮挡或让出则返回 false (fail-closed)</returns>
	internal static bool CanProcessPointer(string consumer, APXPointerAction action, int pointerId, Vector2 screenPosition)
	{
		// 触控输入合法性门禁 (对齐旧 MapCameraNative.TouchBlocked: fingerId < 0 或坐标非有限值立即阻断)
		if (pointerId < 0 || !IsFinite(screenPosition.x) || !IsFinite(screenPosition.y))
		{
			return false;
		}

		APXCentralInputManager manager = Instance;
		if (manager == null)
		{
			if (APXRuntimeHost.Instance != null)
			{
				manager = APXRuntimeHost.Instance.gameObject.AddComponent<APXCentralInputManager>();
			}
		}

		if (manager != null)
		{
			return manager.CanProcessPointerInternal(consumer, action, pointerId, screenPosition, explicitRaycast: true);
		}

		// 宿主未就绪时的安全降级判定 (Fail-Closed 策略, 显式 GraphicRaycaster 射线检测)
		float fallbackFlippedY = (Screen.height > 0) ? ((float)Screen.height - screenPosition.y) : screenPosition.y;
		try
		{
			if (APXOverlayInput.IsBlocked(screenPosition.x, fallbackFlippedY, pointerId))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogOverlayExceptionOnce(consumer, ex);
			return false;
		}

		try
		{
			EventSystem currentEs = EventSystem.current;
			if (CheckGraphicRaycastBlocked(currentEs, pointerId, screenPosition))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogUguiExceptionOnce(consumer, ex);
			return false;
		}

		return true;
	}

	/// <summary>
	/// 指针权限内部综合门禁判定 (Phase 1B.2B.4B-R2)
	/// </summary>
	internal bool CanProcessPointerInternal(string consumer, APXPointerAction action, int pointerId, Vector2 screenPosition, bool explicitRaycast = false)
	{
		// 0. 坐标有限值终极门禁防御 (防非法坐标下透)
		if (!IsFinite(screenPosition.x) || !IsFinite(screenPosition.y))
		{
			return false;
		}

		// 1. Capture-Layer Semantics 门禁判定
		bool captureBlocked = false;
		switch (_captureMode)
		{
			case APXInputCaptureMode.None:
			case APXInputCaptureMode.TextEntry:
			case APXInputCaptureMode.KeyboardOnly:
				// None / TextEntry / KeyboardOnly 不阻止指针输入
				captureBlocked = false;
				break;

			case APXInputCaptureMode.PointerOnly:
			case APXInputCaptureMode.PointerAndKeyboard:
			case APXInputCaptureMode.Modal:
				// 仅当当前捕获持有者与调用方 consumer 一致时允许处理，其他 consumer 均阻断
				// 注：Modal 模式目前作为 PROVISIONAL CONTRACT，非 Owner 同样阻断
				if (string.IsNullOrEmpty(_captureOwner) || !string.Equals(_captureOwner, consumer, StringComparison.Ordinal))
				{
					captureBlocked = true;
				}
				break;
		}

		if (captureBlocked)
		{
			return false;
		}

		// 2. APX Overlay 遮挡查询 (包含 IMGUI Surface、FairyGUI Protect 原生面板以及活跃 PointerCapture)
		float flippedY = (Screen.height > 0) ? ((float)Screen.height - screenPosition.y) : screenPosition.y;
		try
		{
			if (APXOverlayInput.IsBlocked(screenPosition.x, flippedY, pointerId))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogOverlayExceptionOnce(consumer, ex);
			// Fail-Closed: 遮挡检测发生未知异常时，安全阻断底层 Consumer，防止误穿透
			return false;
		}

		// 3. UGUI EventSystem 遮挡查询
		try
		{
			EventSystem currentEs = CurrentAvailableEventSystem ?? EventSystem.current;
			if (currentEs != null)
			{
				bool uguiBlocked;
				if (pointerId >= 0 || explicitRaycast)
				{
					// Touch 或显式坐标鼠标：执行针对 screenPosition 的显式射线检测，严格要求 GraphicRaycaster
					uguiBlocked = CheckGraphicRaycastBlocked(currentEs, pointerId, screenPosition);
				}
				else
				{
					// 快捷鼠标：保持原生 IsPointerOverGameObject() 查询
					uguiBlocked = currentEs.IsPointerOverGameObject();
				}

				if (uguiBlocked)
				{
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			LogUguiExceptionOnce(consumer, ex);
			// Fail-Closed: UGUI 检测异常时阻断底层 Consumer，严禁 fail-open
			return false;
		}

		return true;
	}

	#endregion

	/// <summary>
	/// 手动触发诊断快照
	/// </summary>
	public void RequestDump(string reason)
	{
		EventSystemDiagnostics.DumpCurrentTopology(reason ?? "ManualDump");
	}
}

/// <summary>
/// 输入捕获模式枚举
/// </summary>
internal enum APXInputCaptureMode
{
	None = 0,
	PointerOnly = 1,
	KeyboardOnly = 2,
	PointerAndKeyboard = 3,
	TextEntry = 4,
	Modal = 5
}

/// <summary>
/// APX 全局热键意图注册表枚举 (Phase 1B.2B.3A)
/// </summary>
internal enum APXHotkeyAction
{
	None = 0,
	GameSpeedToggle = 1,
	HandCardDebug = 2,
	StaticPortraitToggle = 3,
	ReplayPlayerToggle = 4,
	AssistBallMenuToggle = 5,
	AssistBallEscape = 6,
	MapCameraFreeLook = 7
}

/// <summary>
/// 热键修饰键掩码 (Phase 1B.2B.3B)
/// </summary>
[Flags]
internal enum APXHotkeyModifiers
{
	None = 0,
	Ctrl = 1,
	Shift = 2,
	Alt = 4
}
