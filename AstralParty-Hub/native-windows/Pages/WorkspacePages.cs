using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XingShu.Native.Controls;
using XingShu.Native.Services;

namespace XingShu.Native;

public partial class MainWindow
{
    private async Task BuildPage(string key, StackPanel body, CancellationToken token)
    {
        switch (key)
        {
            case "cards": case "characters": case "chips": case "maps": BuildLibrary(key, body); break;
            case "overview": BuildOverview(body); break;
            case "accounts": BuildAccounts(body); break;
            case "profile": BuildProfile(body); break;
            case "matches": BuildMatches(body); break;
            case "settings": BuildSettings(body); break;
            case "runtime": BuildRuntime(body); break;
            case "bridge": await BuildBridge(body, token); break;
            case "spectator": await BuildSpectator(body, token); break;
            case "plugins": case "mods": await BuildExtensions(body, key, token); break;
            case "import": BuildImport(body); break;
            case "transactions": await BuildTransactions(body, token); break;
            case "backups": await BuildBackups(body, token); break;
            case "diagnostics": await BuildDiagnostics(body, token); break;
        }
    }
    private void BuildOverview(Panel body)
    {
        var banner = new Grid(); banner.ColumnDefinitions.Add(new ColumnDefinition()); banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        banner.Children.Add(UI.Stack(UI.Badge("星枢 · WINDOWS 原生版", "Gold"), UI.Text("你的游戏工作台", 30, "Ink", true), UI.Text("从游戏数据、图鉴到模组管理，在同一个桌面窗口完成。", 14, "Muted")));
        var logo = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Brand/logo.png")), Width = 88, Height = 88 }; Grid.SetColumn(logo, 1); banner.Children.Add(logo); body.Children.Add(UI.Card(banner));
        var metrics = new WrapPanel();
        foreach (var (label, number) in new[] { ("已保存账号", Accounts.Length), ("游戏连接", Sessions.Length), ("已登记游戏", Targets.Length), ("卡牌资料", Catalog.Rows("cards").Length) })
            metrics.Children.Add(UI.Card(UI.Stack(UI.Text(number.ToString(), 30, "Gold", true), UI.Text(label, 12, "Muted"))));
        body.Children.Add(metrics);
        body.Children.Add(UI.Row(UI.Button("浏览卡牌", () => _ = Navigate("cards"), true), UI.Button("地图攻略", () => _ = Navigate("maps")), UI.Button("查看对局", () => _ = Navigate("matches")), UI.Button("管理插件", () => _ = Navigate("plugins"))));
        UI.Section(body, "游戏安装位置", TargetCards());
        if (Core.Status.Text("bridgeError").Length > 0) body.Children.Add(UI.Card(UI.Stack(UI.Text("游戏通信暂不可用", 16, "Gold", true), UI.Text(Core.Status.Text("bridgeError"), 12, "Muted"))));
        body.Children.Add(UI.Card(UI.Stack(UI.Text("原生界面，离线资料随软件携带", 17, "Ink", true), UI.Text("图鉴和已保存的战绩可离线查看；首次同步数据需要对应游戏账号在线。账号、战绩和安装记录继续沿用原 Hub 数据库。", 13, "Muted"))));
    }
    private WrapPanel TargetCards()
    {
        var cards = new WrapPanel();
        foreach (var target in Targets)
        {
            var name = Platform(target.Text("platform"));
            var stack = UI.Stack(UI.Text(name, 18, "Ink", true), Limit(UI.Text(target.Text("install_path"), 12, "Muted"), 350, 65));
            stack.Children.Add(Action("启动游戏", async () => { var r = await Core.CallAsync("launch_game_uri", new { platformId = target.Text("platform"), executablePath = target.Text("executable_path") }); Notify(r.Text("message")); }));
            cards.Children.Add(UI.Card(stack));
        }
        if (Targets.Length == 0) cards.Children.Add(UI.Empty("尚未登记游戏", "在游戏账号页选择对应渠道的实际游戏程序。")); return cards;
    }
    private void BuildAccounts(Panel body)
    {
        UI.Section(body, "游戏安装位置", TargetCards());
        var platform = UI.Select(["Steam 国服", "TapTap 电脑版", "Steam 国际服", "哔哩哔哩", "WeGame"], "游戏渠道");
        var ids = new[] { "STEAM_CN", "TAPTAP", "STEAM_GLOBAL", "BILIBILI", "WEGAME" };
        var path = UI.Input("游戏程序完整路径", width: 470);
        var browse = UI.Button("选择游戏程序…", () => { var d = new OpenFileDialog { Filter = "游戏程序 (*.exe)|*.exe", Title = "选择实际游戏程序" }; if (d.ShowDialog(this) == true) path.Text = d.FileName; });
        var add = Action("登记此安装位置", async () => { await Core.CallAsync("register_extension_target", new { platformId = ids[platform.SelectedIndex], path = path.Text }); Notify("安装位置已登记。"); await Navigate("accounts"); }, true);
        body.Children.Add(UI.Card(UI.Stack(UI.Text("添加游戏", 18, "Ink", true), UI.Row(platform, browse), path, add)));
        UI.Section(body, "账号身份绑定", BindingEditor());
        body.Children.Add(UI.Text("绑定只记录玩家 ID 和渠道。账号登录由游戏官方客户端完成，Hub 不保存游戏密码。", 12, "Muted"));
    }
    private UIElement BindingEditor()
    {
        var panel = new StackPanel();
        foreach (var role in new[] { "MAIN", "USER_SPECTATOR" })
        {
            var saved = Preferences.Bindings.FirstOrDefault(b => b.Role == role);
            var channelIds = new[] { "STEAM_CN", "TAPTAP", "STEAM_GLOBAL", "BILIBILI", "WEGAME" };
            var channel = UI.Select(channelIds.Select(Platform), role == "MAIN" ? "主账号渠道" : "观战账号渠道", 170);
            channel.SelectedIndex = Math.Max(0, Array.IndexOf(channelIds, saved?.PlatformId ?? "STEAM_CN"));
            var id = UI.Input("玩家 ID（纯数字）", saved?.PlayerId.ToString() ?? "", 200);
            panel.Children.Add(UI.Text(role == "MAIN" ? "主账号" : "独立观战账号", 15, "Gold", true));
            panel.Children.Add(UI.Row(channel, id, Action("保存绑定", async () => {
                if (!long.TryParse(id.Text.Trim(), out var pid) || pid <= 0) throw new InvalidOperationException("请输入有效的玩家 ID。");
                var updated = Preferences.Bindings.Where(b => b.Role != role).Append(new AccountBinding(channelIds[channel.SelectedIndex], pid, role)).ToList();
                await Core.CallAsync("configure_runtime_bindings", new { bindings = updated.Select(b => new { platformId = b.PlatformId, playerId = b.PlayerId, role = b.Role }) });
                Preferences.Bindings = updated; Preferences.Save(); Notify("账号绑定已保存。");
            }), Action("解除绑定", async () => {
                var updated = Preferences.Bindings.Where(b => b.Role != role).ToList();
                await Core.CallAsync("configure_runtime_bindings", new { bindings = updated.Select(b => new { platformId = b.PlatformId, playerId = b.PlayerId, role = b.Role }) });
                Preferences.Bindings = updated; Preferences.Save(); Notify("绑定已解除。");
            })));
        }
        return UI.Card(panel);
    }
    private void BuildProfile(Panel body)
    {
        var snapshot = Account.Get("snapshot");
        if (snapshot.ValueKind != JsonValueKind.Object) { body.Children.Add(UI.Empty("尚未同步此账号", "在右上角选择账号，或先绑定主账号并进入游戏大厅。")); AddSync(body); return; }
        body.Children.Add(UI.Card(UI.Stack(UI.Text(snapshot.Text("name"), 26, "Ink", true), UI.Row(UI.Badge(Platform(Account.Text("platform"))), UI.Badge("玩家 " + snapshot.Text("playerId")), UI.Badge("等级 " + snapshot.Text("level"), "Gold")), UI.Text("最近保存 " + Account.Text("observedAt"), 12, "Muted"))));
        AddSync(body);
        body.Children.Add(UI.Row(UI.Card(UI.Stack(UI.Text(snapshot.Get("heroes").Rows().Length.ToString(), 30, "Gold", true), UI.Text("角色养成记录"))), UI.Card(UI.Stack(UI.Text(snapshot.Get("skins").Rows().Length.ToString(), 30, "Pink", true), UI.Text("皮肤记录"))), UI.Card(UI.Stack(UI.Text(snapshot.Get("matches").Rows().Length.ToString(), 30, "Green", true), UI.Text("已保存对局")))));
        body.Children.Add(UI.Table(snapshot.Get("heroes").Rows().Select(h => new Dictionary<string, string> { ["hero"] = Catalog.Name("characters", h.Number("id")), ["level"] = h.Metric("level"), ["exp"] = h.Metric("exp"), ["pve"] = h.Metric("pveLevel"), ["break"] = h.Flag("breakthrough") ? "已突破" : "未突破" }), ("hero", "角色"), ("level", "羁绊等级"), ("exp", "经验"), ("pve", "合作成长"), ("break", "突破")));
    }
    private void AddSync(Panel body)
    {
        var sync = Action("同步游戏数据", async () => {
            var selected = Session;
            if (selected.ValueKind != JsonValueKind.Object || selected.Number("livePlayerId") <= 0) throw new InvalidOperationException("所选账号尚未连接，请先进入对应账号的游戏大厅。");
            await Core.CallAsync("sync_game_library", new { connectionId = selected.Text("connectionId") });
            Notify("已向此账号发送同步请求；游戏返回数据后点击刷新。");
        }); sync.IsEnabled = Session.ValueKind == JsonValueKind.Object; body.Children.Add(sync);
    }
    private void BuildRuntime(Panel body)
    {
        body.Children.Add(UI.Row(UI.Badge($"在线连接 {Sessions.Length}"), UI.Badge("按渠道 + 玩家 ID 验证身份", "Green")));
        foreach (var session in Sessions)
        {
            var s = UI.Stack(UI.Text(Platform(session.Text("platformId")) + " · 玩家 " + session.Text("livePlayerId", "待登录"), 18, "Ink", true),
                UI.Text("角色 " + session.Text("boundRole") + "  ·  " + session.Text("connectionState"), 13, "Gold"),
                UI.Text("插件 " + session.Text("runtimeVersion") + "  ·  PID " + session.Text("pid"), 12, "Muted"),
                UI.Text(session.Text("executablePath"), 12, "Muted"));
            var context = session.Get("matchContext"); if (context.ValueKind == JsonValueKind.Object) s.Children.Add(UI.Text("对局 " + context.Text("matchState") + " · 观战码 " + context.Text("spectatorCode", "未提供"), 13));
            body.Children.Add(UI.Card(s));
        }
        if (Sessions.Length == 0) body.Children.Add(UI.Empty("等待游戏插件连接", "保持插件启用并进入大厅。如果旧 Hub 仍在运行，游戏通信仍归旧 Hub 使用；原生版会显示提示。"));
        UI.Section(body, "绑定设置", BindingEditor());
    }
    private async Task BuildBridge(Panel body, CancellationToken token)
    {
        var status = await Core.CallAsync("get_bridge_status", cancellation: token);
        body.Children.Add(UI.Card(UI.Stack(UI.Text(status.Flag("isRunning") ? "本地游戏通信已启动" : "本地游戏通信未启动", 20, status.Flag("isRunning") ? "Green" : "Gold", true),
            UI.Text("协议 " + status.Text("protocolVersion") + " · " + status.Text("activeTransport"), 13, "Muted"), UI.Text("已接收 " + status.Metric("totalMessagesReceived") + " 条 · 已发送 " + status.Metric("totalMessagesSent") + " 条", 13))));
        var logs = status.Get("recentMessages").Rows();
        body.Children.Add(UI.Table(logs.Reverse().Select(e => new Dictionary<string, string> { ["time"] = Date(e.Number("timestamp")), ["type"] = e.Text("direction") + " · " + e.Text("messageType"), ["message"] = e.Text("summary") }), ("time", "时间"), ("type", "类型"), ("message", "内容")));
        body.Children.Add(Action("清空本地通信日志", async () => { await Core.CallAsync("clear_bridge_logs"); await Navigate("bridge"); }));
    }
    private async Task BuildExtensions(Panel body, string category, CancellationToken token)
    {
        if (Targets.Length == 0) { body.Children.Add(UI.Empty("尚未登记游戏", "先在游戏账号页选择对应的实际游戏安装位置。")); return; }
        var target = new ComboBox { ItemsSource = Targets.Select(t => new Choice(t.Text("id"), Platform(t.Text("platform")) + " · " + t.Text("install_path"))).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Key", SelectedIndex = 0, Width = 400, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(target); var content = new StackPanel(); body.Children.Add(content);
        async Task Load()
        {
            var id = (target.SelectedItem as Choice)?.Key; if (id is null) return;
            var inventory = await Core.CallAsync("get_extension_inventory", new { installationId = id }, token); token.ThrowIfCancellationRequested(); content.Children.Clear();
            var entries = inventory.Get("entries").Rows().Where(e => category == "plugins" ? e.Text("category") == "xingshu-plugin" : e.Text("category") != "xingshu-plugin").ToArray();
            foreach (var entry in entries)
            {
                var panel = UI.Stack(UI.Row(UI.Badge(entry.Text("status"), "Green"), UI.Badge(entry.Text("version", "版本未知"))), UI.Text(entry.Text("name"), 18, "Ink", true), UI.Text(entry.Text("description"), 13, "Muted"), UI.Text(entry.Text("path"), 12, "Muted"));
                if (entry.Flag("managed")) panel.Children.Add(Action("卸载此扩展", async () => {
                    if (MessageBox.Show(this, "将按记录移除此安装位置的扩展文件：\n" + entry.Text("name") + "\n" + entry.Text("path"), "卸载扩展", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    await Core.CallAsync("uninstall_extension", new { extensionId = entry.Text("id"), isPlugin = category == "plugins", installationId = id }); await Load();
                }));
                content.Children.Add(UI.Card(panel));
            }
            if (entries.Length == 0) content.Children.Add(UI.Empty("未发现已安装扩展", "这里只显示该游戏安装位置中实际检测到的文件及管理记录。"));
            foreach (var warning in inventory.Get("warnings").Rows()) content.Children.Add(UI.Text(warning.Text(), 12, "Gold"));
            var installer = Action("打开电脑端安装助手", async () => { await Core.CallAsync("open_group_installer", new { installationId = id }); });
            installer.IsEnabled = inventory.Flag("installerAvailable"); installer.ToolTip = installer.IsEnabled ? "使用发布包内的安装助手" : "当前目录未携带安装助手，可使用导入安装包";
            content.Children.Add(UI.Row(UI.Button("导入安装包", () => _ = Navigate("import"), true), installer));
        }
        target.SelectionChanged += (_, _) => Run(null, Load); await Load();
    }
    private void BuildImport(Panel body)
    {
        var path = UI.Input("选择 ZIP 安装包或 XSPKG 信息包", width: 430); path.IsReadOnly = true;
        JsonElement inspected = default; string inspectedPath = "";
        var preview = new StackPanel();
        var target = new ComboBox { ItemsSource = Targets.Select(t => new Choice(t.Text("id"), Platform(t.Text("platform")) + " · " + t.Text("install_path"))).ToArray(), DisplayMemberPath = "Label", SelectedIndex = 0, Width = 450, HorizontalAlignment = HorizontalAlignment.Left };
        var install = Action("安装到所选游戏", async () => {
            if (inspected.ValueKind != JsonValueKind.Object || path.Text != inspectedPath || target.SelectedItem is not Choice selection) throw new InvalidOperationException("请先检查安装包并选择游戏安装位置。");
            if (MessageBox.Show(this, $"安装包：{Path.GetFileName(path.Text)}\n目标：{selection.Label}\n\n安装前将检查游戏进程、原文件哈希并创建备份。", "确认安装", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            await Core.CallAsync("install_package", new { archivePath = path.Text, installationId = selection.Key }); Notify("安装完成，原文件已备份。"); await Navigate("transactions");
        }, true); install.IsEnabled = false;
        var browse = UI.Button("选择文件…", () => { var d = new OpenFileDialog { Filter = "星枢安装包 (*.zip;*.xspkg)|*.zip;*.xspkg" }; if (d.ShowDialog(this) == true) { path.Text = d.FileName; install.IsEnabled = false; inspected = default; preview.Children.Clear(); } });
        var check = Action("检查安装包", async () => {
            if (!File.Exists(path.Text)) throw new FileNotFoundException("请选择现有安装包。");
            var xspkg = Path.GetExtension(path.Text).Equals(".xspkg", StringComparison.OrdinalIgnoreCase);
            var result = xspkg ? await Core.CallAsync("inspect_xspkg", new { filePath = path.Text }) : await Core.CallAsync("inspect_package_archive", new { archivePath = path.Text });
            inspected = result; inspectedPath = path.Text; var meta = xspkg ? result : result.Get("package");
            preview.Children.Clear(); preview.Children.Add(UI.Card(UI.Stack(UI.Text(meta.Text("name", "安装包检查完成"), 19, "Green", true),
                UI.Text("标识 " + meta.Text("id", meta.Text("packageId")) + " · 版本 " + meta.Text("version"), 13),
                UI.Text(xspkg ? "XSPKG 可查看包信息；当前安装引擎只安装含 manifest.json 的 ZIP 包。" : "将执行文件校验、备份和事务回滚。", 12, "Muted")))); install.IsEnabled = !xspkg && Targets.Length > 0;
        });
        body.Children.Add(UI.Card(UI.Stack(UI.Row(path, browse, check), target, preview, install)));
        body.Children.Add(UI.Text("安装和还原沿用现有 Rust 引擎的进程检查、哈希门槛、备份与回滚。游戏运行时不会强制关闭游戏。", 12, "Muted"));
    }
    private async Task BuildTransactions(Panel body, CancellationToken token)
    {
        var data = await Core.CallAsync("list_install_transactions", new { limit = 100 }, token);
        foreach (var tx in data.Rows())
        {
            var panel = UI.Stack(UI.Row(UI.Badge(tx.Text("state"), "Gold")), UI.Text(tx.Text("package_id") + " · " + tx.Text("package_version"), 17, "Ink", true), UI.Text(tx.Text("started_at"), 12, "Muted"));
            if (tx.Text("error_message").Length > 0) panel.Children.Add(UI.Text(tx.Text("error_message"), 13, "Pink"));
            if (tx.Text("backup_id").Length > 0 && tx.Text("state") != "ROLLED_BACK") panel.Children.Add(Action("从此事务还原", async () => {
                if (MessageBox.Show(this, "还原事务 " + tx.Text("id") + " 的备份文件？\n请先退出对应的游戏。", "还原文件", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                await Core.CallAsync("rollback_transaction", new { transactionId = tx.Text("id") }); Notify("已完成还原。"); await Navigate("transactions");
            })); body.Children.Add(UI.Card(panel));
        }
        if (data.Rows().Length == 0) body.Children.Add(UI.Empty("暂无安装事务", "通过 Hub 执行安装后，会在这里保留记录。"));
    }
    private async Task BuildBackups(Panel body, CancellationToken token)
    {
        var data = await Core.CallAsync("list_backups", cancellation: token);
        foreach (var backup in data.Rows()) body.Children.Add(UI.Card(UI.Stack(UI.Text(backup.Text("backup_type") + " · " + backup.Text("status"), 17, "Green", true), UI.Text(backup.Text("storage_path"), 12, "Muted"), UI.Text("创建 " + backup.Text("created_at") + " · 校验 " + backup.Text("verified_at", "未记录"), 12), UI.Button("查看对应安装事务", () => _ = Navigate("transactions")))));
        if (data.Rows().Length == 0) body.Children.Add(UI.Empty("暂无管理备份", "执行安装时自动创建备份；还原操作位于对应的安装事务。"));
    }
    private async Task BuildDiagnostics(Panel body, CancellationToken token)
    {
        var report = await Core.CallAsync("get_diagnostic_report", cancellation: token);
        foreach (var item in report.Get("items").Rows())
        {
            var (title, description, recommendation) = DiagnosticCopy(item, report.Get("snapshot"));
            var severity = item.Text("severity");
            body.Children.Add(UI.Card(UI.Stack(UI.Row(UI.Badge(severity switch { "OK" => "正常", "INFO" => "信息", "WARNING" => "需留意", "ERROR" => "待处理", _ => severity }, severity == "ERROR" ? "Pink" : severity == "WARNING" ? "Gold" : "Green")), UI.Text(title, 17, "Ink", true), UI.Text(description, 13, "Muted"), UI.Text(recommendation, 12, "Gold"))));
        }
        var providers = await Core.CallAsync("list_providers", cancellation: token);
        UI.Section(body, "数据来源", UI.Table(providers.Rows().Select(p => new Dictionary<string, string> { ["name"] = p.Text("name"), ["type"] = p.Text("provider_type"), ["version"] = p.Text("version") }), ("name", "名称"), ("type", "类型"), ("version", "版本")));
        body.Children.Add(Action("导出本机诊断报告…", async () => {
            var fresh = await Core.CallAsync("get_diagnostic_report"); var dialog = new SaveFileDialog { Filter = "JSON 报告 (*.json)|*.json", FileName = "星枢诊断报告.json" };
            if (dialog.ShowDialog(this) == true) { await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(fresh, new JsonSerializerOptions { WriteIndented = true })); Notify("报告已保存，包含本机路径和运行状态。请自行选择分享范围。"); }
        }));
    }
    private static (string, string, string) DiagnosticCopy(JsonElement item, JsonElement snapshot)
    {
        var path = snapshot.Get("primary_game").Text("install_path");
        return item.Text("rule_id") switch {
            "GAME_DETECTED" => ("已识别游戏安装", path, "安装前仍会按所选渠道复核文件版本。"),
            "GAME_INVALID" => ("游戏路径尚未验证", path, "请在游戏账号页重新选择实际游戏程序。"),
            "GAME_NOT_FOUND" => ("尚未检测到游戏", "自动检测未找到可验证的安装位置。", "可在游戏账号页手动登记 Steam 或 TapTap 安装位置。"),
            "GAME_RUNNING" => ("游戏正在运行", string.Join("、", snapshot.Get("running_processes").Rows().Select(r => r.Text())), "安装或还原前正常退出对应游戏即可；浏览资料不受影响。"),
            "NO_ACTIVE_LOCKS" => ("未检测到运行中的游戏进程", "当前进程扫描未发现游戏。", "安装引擎仍会在写入前再次复核目标进程与文件。"),
            "XINGSHU_DETECTED" => ("已检测到加载器目录", snapshot.Text("xingshu_path"), "此检查只代表文件存在；插件连接状态见运行时页面。"),
            "XINGSHU_NOT_FOUND" => ("未检测到独立加载器目录", "当前检查未找到独立加载器文件。", "内置运行时使用资源指纹核验，可在运行时插件页查看，不能仅凭此项判断是否安装成功。"),
            "INCOMPLETE_TRANSACTION" => ("存在未完成安装事务", "有安装记录未进入最终状态。", "在安装事务页核对错误和备份，再选择还原。"),
            _ => (item.Text("title"), item.Text("description"), item.Text("recommendation")) };
    }
    private void BuildSettings(Panel body)
    {
        var value = UI.Text($"{Preferences.Scale:P0}", 18, "Gold", true);
        var scale = new Slider { Minimum = .8, Maximum = 1.5, TickFrequency = .05, IsSnapToTickEnabled = true, Value = Preferences.Scale, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 12, 15) };
        scale.ValueChanged += (_, _) => { ApplyScale(scale.Value); value.Text = $"{scale.Value:P0}"; };
        scale.LostMouseCapture += (_, _) => Preferences.Save();
        body.Children.Add(UI.Card(UI.Stack(UI.Text("界面大小", 18, "Ink", true), UI.Text("系统 DPI 自动适配；额外缩放保存在本机。", 12, "Muted"), value, scale, UI.Button("恢复 100%", () => { scale.Value = 1; Preferences.Save(); }))));
        UI.Section(body, "账号绑定", BindingEditor());
        body.Children.Add(UI.Card(UI.Stack(UI.Text("原生客户端", 18, "Ink", true), UI.Text("界面：WPF / .NET 10\n核心：Rust\n本地通信：进程管道\n版本：0.2.0-native1", 14), UI.Text("设置目录：" + Services.Preferences.Folder, 12, "Muted"), UI.Text("游戏数据库：" + Core.Status.Text("dataDirectory"), 12, "Muted"))));
        body.Children.Add(UI.Text("快捷键：Ctrl+K 搜索图鉴；Ctrl+R 刷新当前页；Esc 关闭详情。", 13, "Muted"));
    }
}
