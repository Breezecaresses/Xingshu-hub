using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace APXRuntime.Core;

/// <summary>
/// EventSystem 运行时只读拓扑诊断服务 (Phase 1B.2B.1)
/// 严格只读：绝不创建、销毁、启用、禁用、重亲和或修改任何 EventSystem 实例。
/// </summary>
internal static class EventSystemDiagnostics
{
	private const string LogFileName = "apx_input_topology.log";

	private static string _logPath;

	private static readonly object LogLock = new object();

	/// <summary>
	/// 对当前场景中的 EventSystem 及 UGUI Canvas 拓扑执行一次性只读快照扫描
	/// 严禁在 Update 中高频调用，仅在启动、场景切换完成或手动调试时触发。
	/// </summary>
	/// <param name="reason">触发诊断的原因说明</param>
	public static void DumpCurrentTopology(string reason)
	{
		try
		{
			StringBuilder sb = new StringBuilder(2048);
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

			// 1. 只读扫描当前激活的 EventSystem 实例
			EventSystem[] systems;
			try
			{
				systems = UnityEngine.Object.FindObjectsOfType<EventSystem>();
			}
			catch (Exception ex)
			{
				LogDirect($"[{timestamp}] [EventSystem.DumpError] FindObjectsOfType<EventSystem> 异常: {ex.Message}");
				return;
			}

			int count = systems != null ? systems.Length : 0;
			sb.AppendLine($"[{timestamp}] [EventSystem.Dump] Reason={reason}; ScanScope=Active Objects Only; TotalCount={count}");

			EventSystem currentStatic = EventSystem.current;
			string currentStaticDesc = currentStatic != null 
				? $"{currentStatic.gameObject.name} (InstanceId={currentStatic.gameObject.GetInstanceID()})" 
				: "<null>";
			sb.AppendLine($"[{timestamp}] [EventSystem.StaticCurrent] Current='{currentStaticDesc}'");

			bool ownedFallbackPresent = false;
			int ownedFallbackId = 0;
			bool currentIsOwnedFallback = false;

			APXCentralInputManager inputMgr = APXCentralInputManager.Instance;
			if (inputMgr != null)
			{
				ownedFallbackPresent = inputMgr.OwnedFallbackPresent;
				ownedFallbackId = inputMgr.OwnedFallbackInstanceId;
				currentIsOwnedFallback = inputMgr.CurrentIsOwnedFallback;
			}

			int externalCount = 0;
			if (systems != null)
			{
				for (int s = 0; s < systems.Length; s++)
				{
					EventSystem es = systems[s];
					if (es != null && es.gameObject != null && (ownedFallbackId == 0 || es.gameObject.GetInstanceID() != ownedFallbackId))
					{
						externalCount++;
					}
				}
			}

			sb.AppendLine($"[{timestamp}] [Fallback.Diagnostic] OwnedFallbackPresent={ownedFallbackPresent}, OwnedFallbackInstanceId={ownedFallbackId}, ExternalEventSystemCount={externalCount}, CurrentIsOwnedFallback={currentIsOwnedFallback}");
			if (inputMgr != null)
			{
				sb.AppendLine($"[{timestamp}] [InputCapture.Diagnostic] Mode={inputMgr.CurrentCaptureMode}, Owner='{inputMgr.CurrentCaptureOwner ?? "<none>"}', TextActive={inputMgr.IsTextEntryActive}");
			}

			if (systems != null && systems.Length > 0)
			{
				for (int i = 0; i < systems.Length; i++)
				{
					EventSystem es = systems[i];
					if (es == null)
					{
						sb.AppendLine($"[{timestamp}] [EventSystem.Instance] Index={i}, Object=<null / destroyed>");
						continue;
					}

					GameObject go = es.gameObject;
					string goName = go != null ? go.name : "<null>";
					int goId = go != null ? go.GetInstanceID() : 0;
					int compId = es.GetInstanceID();

					bool activeSelf = go != null && go.activeSelf;
					bool activeInHierarchy = go != null && go.activeInHierarchy;
					bool enabled = es.enabled;
					bool isActiveAndEnabled = es.isActiveAndEnabled;
					bool isCurrent = (currentStatic == es);

					// 当前选中控件
					GameObject selected = es.currentSelectedGameObject;
					string selectedDesc = selected != null 
						? $"{selected.name} (Id={selected.GetInstanceID()})" 
						: "<null>";

					// DDOL 归属判定
					string ddolDesc = "UNKNOWN";
					if (go != null)
					{
						try
						{
							string sceneName = go.scene.name;
							if (sceneName == "DontDestroyOnLoad")
							{
								ddolDesc = "YES";
							}
							else if (!string.IsNullOrEmpty(sceneName))
							{
								ddolDesc = $"NO (Scene='{sceneName}')";
							}
						}
						catch
						{
							ddolDesc = "UNKNOWN";
						}
					}

					// 当前输入模块与同对象上的所有 BaseInputModule
					string curModuleDesc = es.currentInputModule != null 
						? es.currentInputModule.GetType().Name 
						: "<null>";

					StringBuilder modulesDesc = new StringBuilder(128);
					if (go != null)
					{
						BaseInputModule[] allModules = go.GetComponents<BaseInputModule>();
						if (allModules != null && allModules.Length > 0)
						{
							for (int m = 0; m < allModules.Length; m++)
							{
								if (m > 0) modulesDesc.Append(", ");
								BaseInputModule bim = allModules[m];
								if (bim != null)
								{
									modulesDesc.Append($"{bim.GetType().Name}(enabled={bim.enabled})");
								}
								else
								{
									modulesDesc.Append("<null>");
								}
							}
						}
						else
						{
							modulesDesc.Append("None");
						}
					}
					else
					{
						modulesDesc.Append("None");
					}

					sb.AppendLine($"[{timestamp}] [EventSystem.Instance] Index={i}, Name='{goName}', GameObjectId={goId}, ComponentId={compId}, activeSelf={activeSelf}, activeInHierarchy={activeInHierarchy}, enabled={enabled}, isActiveAndEnabled={isActiveAndEnabled}, isCurrent={isCurrent}, currentSelected='{selectedDesc}', DDOL={ddolDesc}, currentInputModule='{curModuleDesc}', Modules=[{modulesDesc}]");
				}
			}

			// 2. 额外辅助扫描：当前激活的 Canvas 与 GraphicRaycaster
			try
			{
				Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
				int canvasCount = canvases != null ? canvases.Length : 0;
				sb.AppendLine($"[{timestamp}] [Canvas.Summary] TotalCanvas={canvasCount}");

				if (canvases != null && canvases.Length > 0)
				{
					for (int c = 0; c < canvases.Length; c++)
					{
						Canvas cv = canvases[c];
						if (cv == null) continue;

						string cName = cv.gameObject != null ? cv.gameObject.name : "<null>";
						bool cActive = cv.gameObject != null && cv.gameObject.activeInHierarchy;
						RenderMode rMode = cv.renderMode;

						GraphicRaycaster gr = cv.GetComponent<GraphicRaycaster>();
						string grDesc = gr != null 
							? (gr.enabled ? "Enabled" : "Disabled") 
							: "None";

						sb.AppendLine($"[{timestamp}] [Canvas.Instance] Index={c}, Name='{cName}', RenderMode={rMode}, activeInHierarchy={cActive}, GraphicRaycaster={grDesc}");
					}
				}
			}
			catch (Exception ex)
			{
				sb.AppendLine($"[{timestamp}] [Canvas.SummaryError] 扫描 Canvas 列表异常: {ex.Message}");
			}

			// 写入日志文件 (Best-Effort)
			LogDirect(sb.ToString().TrimEnd());
		}
		catch
		{
			// 诊断层异常绝不向外抛出
		}
	}

	internal static void LogDirect(string message)
	{
		try
		{
			if (string.IsNullOrEmpty(_logPath))
			{
				_logPath = Path.Combine(Application.persistentDataPath, LogFileName);
			}
			string line = message + Environment.NewLine;
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
