using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XingShu.Native.Services;

namespace XingShu.Native;

internal static class NativeSelfTest
{
    public static async Task Run(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var catalog = new CatalogStore();
        Check(catalog.Rows("cards").Length >= 70, "真实卡牌目录已载入");
        Check(catalog.Rows("characters").Length >= 30, "真实角色目录已载入");
        Check(CatalogStore.Plain("[color=#fff]说明[/color] <b>abc</b> {range=10}") == "说明 abc 10", "游戏文本标记已转换");
        using var rewards = JsonDocument.Parse("{\"1\":1000,\"999999999\":2}");
        Check(catalog.Items(rewards.RootElement) == "星币 × 1000、道具 #999999999 × 2", "养成材料按真实道具表解析，未知道具保留编号");
        Check(catalog.ResourcePath("") is null && catalog.ResourcePath("not-a-resource") is null, "缺失资源不会误读整个资源表");
        var guides = catalog.Guides.Get("maps").Rows();
        Check(guides.Length >= 15, "多地图攻略基础数据已载入");
        var forest = guides.First(m => m.Number("id") == 82017).Get("community");
        Check(forest.Text("status") == "community-reference" && forest.Get("timeline").Rows().Length == 4, "用户攻略标注来源并保存四个进度节点");
        var preferences = new Preferences { StorageFolder = Path.Combine(directory, "settings") };
        var window = new MainWindow(catalog, preferences) { HeadlessTest = true };
        // No Show(), Activate(), input automation or desktop screenshots are used.
        await window.Core.StartAsync(true, Path.Combine(directory, "database"));
        try
        {
            await window.PushBindings();
            Check(!window.Core.Status.Flag("webview"), "核心明确不使用 WebView");
            Check(window.Core.Status.Text("transport") == "stdio", "界面通信仅使用进程管道");
            foreach (var page in MainWindow.Pages)
            {
                await window.Navigate(page.Key); window.Measure(new Size(1220, 810)); window.Arrange(new Rect(0, 0, 1220, 810)); window.UpdateLayout();
                Check(window.CurrentPage == page.Key, "原生页面构建 " + page.Key);
                Check(window.LastPageError.Length == 0, "页面无数据调用或布局异常 " + page.Key + " " + window.LastPageError);
                if (page.Key is "overview" or "cards" or "maps" or "settings") Capture(window, Path.Combine(directory, page.Key + ".png"));
            }
            // Render native card and community guide without opening any window.
            var card = window.FullCard(catalog.Rows("cards")[0], width: 240); CaptureElement(card, 260, 450, Path.Combine(directory, "card-detail.png"));
            Check(catalog.Image(catalog.Rows("cards")[0].Text("image")) is not null, "原生 PNG 资源可解码");
            foreach (var section in new[] { "cards", "characters", "chips" })
            {
                foreach (var item in catalog.Rows(section).Where(i => i.Text("name").Length > 0))
                {
                    window.ShowCatalogDetail(section, item, [item]);
                    var element = (FrameworkElement)window.LastDetailContent!; element.Measure(new Size(850, double.PositiveInfinity)); element.Arrange(new Rect(0, 0, 850, element.DesiredSize.Height)); element.UpdateLayout();
                }
                Check(true, "所有图鉴详情完成原生布局 " + section);
            }
            foreach (var version in catalog.Rows("altArts"))
            {
                var item = catalog.Find("cards", version.Number("cardId")); window.ShowCatalogDetail("cards", item, [item], version);
                ((FrameworkElement)window.LastDetailContent!).Measure(new Size(850, double.PositiveInfinity));
            }
            Check(true, "异画详情兼容静态与视频资源状态");
            var character = catalog.Rows("characters").First(c => c.Get("pvePotentials").Rows().Length > 0 && c.Get("bondRewards").Rows().Length > 0);
            window.ShowCatalogDetail("characters", character, [character]);
            CaptureElement((FrameworkElement)window.LastDetailContent!, 850, 1400, Path.Combine(directory, "character-detail.png"));
            foreach (var map in guides)
            {
                var item = catalog.Find("maps", map.Number("id")); window.ShowMap(item);
                ((FrameworkElement)window.LastDetailContent!).Measure(new Size(850, double.PositiveInfinity));
            }
            Check(true, "所有地图详情均能构建");
            var forestView = (FrameworkElement)window.BuildMapGuide(catalog.Find("maps", 82017));
            CaptureElement(forestView, 850, 1750, Path.Combine(directory, "forest-guide.png"));
            var forestTasks = guides.First(m => m.Number("id") == 82017).Get("difficulties").Rows()[3].Get("missions").Rows();
            Check(forestTasks.Length == 4 && forestTasks[0].Text("name").Contains("闹钟猫头鹰") && forestTasks[0].Text("reward").Contains("植物建筑卡"), "森秋配置任务与参考图差异已保留，优先显示真实配置");
            Check(guides.SelectMany(m => m.Get("difficulties").Rows()).All(d => d.Get("missionIds").Rows().Select(i => i.Number()).SequenceEqual(d.Get("missions").Rows().Select(m => m.Number("id"))) && d.Get("missions").Rows().All(m => m.Text("reward").Length > 0)), "每张已展示地图的任务 ID 均有对应配置任务与奖励");
            foreach (var scale in new[] { .8, 1.0, 1.5 }) { window.ApplyScale(scale); window.Measure(new Size(1220, 810)); window.Arrange(new Rect(0, 0, 1220, 810)); window.UpdateLayout(); Check(Math.Abs(window.Preferences.Scale - scale) < .001, "界面缩放 " + scale); }
            window.ApplyScale(double.NaN); Check(window.Preferences.Scale == 1, "无效缩放安全回退到默认值");
            window.ApplyScale(1.5); await window.Navigate("accounts");
            CaptureElement((FrameworkElement)window.Content, 900, 620, Path.Combine(directory, "accounts-compact.png"));
            var inputs = VisualChildren<System.Windows.Controls.TextBox>((DependencyObject)window.Content).ToArray();
            Check(inputs.Length > 0 && inputs.All(i => i.ActualWidth <= 300), "窄窗口放大时输入框随可用宽度收缩");
            using var complete = JsonDocument.Parse("[{\"kills\":1,\"deaths\":0,\"damage\":47,\"healing\":28,\"coins\":94,\"transfer\":20},{\"kills\":3,\"deaths\":0,\"damage\":117,\"healing\":8,\"coins\":66,\"transfer\":10},{\"kills\":2,\"deaths\":0,\"damage\":75,\"healing\":0,\"coins\":68,\"transfer\":5},{\"kills\":10,\"deaths\":0,\"damage\":214,\"healing\":15,\"coins\":114,\"transfer\":5}]");
            var ratings = MatchRules.Ratings(complete.RootElement.Rows());
            Check(ratings.SequenceEqual(new double?[] { 10, 4.85, 3.13, 10 }), "原生评分与现有网页算法及真实示例一致");
            using var missing = JsonDocument.Parse("[{\"kills\":1}]"); Check(MatchRules.Ratings(missing.RootElement.Rows())[0] is null, "缺失统计不伪造评分");
            Check(new Choice("", "选择账号").ToString() == "选择账号", "下拉框显示标签而非对象记录");
            TestFollow(Check);
            Check(!window.IsVisible, "全程未显示窗口或抢占用户输入");
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { await window.Core.DisposeAsync(); }
    }
    private static void TestFollow(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var bindings = new[] { new AccountBinding("STEAM_CN", 11, "MAIN"), new AccountBinding("TAPTAP", 22, "USER_SPECTATOR") };
        JsonElement[] Sessions(long mainId = 11, long watcherId = 22, string state = "MAIN_IN_MATCH", string code = "00123456", long sequence = 1, string connection = "spec") => JsonSerializer.SerializeToElement(new object[] {
            new { connectionId = "main", connectionState = "CONNECTED", platformId = "STEAM_CN", boundRole = "MAIN", livePlayerId = mainId, matchContext = new { matchState = state, spectatorCode = code, internalRoomId = 71, sequence } },
            new { connectionId = connection, connectionState = "CONNECTED", platformId = "TAPTAP", boundRole = "USER_SPECTATOR", livePlayerId = watcherId }
        }).Rows();
        var flow = new AutoFollowCoordinator();
        check(flow.Plan(false, bindings, Sessions(), now) is null && flow.State == "已暂停", "自动跟随关闭时不发送请求");
        check(flow.Plan(true, [], Sessions(), now) is null, "自动跟随缺少绑定时等待");
        check(flow.Plan(true, bindings, Sessions(mainId: 99), now) is null, "主账号身份不符时不发送");
        check(flow.Plan(true, bindings, Sessions(watcherId: 99), now) is null, "观战账号身份不符时不发送");
        check(flow.Plan(true, bindings, Sessions(connection: "main"), now) is null, "观战目标不能是主账号连接");
        check(flow.Plan(true, bindings, Sessions(code: ""), now) is null, "缺少观战码时等待，不用房间数字冒充观战码");
        var plan = flow.Plan(true, bindings, Sessions(), now)!;
        check(plan.ConnectionId == "spec" && plan.PlayerId == 22 && plan.Code == "00123456", "只发送到绑定观战账号，观战码保留前导零");
        flow.Sent(plan, "request-1", now);
        check(flow.Plan(true, bindings, Sessions(), now) is null, "同一对局不重复发送");
        flow.Response(JsonSerializer.SerializeToElement(new { requestId = "old-request", status = "WATCH_JOIN_ACCEPTED" }));
        check(flow.State == "正在加入", "旧请求响应不会确认新对局");
        flow.Response(JsonSerializer.SerializeToElement(new { requestId = "request-1", status = "WATCH_JOIN_ACCEPTED" }));
        check(flow.State == "等待手牌", "加入确认后继续等待真实手牌");
        JsonElement Snapshot(string connection = "spec", long room = 71, long epoch = 2, long sequence = 10) => JsonSerializer.SerializeToElement(new { connectionId = connection, platformId = "TAPTAP", spectatorAccountPlayerId = 22, roomId = room, spectatorSessionEpoch = epoch, sequenceNumber = sequence });
        flow.Snapshot(Snapshot(room: 72)); check(flow.State == "等待手牌", "其他房间快照不能确认正在观战");
        flow.Snapshot(Snapshot()); check(flow.State == "正在观战", "真实身份及房间一致才进入观战状态");
        flow.Plan(true, bindings, Sessions(state: "MATCH_ENDED", sequence: 0), now);
        check(flow.State == "正在观战", "旧序号的主账号结束消息不会覆盖新状态");
        flow.Plan(true, bindings, Sessions(state: "MATCH_ENDED", sequence: 2), now);
        check(flow.Active is null, "主账号结束对局清除活动目标");
        var next = flow.Plan(true, bindings, Sessions(code: "next", sequence: 3), now)!; flow.Sent(next, "next", now);
        flow.Plan(true, bindings, Sessions(code: "next", sequence: 3), now.AddSeconds(46));
        check(flow.State == "加入超时", "观战超时停止重复发送并允许重试"); flow.Retry();
        check(flow.Plan(true, bindings, Sessions(code: "next", sequence: 3), now.AddSeconds(47)) is not null, "手动重试重新核对连接");
        var fence = new HandSnapshotFence();
        check(fence.Accept(Snapshot(), "spec", "TAPTAP", 22), "手牌接收正确身份快照");
        check(!fence.Accept(Snapshot(connection: "main"), "spec", "TAPTAP", 22), "手牌拒绝主账号来源");
        check(!fence.Accept(Snapshot(epoch: 1), "spec", "TAPTAP", 22), "手牌拒绝旧对局 epoch");
        check(!fence.Accept(Snapshot(sequence: 9), "spec", "TAPTAP", 22), "手牌拒绝回退序号");
        check(fence.Accept(Snapshot(epoch: 3, sequence: 1), "spec", "TAPTAP", 22), "新对局序号可重新从低位开始");
    }
    private static void Capture(MainWindow window, string path) => CaptureElement((FrameworkElement)window.Content, 1220, 810, path);
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
    internal static void CaptureElement(FrameworkElement element, int width, int height, string path)
    {
        var wasUnparented = element.Parent is null;
        Border? backdrop = null;
        if (wasUnparented)
        {
            backdrop = new Border { Background = Controls.UI.Brush("Bg"), Child = element };
            System.Windows.Documents.TextElement.SetFontFamily(backdrop, new FontFamily("Microsoft YaHei UI"));
            element = backdrop;
        }
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
        if (backdrop is not null) backdrop.Child = null;
    }
}
