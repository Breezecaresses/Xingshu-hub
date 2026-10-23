using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XingShu.Native.Controls;
using XingShu.Native.Services;

namespace XingShu.Native;

public record Choice(string Key, string Label) { public override string ToString() => Label; }
public record PageInfo(string Key, string Title, string Group, string Glyph, string Description);
public partial class MainWindow : Window
{
    public static readonly PageInfo[] Pages = [
        new("overview","总览","工作台","◈","管理游戏、查看账号与最近动态"),
        new("accounts","游戏账号","工作台","◎","按渠道和玩家身份管理游戏连接"),
        new("profile","玩家资料","游戏资料","◉","查看所选账号已保存的游戏数据"),
        new("characters","角色中心","游戏资料","◇","技能、皮肤、养成与角色资料"),
        new("cards","卡牌图鉴","游戏资料","▱","完整长卡牌、异画与效果说明"),
        new("chips","筹码图鉴","游戏资料","◌","属性、品质和适用地图"),
        new("maps","地图与攻略","游戏资料","⌘","地图机制、进度事件与合作任务"),
        new("matches","对战记录","游戏资料","▤","历史对局、移动步数与星枢评分"),
        new("runtime","运行时","星枢核心","⚙","查看游戏插件、身份绑定和回合状态"),
        new("spectator","星枢观战","星枢核心","◉","独立观战账号与已收到的真实手牌"),
        new("bridge","游戏通信","星枢核心","⇄","连接状态、心跳与通信日志"),
        new("plugins","运行时插件","包与扩展","▣","按游戏安装位置检查插件状态"),
        new("mods","游戏模组","包与扩展","◇","管理立绘替换及其他模组"),
        new("import","导入安装包","包与扩展","＋","先检查安装包，再选择游戏安装位置"),
        new("transactions","安装事务","维护","☷","检查操作记录并按备份还原"),
        new("backups","数据备份","维护","▥","查看本机备份及完整性记录"),
        new("diagnostics","系统诊断","维护","⌁","诊断本地环境与数据来源"),
        new("settings","软件设置","维护","⚙","界面缩放、账号绑定和本地设置")];

    internal readonly CoreClient Core = new();
    internal readonly CatalogStore Catalog;
    internal readonly Preferences Preferences;
    private readonly Dictionary<string, Button> navButtons = new();
    private CancellationTokenSource navigation = new();
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer pollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    internal JsonElement[] Accounts = [], Sessions = [], Targets = [];
    internal string CurrentPage = "overview";
    internal string LastPageError = "";
    private bool settingAccount, closing, polling;
    internal bool HeadlessTest;
    internal UIElement? LastDetailContent;
    private readonly AutoFollowCoordinator autoFollow = new();
    internal JsonElement Account => Accounts.FirstOrDefault(a => AccountKey(a) == Preferences.Account);
    internal JsonElement Session => Sessions.FirstOrDefault(s => s.Text("connectionState") == "CONNECTED" && s.Text("platformId") + ":" + s.Text("livePlayerId") == Preferences.Account);
    internal static string AccountKey(JsonElement a) => a.Text("platform") + ":" + a.Get("snapshot").Text("playerId");

    public MainWindow(CatalogStore catalog, Preferences preferences)
    {
        Catalog = catalog; Preferences = preferences; InitializeComponent();
        MinWidth = Math.Min(900, SystemParameters.WorkArea.Width); MinHeight = Math.Min(620, SystemParameters.WorkArea.Height);
        Width = Math.Clamp(double.IsFinite(preferences.Width) ? preferences.Width : 1220, MinWidth, SystemParameters.WorkArea.Width);
        Height = Math.Clamp(double.IsFinite(preferences.Height) ? preferences.Height : 810, MinHeight, SystemParameters.WorkArea.Height);
        ApplyScale(preferences.Scale);
        string group = "";
        foreach (var p in Pages)
        {
            if (p.Group != group) { group = p.Group; Navigation.Children.Add(UI.Text(group, 10, "Muted")); }
            var button = UI.Button(p.Glyph + "   " + p.Title, () => _ = Navigate(p.Key));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 8, 4); button.BorderThickness = new Thickness(0);
            Navigation.Children.Add(button); navButtons[p.Key] = button;
        }
        RefreshButton.Click += (_, _) => _ = Navigate(CurrentPage);
        AccountPicker.SelectionChanged += (_, _) => { if (settingAccount) return;
            Preferences.Account = (AccountPicker.SelectedItem as Choice)?.Key ?? ""; Preferences.Save(); _ = Navigate(CurrentPage); };
        toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); };
        pollTimer.Tick += async (_, _) => { if (polling || closing || !Core.IsRunning) return;
            polling = true; try { var s = await Core.CallAsync("get_runtime_sessions"); Sessions = s.Rows();
                StatusText.Text = Sessions.Length > 0 ? $"游戏连接 {Sessions.Length} 个 · 数据按账号隔离" : "本地核心已连接 · 等待游戏插件";
                await UpdateAutoFollow();
                if (CurrentPage == "spectator") RenderHands(await Core.CallAsync("get_spectator_hand_payload"));
            } catch (Exception e) { StatusText.Text = e.Message; } finally { polling = false; } };
        Core.Event += (name, payload) => { if (closing) return; Dispatcher.BeginInvoke(() => {
            if (name == "core://disconnected") StatusText.Text = "核心已断开，已保存的数据不会丢失。";
            if (name == "spectator://hand-snapshot") { autoFollow.Snapshot(payload); RenderHands(payload); RefreshFollowStatus(); }
            if (name == "spectator://session-ended") _ = RefreshEndedHands();
            if (name == "spectator://watch-target-response") { autoFollow.Response(payload); RefreshFollowStatus(); Notify("观战请求：" + payload.Text("status") + " " + payload.Text("reason")); }
        }); };
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control) { _ = Navigate(CurrentPage); e.Handled = true; }
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { FindSearch()?.Focus(); e.Handled = true; }
        };
        Loaded += async (_, _) => await InitializeAsync(); Closing += OnClosing;
    }
    private async Task InitializeAsync()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var dataIndex = Array.IndexOf(args, "--data-dir");
            await Core.StartAsync(args.Contains("--offline"), dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1] : null);
            try { await PushBindings(); }
            catch (Exception e) { Notify("账号绑定未生效，请在游戏账号页修正。" + e.Message); }
            await LoadIdentity();
            if (Core.Status.Text("bridgeError").Length > 0) Notify(Core.Status.Text("bridgeError"));
            await Navigate(Preferences.Page); StatusText.Text = "本地核心已连接 · 等待游戏插件"; pollTimer.Start();
        }
        catch (Exception e) { PageBody.Children.Clear(); PageBody.Children.Add(UI.Empty("无法连接本地核心", e.Message)); StatusText.Text = "启动未完成"; }
    }
    internal async Task PushBindings() => await Core.CallAsync("configure_runtime_bindings", new { bindings = Preferences.Bindings.Select(b => new { platformId = b.PlatformId, playerId = b.PlayerId, role = b.Role }) });
    private async Task LoadIdentity()
    {
        var accounts = Core.CallAsync("get_game_library"); var sessions = Core.CallAsync("get_runtime_sessions"); var targets = Core.CallAsync("list_extension_targets");
        await Task.WhenAll(accounts, sessions, targets); Accounts = accounts.Result.Rows(); Sessions = sessions.Result.Rows(); Targets = targets.Result.Rows();
        var choices = Accounts.Select(a => new Choice(AccountKey(a), a.Get("snapshot").Text("name") + " · " + Platform(a.Text("platform"))))
            .Concat(Sessions.Where(s => s.Number("livePlayerId") > 0).Select(s => new Choice(s.Text("platformId") + ":" + s.Text("livePlayerId"), "在线玩家 " + s.Text("livePlayerId") + " · " + Platform(s.Text("platformId")))))
            .DistinctBy(c => c.Key).ToList();
        // Never fall back to a spectator when the main account is unsynchronized.
        if (!choices.Any(c => c.Key == Preferences.Account))
        {
            var main = Sessions.FirstOrDefault(s => s.Text("boundRole") == "MAIN" && s.Number("livePlayerId") > 0 && s.Text("connectionState") == "CONNECTED");
            Preferences.Account = main.ValueKind != JsonValueKind.Undefined ? main.Text("platformId") + ":" + main.Text("livePlayerId")
                : Accounts.FirstOrDefault(a => a.Text("role") == "MAIN") is var cached && cached.ValueKind != JsonValueKind.Undefined ? AccountKey(cached) : "";
        }
        choices.Insert(0, new Choice("", "选择查看的账号")); settingAccount = true;
        AccountPicker.ItemsSource = choices; AccountPicker.SelectedValue = Preferences.Account; settingAccount = false;
    }
    internal async Task Navigate(string key)
    {
        if (closing) return;
        var page = Pages.FirstOrDefault(p => p.Key == key) ?? Pages[0];
        navigation.Cancel(); navigation.Dispose(); navigation = new(); var token = navigation.Token;
        CurrentPage = page.Key; Preferences.Page = page.Key; Preferences.Save();
        LastPageError = "";
        Breadcrumb.Text = page.Group + " / " + page.Title;
        foreach (var (id, button) in navButtons) button.Background = id == page.Key ? new SolidColorBrush(Color.FromRgb(77, 61, 133)) : Brushes.Transparent;
        PageBody.Children.Clear(); PageScroll.ScrollToTop(); BusyIndicator.Visibility = Visibility.Visible;
        PageBody.Children.Add(UI.Text(page.Title, 27, "Ink", true)); PageBody.Children.Add(UI.Text(page.Description, 13, "Muted"));
        try
        {
            if (Core.IsRunning) await LoadIdentity(); token.ThrowIfCancellationRequested();
            var body = new StackPanel();
            await BuildPage(page.Key, body, token); token.ThrowIfCancellationRequested(); PageBody.Children.Add(body);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!token.IsCancellationRequested) { LastPageError = e.ToString(); PageBody.Children.Add(UI.Empty("当前内容暂不可用", e.Message)); } }
        finally { if (!token.IsCancellationRequested) BusyIndicator.Visibility = Visibility.Collapsed; }
    }
    private TextBox? FindSearch() => FindVisual<TextBox>(PageBody).FirstOrDefault(t => t.Tag as string == "search");
    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var c = VisualTreeHelper.GetChild(root, i); if (c is T t) yield return t; foreach (var item in FindVisual<T>(c)) yield return item; } }
    internal void Notify(string message) { ToastText.Text = message; Toast.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }
    internal async void Run(Button? button, Func<Task> action)
    { if (button is not null) button.IsEnabled = false; try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Notify(e.Message); } finally { if (button is not null) button.IsEnabled = true; } }
    internal Button Action(string title, Func<Task> action, bool primary = false)
    { Button? button = null; button = UI.Button(title, () => Run(button, action), primary); return button; }
    internal void ApplyScale(double scale) { Preferences.Scale = Math.Clamp(double.IsFinite(scale) ? scale : 1, .8, 1.5); AppGrid.LayoutTransform = new ScaleTransform(Preferences.Scale, Preferences.Scale); }
    internal static string Platform(string p) => p switch { "STEAM_CN" => "Steam 国服", "STEAM_GLOBAL" => "Steam 国际服", "TAPTAP" => "TapTap", "BILIBILI" => "哔哩哔哩", "WEGAME" => "WeGame", _ => p };
    internal static string Date(long time) { try { return (time < 1_000_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(time) : DateTimeOffset.FromUnixTimeMilliseconds(time)).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } catch { return "时间未知"; } }
    internal void ShowDetail(string title, UIElement content)
    {
        LastDetailContent = content;
        if (HeadlessTest) return;
        var dialog = new Window { Owner = this, Title = title, Width = Math.Min(980, ActualWidth - 50), Height = Math.Min(760, ActualHeight - 40), MinWidth = 660, MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, Content = new ScrollViewer { Content = content, Padding = new Thickness(26) } };
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        dialog.ShowDialog();
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return; e.Cancel = true; closing = true; pollTimer.Stop(); navigation.Cancel(); autoFollow.Stop();
        if (WindowState == WindowState.Normal) { Preferences.Width = ActualWidth; Preferences.Height = ActualHeight; }
        try { Preferences.Save(); } catch { } await Core.DisposeAsync(); Close();
    }
}
