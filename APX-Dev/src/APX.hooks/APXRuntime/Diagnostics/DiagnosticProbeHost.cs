using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace APXRuntime.Diagnostics
{
    /// <summary>
    /// APX 运行时受控探针宿主 (Phase 2D-6C)
    /// 统一管理 MonsterContextProbe 与 LocalHandCardProbe 的装配与生命周期。
    /// 普通 Release 构建下严格保持 OFF (IsEnabled = false)。
    /// 仅在显式定义 APX_DIAGNOSTIC_PROBE 编译符号的测试构建中激活 (IsEnabled = true)。
    /// 日志与文件输出至 Application.persistentDataPath/APX/diagnostics/，绝不污染系统与工程目录。
    /// </summary>
#if APX_DIAGNOSTIC_PROBE || STANDALONE_PROBE
    [DisallowMultipleComponent]
    public sealed class DiagnosticProbeHost : MonoBehaviour
    {
        private static DiagnosticProbeHost _instance;

        public static bool IsEnabled
        {
            get
            {
#if APX_DIAGNOSTIC_PROBE
                return true;
#else
                return false;
#endif
            }
        }

        public static bool ExportFiles { get; set; } = true;

        public static string SessionId { get; private set; } = "";
        public static string SessionLogDir { get; private set; } = "";

        public static DiagnosticProbeHost Instance => _instance;

        public static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<DiagnosticProbeHost>() == null)
            {
                host.AddComponent<DiagnosticProbeHost>();
                if (host.GetComponent<MonsterContextProbe>() == null)
                {
                    host.AddComponent<MonsterContextProbe>();
                }
#if APX_DIAGNOSTIC_PROBE || STANDALONE_PROBE
                if (host.GetComponent<LocalHandCardProbe>() == null)
                {
                    host.AddComponent<LocalHandCardProbe>();
                }
#endif
                if (host.GetComponent<MonsterImmediateEdgeProbe>() == null)
                {
                    host.AddComponent<MonsterImmediateEdgeProbe>();
                }
                if (host.GetComponent<XingShu.SpectatorTelemetry.SpectatorEventInterceptor>() == null)
                {
                    XingShu.SpectatorTelemetry.SpectatorEventInterceptor.Initialize(host);
                }
                Debug.Log($"[APX Diagnostics] DiagnosticProbeHost 已装配 (Compile Symbol Enabled: {IsEnabled})。");
            }
        }

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 初始化测试会话输出目录 (使用 Application.persistentDataPath 安全可写路径)
        /// </summary>
        public static void EnsureSessionDirectory()
        {
            if (!IsEnabled || !ExportFiles)
            {
                return;
            }

            if (string.IsNullOrEmpty(SessionId))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                SessionId = "2d6c_" + timestamp;
                try
                {
                    string targetRoot = Path.Combine(Application.persistentDataPath, "APX", "diagnostics", SessionId);
                    SessionLogDir = targetRoot;
                    Directory.CreateDirectory(targetRoot);
                    Directory.CreateDirectory(Path.Combine(targetRoot, "monster-maps"));

                    string metaFile = Path.Combine(targetRoot, "probe_meta.txt");
                    StringBuilder meta = new StringBuilder();
                    meta.AppendLine("Phase=Phase 2D-6C");
                    meta.AppendLine("Feature=Monster Context & Local HandCard Controlled Runtime Probes");
                    meta.AppendLine($"SessionId={SessionId}");
                    meta.AppendLine($"StartTime={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    meta.AppendLine($"ProbeEnabled={IsEnabled}");
                    meta.AppendLine($"UnityPersistentDataPath={Application.persistentDataPath}");
                    File.WriteAllText(metaFile, meta.ToString(), Encoding.UTF8);

                    UpdateHookStateFile();

                    Debug.Log("[APX Diagnostics] 运行时受控探针会话目录已建立: " + targetRoot);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[APX Diagnostics] 创建探针会话目录异常: " + ex.Message);
                    SessionLogDir = "";
                }
            }
        }

        public static void UpdateHookStateFile()
        {
            if (string.IsNullOrEmpty(SessionLogDir)) return;
            try
            {
                string hookStateFile = Path.Combine(SessionLogDir, "probe_hook_state.txt");
#if APX_DIAGNOSTIC_PROBE || STANDALONE_PROBE
                bool handCardHook = LocalHandCardProbe.IsRpcHooksRegistered;
                bool monsterHook = MonsterContextProbe.IsRpcHookRegistered;
                int installCount = LocalHandCardProbe.InstallCount + MonsterContextProbe.InstallCount;
                int restoreCount = LocalHandCardProbe.RestoreCount + MonsterContextProbe.RestoreCount;
                bool nested = LocalHandCardProbe.NestedDetected || MonsterContextProbe.NestedDetected;
#else
                bool handCardHook = false;
                bool monsterHook = false;
                int installCount = 0;
                int restoreCount = 0;
                bool nested = false;
#endif

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"HandCardInstalled={handCardHook}");
                sb.AppendLine($"MonsterInstalled={monsterHook}");
                sb.AppendLine($"InstallCount={installCount}");
                sb.AppendLine($"RestoreCount={restoreCount}");
                sb.AppendLine($"NestedDetected={nested}");
                sb.AppendLine($"UpdateTime={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                File.WriteAllText(hookStateFile, sb.ToString(), Encoding.UTF8);

                LogDiagnostic("host", "PROBE_HOOK",
                    $"HandCardInstalled={handCardHook} MonsterInstalled={monsterHook} InstallCount={installCount} RestoreCount={restoreCount} NestedDetected={nested}");
            }
            catch
            {
            }
        }

        public static void LogDiagnostic(string channel, string tag, string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            string sanitized = Sanitize(message);
            string fullLog = $"[{tag}] {sanitized}";
            Debug.Log(fullLog);

            if (!ExportFiles || string.IsNullOrEmpty(SessionLogDir))
            {
                return;
            }

            try
            {
                string fileName = (channel == "monster") ? "monster_context.ndjson" : "handcard_probe.ndjson";
                string logFile = Path.Combine(SessionLogDir, fileName);
                string jsonLine = $"{{\"time\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\",\"tag\":\"{tag}\",\"content\":\"{EscapeJson(sanitized)}\"}}" + Environment.NewLine;
                File.AppendAllText(logFile, jsonLine, Encoding.UTF8);

                if (channel == "handcard" && (tag == "HC_LOCAL_PROBE" || tag == "HC_PLAY_REVEAL" || tag == "HC_DRAW" || tag == "HC_DEAL"))
                {
                    string timelineFile = Path.Combine(SessionLogDir, "handcard_event_timeline.csv");
                    if (!File.Exists(timelineFile))
                    {
                        File.WriteAllText(timelineFile, "Time,Tag,Details" + Environment.NewLine, Encoding.UTF8);
                    }
                    string csvLine = $"\"{DateTime.Now:HH:mm:ss.fff}\",\"{tag}\",\"{EscapeCsv(sanitized)}\"" + Environment.NewLine;
                    File.AppendAllText(timelineFile, csvLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "";
            }
            return input.Replace("token", "***")
                        .Replace("Token", "***")
                        .Replace("cookie", "***")
                        .Replace("Cookie", "***")
                        .Replace("authorization", "***")
                        .Replace("Authorization", "***")
                        .Replace("password", "***")
                        .Replace("Password", "***");
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\"", "\"\"");
        }
    }
#endif
}
