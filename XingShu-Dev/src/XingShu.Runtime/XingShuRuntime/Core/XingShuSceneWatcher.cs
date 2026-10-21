using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XingShuRuntime.Core;

[DisallowMultipleComponent]
internal sealed class XingShuSceneWatcher : MonoBehaviour
{
	public static event Action<Scene, LoadSceneMode> SceneLoaded;

	public static event Action<Scene> SceneUnloaded;

	public static event Action<Scene, Scene> ActiveSceneChanged;

	private bool _subscribed;

	private void Awake()
	{
		Subscribe();
	}

	private void OnEnable()
	{
		Subscribe();
	}

	private void OnDisable()
	{
		Unsubscribe();
	}

	private void OnDestroy()
	{
		Unsubscribe();
	}

	private void Subscribe()
	{
		if (_subscribed)
		{
			return;
		}
		try
		{
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
			UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
			UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
			_subscribed = true;
			RuntimeCompatibilityHost.LogLifecycle("[SceneWatcher.Subscribe] 已成功挂接 UnityEngine.SceneManagement.SceneManager 场景生命周期事件");
		}
		catch (Exception ex)
		{
			RuntimeCompatibilityHost.LogLifecycle($"[SceneWatcher.Subscribe] 挂接场景生命周期事件异常: {ex.Message}");
		}
	}

	private void Unsubscribe()
	{
		if (!_subscribed)
		{
			return;
		}
		try
		{
			UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
			UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
			UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
			_subscribed = false;
			RuntimeCompatibilityHost.LogLifecycle("[SceneWatcher.Unsubscribe] 已解绑场景生命周期事件");
		}
		catch
		{
		}
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		string text = scene.name ?? "<null>";
		int buildIndex = scene.buildIndex;
		RuntimeCompatibilityHost.LogLifecycle($"[SceneLoaded] Name={text}, BuildIndex={buildIndex}, Mode={mode}");
		try
		{
			XingShuSceneWatcher.SceneLoaded?.Invoke(scene, mode);
		}
		catch (Exception ex)
		{
			RuntimeCompatibilityHost.LogLifecycle($"[SceneLoaded.DispatchError] 广播场景加载事件异常: {ex.Message}");
		}
	}

	private static void OnSceneUnloaded(Scene scene)
	{
		string text = scene.name ?? "<null>";
		int buildIndex = scene.buildIndex;
		RuntimeCompatibilityHost.LogLifecycle($"[SceneUnloaded] Name={text}, BuildIndex={buildIndex}");
		try
		{
			XingShuSceneWatcher.SceneUnloaded?.Invoke(scene);
		}
		catch (Exception ex)
		{
			RuntimeCompatibilityHost.LogLifecycle($"[SceneUnloaded.DispatchError] 广播场景卸载事件异常: {ex.Message}");
		}
	}

	private static void OnActiveSceneChanged(Scene from, Scene to)
	{
		string text = from.name ?? "<null>";
		string text2 = to.name ?? "<null>";
		RuntimeCompatibilityHost.LogLifecycle($"[ActiveSceneChanged] From='{text}' (Index={from.buildIndex}) -> To='{text2}' (Index={to.buildIndex})");
		try
		{
			XingShuSceneWatcher.ActiveSceneChanged?.Invoke(from, to);
		}
		catch (Exception ex)
		{
			RuntimeCompatibilityHost.LogLifecycle($"[ActiveSceneChanged.DispatchError] 广播活动场景变更事件异常: {ex.Message}");
		}
	}
}
