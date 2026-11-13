using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class ReleaseInfo
{
    public int FormatVersion;
    public string Version;
    public string HubSha256;
    public PlatformInfo[] Platforms;
}

internal sealed class PlatformInfo
{
    public string Id;
    public string Label;
    public string ExeName;
    public string GameAssemblySha256;
    public string GameVersion;
    public string ResourceVersion;
    public string BundleRelativePath;
    public string Payload;
    public string PayloadSha256;
    public string[] AcceptedHashes;
    public override string ToString() { return Label; }
}

internal sealed class InstallReceipt
{
    public string Version;
    public string Platform;
    public string Target;
    public string BeforeSha256;
    public string InstalledSha256;
    public bool ReplacedConflict;
    public string ConflictBackupRelative;
}

internal sealed class ConflictItem
{
    public string PluginName;
    public string Category; // "target-bundle" | "file" | "directory" | "locallow-file"
    public string RelativePath;
    public string FullPath;
    public string Sha256;
}

internal sealed class ConflictReport
{
    public bool TargetIsConflict;
    public bool TargetIsValidUnityFs;
    public string CurrentTargetSha256;
    public readonly List<ConflictItem> Items = new List<ConflictItem>();
    public bool HasConflicts { get { return TargetIsConflict || Items.Count > 0; } }
    public string[] PluginNames
    {
        get { return Items.Select(i => i.PluginName).Where(s => !String.IsNullOrEmpty(s)).Distinct().ToArray(); }
    }
}

internal sealed class DiagnosticLog
{
    private readonly List<string> lines = new List<string>();
    public string Action = "";
    public string PlatformId = "";
    public string PlatformLabel = "";
    public string ExpectedGameVersion = "";
    public string ExpectedResourceVersion = "";
    public string Selection = "";
    public string GameRootPath = "";
    public string ExpectedGameAssemblySha256 = "";
    public string ActualGameAssemblySha256 = "";
    public string TargetPath = "";
    public string TargetExpectedSha256 = "";
    public string TargetActualSha256 = "";
    public string TargetHeaderInfo = "";
    public readonly List<string> DetectedConflicts = new List<string>();
    public string FailureReason = "";
    public string FailureSuggestion = "";
    public string ExceptionTrace = "";
    public string LastSavedPath = "";

    public void Add(string message)
    {
        lines.Add("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message);
    }

    public void SetFailure(Exception ex)
    {
        FailureReason = ex.Message;
        ExceptionTrace = ex.GetType().FullName + " (0x" + ex.HResult.ToString("X8") + "): " + ex.Message +
            (String.IsNullOrEmpty(ex.StackTrace) ? "" : "\r\n" + ex.StackTrace);
        FailureSuggestion = InferSuggestion(ex);
        Add("FAIL: " + ex.Message);
    }

    private static string InferSuggestion(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (ex is UnauthorizedAccessException || msg.Contains("写入权限"))
            return "1) 请先完全退出游戏与星枢 Hub；2) 若游戏装在 Program Files 等受保护目录，请右键使用管理员权限运行或检查杀毒软件拦截。";
        if (msg.Contains("进程") || msg.Contains("退出"))
            return "请在任务管理器确认无残留 AstralParty_CN.exe / 吉星派对.exe 进程后重试。";
        if (msg.Contains("冲突插件"))
            return "请确认删除并替换同定位冲突插件（如 APX），助手会先完整备份旧文件，再清理冲突并安装星枢。";
        if (msg.Contains("游戏版本或渠道不匹配"))
            return "请核对左侧选择的平台渠道（Steam/TapTap/B站/WeGame）是否与所选游戏目录一致，并在官方平台更新游戏到对应版本。";
        if (msg.Contains("当前资源与本发布包不匹配") || msg.Contains("未找到本包对应的资源缓存"))
            return "请先通过官方平台“验证游戏文件完整性/修复”，启动游戏进入一次大厅使官方资源更新落地，然后退出游戏再试。";
        if (msg.Contains("发布包清单") || msg.Contains("插件文件校验失败"))
            return "安装包文件不完整或损坏，请重新完整解压原始 ZIP 后从解压目录运行星枢安装助手.exe。";
        return "请将此失败日志发给开发者排查，日志已包含版本指纹、路径与异常诊断信息（不包含密码或账号凭据）。";
    }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 星枢桌面安装诊断与失败日志 ===");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine("助手版本: " + (GroupTest.Release != null ? GroupTest.Release.Version : "未知"));
        sb.AppendLine("操作系统: " + Environment.OSVersion.VersionString + " (64位=" + Environment.Is64BitOperatingSystem + ")");
        sb.AppendLine("执行操作: " + (String.IsNullOrEmpty(Action) ? "未执行" : Action));
        sb.AppendLine("目标渠道: " + PlatformLabel + " (" + PlatformId + ")");
        sb.AppendLine("适配基线: 游戏 " + ExpectedGameVersion + " / 资源 " + ExpectedResourceVersion);
        sb.AppendLine("选择路径: " + Selection);
        sb.AppendLine("解析根目录: " + GameRootPath);
        sb.AppendLine("GameAssembly.dll 期望 SHA256: " + ExpectedGameAssemblySha256);
        sb.AppendLine("GameAssembly.dll 实际 SHA256: " + ActualGameAssemblySha256);
        sb.AppendLine("目标资源路径: " + TargetPath);
        sb.AppendLine("目标资源期望 SHA256: " + TargetExpectedSha256);
        sb.AppendLine("目标资源实际 SHA256: " + TargetActualSha256);
        if (!String.IsNullOrEmpty(TargetHeaderInfo)) sb.AppendLine("目标容器探测: " + TargetHeaderInfo);
        if (DetectedConflicts.Count > 0)
        {
            sb.AppendLine("检测到的冲突插件/残留:");
            foreach (var item in DetectedConflicts) sb.AppendLine("  - " + item);
        }
        else sb.AppendLine("检测到的冲突插件/残留: 无");
        sb.AppendLine();
        sb.AppendLine("--- 失败原因 ---");
        sb.AppendLine(String.IsNullOrEmpty(FailureReason) ? "无失败记录（最后一次操作未报错）" : FailureReason);
        if (!String.IsNullOrEmpty(FailureSuggestion))
        {
            sb.AppendLine();
            sb.AppendLine("--- 建议处理方式 ---");
            sb.AppendLine(FailureSuggestion);
        }
        sb.AppendLine();
        sb.AppendLine("--- 操作过程记录 ---");
        foreach (var entry in lines) sb.AppendLine(entry);
        if (!String.IsNullOrEmpty(ExceptionTrace))
        {
            sb.AppendLine();
            sb.AppendLine("--- 异常堆栈 ---");
            sb.AppendLine(ExceptionTrace);
        }
        return sb.ToString();
    }

    public string SaveAutoFailure()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "Logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "desktop-installer-last-failure.log");
            File.WriteAllText(path, Render(), new UTF8Encoding(true));
            LastSavedPath = path;
            return path;
        }
        catch { return ""; }
    }
}

internal static class GroupTest
{
    internal static readonly string PackageRoot = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static ReleaseInfo Release;

    internal static string CompatibleVersion(PlatformInfo platform)
    {
        return "吉星派对" + (String.IsNullOrWhiteSpace(platform.GameVersion) ? "" : " " + platform.GameVersion) +
            (String.IsNullOrWhiteSpace(platform.ResourceVersion) ? "（清单指定资源）" : " / 资源 " + platform.ResourceVersion);
    }

    internal static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    internal static string Within(string root, string relative)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(prefix, relative));
        if (Path.IsPathRooted(relative) || !result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("文件路径超出选定目录。");
        return result;
    }

    internal static void LoadPackage()
    {
        var manifest = Within(PackageRoot, "release.json");
        if (Hash(manifest) != PackageIdentity.ManifestSha256)
            throw new InvalidOperationException("发布包清单校验失败，请重新完整解压原始压缩包。");
        Release = Json.Deserialize<ReleaseInfo>(File.ReadAllText(manifest, Encoding.UTF8));
        if (Release == null || Release.FormatVersion != 1 || Release.Platforms == null ||
            Release.Platforms.Length < 1 || Release.Platforms.Length > 4 ||
            Release.Platforms.Any(p => p == null || !new[] { "STEAM_CN", "TAPTAP", "BILIBILI", "WEGAME" }.Contains(p.Id)) ||
            Release.Platforms.Select(p => p.Id).Distinct().Count() != Release.Platforms.Length)
            throw new InvalidOperationException("不支持的发布包格式。");
    }

    internal static void VerifyPayload(PlatformInfo platform)
    {
        if (Hash(Within(PackageRoot, platform.Payload)) != platform.PayloadSha256)
            throw new InvalidOperationException("插件文件校验失败，请重新完整解压发布包。");
    }

    internal static string GameRoot(PlatformInfo platform, string selection, DiagnosticLog diag = null)
    {
        if (diag != null)
        {
            diag.PlatformId = platform.Id;
            diag.PlatformLabel = platform.Label;
            diag.ExpectedGameVersion = platform.GameVersion ?? "";
            diag.ExpectedResourceVersion = platform.ResourceVersion ?? "";
            diag.Selection = selection ?? "";
            diag.ExpectedGameAssemblySha256 = platform.GameAssemblySha256 ?? "";
            diag.TargetExpectedSha256 = platform.PayloadSha256 ?? "";
        }
        if (String.IsNullOrWhiteSpace(selection)) throw new InvalidOperationException("请先选择游戏程序。");
        var root = Path.GetFullPath(selection.Trim().Trim('"'));
        if (File.Exists(root))
        {
            if (!String.Equals(Path.GetFileName(root), platform.ExeName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("所选游戏程序与平台不一致。");
            root = Path.GetDirectoryName(root);
        }
        if (diag != null) diag.GameRootPath = root;
        if (!File.Exists(Within(root, platform.ExeName)))
            throw new InvalidOperationException("此目录没有对应的游戏程序，请选择实际游戏 exe，不能选择启动器。");
        var gaPath = Within(root, "GameAssembly.dll");
        if (!File.Exists(gaPath))
            throw new InvalidOperationException("所选目录缺少 GameAssembly.dll，请确认选择的是完整游戏目录。");
        var gaHash = Hash(gaPath);
        if (diag != null) diag.ActualGameAssemblySha256 = gaHash;
        if (gaHash != platform.GameAssemblySha256)
            throw new InvalidOperationException("游戏版本或渠道不匹配；本包仅支持" + CompatibleVersion(platform) + " 的" + platform.Label + "客户端。");
        return root;
    }

    private static string ResolveDataDirRelative(PlatformInfo platform)
    {
        var parts = (platform.BundleRelativePath ?? "").Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts[0].EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            return parts[0];
        return Path.GetFileNameWithoutExtension(platform.ExeName) + "_Data";
    }

    private static bool IsValidUnityFs(string path, out string headerDesc)
    {
        var info = new FileInfo(path);
        if (info.Length < 128 || info.Length > 64L * 1024 * 1024)
        {
            headerDesc = "size=" + info.Length + " (超出合理 UnityFS 范围)";
            return false;
        }
        var header = new byte[8];
        using (var fs = File.OpenRead(path))
        {
            int read = fs.Read(header, 0, 8);
            if (read < 8) { headerDesc = "读取字节不足 8"; return false; }
        }
        bool ok = header[0] == 'U' && header[1] == 'n' && header[2] == 'i' && header[3] == 't' &&
                  header[4] == 'y' && header[5] == 'F' && header[6] == 'S' && header[7] == 0;
        headerDesc = (ok ? "UnityFS\\0" : "非UnityFS(" + BitConverter.ToString(header) + ")") + ", size=" + info.Length;
        return ok;
    }

    private static bool ContainsAsciiMarker(string path, params string[] markers)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var text = Encoding.ASCII.GetString(data);
            return markers.Any(m => text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch { return false; }
    }

    private static string NormalizeRel(string rel)
    {
        return (rel ?? "").Replace('\\', '/').TrimStart('/');
    }

    private static void AddFileConflictIfExists(ConflictReport report, string root, string rel, string pluginName, string category = "file")
    {
        var full = Within(root, rel);
        if (!File.Exists(full)) return;
        report.Items.Add(new ConflictItem
        {
            PluginName = pluginName,
            Category = category,
            RelativePath = NormalizeRel(rel),
            FullPath = full,
            Sha256 = Hash(full)
        });
    }

    internal static ConflictReport ScanConflicts(PlatformInfo platform, string root, string target, string currentTargetSha256, DiagnosticLog diag = null)
    {
        var report = new ConflictReport { CurrentTargetSha256 = currentTargetSha256 };
        string headerDesc;
        report.TargetIsValidUnityFs = IsValidUnityFs(target, out headerDesc);
        if (diag != null) diag.TargetHeaderInfo = headerDesc;

        var dataDir = ResolveDataDirRelative(platform);
        var apxBridge = dataDir + "/Plugins/x86_64/APXLive2DBridge.dll";
        var apxCubism = dataDir + "/Plugins/x86_64/Live2DCubismCore.dll";
        var apxUiRoot = "dependence/apx_modern_assist_ui.bundle";
        var apxUiData = dataDir + "/dependence/apx_modern_assist_ui.bundle";
        var apxExtraData = dataDir + "/com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__data";
        var apxExtraInfo = dataDir + "/com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__info";
        var apxLegacyData = dataDir + "/com.unity.addressables/AssetBundles/57a198703b66364d5420d39c0d251747/3486f99276eba77677d4880fd8d32918/__data";
        var apxLegacyInfo = dataDir + "/com.unity.addressables/AssetBundles/57a198703b66364d5420d39c0d251747/3486f99276eba77677d4880fd8d32918/__info";

        AddFileConflictIfExists(report, root, apxBridge, "APX 插件");
        AddFileConflictIfExists(report, root, apxUiRoot, "APX 插件");
        AddFileConflictIfExists(report, root, apxUiData, "APX 插件");
        AddFileConflictIfExists(report, root, apxExtraData, "APX 插件");
        if (File.Exists(Within(root, apxExtraData))) AddFileConflictIfExists(report, root, apxExtraInfo, "APX 插件");
        AddFileConflictIfExists(report, root, "runtime_dependencies.json", "APX 插件");
        AddFileConflictIfExists(report, root, dataDir + "/runtime_dependencies.json", "APX 插件");
        if (!String.Equals(NormalizeRel(platform.BundleRelativePath), NormalizeRel(apxLegacyData), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(Within(root, apxLegacyData)))
            {
                AddFileConflictIfExists(report, root, apxLegacyData, "APX 旧版或同定位插件");
                AddFileConflictIfExists(report, root, apxLegacyInfo, "APX 旧版或同定位插件");
            }
        }

        // Only treat Live2DCubismCore.dll as APX companion if APX markers are present.
        if (report.Items.Any(i => i.PluginName.Contains("APX")))
            AddFileConflictIfExists(report, root, apxCubism, "APX 插件");

        foreach (var injectorDir in new[] { "BepInEx", "MelonLoader" })
        {
            var fullDir = Within(root, injectorDir);
            if (Directory.Exists(fullDir))
                report.Items.Add(new ConflictItem { PluginName = injectorDir + " 注入框架", Category = "directory", RelativePath = injectorDir, FullPath = fullDir, Sha256 = "" });
        }
        foreach (var injectorFile in new[] { "doorstop_config.ini", "winhttp.dll" })
            AddFileConflictIfExists(report, root, injectorFile, "插件注入框架 (Doorstop/BepInEx)");

        bool targetIsKnown = currentTargetSha256 == platform.PayloadSha256 || platform.AcceptedHashes.Contains(currentTargetSha256);
        if (!targetIsKnown && (report.TargetIsValidUnityFs || report.Items.Any(i => i.PluginName.Contains("APX"))))
        {
            report.TargetIsConflict = true;
            var bundlePlugin = report.Items.Any(i => i.PluginName.Contains("APX")) || ContainsAsciiMarker(target, "APXLive2D", "APX.Runtime", "apx_modern_assist_ui")
                ? "APX 插件" : "同定位覆盖插件 / MOD";
            report.Items.Insert(0, new ConflictItem
            {
                PluginName = bundlePlugin,
                Category = "target-bundle",
                RelativePath = NormalizeRel(platform.BundleRelativePath),
                FullPath = target,
                Sha256 = currentTargetSha256
            });
        }

        // Clean LocalLow APX active files too when APX/target conflict is already present in this game directory.
        if (report.HasConflicts && report.Items.Any(i => i.PluginName.Contains("APX")))
        {
            var localLowProduct = platform.Id == "STEAM_CN" ? "AstralParty_CN" : "吉星派对";
            var localLowRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Feimo", localLowProduct);
            foreach (var name in new[] { "runtime_dependencies.json", "apx_modern_assist_ui.bundle" })
            {
                var path = Path.Combine(localLowRoot, name);
                if (File.Exists(path))
                {
                    report.Items.Add(new ConflictItem
                    {
                        PluginName = "APX 插件 (LocalLow 缓存)",
                        Category = "locallow-file",
                        RelativePath = "LocalLow/Feimo/" + localLowProduct + "/" + name,
                        FullPath = path,
                        Sha256 = Hash(path)
                    });
                }
            }
        }

        if (diag != null)
        {
            diag.DetectedConflicts.Clear();
            foreach (var item in report.Items)
                diag.DetectedConflicts.Add(item.PluginName + " [" + item.Category + "] " + item.RelativePath +
                    (String.IsNullOrEmpty(item.Sha256) ? "" : " (" + item.Sha256.Substring(0, 12) + ")"));
        }
        return report;
    }

    internal static ConflictReport InspectConflicts(PlatformInfo platform, string selection, DiagnosticLog diag = null)
    {
        VerifyPayload(platform);
        var root = GameRoot(platform, selection, diag);
        var target = Within(root, platform.BundleRelativePath);
        if (diag != null) diag.TargetPath = target;
        if (!File.Exists(target))
            throw new InvalidOperationException("未找到本包对应的资源缓存（" + CompatibleVersion(platform) + "）。请先完成官方游戏更新并正常进入一次大厅，再退出游戏重试。");
        var current = Hash(target);
        if (diag != null) diag.TargetActualSha256 = current;
        var report = ScanConflicts(platform, root, target, current, diag);
        bool known = current == platform.PayloadSha256 || platform.AcceptedHashes.Contains(current);
        if (!known && !report.TargetIsConflict)
            throw new InvalidOperationException("当前资源与本发布包不匹配，未修改任何游戏文件。请先通过官方平台校验/修复游戏，完成更新后重试。");
        return report;
    }

    internal static string FormatConflictPrompt(ConflictReport report)
    {
        var names = report.PluginNames.Length > 0 ? String.Join("、", report.PluginNames) : "同定位冲突插件";
        var preview = String.Join("\r\n", report.Items.Take(8).Select(i => "• [" + i.PluginName + "] " + i.RelativePath));
        if (report.Items.Count > 8) preview += "\r\n• ... 共 " + report.Items.Count + " 项冲突文件/目录";
        return "检测到同定位冲突插件（" + names + "）：\r\n" + preview +
            "\r\n\r\n继续安装将先完整备份上述冲突插件到 .xingshu-backup，再删除原有冲突文件并替换为星枢（不移动无关目录）。";
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private static string ArchiveAndRemoveConflicts(ConflictReport report, string backupDirectory, DiagnosticLog diag)
    {
        var key = DateTime.UtcNow.ToString("yyMMddHHmmss") + "-" + report.CurrentTargetSha256.Substring(0, 6);
        var conflictDir = Within(backupDirectory, "conflicts/" + key);
        Directory.CreateDirectory(conflictDir);
        var manifestItems = new List<Dictionary<string, string>>();
        int index = 0;
        foreach (var item in report.Items)
        {
            index++;
            string backupName = item.Category == "target-bundle"
                ? "target.__data"
                : index.ToString("000") + "_" + Path.GetFileName(item.FullPath);
            var backupDest = Path.GetFullPath(Path.Combine(conflictDir, backupName));
            if (!backupDest.StartsWith(Path.GetFullPath(conflictDir), StringComparison.OrdinalIgnoreCase))
                throw new IOException("冲突备份路径越界。");
            if (item.Category == "directory")
            {
                if (Directory.Exists(item.FullPath))
                {
                    CopyDirectory(item.FullPath, backupDest);
                    Directory.Delete(item.FullPath, true);
                }
            }
            else if (File.Exists(item.FullPath))
            {
                File.Copy(item.FullPath, backupDest, true);
                if (!String.IsNullOrEmpty(item.Sha256) && Hash(backupDest) != item.Sha256)
                    throw new IOException("冲突文件备份校验失败：" + item.RelativePath);
                if (item.Category != "target-bundle")
                {
                    File.SetAttributes(item.FullPath, File.GetAttributes(item.FullPath) & ~FileAttributes.ReadOnly);
                    File.Delete(item.FullPath);
                }
            }
            manifestItems.Add(new Dictionary<string, string>
            {
                { "plugin", item.PluginName },
                { "category", item.Category },
                { "relativePath", item.RelativePath },
                { "backupName", backupName },
                { "originalPath", item.FullPath },
                { "sha256", item.Sha256 ?? "" }
            });
        }
        File.WriteAllText(Path.Combine(conflictDir, "conflicts.json"), Json.Serialize(manifestItems), new UTF8Encoding(false));
        if (diag != null) diag.Add("已备份并清理 " + report.Items.Count + " 项冲突文件至 " + conflictDir);
        return "conflicts/" + key;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

    private static string CanonicalFile(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var value = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(stream.SafeFileHandle, value, (uint)value.Capacity, 0);
            if (length == 0 || length >= value.Capacity) throw new IOException("无法确认游戏程序的实际文件位置。");
            return value.ToString();
        }
    }

    internal static void EnsureGameClosed(PlatformInfo platform, string root)
    {
        var selected = CanonicalFile(Within(root, platform.ExeName));
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(platform.ExeName));
        try
        {
            foreach (var process in processes)
            {
                string actual;
                try { if (process.HasExited) continue; actual = CanonicalFile(process.MainModule.FileName); }
                catch (Exception)
                {
                    if (process.HasExited) continue;
                    throw new InvalidOperationException("无法确认同名游戏进程的位置，请正常退出该进程后重试。");
                }
                if (String.Equals(selected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请先从游戏菜单退出所选目录的游戏，再安装或还原。助手不会强制结束游戏。");
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static void WriteNew(string path, byte[] bytes)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }

    internal static void ReplaceWith(string source, string target, string expected)
    {
        var stage = target + ".xingshu-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, stage, false);
            if (Hash(stage) != expected) throw new IOException("暂存文件校验失败，原文件保持不变。");
            var attributes = File.GetAttributes(target);
            try
            {
                File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
                File.Replace(stage, target, null);
            }
            finally { if (File.Exists(target)) File.SetAttributes(target, attributes); }
            if (Hash(target) != expected) throw new IOException("替换后校验失败。");
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }

    internal static string Perform(PlatformInfo platform, string selection, string action, bool replaceConflicts = false, DiagnosticLog diag = null)
    {
        if (diag != null)
        {
            diag.Action = action + (replaceConflicts ? " (replace-conflicts)" : "");
            diag.Add("开始执行操作: " + diag.Action);
        }
        VerifyPayload(platform);
        var root = GameRoot(platform, selection, diag);
        var target = Within(root, platform.BundleRelativePath);
        if (diag != null) diag.TargetPath = target;
        var lockName = "Local\\XingShuGroupTest-" + platform.Id;
        using (var mutex = new Mutex(false, lockName))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("另一个安装任务正在进行，请稍后重试。");
            try
            {
                if (!File.Exists(target))
                    throw new InvalidOperationException("未找到本包对应的资源缓存（" + CompatibleVersion(platform) + "）。请先完成官方游戏更新并正常进入一次大厅，再退出游戏重试。");
                var current = Hash(target);
                if (diag != null) diag.TargetActualSha256 = current;
                var accepted = platform.AcceptedHashes.Contains(current);
                var conflict = ScanConflicts(platform, root, target, current, diag);
                if (current != platform.PayloadSha256 && !accepted && !conflict.TargetIsConflict)
                    throw new InvalidOperationException("当前资源与本发布包不匹配，未修改任何游戏文件。请先通过官方平台校验/修复游戏，完成更新后重试。");
                if (action == "check")
                {
                    if (conflict.HasConflicts)
                    {
                        var names = conflict.PluginNames.Length > 0 ? String.Join("、", conflict.PluginNames) : "同定位插件";
                        return platform.Label + "：游戏程序匹配，但检测到冲突插件（" + names + "，共 " + conflict.Items.Count + " 项）。安装时需先确认备份并删除替换。";
                    }
                    return platform.Label + "：版本匹配，" + (current == platform.PayloadSha256 ? "已安装本版。" : "可以安装。");
                }
                EnsureGameClosed(platform, root);
                if (action == "check-closed") return platform.Label + "：所选目录的游戏未运行。";
                var backupDirectory = Within(root, ".xingshu-backup/" + Release.Version + "/" + platform.Id);
                var backup = Within(backupDirectory, "__data.before");
                var receiptPath = Within(backupDirectory, "receipt.json");
                if (action == "restore")
                {
                    if (!File.Exists(receiptPath))
                        throw new InvalidOperationException("没有此助手的安装前备份。若由其他方式安装，请使用原备份或官方平台校验游戏。");
                    var receipt = Json.Deserialize<InstallReceipt>(File.ReadAllText(receiptPath, Encoding.UTF8));
                    string restoreSource = backup;
                    if (receipt != null && receipt.ReplacedConflict && !String.IsNullOrEmpty(receipt.ConflictBackupRelative))
                    {
                        var candidateSource = Within(backupDirectory, receipt.ConflictBackupRelative + "/target.__data");
                        if (File.Exists(candidateSource)) restoreSource = candidateSource;
                    }
                    if (!File.Exists(restoreSource))
                        throw new InvalidOperationException("没有此助手的安装前备份。若由其他方式安装，请使用原备份或官方平台校验游戏。");
                    bool validBefore = receipt != null && (platform.AcceptedHashes.Contains(receipt.BeforeSha256) || receipt.ReplacedConflict);
                    if (receipt == null || receipt.Version != Release.Version || receipt.Platform != platform.Id ||
                        receipt.Target != platform.BundleRelativePath || receipt.InstalledSha256 != platform.PayloadSha256 ||
                        !validBefore || Hash(restoreSource) != receipt.BeforeSha256)
                        throw new InvalidOperationException("安装前备份校验失败，未还原。");
                    if (current == receipt.BeforeSha256) return "已是安装前版本，无需重复还原。";
                    if (current != receipt.InstalledSha256)
                        throw new InvalidOperationException("游戏文件已被其他更新修改，不能覆盖还原。请使用官方平台修复。");
                    EnsureGameClosed(platform, root);
                    ReplaceWith(restoreSource, target, receipt.BeforeSha256);
                    if (receipt.ReplacedConflict)
                        return platform.Label + "：已还原至安装星枢前的文件（注意：替换前为冲突插件版本，如需官方原版请通过官方平台修复）。";
                    return platform.Label + "：已还原到本次安装前的版本。";
                }
                if (action != "install") throw new ArgumentException("未知操作。");
                if (conflict.HasConflicts && !replaceConflicts)
                    throw new InvalidOperationException("CONFLICT_CONFIRM_REQUIRED: " + FormatConflictPrompt(conflict) + "\r\n如通过命令行安装，请确认后附加 --replace-conflicts。");
                if (current == platform.PayloadSha256 && !conflict.HasConflicts)
                    return platform.Label + "：已是本版，无需重复安装。";

                Directory.CreateDirectory(backupDirectory);
                string conflictBackupRel = "";
                if (conflict.HasConflicts)
                    conflictBackupRel = ArchiveAndRemoveConflicts(conflict, backupDirectory, diag);

                string restoreBackupFile = backup;
                if (accepted || current == platform.PayloadSha256)
                {
                    if (File.Exists(backup))
                    {
                        if (Hash(backup) != current) throw new IOException("已有备份与当前文件不同，已停止安装以保留原备份。");
                    }
                    else File.Copy(target, backup, false);
                    if (Hash(backup) != current) throw new IOException("安装前备份校验失败，未替换游戏文件。");
                }
                else
                {
                    restoreBackupFile = Within(backupDirectory, conflictBackupRel + "/target.__data");
                    if (!File.Exists(restoreBackupFile) || Hash(restoreBackupFile) != current)
                        throw new IOException("冲突插件备份校验失败，未替换游戏文件。");
                }

                if (accepted || current == platform.PayloadSha256)
                {
                    var receiptObj = new InstallReceipt {
                        Version = Release.Version, Platform = platform.Id, Target = platform.BundleRelativePath,
                        BeforeSha256 = current, InstalledSha256 = platform.PayloadSha256
                    };
                    var receiptBytes = Encoding.UTF8.GetBytes(Json.Serialize(receiptObj));
                    if (File.Exists(receiptPath))
                    {
                        if (!File.ReadAllBytes(receiptPath).SequenceEqual(receiptBytes))
                            throw new IOException("已有安装记录不匹配，未修改游戏文件。");
                    }
                    else WriteNew(receiptPath, receiptBytes);
                }
                else
                {
                    var receiptObj = new InstallReceipt {
                        Version = Release.Version, Platform = platform.Id, Target = platform.BundleRelativePath,
                        BeforeSha256 = current, InstalledSha256 = platform.PayloadSha256,
                        ReplacedConflict = true, ConflictBackupRelative = conflictBackupRel
                    };
                    var receiptBytes = Encoding.UTF8.GetBytes(Json.Serialize(receiptObj));
                    if (!File.Exists(receiptPath)) WriteNew(receiptPath, receiptBytes);
                }

                EnsureGameClosed(platform, root);
                if (current != platform.PayloadSha256)
                {
                    try { ReplaceWith(Within(PackageRoot, platform.Payload), target, platform.PayloadSha256); }
                    catch
                    {
                        if (Hash(target) != current) ReplaceWith(restoreBackupFile, target, current);
                        throw;
                    }
                }
                if (diag != null) diag.Add("安装与校验成功完成");
                if (conflict.HasConflicts)
                    return platform.Label + "：已备份并删除原冲突插件（" + conflict.Items.Count + " 项），成功安装并校验星枢。请从官方平台正常启动游戏。";
                return platform.Label + "：安装完成，已保留安装前备份。请从官方平台正常启动游戏。";
            }
            finally { mutex.ReleaseMutex(); }
        }
    }

    internal static void StartHub()
    {
        var hub = Within(PackageRoot, "Hub/xingshu-hub.exe");
        if (Hash(hub) != Release.HubSha256) throw new IOException("Hub 文件校验失败，请重新解压安装包。");
        var running = Process.GetProcessesByName("xingshu-hub");
        try { if (running.Length != 0) throw new InvalidOperationException("星枢 Hub 已在运行，请先退出旧窗口，再启动此版本。"); }
        finally { foreach (var process in running) process.Dispose(); }
        Process.Start(new ProcessStartInfo(hub) { WorkingDirectory = Path.GetDirectoryName(hub), UseShellExecute = true });
    }

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
        }
        var diag = new DiagnosticLog();
        string exportLogPath = null;
        try
        {
            LoadPackage();
            var filtered = new List<string>();
            bool replaceConflicts = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--replace-conflicts") { replaceConflicts = true; continue; }
                if (args[i] == "--export-log" && i + 1 < args.Length) { exportLogPath = args[++i]; continue; }
                filtered.Add(args[i]);
            }
            if (filtered.Count == 1 && filtered[0] == "--tab-android" || (filtered.Count == 2 && filtered[0] == "--tab" && filtered[1] == "android"))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerWindow(null, null, true));
                return 0;
            }
            if (filtered.Count == 3 && filtered[0] == "--ui")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerWindow(filtered[1], filtered[2], false));
                return 0;
            }
            if (filtered.Count > 0)
            {
                string result;
                if (filtered.Count == 1 && filtered[0] == "--verify-package")
                {
                    foreach (var p in Release.Platforms) VerifyPayload(p);
                    if (Hash(Within(PackageRoot, "Hub/xingshu-hub.exe")) != Release.HubSha256) throw new IOException("Hub hash mismatch");
                    result = "PASS: " + Release.Version;
                }
                else if (filtered.Count == 3 && new[] { "--check", "--check-closed", "--install", "--restore" }.Contains(filtered[0]))
                {
                    var platform = Release.Platforms.Single(p => p.Id == filtered[1]);
                    result = Perform(platform, filtered[2], filtered[0].Substring(2), replaceConflicts, diag);
                }
                else throw new ArgumentException("Usage: --verify-package | [--replace-conflicts] [--export-log PATH] --check/--install/--restore PLATFORM GAME_ROOT");
                if (!String.IsNullOrEmpty(exportLogPath))
                    File.WriteAllText(exportLogPath, diag.Render(), new UTF8Encoding(true));
                Console.WriteLine(result);
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerWindow(null, null, false));
            return 0;
        }
        catch (Exception ex)
        {
            diag.SetFailure(ex);
            var saved = diag.SaveAutoFailure();
            if (!String.IsNullOrEmpty(exportLogPath))
            {
                try { File.WriteAllText(exportLogPath, diag.Render(), new UTF8Encoding(true)); }
                catch { }
            }
            if (args.Length > 0)
            {
                Console.Error.WriteLine(ex.Message);
                if (!String.IsNullOrEmpty(saved)) Console.Error.WriteLine("失败诊断日志已保存：" + saved);
            }
            else MessageBox.Show(ex.Message + (String.IsNullOrEmpty(saved) ? "" : "\r\n\r\n诊断日志已自动保存至：\r\n" + saved), "星枢安装助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal static class AuroraPalette
{
    internal static readonly Color Canvas = Color.FromArgb(252, 251, 248);
    internal static readonly Color Surface = Color.FromArgb(255, 255, 255);
    internal static readonly Color SurfaceSubtle = Color.FromArgb(247, 245, 239);
    internal static readonly Color SurfaceLog = Color.FromArgb(250, 249, 245);
    internal static readonly Color Border = Color.FromArgb(228, 223, 213);
    internal static readonly Color BorderStrong = Color.FromArgb(209, 203, 190);
    internal static readonly Color Ink = Color.FromArgb(28, 28, 28);
    internal static readonly Color InkMuted = Color.FromArgb(95, 92, 86);
    internal static readonly Color InkSubtle = Color.FromArgb(130, 126, 118);
    internal static readonly Color AccentSoft = Color.FromArgb(255, 241, 229);
    internal static readonly Color AccentBorder = Color.FromArgb(245, 207, 175);
    internal static readonly Color AccentInk = Color.FromArgb(184, 75, 8);
    internal static readonly Color OkSoft = Color.FromArgb(232, 246, 237);
    internal static readonly Color OkInk = Color.FromArgb(21, 128, 61);
    internal static readonly Color ErrSoft = Color.FromArgb(253, 236, 240);
    internal static readonly Color ErrInk = Color.FromArgb(190, 18, 60);

    internal static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        if (d > bounds.Width) d = bounds.Width;
        if (d > bounds.Height) d = bounds.Height;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void PaintAuroraHeader(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var baseBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            bounds, Color.FromArgb(255, 245, 233), Color.FromArgb(252, 251, 248), 90f))
        {
            g.FillRectangle(baseBrush, bounds);
        }
        using (var warmGlow = new System.Drawing.Drawing2D.GraphicsPath())
        {
            warmGlow.AddEllipse(-60, -90, 360, 220);
            using (var pgb = new System.Drawing.Drawing2D.PathGradientBrush(warmGlow))
            {
                pgb.CenterColor = Color.FromArgb(72, 255, 142, 42);
                pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 142, 42) };
                g.FillPath(pgb, warmGlow);
            }
        }
        using (var roseGlow = new System.Drawing.Drawing2D.GraphicsPath())
        {
            roseGlow.AddEllipse(bounds.Width - 360, -80, 360, 210);
            using (var pgb = new System.Drawing.Drawing2D.PathGradientBrush(roseGlow))
            {
                pgb.CenterColor = Color.FromArgb(52, 255, 95, 162);
                pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 95, 162) };
                g.FillPath(pgb, roseGlow);
            }
        }
        using (var pen = new Pen(Border, 1f))
            g.DrawLine(pen, 0, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    internal static Icon CreateStarIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Ink))
                g.FillEllipse(bg, 1, 1, 30, 30);
            var pts = new[] {
                new PointF(16f, 5f), new PointF(19.2f, 12.8f), new PointF(27f, 16f), new PointF(19.2f, 19.2f),
                new PointF(16f, 27f), new PointF(12.8f, 19.2f), new PointF(5f, 16f), new PointF(12.8f, 12.8f)
            };
            using (var star = new SolidBrush(Color.FromArgb(255, 196, 108)))
                g.FillPolygon(star, pts);
            IntPtr hIcon = bmp.GetHicon();
            try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
            finally { DestroyIcon(hIcon); }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed class AuroraCardPanel : Panel
{
    internal Color CardBack = AuroraPalette.Surface;
    internal Color CardBorder = AuroraPalette.Border;
    internal int Radius = 14;

    internal AuroraCardPanel()
    {
        DoubleBuffered = true;
        BackColor = AuroraPalette.Canvas;
        Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 2);
        using (var shadowPath = AuroraPalette.RoundedRect(new Rectangle(0, 1, Width - 1, Height - 2), Radius))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(10, 28, 28, 28)))
            g.FillPath(shadowBrush, shadowPath);
        using (var path = AuroraPalette.RoundedRect(rect, Radius))
        using (var fill = new SolidBrush(CardBack))
        using (var pen = new Pen(CardBorder, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
    }
}

internal enum AuroraBtnKind { Primary, Accent, Secondary, DangerSoft }

internal sealed class AuroraButton : Button
{
    internal AuroraBtnKind Kind = AuroraBtnKind.Secondary;
    internal int Radius = 9;
    private bool hovered, pressed;

    internal AuroraButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.25F, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : AuroraPalette.Surface);
        Color bg, fg, bd;
        if (!Enabled)
        {
            bg = Color.FromArgb(241, 239, 233);
            fg = Color.FromArgb(165, 160, 152);
            bd = Color.FromArgb(228, 224, 216);
        }
        else if (Kind == AuroraBtnKind.Primary)
        {
            bg = pressed ? Color.FromArgb(55, 52, 48) : (hovered ? Color.FromArgb(46, 44, 40) : AuroraPalette.Ink);
            fg = Color.White;
            bd = bg;
        }
        else if (Kind == AuroraBtnKind.Accent)
        {
            bg = pressed ? Color.FromArgb(250, 221, 196) : (hovered ? Color.FromArgb(255, 232, 212) : AuroraPalette.AccentSoft);
            fg = AuroraPalette.AccentInk;
            bd = AuroraPalette.AccentBorder;
        }
        else if (Kind == AuroraBtnKind.DangerSoft)
        {
            bg = hovered ? Color.FromArgb(252, 225, 232) : AuroraPalette.ErrSoft;
            fg = AuroraPalette.ErrInk;
            bd = Color.FromArgb(243, 198, 210);
        }
        else
        {
            bg = pressed ? Color.FromArgb(234, 229, 218) : (hovered ? Color.FromArgb(239, 235, 226) : AuroraPalette.SurfaceSubtle);
            fg = AuroraPalette.Ink;
            bd = AuroraPalette.BorderStrong;
        }
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = AuroraPalette.RoundedRect(rect, Radius))
        using (var fill = new SolidBrush(bg))
        using (var pen = new Pen(bd, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, rect, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class AuroraPillBadge : Label
{
    internal Color FillColor = AuroraPalette.AccentSoft;
    internal Color EdgeColor = AuroraPalette.AccentBorder;
    internal Color InkColor = AuroraPalette.AccentInk;

    internal AuroraPillBadge()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleCenter;
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = AuroraPalette.RoundedRect(rect, Height / 2))
        using (var fill = new SolidBrush(FillColor))
        using (var pen = new Pen(EdgeColor, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, rect, InkColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed class InstallerWindow : Form
{
    private readonly Panel pcPanel = new Panel(), androidPanel = new Panel();
    private readonly AuroraButton tabPcBtn = new AuroraButton(), tabAndroidBtn = new AuroraButton();
    private readonly AuroraPillBadge stateBadge = new AuroraPillBadge();
    private readonly ComboBox platforms = new ComboBox();
    private readonly TextBox selection = new TextBox();
    private readonly TextBox status = new TextBox();
    private readonly AuroraButton pcInstallBtn = new AuroraButton(), pcRestoreBtn = new AuroraButton(), pcCheckBtn = new AuroraButton(), pcHubBtn = new AuroraButton();

    private readonly TextBox adbBox = new TextBox(), wirelessAddrBox = new TextBox(), androidLog = new TextBox();
    private readonly ComboBox androidDevices = new ComboBox();
    private readonly AuroraButton andRefreshBtn = new AuroraButton(), andConnectBtn = new AuroraButton(), andInstallBtn = new AuroraButton(), andRestoreBtn = new AuroraButton(), andCheckBtn = new AuroraButton(), andApkBtn = new AuroraButton(), andExportBtn = new AuroraButton();
    private readonly Label andAvailLabel = new Label();
    private readonly List<string> andSerials = new List<string>();
    private string androidRoot, androidManifest, androidWorkerExe, androidApkPath;
    private bool activeTabAndroid;
    private DiagnosticLog lastDiag = new DiagnosticLog();

    internal InstallerWindow(string initialPlatform = null, string initialPath = null, bool startAndroidTab = false)
    {
        DoubleBuffered = true;
        Text = "星枢安装助手 · " + GroupTest.Release.Version;
        ClientSize = new Size(760, 608);
        MinimumSize = MaximumSize = Size;
        BackColor = AuroraPalette.Canvas;
        ForeColor = AuroraPalette.Ink;
        Font = new Font("Microsoft YaHei UI", 9.25F);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        try { Icon = AuroraPalette.CreateStarIcon(); } catch { }

        ResolveAndroidBundlePaths();

        // Top Aurora Header Band
        var header = new Panel { Left = 0, Top = 0, Width = 760, Height = 82, BackColor = AuroraPalette.Canvas };
        header.Paint += delegate(object s, PaintEventArgs pe)
        {
            AuroraPalette.PaintAuroraHeader(pe.Graphics, header.ClientRectangle);
            using (var bg = new SolidBrush(AuroraPalette.Ink))
            using (var star = new SolidBrush(Color.FromArgb(255, 196, 108)))
            {
                pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pe.Graphics.FillEllipse(bg, 24, 18, 42, 42);
                var pts = new[] {
                    new PointF(45f, 25f), new PointF(48.8f, 35.2f), new PointF(59f, 39f), new PointF(48.8f, 42.8f),
                    new PointF(45f, 53f), new PointF(41.2f, 42.8f), new PointF(31f, 39f), new PointF(41.2f, 35.2f)
                };
                pe.Graphics.FillPolygon(star, pts);
            }
        };

        var titleLbl = new Label { Text = "星枢安装助手", Left = 78, Top = 16, Width = 134, Height = 28, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold), ForeColor = AuroraPalette.Ink };
        var verBadge = new AuroraPillBadge { Text = GroupTest.Release.Version, Left = 216, Top = 20, Width = 192, Height = 22 };
        var subLbl = new Label
        {
            Text = "统一安装与维护 · " + String.Join("、", GroupTest.Release.Platforms.Select(GroupTest.CompatibleVersion).Distinct()),
            Left = 78, Top = 46, Width = 400, Height = 20, BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = AuroraPalette.InkMuted
        };

        // Unified Segmented Mode Switcher
        var tabTrack = new Panel { Left = 486, Top = 20, Width = 250, Height = 40, BackColor = Color.FromArgb(239, 234, 224) };
        tabTrack.Paint += delegate(object s, PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = AuroraPalette.RoundedRect(new Rectangle(0, 0, tabTrack.Width - 1, tabTrack.Height - 1), 11))
            using (var fill = new SolidBrush(Color.FromArgb(239, 234, 224)))
            using (var pen = new Pen(AuroraPalette.Border, 1f)) { pe.Graphics.FillPath(fill, path); pe.Graphics.DrawPath(pen, path); }
        };
        tabPcBtn.Text = "电脑端安装"; tabPcBtn.SetBounds(4, 4, 118, 32); tabPcBtn.Radius = 8;
        tabAndroidBtn.Text = "安卓端安装"; tabAndroidBtn.SetBounds(126, 4, 120, 32); tabAndroidBtn.Radius = 8;
        tabPcBtn.Click += delegate { SwitchTab(false); };
        tabAndroidBtn.Click += delegate { SwitchTab(true); };
        tabTrack.Controls.AddRange(new Control[] { tabPcBtn, tabAndroidBtn });
        header.Controls.AddRange(new Control[] { titleLbl, verBadge, subLbl, tabTrack });
        Controls.Add(header);

        stateBadge.SetBounds(24, 92, 130, 24);
        stateBadge.Text = "就绪";
        stateBadge.FillColor = AuroraPalette.OkSoft;
        stateBadge.EdgeColor = Color.FromArgb(187, 228, 200);
        stateBadge.InkColor = AuroraPalette.OkInk;
        var helpTopBtn = new AuroraButton { Text = "安装说明", Left = 548, Top = 90, Width = 90, Height = 28, Kind = AuroraBtnKind.Secondary };
        helpTopBtn.Click += delegate {
            if (activeTabAndroid && !String.IsNullOrEmpty(androidRoot) && File.Exists(Path.Combine(androidRoot, "手机独立安装说明.md")))
                Process.Start(new ProcessStartInfo(Path.Combine(androidRoot, "手机独立安装说明.md")) { UseShellExecute = true });
            else
                RunPc(delegate { Process.Start(new ProcessStartInfo(GroupTest.Within(GroupTest.PackageRoot, "先读我.txt")) { UseShellExecute = true }); return "已打开安装说明文档。"; });
        };
        var hubTopBtn = new AuroraButton { Text = "启动星枢 Hub", Left = 646, Top = 90, Width = 92, Height = 28, Kind = AuroraBtnKind.Accent };
        hubTopBtn.Click += delegate { RunPc(delegate { GroupTest.StartHub(); return "已启动星枢 Hub。请在设置和游戏账号页选择自己的主端、观战端及账号。"; }); };
        var topHint = new Label { Text = "操作前请正常退出游戏 · 冲突插件会自动备份后再替换", Left = 164, Top = 95, Width = 376, Height = 20, ForeColor = AuroraPalette.InkMuted, Font = new Font("Microsoft YaHei UI", 8.5F) };
        Controls.AddRange(new Control[] { stateBadge, topHint, helpTopBtn, hubTopBtn });

        pcPanel.SetBounds(24, 126, 712, 464); pcPanel.BackColor = AuroraPalette.Canvas;
        androidPanel.SetBounds(24, 126, 712, 464); androidPanel.BackColor = AuroraPalette.Canvas;
        BuildPcTab(initialPlatform, initialPath);
        BuildAndroidTab();
        Controls.Add(pcPanel);
        Controls.Add(androidPanel);

        SwitchTab(startAndroidTab);
        Shown += delegate { if (startAndroidTab) RefreshAndroidDevices(); };
    }

    private void ResolveAndroidBundlePaths()
    {
        string dir = Path.Combine(GroupTest.PackageRoot, "Android");
        string manifest = Path.Combine(dir, "candidate.json");
        string worker = Path.Combine(dir, "星枢安卓安装助手.exe");
        if (Directory.Exists(dir) && File.Exists(manifest))
        {
            androidRoot = dir;
            androidManifest = manifest;
            androidWorkerExe = File.Exists(worker) ? worker : "";
            string apk = Path.Combine(dir, "星枢手机安装助手.apk");
            androidApkPath = File.Exists(apk) ? apk : "";
        }
        else if (File.Exists(Path.Combine(GroupTest.PackageRoot, "candidate.json")))
        {
            androidRoot = GroupTest.PackageRoot;
            androidManifest = Path.Combine(GroupTest.PackageRoot, "candidate.json");
            string apk = Path.Combine(GroupTest.PackageRoot, "星枢手机安装助手.apk");
            androidApkPath = File.Exists(apk) ? apk : "";
        }
    }

    private void SwitchTab(bool showAndroid)
    {
        activeTabAndroid = showAndroid;
        pcPanel.Visible = !showAndroid;
        androidPanel.Visible = showAndroid;
        tabPcBtn.Kind = showAndroid ? AuroraBtnKind.Secondary : AuroraBtnKind.Primary;
        tabAndroidBtn.Kind = showAndroid ? AuroraBtnKind.Primary : AuroraBtnKind.Secondary;
        tabPcBtn.Invalidate();
        tabAndroidBtn.Invalidate();
        if (showAndroid && androidDevices.Items.Count == 0 && !String.IsNullOrEmpty(androidManifest))
            RefreshAndroidDevices();
    }

    private void SetStatusBadge(string text, bool isError, bool isBusy)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string, bool, bool>(SetStatusBadge), text, isError, isBusy); return; }
        stateBadge.Text = text;
        if (isBusy)
        {
            stateBadge.FillColor = AuroraPalette.AccentSoft;
            stateBadge.EdgeColor = AuroraPalette.AccentBorder;
            stateBadge.InkColor = AuroraPalette.AccentInk;
        }
        else if (isError)
        {
            stateBadge.FillColor = AuroraPalette.ErrSoft;
            stateBadge.EdgeColor = Color.FromArgb(243, 198, 210);
            stateBadge.InkColor = AuroraPalette.ErrInk;
        }
        else
        {
            stateBadge.FillColor = AuroraPalette.OkSoft;
            stateBadge.EdgeColor = Color.FromArgb(187, 228, 200);
            stateBadge.InkColor = AuroraPalette.OkInk;
        }
        stateBadge.Invalidate();
    }

    private void BuildPcTab(string initialPlatform, string initialPath)
    {
        var cfgCard = new AuroraCardPanel { Left = 0, Top = 0, Width = 712, Height = 196 };
        var stepLbl = new Label { Text = "1. 选择游戏渠道与程序位置", Left = 18, Top = 14, Width = 360, Height = 22, BackColor = AuroraPalette.Surface, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = AuroraPalette.Ink };
        var chLbl = new Label { Text = "游戏平台", Left = 18, Top = 48, Width = 70, Height = 24, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkMuted };
        platforms.SetBounds(92, 44, 210, 28);
        platforms.DropDownStyle = ComboBoxStyle.DropDownList;
        platforms.Items.AddRange(GroupTest.Release.Platforms.Cast<object>().ToArray());
        platforms.SelectedIndex = 0;

        var pathLbl = new Label { Text = "程序路径", Left = 18, Top = 86, Width = 70, Height = 24, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkMuted };
        selection.SetBounds(92, 83, 476, 26);
        selection.BorderStyle = BorderStyle.FixedSingle;
        if (initialPlatform != null)
        {
            var index = Array.FindIndex(GroupTest.Release.Platforms, p => p.Id == initialPlatform);
            if (index < 0) throw new InvalidOperationException("本包未提供所选渠道的运行时文件。");
            platforms.SelectedIndex = index;
            selection.Text = initialPath ?? "";
        }
        var browse = new AuroraButton { Text = "选择游戏…", Left = 578, Top = 80, Width = 114, Height = 30, Kind = AuroraBtnKind.Secondary };
        browse.Click += delegate {
            var p = (PlatformInfo)platforms.SelectedItem;
            using (var dialog = new OpenFileDialog { Title = "选择 " + p.ExeName, Filter = "游戏程序|" + p.ExeName, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) selection.Text = dialog.FileName;
        };

        pcInstallBtn.Text = "安装 / 更新插件"; pcInstallBtn.SetBounds(18, 134, 178, 40); pcInstallBtn.Kind = AuroraBtnKind.Primary;
        pcInstallBtn.Click += delegate { InstallPcWithConflictPrompt(); };
        pcCheckBtn.Text = "检查与冲突扫描"; pcCheckBtn.SetBounds(206, 134, 152, 40); pcCheckBtn.Kind = AuroraBtnKind.Secondary;
        pcCheckBtn.Click += delegate { ActPc("check", false); };
        pcRestoreBtn.Text = "还原安装前版本"; pcRestoreBtn.SetBounds(368, 134, 152, 40); pcRestoreBtn.Kind = AuroraBtnKind.Secondary;
        pcRestoreBtn.Click += delegate { ActPc("restore", false); };
        var exportPcBtn = new AuroraButton { Text = "导出失败日志", Left = 530, Top = 134, Width = 162, Height = 40, Kind = AuroraBtnKind.Secondary };
        exportPcBtn.Click += delegate { ExportPcDiagnosticLog(); };
        cfgCard.Controls.AddRange(new Control[] { stepLbl, chLbl, platforms, pathLbl, selection, browse, pcInstallBtn, pcCheckBtn, pcRestoreBtn, exportPcBtn });

        var logCard = new AuroraCardPanel { Left = 0, Top = 208, Width = 712, Height = 252 };
        var logTitle = new Label { Text = "2. 执行状态与诊断日志", Left = 18, Top = 12, Width = 280, Height = 22, BackColor = AuroraPalette.Surface, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = AuroraPalette.Ink };
        status.SetBounds(18, 42, 674, 192);
        status.Multiline = true;
        status.ReadOnly = true;
        status.BorderStyle = BorderStyle.FixedSingle;
        status.BackColor = AuroraPalette.SurfaceLog;
        status.ForeColor = AuroraPalette.Ink;
        status.ScrollBars = ScrollBars.Vertical;
        status.Text = "请先选择游戏平台与主程序（勿选启动器），建议先点“检查与冲突扫描”。\r\n安装成功后请通过官方启动器进入游戏，可在游戏内直接呼出星枢面板。";
        logCard.Controls.AddRange(new Control[] { logTitle, status });
        pcPanel.Controls.AddRange(new Control[] { cfgCard, logCard });
    }

    private void BuildAndroidTab()
    {
        var devCard = new AuroraCardPanel { Left = 0, Top = 0, Width = 712, Height = 248 };
        var titleLbl = new Label { Text = "1. 连接手机（USB 调试或局域网无线调试）", Left = 18, Top = 12, Width = 400, Height = 22, BackColor = AuroraPalette.Surface, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = AuroraPalette.Ink };
        andAvailLabel.SetBounds(420, 14, 272, 20);
        andAvailLabel.BackColor = AuroraPalette.Surface;
        andAvailLabel.TextAlign = ContentAlignment.MiddleRight;
        andAvailLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
        andAvailLabel.ForeColor = String.IsNullOrEmpty(androidManifest) ? AuroraPalette.ErrInk : AuroraPalette.OkInk;
        andAvailLabel.Text = String.IsNullOrEmpty(androidManifest) ? "未检测到 Android 资源包" : "已就绪：官方安卓 3.3.0 兼容";

        var adbLbl = new Label { Text = "ADB 工具", Left = 18, Top = 46, Width = 70, Height = 24, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkMuted };
        adbBox.SetBounds(92, 43, 476, 26); adbBox.BorderStyle = BorderStyle.FixedSingle; adbBox.Text = FindBundledAdb();
        var adbBrowse = new AuroraButton { Text = "选择 ADB…", Left = 578, Top = 40, Width = 114, Height = 30, Kind = AuroraBtnKind.Secondary };
        adbBrowse.Click += delegate {
            using (var picker = new OpenFileDialog { Filter = "ADB|adb.exe", Title = "选择 adb.exe" })
                if (picker.ShowDialog(this) == DialogResult.OK) adbBox.Text = picker.FileName;
        };

        var devLbl = new Label { Text = "目标手机", Left = 18, Top = 82, Width = 70, Height = 24, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkMuted };
        androidDevices.SetBounds(92, 79, 476, 28); androidDevices.DropDownStyle = ComboBoxStyle.DropDownList;
        andRefreshBtn.Text = "刷新设备"; andRefreshBtn.SetBounds(578, 78, 114, 30); andRefreshBtn.Kind = AuroraBtnKind.Secondary;
        andRefreshBtn.Click += delegate { RefreshAndroidDevices(); };

        var wifiLbl = new Label { Text = "无线连接", Left = 18, Top = 118, Width = 70, Height = 24, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkMuted };
        wirelessAddrBox.SetBounds(92, 115, 356, 26); wirelessAddrBox.BorderStyle = BorderStyle.FixedSingle;
        wirelessAddrBox.Text = "";
        var wifiHint = new Label { Text = "例 192.168.1.8:35001", Left = 456, Top = 118, Width = 116, Height = 22, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkSubtle, Font = new Font("Microsoft YaHei UI", 8F) };
        andConnectBtn.Text = "无线连接"; andConnectBtn.SetBounds(578, 114, 114, 30); andConnectBtn.Kind = AuroraBtnKind.Secondary;
        andConnectBtn.Click += delegate { ConnectWirelessAdb(); };

        andInstallBtn.Text = "安装 / 更新手机端"; andInstallBtn.SetBounds(18, 160, 178, 38); andInstallBtn.Kind = AuroraBtnKind.Primary;
        andCheckBtn.Text = "检查与冲突扫描"; andCheckBtn.SetBounds(204, 160, 138, 38); andCheckBtn.Kind = AuroraBtnKind.Secondary;
        andRestoreBtn.Text = "还原官方原版"; andRestoreBtn.SetBounds(350, 160, 128, 38); andRestoreBtn.Kind = AuroraBtnKind.Secondary;
        andApkBtn.Text = "安装手机助手 APK"; andApkBtn.SetBounds(486, 160, 206, 38); andApkBtn.Kind = AuroraBtnKind.Accent;

        andExportBtn.Text = "导出安卓失败日志"; andExportBtn.SetBounds(18, 206, 148, 30); andExportBtn.Kind = AuroraBtnKind.Secondary;
        var andGuideLbl = new Label { Text = "提示：手机需先打开官方《吉星派对》进一次大厅再退出；无线首次连接请先配对或使用 USB。", Left = 176, Top = 211, Width = 516, Height = 20, BackColor = AuroraPalette.Surface, ForeColor = AuroraPalette.InkMuted, Font = new Font("Microsoft YaHei UI", 8.5F) };

        andCheckBtn.Click += delegate { RunAndroidWorker("--check", false); };
        andInstallBtn.Click += delegate { RunAndroidInstallFlow(); };
        andRestoreBtn.Click += delegate { RunAndroidWorker("--restore", false); };
        andApkBtn.Click += delegate { InstallPhoneHelperApk(); };
        andExportBtn.Click += delegate { ExportAndroidLog(); };

        devCard.Controls.AddRange(new Control[] {
            titleLbl, andAvailLabel, adbLbl, adbBox, adbBrowse,
            devLbl, androidDevices, andRefreshBtn, wifiLbl, wirelessAddrBox, wifiHint, andConnectBtn,
            andInstallBtn, andCheckBtn, andRestoreBtn, andApkBtn, andExportBtn, andGuideLbl
        });

        var andLogCard = new AuroraCardPanel { Left = 0, Top = 258, Width = 712, Height = 202 };
        var andLogTitle = new Label { Text = "2. 安卓同步与校验日志", Left = 18, Top = 10, Width = 280, Height = 22, BackColor = AuroraPalette.Surface, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = AuroraPalette.Ink };
        androidLog.SetBounds(18, 38, 674, 146);
        androidLog.Multiline = true; androidLog.ReadOnly = true; androidLog.BorderStyle = BorderStyle.FixedSingle;
        androidLog.BackColor = AuroraPalette.SurfaceLog; androidLog.ForeColor = AuroraPalette.Ink; androidLog.ScrollBars = ScrollBars.Vertical;
        androidLog.Text = String.IsNullOrEmpty(androidManifest)
            ? "未检测到 Android 安装资源目录。请使用完整版发布包，或切换至“电脑端安装”。"
            : "已识别内置手机安装包。连接手机并允许 USB 调试后点击“刷新设备”，或输入局域网无线调试地址连接。";
        andLogCard.Controls.AddRange(new Control[] { andLogTitle, androidLog });
        androidPanel.Controls.AddRange(new Control[] { devCard, andLogCard });

        if (String.IsNullOrEmpty(androidManifest))
            andInstallBtn.Enabled = andCheckBtn.Enabled = andRestoreBtn.Enabled = andRefreshBtn.Enabled = andConnectBtn.Enabled = andApkBtn.Enabled = false;
    }

    private string FindBundledAdb()
    {
        if (!String.IsNullOrEmpty(androidRoot))
        {
            string bundled = Path.Combine(androidRoot, "platform-tools", "adb.exe");
            if (File.Exists(bundled)) return bundled;
        }
        string direct = Path.Combine(GroupTest.PackageRoot, "platform-tools", "adb.exe");
        if (File.Exists(direct)) return direct;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { string f = Path.Combine(folder.Trim('"'), "adb.exe"); if (File.Exists(f)) return f; } catch { }
        }
        return "";
    }

    private void AppendAndroidLog(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(AppendAndroidLog), text); return; }
        androidLog.AppendText((androidLog.TextLength > 0 ? "\r\n" : "") + text);
    }

    private void SetAndroidBusy(bool busy)
    {
        if (InvokeRequired) { BeginInvoke(new Action<bool>(SetAndroidBusy), busy); return; }
        andInstallBtn.Enabled = andCheckBtn.Enabled = andRestoreBtn.Enabled = andRefreshBtn.Enabled = andConnectBtn.Enabled = !busy && !String.IsNullOrEmpty(androidManifest);
        andApkBtn.Enabled = !busy && !String.IsNullOrEmpty(androidApkPath);
        SetStatusBadge(busy ? "处理中" : "就绪", false, busy);
    }

    private string SelectedAndroidSerial()
    {
        int idx = androidDevices.SelectedIndex;
        if (idx < 0 || idx >= andSerials.Count) return "";
        return andSerials[idx];
    }

    private static string RunHiddenProcess(string file, string args, out int code, int timeoutMs = 120000)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using (var p = Process.Start(psi))
        {
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { p.Kill(); code = -1; return "操作超时"; }
            System.Threading.Tasks.Task.WaitAll(outTask, errTask);
            code = p.ExitCode;
            return (outTask.Result + "\r\n" + errTask.Result).Trim();
        }
    }

    private static string QuoteArg(string v)
    {
        var sb = new StringBuilder("\"");
        int s = 0;
        foreach (char c in v)
        {
            if (c == '\\') { s++; continue; }
            sb.Append('\\', c == '"' ? s * 2 + 1 : s);
            sb.Append(c); s = 0;
        }
        sb.Append('\\', s * 2);
        return sb.Append('"').ToString();
    }

    private async void RefreshAndroidDevices()
    {
        string adb = adbBox.Text.Trim();
        if (!File.Exists(adb)) { AppendAndroidLog("未找到 adb.exe，请先选择有效的 Android platform-tools 路径。"); return; }
        SetAndroidBusy(true);
        try
        {
            int code;
            string outText = await System.Threading.Tasks.Task.Run(delegate { return RunHiddenProcess(adb, "devices -l", out code, 15000); });
            androidDevices.Items.Clear();
            andSerials.Clear();
            foreach (string rawLine in outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("*") || line.StartsWith("List ")) continue;
                var parts = Regex.Split(line, @"\s+");
                if (parts.Length < 2) continue;
                string serial = parts[0], st = parts[1];
                string model = "";
                foreach (var p in parts) if (p.StartsWith("model:")) model = p.Substring(6).Replace('_', ' ');
                andSerials.Add(serial);
                string label = serial + (model.Length > 0 ? " · " + model : "") + " [" + (st == "device" ? "已授权" : st) + "]";
                androidDevices.Items.Add(label);
            }
            if (androidDevices.Items.Count == 1) androidDevices.SelectedIndex = 0;
            AppendAndroidLog(androidDevices.Items.Count == 0
                ? "未发现设备。请确认手机开启开发者模式与 USB 调试，或在上方输入无线调试 IP:端口。"
                : "已刷新设备列表（共 " + androidDevices.Items.Count + " 台）；请在下拉框确认目标手机。");
        }
        catch (Exception ex) { AppendAndroidLog("刷新失败：" + ex.Message); SetStatusBadge("设备读取异常", true, false); }
        finally { SetAndroidBusy(false); }
    }

    private async void ConnectWirelessAdb()
    {
        string adb = adbBox.Text.Trim(), addr = wirelessAddrBox.Text.Trim();
        if (!File.Exists(adb)) { AppendAndroidLog("请先选择有效的 adb.exe。"); return; }
        if (!Regex.IsMatch(addr, @"^\d{1,3}(?:\.\d{1,3}){3}:\d{2,5}$"))
        { AppendAndroidLog("请输入完整的局域网 IP:端口，例如 192.168.1.8:35001。"); return; }
        SetAndroidBusy(true);
        try
        {
            int code = 0;
            string res = await System.Threading.Tasks.Task.Run(delegate { return RunHiddenProcess(adb, "connect " + QuoteArg(addr), out code, 20000); });
            AppendAndroidLog(res);
            RefreshAndroidDevices();
        }
        catch (Exception ex) { AppendAndroidLog("无线连接失败：" + ex.Message); SetStatusBadge("连接失败", true, false); }
        finally { SetAndroidBusy(false); }
    }

    private async void RunAndroidWorker(string actionFlag, bool replaceConflicts)
    {
        string serial = SelectedAndroidSerial(), adb = adbBox.Text.Trim();
        if (String.IsNullOrEmpty(serial)) { AppendAndroidLog("请先选择目标手机。"); return; }
        if (!File.Exists(adb)) { AppendAndroidLog("请先选择 adb.exe。"); return; }
        if (String.IsNullOrEmpty(androidWorkerExe) || !File.Exists(androidWorkerExe))
        { AppendAndroidLog("缺少 Android\\星枢安卓安装助手.exe 核心组件。"); return; }
        SetAndroidBusy(true);
        try
        {
            string logTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "Logs", "android-installer-last.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logTarget));
            var argList = new List<string> {
                actionFlag, "--manifest", QuoteArg(androidManifest), "--adb", QuoteArg(adb),
                "--serial", QuoteArg(serial), "--export-log", QuoteArg(logTarget)
            };
            if (replaceConflicts) argList.Add("--replace-conflicts");
            int code = 0;
            string result = await System.Threading.Tasks.Task.Run(delegate { return RunHiddenProcess(androidWorkerExe, String.Join(" ", argList.ToArray()), out code, 180000); });
            AppendAndroidLog(result.Length > 0 ? result : (code == 0 ? "操作完成。" : "操作未完成。"));
            SetStatusBadge(code == 0 ? "完成" : "需要注意", code != 0, false);
        }
        catch (Exception ex) { AppendAndroidLog("执行异常：" + ex.Message); SetStatusBadge("失败", true, false); }
        finally { SetAndroidBusy(false); }
    }

    private async void RunAndroidInstallFlow()
    {
        string serial = SelectedAndroidSerial(), adb = adbBox.Text.Trim();
        if (String.IsNullOrEmpty(serial)) { AppendAndroidLog("请先选择目标手机。"); return; }
        if (String.IsNullOrEmpty(androidWorkerExe) || !File.Exists(androidWorkerExe)) { AppendAndroidLog("缺少 Android 核心安装组件。"); return; }
        SetAndroidBusy(true);
        try
        {
            int checkCode = 0;
            string checkArgs = String.Join(" ", new[] { "--check", "--manifest", QuoteArg(androidManifest), "--adb", QuoteArg(adb), "--serial", QuoteArg(serial) });
            string checkOut = await System.Threading.Tasks.Task.Run(delegate { return RunHiddenProcess(androidWorkerExe, checkArgs, out checkCode, 90000); });
            AppendAndroidLog(checkOut);
            if (checkCode != 0) { SetStatusBadge("检查未通过", true, false); return; }
            bool replace = false;
            if (checkOut.IndexOf("CONFLICT_DETECTED=", StringComparison.Ordinal) >= 0)
            {
                var choice = MessageBox.Show(this, "检测到手机端已安装 APX 或同定位冲突插件，是否先完整备份旧文件并删除替换为星枢？", "检测到冲突插件", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes) { AppendAndroidLog("用户取消安装：保留原有文件未修改。"); SetStatusBadge("已取消", false, false); return; }
                replace = true;
            }
            string logTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "Logs", "android-installer-last.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logTarget));
            var argList = new List<string> {
                "--install", "--manifest", QuoteArg(androidManifest), "--adb", QuoteArg(adb),
                "--serial", QuoteArg(serial), "--export-log", QuoteArg(logTarget)
            };
            if (replace) argList.Add("--replace-conflicts");
            int instCode = 0;
            string instOut = await System.Threading.Tasks.Task.Run(delegate { return RunHiddenProcess(androidWorkerExe, String.Join(" ", argList.ToArray()), out instCode, 180000); });
            AppendAndroidLog(instOut);
            SetStatusBadge(instCode == 0 ? "安装完成" : "安装未完成", instCode != 0, false);
        }
        catch (Exception ex) { AppendAndroidLog("安装异常：" + ex.Message); SetStatusBadge("失败", true, false); }
        finally { SetAndroidBusy(false); }
    }

    private async void InstallPhoneHelperApk()
    {
        string serial = SelectedAndroidSerial(), adb = adbBox.Text.Trim();
        if (String.IsNullOrEmpty(serial)) { AppendAndroidLog("请先选择目标手机。"); return; }
        if (String.IsNullOrEmpty(androidApkPath) || !File.Exists(androidApkPath)) { AppendAndroidLog("当前安装包未包含手机助手 APK。"); return; }
        SetAndroidBusy(true);
        try
        {
            int code = 0;
            string outText = await System.Threading.Tasks.Task.Run(delegate {
                return RunHiddenProcess(adb, "-s " + QuoteArg(serial) + " install -r " + QuoteArg(androidApkPath), out code, 180000);
            });
            AppendAndroidLog(outText.Contains("Success") ? "星枢手机安装助手 APK 推送成功！可在手机桌面直接打开进行本机更新。" : outText);
            SetStatusBadge(outText.Contains("Success") ? "APK 已安装" : "推送未完成", !outText.Contains("Success"), false);
        }
        catch (Exception ex) { AppendAndroidLog("APK 安装失败：" + ex.Message); SetStatusBadge("失败", true, false); }
        finally { SetAndroidBusy(false); }
    }

    private void ExportAndroidLog()
    {
        try
        {
            using (var dlg = new SaveFileDialog { Title = "导出安卓安装诊断日志", Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt", FileName = "XingShu-Android-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string lastLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "Logs", "android-installer-last.log");
                string content = File.Exists(lastLog) ? File.ReadAllText(lastLog, Encoding.UTF8) + "\r\n\r\n--- 界面输出 ---\r\n" + androidLog.Text : androidLog.Text;
                File.WriteAllText(dlg.FileName, content, new UTF8Encoding(true));
                AppendAndroidLog("已导出日志：" + dlg.FileName);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "星枢安装助手", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void InstallPcWithConflictPrompt()
    {
        var platform = (PlatformInfo)platforms.SelectedItem;
        var diag = new DiagnosticLog();
        lastDiag = diag;
        try
        {
            UseWaitCursor = true;
            SetStatusBadge("扫描中", false, true);
            var conflicts = GroupTest.InspectConflicts(platform, selection.Text, diag);
            bool replace = false;
            if (conflicts.HasConflicts)
            {
                var prompt = GroupTest.FormatConflictPrompt(conflicts) + "\r\n\r\n是否删除原冲突插件并替换为星枢？";
                var choice = MessageBox.Show(this, prompt, "检测到同定位冲突插件", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes)
                {
                    var cancelled = new InvalidOperationException("用户取消安装：检测到同定位冲突插件，未修改游戏文件。");
                    diag.SetFailure(cancelled);
                    var logFile = diag.SaveAutoFailure();
                    status.Text = cancelled.Message + (String.IsNullOrEmpty(logFile) ? "" : "\r\n自动日志：" + logFile);
                    SetStatusBadge("已取消", false, false);
                    return;
                }
                replace = true;
            }
            status.Text = GroupTest.Perform(platform, selection.Text, "install", replace, diag);
            SetStatusBadge("安装完成", false, false);
        }
        catch (Exception ex) { HandleFailure(ex, diag); }
        finally { UseWaitCursor = false; }
    }

    private void ActPc(string action, bool replaceConflicts)
    {
        var diag = new DiagnosticLog();
        lastDiag = diag;
        RunPc(delegate { return GroupTest.Perform((PlatformInfo)platforms.SelectedItem, selection.Text, action, replaceConflicts, diag); }, diag);
    }

    private void RunPc(Func<string> action, DiagnosticLog diag = null)
    {
        if (diag != null) lastDiag = diag;
        try { UseWaitCursor = true; SetStatusBadge("执行中", false, true); status.Text = action(); SetStatusBadge("完成", false, false); }
        catch (Exception ex) { HandleFailure(ex, diag ?? lastDiag); }
        finally { UseWaitCursor = false; }
    }

    private void HandleFailure(Exception ex, DiagnosticLog diag)
    {
        var wrapped = ex is UnauthorizedAccessException
            ? new UnauthorizedAccessException("没有所选目录的写入权限。请确认游戏已退出，并选择当前用户可写入的游戏安装目录。", ex)
            : ex;
        diag.SetFailure(wrapped);
        var saved = diag.SaveAutoFailure();
        status.Text = wrapped.Message + "\r\n\r\n可点击上方“导出失败日志”保存完整诊断报告。" +
            (String.IsNullOrEmpty(saved) ? "" : "\r\n自动备份日志：" + saved);
        SetStatusBadge("需要注意", true, false);
    }

    private void ExportPcDiagnosticLog()
    {
        try
        {
            if (String.IsNullOrEmpty(lastDiag.Selection) && !String.IsNullOrWhiteSpace(selection.Text))
            {
                try { GroupTest.InspectConflicts((PlatformInfo)platforms.SelectedItem, selection.Text, lastDiag); }
                catch (Exception ex) { if (String.IsNullOrEmpty(lastDiag.FailureReason)) lastDiag.SetFailure(ex); }
            }
            using (var dialog = new SaveFileDialog
            {
                Title = "导出安装诊断 / 失败日志",
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt",
                FileName = "XingShu-Install-Failure-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, lastDiag.Render(), new UTF8Encoding(true));
                status.Text = "已导出失败/诊断日志：\r\n" + dialog.FileName + "\r\n\r\n" + status.Text;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出日志失败：" + ex.Message, "星枢安装助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
