using System;
using System.IO;
using System.Text;
using UnityEngine;
using XingShu.Runtime;

namespace APXRuntime.Core;

[DisallowMultipleComponent]
internal sealed class APXRuntimeHost : MonoBehaviour
{
	private const string LogFileName = "apx_runtime_lifecycle.log";

	private static string _logPath;

	private static readonly object LogLock = new object();

	public static APXRuntimeHost Instance { get; private set; }

	public static string HostName => "[APX] Utility Runtime";

	/// <summary>
	/// 兼容属性：获取关联的星枢规范运行时宿主
	/// </summary>
	public XingShuRuntimeHost XingShuHost => XingShuRuntimeHost.Instance;

	private void Awake()
	{
		if (Instance == null)
		{
			Instance = this;
			UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
			LogLifecycle($"[RuntimeHost.Awake] 强类型唯一宿主已启动; GameObject='{base.gameObject.name}', InstanceId={base.gameObject.GetInstanceID()}");

			// 确保挂载规范星枢运行时组件并完成初始化
			if (base.GetComponent<XingShuRuntimeHost>() == null)
			{
				base.gameObject.AddComponent<XingShuRuntimeHost>();
			}
		}
		else if (Instance == this)
		{
			LogLifecycle("[RuntimeHost.Awake] Instance == this 重复唤醒检测");
		}
		else if (Instance.gameObject == base.gameObject)
		{
			LogLifecycle($"[RuntimeHost.Awake] 检测到同宿主重复挂载 APXRuntimeHost 组件，已安全销毁重复组件 (ComponentId={base.GetInstanceID()})");
			UnityEngine.Object.Destroy(this);
		}
		else
		{
			LogLifecycle($"[RuntimeHost.Awake] 检测到异地重复宿主 GameObject 尝试创建 (InstanceId={base.gameObject.GetInstanceID()})，已安全销毁重复 GameObject");
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			LogLifecycle($"[RuntimeHost.OnDestroy] 宿主正在销毁 (InstanceId={base.gameObject.GetInstanceID()})，重置单例引用");
			Instance = null;
		}
	}

	internal static void LogLifecycle(string message)
	{
		try
		{
			if (string.IsNullOrEmpty(_logPath))
			{
				_logPath = Path.Combine(Application.persistentDataPath, LogFileName);
			}
			string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
			lock (LogLock)
			{
				File.AppendAllText(_logPath, line, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}
}
