using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
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
}

internal static class GroupTest
{
    internal static readonly string PackageRoot = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static ReleaseInfo Release;

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
        if (Release == null || Release.FormatVersion != 1 || Release.Platforms == null || Release.Platforms.Length != 2)
            throw new InvalidOperationException("不支持的发布包格式。");
    }

    internal static void VerifyPayload(PlatformInfo platform)
    {
        if (Hash(Within(PackageRoot, platform.Payload)) != platform.PayloadSha256)
            throw new InvalidOperationException("插件文件校验失败，请重新完整解压发布包。");
    }

    internal static string GameRoot(PlatformInfo platform, string selection)
    {
        if (String.IsNullOrWhiteSpace(selection)) throw new InvalidOperationException("请先选择游戏程序。");
        var root = Path.GetFullPath(selection.Trim().Trim('"'));
        if (File.Exists(root))
        {
            if (!String.Equals(Path.GetFileName(root), platform.ExeName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("所选游戏程序与平台不一致。");
            root = Path.GetDirectoryName(root);
        }
        if (!File.Exists(Within(root, platform.ExeName)))
            throw new InvalidOperationException("此目录没有对应的游戏程序，请选择实际游戏 exe，不能选择启动器。");
        if (Hash(Within(root, "GameAssembly.dll")) != platform.GameAssemblySha256)
            throw new InvalidOperationException("游戏版本或渠道不匹配；本包只适配 3.3.0 / 资源 026 的对应 Windows 客户端。");
        return root;
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

    internal static string Perform(PlatformInfo platform, string selection, string action)
    {
        VerifyPayload(platform);
        var root = GameRoot(platform, selection);
        var target = Within(root, platform.BundleRelativePath);
        var lockName = "Local\\XingShuGroupTest-" + platform.Id;
        using (var mutex = new Mutex(false, lockName))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("另一个安装任务正在进行，请稍后重试。");
            try
            {
                if (!File.Exists(target))
                    throw new InvalidOperationException("未找到资源 026 缓存。请先完成官方游戏更新并正常进入一次大厅，再退出游戏重试。");
                var current = Hash(target);
                var accepted = platform.AcceptedHashes.Contains(current);
                if (current != platform.PayloadSha256 && !accepted)
                    throw new InvalidOperationException("当前资源与本发布包不匹配，未修改任何游戏文件。请先通过官方平台校验/修复游戏，完成更新后重试。");
                if (action == "check") return platform.Label + "：版本匹配，" + (current == platform.PayloadSha256 ? "已安装本版。" : "可以安装。");
                EnsureGameClosed(platform, root);
                if (action == "check-closed") return platform.Label + "：所选目录的游戏未运行。";
                var backupDirectory = Within(root, ".xingshu-backup/" + Release.Version + "/" + platform.Id);
                var backup = Within(backupDirectory, "__data.before");
                var receiptPath = Within(backupDirectory, "receipt.json");
                if (action == "restore")
                {
                    if (!File.Exists(receiptPath) || !File.Exists(backup))
                        throw new InvalidOperationException("没有此助手的安装前备份。若由其他方式安装，请使用原备份或官方平台校验游戏。");
                    var receipt = Json.Deserialize<InstallReceipt>(File.ReadAllText(receiptPath, Encoding.UTF8));
                    if (receipt == null || receipt.Version != Release.Version || receipt.Platform != platform.Id ||
                        receipt.Target != platform.BundleRelativePath || receipt.InstalledSha256 != platform.PayloadSha256 ||
                        !platform.AcceptedHashes.Contains(receipt.BeforeSha256) || Hash(backup) != receipt.BeforeSha256)
                        throw new InvalidOperationException("安装前备份校验失败，未还原。");
                    if (current == receipt.BeforeSha256) return "已是安装前版本，无需重复还原。";
                    if (current != receipt.InstalledSha256)
                        throw new InvalidOperationException("游戏文件已被其他更新修改，不能覆盖还原。请使用官方平台修复。");
                    EnsureGameClosed(platform, root);
                    ReplaceWith(backup, target, receipt.BeforeSha256);
                    return platform.Label + "：已还原到本次安装前的版本。";
                }
                if (action != "install") throw new ArgumentException("未知操作。");
                if (current == platform.PayloadSha256) return platform.Label + "：已是本版，无需重复安装。";
                Directory.CreateDirectory(backupDirectory);
                if (File.Exists(backup))
                {
                    if (Hash(backup) != current) throw new IOException("已有备份与当前文件不同，已停止安装以保留原备份。");
                }
                else File.Copy(target, backup, false);
                if (Hash(backup) != current) throw new IOException("安装前备份校验失败，未替换游戏文件。");
                var receiptBytes = Encoding.UTF8.GetBytes(Json.Serialize(new InstallReceipt {
                    Version = Release.Version, Platform = platform.Id, Target = platform.BundleRelativePath,
                    BeforeSha256 = current, InstalledSha256 = platform.PayloadSha256
                }));
                if (File.Exists(receiptPath))
                {
                    if (!File.ReadAllBytes(receiptPath).SequenceEqual(receiptBytes))
                        throw new IOException("已有安装记录不匹配，未修改游戏文件。");
                }
                else WriteNew(receiptPath, receiptBytes);
                EnsureGameClosed(platform, root);
                try { ReplaceWith(Within(PackageRoot, platform.Payload), target, platform.PayloadSha256); }
                catch
                {
                    if (Hash(target) != current) ReplaceWith(backup, target, current);
                    throw;
                }
                return platform.Label + "：安装完成，已保留安装前备份。请从官方平台正常启动游戏。";
            }
            finally { mutex.ReleaseMutex(); }
        }
    }

    internal static void StartHub()
    {
        var hub = Within(PackageRoot, "Hub/xingshu-hub.exe");
        if (Hash(hub) != Release.HubSha256) throw new IOException("Hub 文件校验失败，请重新解压测试包。");
        var running = Process.GetProcessesByName("xingshu-hub");
        try { if (running.Length != 0) throw new InvalidOperationException("星枢 Hub 已在运行，请先退出旧窗口，再启动此测试版。"); }
        finally { foreach (var process in running) process.Dispose(); }
        Process.Start(new ProcessStartInfo(hub) { WorkingDirectory = Path.GetDirectoryName(hub), UseShellExecute = true });
    }

    [STAThread]
    internal static int Main(string[] args)
    {
        try
        {
            LoadPackage();
            if (args.Length == 3 && args[0] == "--ui")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerWindow(args[1], args[2]));
                return 0;
            }
            if (args.Length > 0)
            {
                string result;
                if (args.Length == 1 && args[0] == "--verify-package")
                {
                    foreach (var p in Release.Platforms) VerifyPayload(p);
                    if (Hash(Within(PackageRoot, "Hub/xingshu-hub.exe")) != Release.HubSha256) throw new IOException("Hub hash mismatch");
                    result = "PASS: " + Release.Version;
                }
                else if (args.Length == 3 && new[] { "--check", "--check-closed", "--install", "--restore" }.Contains(args[0]))
                {
                    var platform = Release.Platforms.Single(p => p.Id == args[1]);
                    result = Perform(platform, args[2], args[0].Substring(2));
                }
                else throw new ArgumentException("Usage: --verify-package | --check/--install/--restore PLATFORM GAME_ROOT");
                Console.WriteLine(result);
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerWindow());
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length > 0) Console.Error.WriteLine(ex.Message);
            else MessageBox.Show(ex.Message, "星枢安装助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal sealed class InstallerWindow : Form
{
    private readonly ComboBox platforms = new ComboBox();
    private readonly TextBox selection = new TextBox();
    private readonly TextBox status = new TextBox();

    internal InstallerWindow(string initialPlatform = null, string initialPath = null)
    {
        Text = "星枢安装助手 · " + GroupTest.Release.Version;
        ClientSize = new Size(660, 420);
        MinimumSize = MaximumSize = Size;
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        var intro = new Label { Left = 22, Top = 18, Width = 612, Height = 48,
            Text = "适配吉星派对 3.3.0 / 资源 026（Windows）\r\n先退出游戏，再选择对应平台和游戏程序；主端、观战端分别安装。" };
        Controls.Add(intro);
        platforms.SetBounds(22, 80, 210, 28);
        platforms.DropDownStyle = ComboBoxStyle.DropDownList;
        platforms.Items.AddRange(GroupTest.Release.Platforms.Cast<object>().ToArray());
        platforms.SelectedIndex = 0;
        Controls.Add(platforms);
        selection.SetBounds(22, 124, 484, 28);
        if (initialPlatform != null)
        {
            var index = Array.FindIndex(GroupTest.Release.Platforms, p => p.Id == initialPlatform);
            if (index < 0) throw new InvalidOperationException("本包未提供所选渠道的运行时文件。");
            platforms.SelectedIndex = index;
            selection.Text = initialPath ?? "";
        }
        Controls.Add(selection);
        var browse = Button("选择游戏…", 516, 122, 118, delegate {
            var p = (PlatformInfo)platforms.SelectedItem;
            using (var dialog = new OpenFileDialog { Title = "选择 " + p.ExeName, Filter = "游戏程序|" + p.ExeName, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) selection.Text = dialog.FileName;
        });
        Controls.Add(browse);
        Controls.Add(Button("安装 / 更新插件", 22, 172, 170, delegate { Act("install"); }));
        Controls.Add(Button("还原安装前版本", 204, 172, 170, delegate { Act("restore"); }));
        Controls.Add(Button("启动星枢 Hub", 386, 172, 156, delegate { Run(delegate { GroupTest.StartHub(); return "已启动星枢 Hub。请在设置和游戏账号页选择自己的主端、观战端及账号。"; }); }));
        Controls.Add(Button("说明", 554, 172, 80, delegate { Run(delegate {
            Process.Start(new ProcessStartInfo(GroupTest.Within(GroupTest.PackageRoot, "先读我.txt")) { UseShellExecute = true }); return "已打开安装与测试说明。";
        }); }));
        status.SetBounds(22, 224, 612, 166);
        status.Multiline = true;
        status.ReadOnly = true;
        status.ScrollBars = ScrollBars.Vertical;
        status.Text = "星枢群发测试版。\r\n\r\n请先点“说明”。安装成功后通过 Steam / TapTap 官方启动器进入游戏。完整观战手牌仍需已登录的官方观战客户端。";
        Controls.Add(status);
    }

    private static Button Button(string text, int x, int y, int width, EventHandler handler)
    {
        var button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 36 };
        button.Click += handler;
        return button;
    }

    private void Act(string action)
    {
        Run(delegate { return GroupTest.Perform((PlatformInfo)platforms.SelectedItem, selection.Text, action); });
    }

    private void Run(Func<string> action)
    {
        try { UseWaitCursor = true; status.Text = action(); }
        catch (UnauthorizedAccessException) { status.Text = "没有所选目录的写入权限。请确认游戏已退出，并选择当前用户可写入的游戏安装目录。"; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { UseWaitCursor = false; }
    }
}
