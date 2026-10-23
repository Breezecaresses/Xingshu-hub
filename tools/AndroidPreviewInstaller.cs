using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class AndroidPreviewInstaller : Form
{
    readonly TextBox adb = new TextBox(), log = new TextBox();
    readonly ComboBox devices = new ComboBox();
    readonly Button refresh = new Button(), install = new Button(), restore = new Button();
    static readonly string Home = AppDomain.CurrentDomain.BaseDirectory;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    [STAThread] static int Main(string[] args)
    {
        try {
            if (args.Length > 0) {
                // GUI executables have no console code page. Write UTF-8 to
                // redirected pipes without calling SetConsoleOutputCP.
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                var options = new Dictionary<string, string>();
                for (int i = 1; i < args.Length; i += 2) {
                    if (i + 1 == args.Length) throw new Exception("Missing option value");
                    options.Add(args[i], args[i + 1]);
                }
                var worker = new Deployment(Get(options, "--manifest", Path.Combine(Home, "candidate.json")),
                    Get(options, "--adb", FindAdb()), Get(options, "--serial", ""), Get(options, "--backup-dir", ""), Console.WriteLine);
                if (args[0] == "--verify-package") worker.VerifyPackage();
                else if (args[0] == "--check") worker.Inspect();
                else if (args[0] == "--install" || args[0] == "--restore") worker.Apply(args[0] == "--restore");
                else throw new Exception("Unknown action");
                return 0;
            }
            Application.EnableVisualStyles(); Application.Run(new AndroidPreviewInstaller()); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    static string Get(Dictionary<string,string> values, string key, string fallback) { return values.ContainsKey(key) ? values[key] : fallback; }
    static string FindAdb()
    {
        string bundled = Path.Combine(Home, "platform-tools", "adb.exe");
        if (File.Exists(bundled)) return bundled;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
            try { string file = Path.Combine(folder.Trim('"'), "adb.exe"); if (File.Exists(file)) return file; } catch { }
        }
        string[] known = { @"E:\TapTap\Support\android_emulator\engine\nx_main\adb.exe", @"C:\Program Files\Netease\MuMuPlayer-12.0\shell\adb.exe" };
        foreach (string file in known) if (File.Exists(file)) return file;
        return "";
    }
    AndroidPreviewInstaller()
    {
        Text = "星枢 · 安卓安装助手"; ClientSize = new Size(680, 465); MinimumSize = new Size(690, 500);
        BackColor = Color.FromArgb(255, 246, 222); Font = new Font("Microsoft YaHei UI", 9F);
        var title = new Label { Text = "电脑连接手机 · 安装 / 更新 / 还原", Left = 20, Top = 16, Width = 620, Font = new Font(Font.FontFamily, 13F, FontStyle.Bold) };
        var help = new Label { Text = "先在官方游戏完成资源更新并进过大厅，正常退出游戏。\n手机需开启 USB 调试并确认授权；模拟器需先连接对应 ADB 端口。", Left = 20, Top = 53, Width = 640, Height = 43 };
        adb.SetBounds(20, 104, 515, 28); adb.Text = FindAdb();
        var browse = new Button { Text = "选择 ADB", Left = 545, Top = 102, Width = 110, Height = 30 };
        browse.Click += delegate { using (var picker = new OpenFileDialog { Filter = "ADB|adb.exe", Title = "选择已安装的 Android platform-tools 中的 adb.exe" }) if (picker.ShowDialog() == DialogResult.OK) adb.Text = picker.FileName; };
        devices.SetBounds(20, 145, 515, 28); devices.DropDownStyle = ComboBoxStyle.DropDownList;
        refresh.Text = "刷新设备"; refresh.SetBounds(545, 143, 110, 30); refresh.Click += delegate { RefreshDevices(); };
        install.Text = "安装 / 更新"; install.SetBounds(20, 187, 310, 37); install.BackColor = Color.FromArgb(255, 35, 151);
        restore.Text = "还原原版"; restore.SetBounds(345, 187, 310, 37); restore.BackColor = Color.FromArgb(255, 197, 0);
        install.Click += delegate { Run(false); }; restore.Click += delegate { Run(true); };
        log.SetBounds(20, 238, 635, 205); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
        log.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        Controls.AddRange(new Control[] { title, help, adb, browse, devices, refresh, install, restore, log });
        Shown += delegate { RefreshDevices(); };
    }
    void Write(string value) { if (InvokeRequired) { BeginInvoke(new Action<string>(Write), value); return; } log.AppendText(value + Environment.NewLine); }
    async void RefreshDevices()
    {
        refresh.Enabled = false;
        try {
            string executable = adb.Text;
            if (!File.Exists(executable)) throw new Exception("未找到 ADB。请完整解压包含 platform-tools 的安装包，或点击“选择 ADB”。");
            string result = await Task.Run(() => Deployment.Execute(executable, "devices"));
            devices.Items.Clear();
            foreach (Match match in Regex.Matches(result, @"(?m)^([^\s]+)\s+device\s*$")) devices.Items.Add(match.Groups[1].Value);
            if (devices.Items.Count == 1) devices.SelectedIndex = 0;
            Write(devices.Items.Count == 0 ? "未找到已授权设备。请检查连接，在手机上确认 USB 调试授权后刷新。" : "请选择目标设备；多个设备时不会自动选择。");
        } catch (Exception error) { Write("读取设备失败：" + error.Message); }
        finally { refresh.Enabled = true; }
    }
    async void Run(bool undo)
    {
        if (devices.SelectedItem == null) { Write("请先选择目标设备。"); return; }
        install.Enabled = restore.Enabled = refresh.Enabled = false;
        try {
            string serial = (string)devices.SelectedItem, executable = adb.Text;
            await Task.Run(() => new Deployment(Path.Combine(Home, "candidate.json"), executable, serial, "", Write).Apply(undo));
            Write("完成。现在可以从官方入口正常启动游戏。");
        } catch (Exception error) { Write("未完成：" + error.Message); }
        finally { install.Enabled = restore.Enabled = refresh.Enabled = true; }
    }

    internal sealed class Deployment
    {
        const string Package = "com.feimo.astralparty";
        readonly Dictionary<string,object> manifest;
        readonly string manifestDirectory, executable, serial, backupDirectory;
        readonly Action<string> report;
        string Value(string key) { return Convert.ToString(manifest[key]); }
        string Target { get { return "/sdcard/Android/data/" + Package + "/files/" + Value("bundleRelativePath"); } }
        string Backup { get { return Path.Combine(backupDirectory, "assembly.vanilla.__data"); } }
        string Receipt { get { return Path.Combine(backupDirectory, "deployment.json"); } }
        string Source { get { return Payload("candidate"); } }
        string Payload(string key) {
                string path = Path.GetFullPath(Path.Combine(manifestDirectory, Value(key)));
                if (!path.StartsWith(manifestDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("安装包文件路径越界");
                return path;
        }
        internal Deployment(string file, string adbPath, string device, string backups, Action<string> logger)
        {
            manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(file));
            manifest = Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file, Encoding.UTF8));
            executable = adbPath; serial = device; report = logger;
            backupDirectory = backups.Length > 0 ? Path.GetFullPath(backups) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "AndroidBackups", HashBytes(Encoding.UTF8.GetBytes(device)).Substring(0, 16));
            if (Value("package") != Package || Value("platform") != "ANDROID_CN"
                || !Regex.IsMatch(Value("bundleRelativePath"), @"\Acom\.unity\.addressables/AssetBundles/[a-f0-9]{32}/[a-f0-9]{32}/__data\z")
                || !Regex.IsMatch(Value("candidateSha256"), @"\A[a-f0-9]{64}\z") || !Regex.IsMatch(Value("originalSha256"), @"\A[a-f0-9]{64}\z"))
                throw new Exception("安装包清单不符合此预览的格式");
        }
        internal void VerifyPackage() { if (Hash(Source) != Value("candidateSha256")) throw new Exception("安装包损坏：文件校验不符"); report("安装包完整性校验通过"); }
        internal void Check()
        {
            if (serial.Length == 0 || serial.StartsWith("-") || Regex.IsMatch(serial, @"[\s""']")) throw new Exception("请选择有效设备序列号");
            if (Adb("get-state").Trim() != "device") throw new Exception("设备未授权或未就绪");
            Match version = Regex.Match(Adb("shell", "dumpsys package " + Package), @"(?m)^\s*versionName=(\S+)\s*$");
            if (!version.Success || version.Groups[1].Value != Value("appVersion")) throw new Exception("游戏版本不匹配，请使用对应预览包");
            report("游戏版本与设备已确认：" + serial);
        }
        bool Known(string value)
        {
            if (value == Value("originalSha256") || value == Value("candidateSha256")) return true;
            object entries;
            if (manifest.TryGetValue("supportedPreviousSha256", out entries)) {
                var values = entries as System.Collections.IEnumerable;
                if (values != null) foreach (object entry in values) if (Convert.ToString(entry) == value) return true;
            }
            return false;
        }
        internal void Inspect()
        {
            VerifyPackage(); Check();
            string current = RemoteHash(Target);
            report("当前游戏缓存：" + (current == Value("candidateSha256") ? "本版星枢" : current == Value("originalSha256") ? "官方原版" : Known(current) ? "可升级的旧版星枢" : "未知版本，禁止覆盖"));
            report("SHA256：" + current);
            if (!Known(current)) throw new Exception("游戏缓存不在已验证版本中，请先使用官方资源修复。");
            EnsureGameClosed();
            report("游戏已关闭，资源可读取；安装时将再次核对并保留备份。");
        }
        void EnsureGameClosed()
        {
            if (Regex.IsMatch(Adb("shell", "ps -A"), @"(?m)\s" + Regex.Escape(Package) + @"(?::[^\s]+)?\s*$"))
                throw new Exception("游戏仍在运行。请正常退出游戏后重试，不会强制中断对局。");
        }
        internal void Apply(bool undo)
        {
            if (!undo) VerifyPackage(); Check();
            string current = RemoteHash(Target), vanilla = Value("originalSha256"), expected = undo ? vanilla : Value("candidateSha256");
            var previous = File.Exists(Receipt) ? Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Receipt, Encoding.UTF8)) : new Dictionary<string,object>();
            bool tracked = previous.ContainsKey("serial") && Convert.ToString(previous["serial"]) == serial
                && previous.ContainsKey("target") && Convert.ToString(previous["target"]) == Target
                && previous.ContainsKey("installedSha256") && Convert.ToString(previous["installedSha256"]) == current;
            if (!Known(current) && !tracked) throw new Exception("缓存不是原版或已验证的星枢版本，已停止。请先使用官方资源修复。");
            EnsureGameClosed();
            if (current == expected) { report((undo ? "已经是官方原版" : "已经安装本版星枢") + "，SHA256 校验通过，无需重复写入。"); return; }
            if (File.Exists(Backup)) { if (Hash(Backup) != vanilla) throw new Exception("原版备份校验失败"); }
            else {
                Directory.CreateDirectory(backupDirectory);
                if (current == vanilla) Adb("pull", Target, Backup);
                else if (manifest.ContainsKey("vanilla") && Hash(Payload("vanilla")) == vanilla) File.Copy(Payload("vanilla"), Backup, false);
                else throw new Exception("缺少有效原版备份，请沿用原安装工具还原后重试。");
                if (Hash(Backup) != vanilla) throw new Exception("备份校验失败，未修改游戏");
            }
            string snapshot = Path.Combine(backupDirectory, "before-" + current + ".__data");
            if (!File.Exists(snapshot)) Adb("pull", Target, snapshot);
            if (Hash(snapshot) != current) throw new Exception("安装前快照校验失败，未修改游戏");
            string stage = "/data/local/tmp/xingshu-" + expected + ".bundle";
            Adb("push", undo ? Backup : Source, stage);
            if (RemoteHash(stage) != expected || RemoteHash(Target) != current) throw new Exception("暂存校验失败或游戏缓存已变化，未修改游戏");
            EnsureGameClosed();
            try {
                Adb("shell", "cp " + QuoteShell(stage) + " " + QuoteShell(Target));
                if (RemoteHash(Target) != expected) throw new Exception("写入校验失败");
            } catch (Exception error) {
                Adb("push", snapshot, stage);
                if (RemoteHash(stage) != current) throw new Exception("还原暂存失败，请保留备份并使用官方修复：" + error.Message);
                Adb("shell", "cp " + QuoteShell(stage) + " " + QuoteShell(Target));
                if (RemoteHash(Target) != current) throw new Exception("还原校验失败，请使用官方资源修复");
                throw new Exception("安装失败，已校验恢复安装前版本：" + error.Message);
            }
            var record = new Dictionary<string,object> { {"serial",serial}, {"package",Package}, {"target",Target}, {"originalSha256",vanilla}, {"installedSha256",expected}, {"backup",Backup}, {"action",undo ? "restore" : "install"}, {"timestamp",DateTime.UtcNow.ToString("o")} };
            string temporary = Receipt + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(record), new UTF8Encoding(false));
            if (File.Exists(Receipt)) File.Replace(temporary, Receipt, null); else File.Move(temporary, Receipt);
            try { Adb("shell", "rm -f " + QuoteShell(stage)); } catch { }
            report((undo ? "还原" : "安装") + "完成，SHA256 校验通过。备份：" + backupDirectory);
        }
        string Adb(params string[] args) { var all = new List<string> { "-s", serial }; all.AddRange(args); return Execute(executable, all.ToArray()); }
        string RemoteHash(string path) {
            Match value = Regex.Match(Adb("shell", "sha256sum " + QuoteShell(path)), @"\A([a-fA-F0-9]{64})\s");
            if (!value.Success) throw new Exception("设备无法读取目标缓存或 SHA256 响应无效");
            return value.Groups[1].Value.ToLowerInvariant();
        }
        static string QuoteShell(string value) { return "'" + value.Replace("'", "'\"'\"'") + "'"; }
        static string QuoteArgument(string value) {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        internal static string Execute(string file, params string[] args)
        {
            var quoted = new List<string>(); foreach (string arg in args) quoted.Add(QuoteArgument(arg));
            var info = new ProcessStartInfo(file, string.Join(" ", quoted.ToArray())) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            using (var process = Process.Start(info)) {
                Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120000)) { process.Kill(); throw new Exception("ADB 操作超时"); }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new Exception((error.Result + "\n" + output.Result).Trim());
                return output.Result;
            }
        }
        static string Hash(string file) { using (var stream = File.OpenRead(file)) using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string HashBytes(byte[] bytes) { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
