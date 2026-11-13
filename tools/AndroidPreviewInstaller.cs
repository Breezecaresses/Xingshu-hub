using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class AndroidPreviewInstaller : Form
{
    static readonly Color CanvasColor = Color.FromArgb(252, 251, 248);
    static readonly Color CardColor = Color.FromArgb(255, 255, 255);
    static readonly Color SubtleColor = Color.FromArgb(247, 245, 239);
    static readonly Color LogColor = Color.FromArgb(250, 249, 245);
    static readonly Color BorderColor = Color.FromArgb(228, 223, 213);
    static readonly Color InkColor = Color.FromArgb(28, 28, 28);
    static readonly Color MutedColor = Color.FromArgb(95, 92, 86);
    static readonly Color AccentSoft = Color.FromArgb(255, 241, 229);
    static readonly Color AccentBorder = Color.FromArgb(245, 207, 175);
    static readonly Color AccentInk = Color.FromArgb(184, 75, 8);

    readonly TextBox adb = new TextBox(), log = new TextBox();
    readonly ComboBox devices = new ComboBox();
    readonly Button refresh = new Button(), checkBtn = new Button(), install = new Button(), restore = new Button(), exportLogBtn = new Button();
    static readonly string Home = AppDomain.CurrentDomain.BaseDirectory;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    AndroidDiagLog lastDiag = new AndroidDiagLog();

    [STAThread] static int Main(string[] args)
    {
        var diag = new AndroidDiagLog();
        string exportLogPath = "";
        try {
            bool standaloneUi = false;
            var argList = new List<string>();
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--standalone-ui") { standaloneUi = true; continue; }
                argList.Add(args[i]);
            }
            if (argList.Count > 0) {
                // GUI executables have no console code page. Write UTF-8 to
                // redirected pipes without calling SetConsoleOutputCP.
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                string action = "";
                bool replaceConflicts = false;
                var options = new Dictionary<string, string>();
                for (int i = 0; i < argList.Count; i++) {
                    string a = argList[i];
                    if (a == "--verify-package" || a == "--check" || a == "--install" || a == "--restore") { action = a; continue; }
                    if (a == "--replace-conflicts") { replaceConflicts = true; continue; }
                    if (a.StartsWith("--")) {
                        if (i + 1 >= argList.Count) throw new Exception("Missing option value: " + a);
                        options[a] = argList[++i];
                    } else if (action.Length == 0) {
                        action = a;
                    } else {
                        throw new Exception("Unexpected argument: " + a);
                    }
                }
                exportLogPath = Get(options, "--export-log", "");
                var worker = new Deployment(Get(options, "--manifest", Path.Combine(Home, "candidate.json")),
                    Get(options, "--adb", FindAdb()), Get(options, "--serial", ""), Get(options, "--backup-dir", ""), Console.WriteLine, diag);
                if (action == "--verify-package") worker.VerifyPackage();
                else if (action == "--check") worker.Inspect();
                else if (action == "--install" || action == "--restore") worker.Apply(action == "--restore", replaceConflicts);
                else throw new Exception("Unknown action");
                if (exportLogPath.Length > 0) File.WriteAllText(exportLogPath, diag.Render(), new UTF8Encoding(true));
                return 0;
            }
            if (!standaloneUi) {
                string parentUnified = Path.GetFullPath(Path.Combine(Home, "..", "星枢安装助手.exe"));
                string parentRelease = Path.GetFullPath(Path.Combine(Home, "..", "release.json"));
                if (File.Exists(parentUnified) && File.Exists(parentRelease)) {
                    Process.Start(new ProcessStartInfo(parentUnified, "--tab-android") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(parentUnified) });
                    return 0;
                }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new AndroidPreviewInstaller()); return 0;
        } catch (Exception error) {
            diag.SetFailure(error);
            string saved = diag.SaveAutoFailure();
            if (exportLogPath.Length > 0) {
                try { File.WriteAllText(exportLogPath, diag.Render(), new UTF8Encoding(true)); } catch { }
            }
            if (args.Length > 0) {
                Console.Error.WriteLine(error.Message);
                if (saved.Length > 0) Console.Error.WriteLine("失败诊断日志已保存：" + saved);
            } else {
                MessageBox.Show(error.Message + (saved.Length > 0 ? "\r\n\r\n诊断日志已保存：" + saved : ""), "星枢安装助手 · 安卓端", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
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
    static void StyleAuroraButton(Button b, bool primary, bool accent = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Microsoft YaHei UI", 9.25F, FontStyle.Bold);
        if (primary) { b.BackColor = InkColor; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = InkColor; }
        else if (accent) { b.BackColor = AccentSoft; b.ForeColor = AccentInk; b.FlatAppearance.BorderColor = AccentBorder; }
        else { b.BackColor = SubtleColor; b.ForeColor = InkColor; b.FlatAppearance.BorderColor = Color.FromArgb(209, 203, 190); }
    }
    AndroidPreviewInstaller()
    {
        DoubleBuffered = true;
        Text = "星枢安装助手 · 安卓端 (ADB)"; ClientSize = new Size(720, 548); MinimumSize = MaximumSize = Size;
        BackColor = CanvasColor; ForeColor = InkColor; Font = new Font("Microsoft YaHei UI", 9.25F);
        StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
        var header = new Panel { Left = 0, Top = 0, Width = 720, Height = 78, BackColor = Color.FromArgb(255, 245, 233) };
        header.Paint += delegate(object s, PaintEventArgs pe) {
            using (var pen = new Pen(BorderColor, 1f)) pe.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(InkColor)) pe.Graphics.FillEllipse(bg, 22, 16, 40, 40);
            var pts = new[] { new PointF(42f, 22f), new PointF(45.5f, 32.5f), new PointF(56f, 36f), new PointF(45.5f, 39.5f), new PointF(42f, 50f), new PointF(38.5f, 39.5f), new PointF(28f, 36f), new PointF(38.5f, 32.5f) };
            using (var star = new SolidBrush(Color.FromArgb(255, 196, 108))) pe.Graphics.FillPolygon(star, pts);
        };
        var title = new Label { Text = "星枢安装助手 · 安卓端", Left = 74, Top = 15, Width = 360, Height = 26, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), ForeColor = InkColor };
        var help = new Label { Text = "先在官方游戏更新资源并进入大厅一次后正常退出；冲突插件将先备份再替换。", Left = 74, Top = 45, Width = 620, Height = 20, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = MutedColor };
        header.Controls.AddRange(new Control[] { title, help });
        var card = new Panel { Left = 20, Top = 94, Width = 680, Height = 166, BackColor = CardColor, BorderStyle = BorderStyle.FixedSingle };
        var adbLbl = new Label { Text = "ADB 路径", Left = 16, Top = 18, Width = 66, Height = 24, ForeColor = MutedColor };
        adb.SetBounds(86, 15, 464, 26); adb.BorderStyle = BorderStyle.FixedSingle; adb.Text = FindAdb();
        var browse = new Button { Text = "选择 ADB…", Left = 560, Top = 13, Width = 104, Height = 30 }; StyleAuroraButton(browse, false);
        browse.Click += delegate { using (var picker = new OpenFileDialog { Filter = "ADB|adb.exe", Title = "选择 adb.exe" }) if (picker.ShowDialog() == DialogResult.OK) adb.Text = picker.FileName; };
        var devLbl = new Label { Text = "目标手机", Left = 16, Top = 56, Width = 66, Height = 24, ForeColor = MutedColor };
        devices.SetBounds(86, 53, 464, 28); devices.DropDownStyle = ComboBoxStyle.DropDownList;
        refresh.Text = "刷新设备"; refresh.SetBounds(560, 52, 104, 30); StyleAuroraButton(refresh, false); refresh.Click += delegate { RefreshDevices(); };
        install.Text = "安装 / 更新手机端"; install.SetBounds(16, 104, 186, 40); StyleAuroraButton(install, true);
        restore.Text = "还原官方原版"; restore.SetBounds(212, 104, 140, 40); StyleAuroraButton(restore, false);
        checkBtn.Text = "检查与冲突扫描"; checkBtn.SetBounds(362, 104, 152, 40); StyleAuroraButton(checkBtn, false);
        exportLogBtn.Text = "导出失败日志"; exportLogBtn.SetBounds(524, 104, 140, 40); StyleAuroraButton(exportLogBtn, false, true);
        card.Controls.AddRange(new Control[] { adbLbl, adb, browse, devLbl, devices, refresh, install, restore, checkBtn, exportLogBtn });
        install.Click += delegate { RunInstallWithConflictPrompt(); };
        restore.Click += delegate { RunAction(true, false); };
        checkBtn.Click += delegate { RunCheck(); };
        exportLogBtn.Click += delegate { ExportLog(); };
        log.SetBounds(20, 274, 680, 254); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
        log.BorderStyle = BorderStyle.FixedSingle; log.BackColor = LogColor; log.ForeColor = InkColor;
        log.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        Controls.AddRange(new Control[] { header, card, log });
        Shown += delegate { RefreshDevices(); };
    }
    void Write(string value) { if (InvokeRequired) { BeginInvoke(new Action<string>(Write), value); return; } log.AppendText(value + Environment.NewLine); }
    void SetBusy(bool busy) { install.Enabled = restore.Enabled = checkBtn.Enabled = refresh.Enabled = !busy; }
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
    async void RunCheck()
    {
        if (devices.SelectedItem == null) { Write("请先选择目标设备。"); return; }
        SetBusy(true);
        var diag = new AndroidDiagLog(); lastDiag = diag;
        try {
            string serial = (string)devices.SelectedItem, executable = adb.Text;
            await Task.Run(() => new Deployment(Path.Combine(Home, "candidate.json"), executable, serial, "", Write, diag).Inspect());
        } catch (Exception error) {
            diag.SetFailure(error); string saved = diag.SaveAutoFailure();
            Write("检查未通过：" + error.Message + (saved.Length > 0 ? "（日志已保存：" + saved + "）" : ""));
        } finally { SetBusy(false); }
    }
    async void RunInstallWithConflictPrompt()
    {
        if (devices.SelectedItem == null) { Write("请先选择目标设备。"); return; }
        SetBusy(true);
        var diag = new AndroidDiagLog(); lastDiag = diag;
        try {
            string serial = (string)devices.SelectedItem, executable = adb.Text, manifestFile = Path.Combine(Home, "candidate.json");
            var worker = new Deployment(manifestFile, executable, serial, "", Write, diag);
            var conflicts = await Task.Run(() => worker.InspectAndScanConflicts());
            bool replace = false;
            if (conflicts.HasConflicts) {
                var prompt = Deployment.FormatConflictPrompt(conflicts) + "\r\n\r\n是否先备份并删除原冲突插件，再安装星枢？";
                if (MessageBox.Show(this, prompt, "检测到同定位冲突插件", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) {
                    var cancelled = new Exception("用户取消安装：检测到同定位冲突插件，未修改设备文件。");
                    diag.SetFailure(cancelled); diag.SaveAutoFailure();
                    Write(cancelled.Message);
                    return;
                }
                replace = true;
            }
            await Task.Run(() => worker.Apply(false, replace));
            Write("完成。现在可以从官方入口正常启动游戏。");
        } catch (Exception error) {
            diag.SetFailure(error); string saved = diag.SaveAutoFailure();
            Write("未完成：" + error.Message + (saved.Length > 0 ? "\r\n自动日志：" + saved : ""));
        } finally { SetBusy(false); }
    }
    async void RunAction(bool undo, bool replaceConflicts)
    {
        if (devices.SelectedItem == null) { Write("请先选择目标设备。"); return; }
        SetBusy(true);
        var diag = new AndroidDiagLog(); lastDiag = diag;
        try {
            string serial = (string)devices.SelectedItem, executable = adb.Text;
            await Task.Run(() => new Deployment(Path.Combine(Home, "candidate.json"), executable, serial, "", Write, diag).Apply(undo, replaceConflicts));
            Write("完成。现在可以从官方入口正常启动游戏。");
        } catch (Exception error) {
            diag.SetFailure(error); string saved = diag.SaveAutoFailure();
            Write("未完成：" + error.Message + (saved.Length > 0 ? "\r\n自动日志：" + saved : ""));
        } finally { SetBusy(false); }
    }
    void ExportLog()
    {
        try {
            using (var dialog = new SaveFileDialog { Title = "导出安卓安装失败 / 诊断日志", Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt", FileName = "XingShu-Android-Failure-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, lastDiag.Render(), new UTF8Encoding(true));
                Write("已导出失败日志：" + dialog.FileName);
            }
        } catch (Exception error) { MessageBox.Show(this, "导出失败：" + error.Message, "星枢 · 安卓安装助手", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    internal sealed class AndroidDiagLog
    {
        readonly List<string> lines = new List<string>();
        public string Action = "", Serial = "", AdbPath = "", AppVersion = "", ExpectedAppVersion = "";
        public string ExpectedCatalogSha = "", ActualCatalogSha = "", TargetPath = "", ExpectedCandidateSha = "", ActualTargetSha = "", TargetProbeInfo = "";
        public readonly List<string> Conflicts = new List<string>();
        public string FailureReason = "", FailureSuggestion = "", ExceptionTrace = "";
        public void Add(string msg) { lines.Add("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg); }
        public void SetFailure(Exception error)
        {
            FailureReason = error.Message;
            ExceptionTrace = error.GetType().FullName + ": " + error.Message + (string.IsNullOrEmpty(error.StackTrace) ? "" : "\r\n" + error.StackTrace);
            FailureSuggestion = InferSuggestion(error.Message ?? "");
            Add("FAIL: " + error.Message);
        }
        static string InferSuggestion(string msg)
        {
            if (msg.Contains("CONFLICT_CONFIRM_REQUIRED") || msg.Contains("冲突插件"))
                return "检测到 APX 或同定位冲突插件，请确认删除原有冲突插件并替换安装星枢（助手会先完整备份旧文件）。";
            if (msg.Contains("游戏版本不匹配") || msg.Contains("dumpsys"))
                return "请确认手机已安装对应版本的官方《吉星派对》APK（而非其他渠道服或分身）。";
            if (msg.Contains("未授权") || msg.Contains("offline") || msg.Contains("unauthorized"))
                return "请在手机上允许 USB 调试授权或重新开启无线调试；小米等机型若提示权限拒绝可检查“USB调试（安全设置）”。";
            if (msg.Contains("仍在运行"))
                return "请完全关闭手机上的《吉星派对》后再试。";
            if (msg.Contains("已验证版本") || msg.Contains("资源清单") || msg.Contains("official") || msg.Contains("官方资源修复"))
                return "请先在官方《吉星派对》完成资源更新并进入大厅一次，退出后再试；若资源被损坏请使用游戏登录界面的资源修复。";
            return "请将此日志提交排查，日志仅包含文件指纹、设备检查与 ADB 返回信息，不含账号凭据。";
        }
        public string Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 星枢安卓安装诊断与失败日志 ===");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("执行操作: " + (Action.Length > 0 ? Action : "未执行"));
            sb.AppendLine("ADB 路径: " + AdbPath);
            sb.AppendLine("设备序列号: " + Serial);
            sb.AppendLine("期望游戏版本: " + ExpectedAppVersion + " | 实测版本: " + AppVersion);
            sb.AppendLine("官方清单期望 SHA256: " + ExpectedCatalogSha);
            sb.AppendLine("官方清单实测 SHA256: " + ActualCatalogSha);
            sb.AppendLine("目标资源路径: " + TargetPath);
            sb.AppendLine("目标期望(星枢) SHA256: " + ExpectedCandidateSha);
            sb.AppendLine("目标实测 SHA256: " + ActualTargetSha);
            if (TargetProbeInfo.Length > 0) sb.AppendLine("目标容器探测: " + TargetProbeInfo);
            sb.AppendLine("冲突插件扫描: " + (Conflicts.Count == 0 ? "无" : string.Join("; ", Conflicts)));
            sb.AppendLine();
            sb.AppendLine("--- 失败原因 ---");
            sb.AppendLine(FailureReason.Length > 0 ? FailureReason : "无失败记录");
            if (FailureSuggestion.Length > 0) { sb.AppendLine(); sb.AppendLine("--- 建议处理方式 ---"); sb.AppendLine(FailureSuggestion); }
            sb.AppendLine();
            sb.AppendLine("--- 过程日志 ---");
            foreach (var l in lines) sb.AppendLine(l);
            if (ExceptionTrace.Length > 0) { sb.AppendLine(); sb.AppendLine("--- 异常堆栈 ---"); sb.AppendLine(ExceptionTrace); }
            return sb.ToString();
        }
        public string SaveAutoFailure()
        {
            try {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "Logs");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "android-installer-last-failure.log");
                File.WriteAllText(file, Render(), new UTF8Encoding(true));
                return file;
            } catch { return ""; }
        }
    }

    internal sealed class ConflictItem
    {
        public string PluginName, Category, RelativePath, RemotePath, Sha256;
    }

    internal sealed class ConflictReport
    {
        public bool TargetIsConflict, TargetIsValidUnityFs;
        public string CurrentTargetSha256 = "";
        public readonly List<ConflictItem> Items = new List<ConflictItem>();
        public bool HasConflicts { get { return TargetIsConflict || Items.Count > 0; } }
        public string[] PluginNames { get { return Items.Select(i => i.PluginName).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray(); } }
    }

    internal sealed class Deployment
    {
        const string Package = "com.feimo.astralparty";
        const string FilesRoot = "/sdcard/Android/data/" + Package + "/files";
        readonly Dictionary<string,object> manifest;
        readonly string manifestDirectory, executable, serial, backupDirectory;
        readonly Action<string> report;
        readonly AndroidDiagLog diag;
        string Value(string key) { return manifest.ContainsKey(key) ? Convert.ToString(manifest[key]) : ""; }
        string Target { get { return FilesRoot + "/" + Value("bundleRelativePath"); } }
        string CatalogPath { get { return FilesRoot + "/com.unity.addressables/catalog_3.3.0.json"; } }
        string Backup { get { return Path.Combine(backupDirectory, "assembly.vanilla.__data"); } }
        string Receipt { get { return Path.Combine(backupDirectory, "deployment.json"); } }
        string Source { get { return Payload("candidate"); } }
        string Payload(string key) {
            string path = Path.GetFullPath(Path.Combine(manifestDirectory, Value(key)));
            if (!path.StartsWith(manifestDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("安装包文件路径越界");
            return path;
        }
        void Log(string message) { if (diag != null) diag.Add(message); report(message); }
        internal Deployment(string file, string adbPath, string device, string backups, Action<string> logger, AndroidDiagLog diagnostic = null)
        {
            manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(file));
            manifest = Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file, Encoding.UTF8));
            executable = adbPath; serial = device; report = logger; diag = diagnostic;
            backupDirectory = backups.Length > 0 ? Path.GetFullPath(backups) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingShu", "AndroidBackups", HashBytes(Encoding.UTF8.GetBytes(device)).Substring(0, 16));
            if (Value("package") != Package || Value("platform") != "ANDROID_CN"
                || !Regex.IsMatch(Value("bundleRelativePath"), @"\Acom\.unity\.addressables/AssetBundles/[a-f0-9]{32}/[a-f0-9]{32}/__data\z")
                || !Regex.IsMatch(Value("candidateSha256"), @"\A[a-f0-9]{64}\z") || !Regex.IsMatch(Value("originalSha256"), @"\A[a-f0-9]{64}\z"))
                throw new Exception("安装包清单不符合此预览的格式");
            if (diag != null) {
                diag.AdbPath = executable; diag.Serial = serial; diag.ExpectedAppVersion = Value("appVersion");
                diag.ExpectedCatalogSha = Value("catalogSha256"); diag.TargetPath = Target; diag.ExpectedCandidateSha = Value("candidateSha256");
            }
        }
        internal void VerifyPackage() { if (Hash(Source) != Value("candidateSha256")) throw new Exception("安装包损坏：文件校验不符"); Log("安装包完整性校验通过"); }
        internal void Check()
        {
            if (serial.Length == 0 || serial.StartsWith("-") || Regex.IsMatch(serial, @"[\s""']")) throw new Exception("请选择有效设备序列号");
            if (Adb("get-state").Trim() != "device") throw new Exception("设备未授权或未就绪");
            Match version = Regex.Match(Adb("shell", "dumpsys package " + Package), @"(?m)^\s*versionName=(\S+)\s*$");
            if (diag != null) diag.AppVersion = version.Success ? version.Groups[1].Value : "未检测到";
            if (!version.Success || version.Groups[1].Value != Value("appVersion")) throw new Exception("游戏版本不匹配，请使用对应预览包");
            Log("游戏版本与设备已确认：" + serial);
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
        bool HasPinnedCatalog() { return Regex.IsMatch(Value("catalogSha256"), @"\A[a-f0-9]{64}\z"); }
        bool VerifyCatalogMatches()
        {
            if (!HasPinnedCatalog()) return false;
            string actual = TryRemoteHash(CatalogPath) ?? "";
            if (diag != null) diag.ActualCatalogSha = actual;
            return string.Equals(actual, Value("catalogSha256"), StringComparison.OrdinalIgnoreCase);
        }
        bool ProbeRemoteUnityFs(string currentHash, out string probeDesc, out bool containsApxMarker)
        {
            containsApxMarker = false;
            probeDesc = "";
            string tempFile = Path.Combine(Path.GetTempPath(), "xingshu-probe-" + Guid.NewGuid().ToString("N") + ".__data");
            try {
                Adb("pull", Target, tempFile);
                if (!File.Exists(tempFile) || Hash(tempFile) != currentHash) {
                    probeDesc = "拉取校验不一致";
                    return false;
                }
                long len = new FileInfo(tempFile).Length;
                if (len < 128 || len > 64L * 1024 * 1024) {
                    probeDesc = "size=" + len + " (超出合理 UnityFS 范围)";
                    return false;
                }
                byte[] data = File.ReadAllBytes(tempFile);
                bool ok = data.Length >= 8 && data[0] == 'U' && data[1] == 'n' && data[2] == 'i' && data[3] == 't'
                    && data[4] == 'y' && data[5] == 'F' && data[6] == 'S' && data[7] == 0;
                if (ok) {
                    string ascii = Encoding.ASCII.GetString(data);
                    containsApxMarker = ascii.IndexOf("APXLive2D", StringComparison.OrdinalIgnoreCase) >= 0
                        || ascii.IndexOf("apx_modern_assist_ui", StringComparison.OrdinalIgnoreCase) >= 0
                        || ascii.IndexOf("APX.Runtime", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                probeDesc = (ok ? "UnityFS\\0" : "非UnityFS") + ", size=" + len + (containsApxMarker ? ", APX-marker" : "");
                return ok;
            } catch (Exception ex) {
                probeDesc = "探测异常: " + ex.Message;
                return false;
            } finally {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        internal ConflictReport ScanConflicts(string currentTargetHash, bool tracked)
        {
            var reportObj = new ConflictReport { CurrentTargetSha256 = currentTargetHash };
            bool catalogOk = HasPinnedCatalog() && VerifyCatalogMatches();
            // Companion file scanning runs only when the release manifest pins official catalogSha256
            // so legacy unit fixtures without real multi-file Android storage keep their exact contract.
            if (catalogOk) {
                string relTarget = Value("bundleRelativePath").Replace('\\', '/');
                string[] apxPaths = new[] {
                    "runtime_dependencies.json",
                    "apx_modern_assist_ui.bundle",
                    "dependence/apx_modern_assist_ui.bundle",
                    "Plugins/x86_64/APXLive2DBridge.dll",
                    "com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__data"
                };
                foreach (string rel in apxPaths) {
                    string remote = FilesRoot + "/" + rel;
                    string h = TryRemoteHash(remote);
                    if (h != null)
                        reportObj.Items.Add(new ConflictItem { PluginName = "APX 插件", Category = "companion-file", RelativePath = rel, RemotePath = remote, Sha256 = h });
                }
                string apxExtraInfo = "com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__info";
                if (reportObj.Items.Any(i => i.RelativePath.EndsWith("b7a6b1b538aa75bef22b9d95f0ff93bf/__data"))) {
                    string h = TryRemoteHash(FilesRoot + "/" + apxExtraInfo);
                    if (h != null) reportObj.Items.Add(new ConflictItem { PluginName = "APX 插件", Category = "companion-file", RelativePath = apxExtraInfo, RemotePath = FilesRoot + "/" + apxExtraInfo, Sha256 = h });
                }
                string legacyApx = "com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__data";
                if (!string.Equals(relTarget, legacyApx, StringComparison.OrdinalIgnoreCase)) {
                    string h = TryRemoteHash(FilesRoot + "/" + legacyApx);
                    if (h != null) {
                        reportObj.Items.Add(new ConflictItem { PluginName = "APX 旧版或同定位插件", Category = "companion-file", RelativePath = legacyApx, RemotePath = FilesRoot + "/" + legacyApx, Sha256 = h });
                        string hi = TryRemoteHash(FilesRoot + "/com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__info");
                        if (hi != null) reportObj.Items.Add(new ConflictItem { PluginName = "APX 旧版或同定位插件", Category = "companion-file", RelativePath = "com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__info", RemotePath = FilesRoot + "/com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__info", Sha256 = hi });
                    }
                }
                if (reportObj.Items.Any(i => i.PluginName.Contains("APX"))) {
                    string cubism = "Plugins/x86_64/Live2DCubismCore.dll";
                    string hc = TryRemoteHash(FilesRoot + "/" + cubism);
                    if (hc != null) reportObj.Items.Add(new ConflictItem { PluginName = "APX 插件", Category = "companion-file", RelativePath = cubism, RemotePath = FilesRoot + "/" + cubism, Sha256 = hc });
                }
            }
            if (!Known(currentTargetHash) && !tracked) {
                if (catalogOk) {
                    string probeDesc; bool apxMarker;
                    reportObj.TargetIsValidUnityFs = ProbeRemoteUnityFs(currentTargetHash, out probeDesc, out apxMarker);
                    if (diag != null) diag.TargetProbeInfo = probeDesc;
                    if (reportObj.TargetIsValidUnityFs) {
                        reportObj.TargetIsConflict = true;
                        string pluginLabel = (apxMarker || reportObj.Items.Any(i => i.PluginName.Contains("APX"))) ? "APX 插件" : "同定位覆盖插件 / MOD";
                        reportObj.Items.Insert(0, new ConflictItem { PluginName = pluginLabel, Category = "target-bundle", RelativePath = Value("bundleRelativePath"), RemotePath = Target, Sha256 = currentTargetHash });
                    }
                }
            }
            if (diag != null) {
                diag.Conflicts.Clear();
                foreach (var item in reportObj.Items)
                    diag.Conflicts.Add(item.PluginName + " [" + item.Category + "] " + item.RelativePath);
            }
            return reportObj;
        }
        internal static string FormatConflictPrompt(ConflictReport reportObj)
        {
            string names = reportObj.PluginNames.Length > 0 ? string.Join("、", reportObj.PluginNames) : "同定位冲突插件";
            string preview = string.Join("\r\n", reportObj.Items.Take(6).Select(i => "• [" + i.PluginName + "] " + i.RelativePath));
            if (reportObj.Items.Count > 6) preview += "\r\n• ... 共 " + reportObj.Items.Count + " 项冲突文件";
            return "检测到同定位冲突插件（" + names + "）：\r\n" + preview +
                "\r\n\r\n继续将先完整备份上述冲突文件到电脑备份目录，然后删除手机上的冲突文件并替换为星枢（不移动其他目录 MOD）。";
        }
        internal ConflictReport InspectAndScanConflicts()
        {
            if (diag != null) diag.Action = "inspect";
            VerifyPackage(); Check();
            string current = RemoteHash(Target);
            if (diag != null) diag.ActualTargetSha = current;
            var conflicts = ScanConflicts(current, false);
            if (!Known(current) && !conflicts.TargetIsConflict)
                throw new Exception("游戏缓存不在已验证版本中，请先使用官方资源修复。");
            EnsureGameClosed();
            return conflicts;
        }
        internal void Inspect()
        {
            if (diag != null) diag.Action = "check";
            VerifyPackage(); Check();
            string current = RemoteHash(Target);
            if (diag != null) diag.ActualTargetSha = current;
            var conflicts = ScanConflicts(current, false);
            string cacheLabel = current == Value("candidateSha256") ? "本版星枢"
                : current == Value("originalSha256") ? "官方原版"
                : Known(current) ? "可升级的旧版星枢"
                : conflicts.TargetIsConflict ? "同定位冲突插件可备份替换"
                : "未知版本，禁止覆盖";
            Log("当前游戏缓存：" + cacheLabel);
            Log("SHA256：" + current);
            if (!Known(current) && !conflicts.TargetIsConflict) throw new Exception("游戏缓存不在已验证版本中，请先使用官方资源修复。");
            EnsureGameClosed();
            if (conflicts.HasConflicts) {
                Log("CONFLICT_DETECTED=" + string.Join(",", conflicts.PluginNames));
                Log(FormatConflictPrompt(conflicts));
            } else {
                Log("游戏已关闭，资源可读取；安装时将再次核对并保留备份。");
            }
        }
        void EnsureGameClosed()
        {
            if (Regex.IsMatch(Adb("shell", "ps -A"), @"(?m)\s" + Regex.Escape(Package) + @"(?::[^\s]+)?\s*$"))
                throw new Exception("游戏仍在运行。请正常退出游戏后重试，不会强制中断对局。");
        }
        void ArchiveAndRemoveConflicts(ConflictReport conflicts)
        {
            string key = DateTime.UtcNow.ToString("yyMMddHHmmss") + "-" + conflicts.CurrentTargetSha256.Substring(0, 6);
            string conflictDir = Path.Combine(backupDirectory, "conflicts", key);
            Directory.CreateDirectory(conflictDir);
            var recorded = new List<Dictionary<string, string>>();
            int index = 0;
            foreach (var item in conflicts.Items) {
                index++;
                string backupName = item.Category == "target-bundle"
                    ? "target.__data"
                    : index.ToString("000") + "_" + Path.GetFileName(item.RelativePath);
                string dest = Path.GetFullPath(Path.Combine(conflictDir, backupName));
                if (!dest.StartsWith(Path.GetFullPath(conflictDir), StringComparison.OrdinalIgnoreCase)) throw new Exception("冲突备份路径越界");
                Adb("pull", item.RemotePath, dest);
                if (!string.IsNullOrEmpty(item.Sha256) && Hash(dest) != item.Sha256)
                    throw new Exception("冲突文件备份校验失败：" + item.RelativePath);
                recorded.Add(new Dictionary<string, string> {
                    {"plugin", item.PluginName}, {"category", item.Category}, {"relativePath", item.RelativePath}, {"backupName", backupName}, {"remotePath", item.RemotePath}, {"sha256", item.Sha256 ?? ""}
                });
            }
            File.WriteAllText(Path.Combine(conflictDir, "conflicts.json"), Json.Serialize(recorded), new UTF8Encoding(false));
            foreach (var item in conflicts.Items.Where(i => i.Category != "target-bundle")) {
                Adb("shell", "rm -f " + QuoteShell(item.RemotePath));
            }
            Log("已备份并删除 " + conflicts.Items.Count + " 项冲突插件文件：" + conflictDir);
        }
        internal void Apply(bool undo, bool replaceConflicts = false)
        {
            if (diag != null) diag.Action = undo ? "restore" : (replaceConflicts ? "install-replace-conflicts" : "install");
            if (!undo) VerifyPackage(); Check();
            string current = RemoteHash(Target), vanilla = Value("originalSha256"), expected = undo ? vanilla : Value("candidateSha256");
            if (diag != null) diag.ActualTargetSha = current;
            var previous = File.Exists(Receipt) ? Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Receipt, Encoding.UTF8)) : new Dictionary<string,object>();
            bool tracked = previous.ContainsKey("serial") && Convert.ToString(previous["serial"]) == serial
                && previous.ContainsKey("target") && Convert.ToString(previous["target"]) == Target
                && previous.ContainsKey("installedSha256") && Convert.ToString(previous["installedSha256"]) == current;
            var conflicts = undo ? new ConflictReport { CurrentTargetSha256 = current } : ScanConflicts(current, tracked);
            if (!Known(current) && !tracked && !conflicts.TargetIsConflict)
                throw new Exception("缓存不是原版或已验证的星枢版本，已停止。请先使用官方资源修复。");
            EnsureGameClosed();
            if (!undo && conflicts.HasConflicts && !replaceConflicts)
                throw new Exception("CONFLICT_CONFIRM_REQUIRED: " + FormatConflictPrompt(conflicts));
            if (current == expected && !conflicts.HasConflicts) { Log((undo ? "已经是官方原版" : "已经安装本版星枢") + "，SHA256 校验通过，无需重复写入。"); return; }
            if (File.Exists(Backup)) { if (Hash(Backup) != vanilla) throw new Exception("原版备份校验失败"); }
            else {
                Directory.CreateDirectory(backupDirectory);
                if (current == vanilla) Adb("pull", Target, Backup);
                else if (manifest.ContainsKey("vanilla") && Hash(Payload("vanilla")) == vanilla) File.Copy(Payload("vanilla"), Backup, false);
                else throw new Exception("缺少有效原版备份，请沿用原安装工具还原后重试。");
                if (Hash(Backup) != vanilla) throw new Exception("备份校验失败，未修改游戏");
            }
            if (!undo && conflicts.HasConflicts) ArchiveAndRemoveConflicts(conflicts);
            string snapshot = Path.Combine(backupDirectory, "before-" + current + ".__data");
            if (!File.Exists(snapshot)) Adb("pull", Target, snapshot);
            if (Hash(snapshot) != current) throw new Exception("安装前快照校验失败，未修改游戏");
            if (current != expected) {
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
                try { Adb("shell", "rm -f " + QuoteShell(stage)); } catch { }
            }
            var record = new Dictionary<string,object> { {"serial",serial}, {"package",Package}, {"target",Target}, {"originalSha256",vanilla}, {"installedSha256",expected}, {"backup",Backup}, {"action",undo ? "restore" : "install"}, {"replacedConflict", conflicts.HasConflicts}, {"timestamp",DateTime.UtcNow.ToString("o")} };
            string temporary = Receipt + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(record), new UTF8Encoding(false));
            if (File.Exists(Receipt)) File.Replace(temporary, Receipt, null); else File.Move(temporary, Receipt);
            if (conflicts.HasConflicts)
                Log("已备份并删除原冲突插件（" + conflicts.Items.Count + " 项），安装完成且 SHA256 校验通过。备份：" + backupDirectory);
            else
                Log((undo ? "还原" : "安装") + "完成，SHA256 校验通过。备份：" + backupDirectory);
        }
        string Adb(params string[] args) { var all = new List<string> { "-s", serial }; all.AddRange(args); return Execute(executable, all.ToArray()); }
        string TryRemoteHash(string path)
        {
            try { return RemoteHash(path); } catch { return null; }
        }
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
