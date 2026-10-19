using System;
using System.Threading;
using APXRuntime.Core;
using UnityEngine;

namespace APXRuntime.DataService;

/// <summary>
/// 星引擎只读对局数据服务主组件。
/// 挂载于 APXRuntimeHost 唯一宿主上，随游戏生命周期自动装配。
/// 游戏主线程每 100ms 刷新一次不可变快照，HTTP 后台线程只读快照对外提供 REST API。
/// </summary>
[DisallowMultipleComponent]
public sealed class StarEngineDataService : MonoBehaviour
{
	private const float SnapshotInterval = 0.1f; // 10 Hz 采样频率

	private static StarEngineDataService _instance;
	private LocalDataHttpServer _server;
	private volatile MatchDataSnapshot _latestSnapshot;
	private float _lastSnapshotTime;

	public static StarEngineDataService Instance => _instance;

	public MatchDataSnapshot CurrentSnapshot => _latestSnapshot;

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<StarEngineDataService>() == null)
		{
			host.AddComponent<StarEngineDataService>();
			APXRuntimeHost.LogLifecycle("[DataService] StarEngineDataService mounted to runtime host.");
		}
	}

	private void Awake()
	{
		if (_instance != null && _instance != this)
		{
			Destroy(this);
			return;
		}

		_instance = this;
		_latestSnapshot = MatchDataSnapshot.CreateNoMatch();

		_server = new LocalDataHttpServer(() => _latestSnapshot);
		bool started = _server.Start();

		APXRuntimeHost.LogLifecycle($"[DataService] StarEngineDataService Awake completed. HttpStarted={started}");
	}

	private void LateUpdate()
	{
		if (Time.unscaledTime - _lastSnapshotTime < SnapshotInterval)
		{
			return;
		}

		_lastSnapshotTime = Time.unscaledTime;

		try
		{
			MatchDataSnapshot next = SnapshotBuilder.Build();
			Interlocked.Exchange(ref _latestSnapshot, next);
		}
		catch (Exception ex)
		{
			APXRuntimeHost.LogLifecycle($"[DataService] SnapshotBuilder exception: {ex.Message}");
		}
	}

	private void OnDestroy()
	{
		if (_server != null)
		{
			_server.Dispose();
			_server = null;
		}

		if (_instance == this)
		{
			_instance = null;
		}

		APXRuntimeHost.LogLifecycle("[DataService] StarEngineDataService destroyed, HTTP listener stopped.");
	}
}
