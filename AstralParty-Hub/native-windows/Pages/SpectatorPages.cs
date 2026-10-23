using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using XingShu.Native.Controls;
using XingShu.Native.Services;

namespace XingShu.Native;

public partial class MainWindow
{
    private Panel? handPanel;
    private string selectedWatcher = "";
    private TextBlock? followStatus;
    private readonly HandSnapshotFence handFence = new();
    private async Task UpdateAutoFollow()
    {
        var plan = autoFollow.Plan(Preferences.AutoFollow, Preferences.Bindings, Sessions, DateTimeOffset.UtcNow);
        RefreshFollowStatus();
        if (plan is null || closing) return;
        if (selectedWatcher != plan.ConnectionId) { selectedWatcher = plan.ConnectionId; handFence.Reset(); }
        try
        {
            var id = await Core.CallAsync("send_watch_target", new { connectionId = plan.ConnectionId, target = new { watchCode = plan.Code, spectatorCode = plan.Code, internalRoomId = plan.RoomId, roomId = plan.RoomId }, expectedSpectatorPlayerId = plan.PlayerId });
            autoFollow.Sent(plan, id.Text(), DateTimeOffset.UtcNow);
        }
        catch (Exception e) { autoFollow.Failed(plan, e.Message); }
        RefreshFollowStatus();
    }
    private void RefreshFollowStatus()
    { if (followStatus is not null) followStatus.Text = autoFollow.State + " · " + autoFollow.Detail; }
    private async Task RefreshEndedHands()
    {
        try { RenderHands(await Core.CallAsync("get_spectator_hand_payload")); }
        catch (Exception e) { StatusText.Text = e.Message; }
    }
    private async Task BuildSpectator(Panel body, CancellationToken token)
    {
        var binding = Preferences.Bindings.FirstOrDefault(b => b.Role == "USER_SPECTATOR");
        var watchers = Sessions.Where(s => binding is not null && AutoFollowCoordinator.Identity(s, binding)).ToArray();
        var picker = new ComboBox { ItemsSource = watchers.Select(s => new Choice(s.Text("connectionId"), Platform(s.Text("platformId")) + " · " + s.Text("livePlayerId"))).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Key", Width = 280 };
        picker.SelectedValue = selectedWatcher; if (picker.SelectedIndex < 0 && watchers.Length > 0) picker.SelectedIndex = 0;
        selectedWatcher = (picker.SelectedItem as Choice)?.Key ?? "";
        var code = UI.Input("游戏内观战码", width: 240);
        var status = UI.Text("选择已绑定的独立观战账号，再输入游戏内观战码。", 12, "Muted");
        var enabled = new CheckBox { Content = "自动跟随主账号的下一场对局", IsChecked = Preferences.AutoFollow };
        followStatus = UI.Text(autoFollow.State + " · " + autoFollow.Detail, 13, "Gold");
        var thisStatus = followStatus;
        enabled.Click += (_, _) => { Preferences.AutoFollow = enabled.IsChecked == true; Preferences.Save(); if (!Preferences.AutoFollow) autoFollow.Stop(); RefreshFollowStatus(); };
        var retry = Action("重试自动跟随", async () => { autoFollow.Retry(); await UpdateAutoFollow(); });
        var join = Action("加入观战", async () => {
            Preferences.AutoFollow = false; enabled.IsChecked = false; Preferences.Save(); autoFollow.Stop(); RefreshFollowStatus();
            var session = watchers.FirstOrDefault(s => s.Text("connectionId") == selectedWatcher);
            if (session.ValueKind != JsonValueKind.Object || session.Number("livePlayerId") <= 0) throw new InvalidOperationException("请先绑定并连接独立观战账号。");
            if (string.IsNullOrWhiteSpace(code.Text) || code.Text.Length > 128) throw new InvalidOperationException("请输入有效的游戏内观战码。");
            var id = await Core.CallAsync("send_watch_target", new { connectionId = session.Text("connectionId"), target = new { watchCode = code.Text.Trim(), spectatorCode = code.Text.Trim() }, expectedSpectatorPlayerId = session.Number("livePlayerId") });
            status.Text = "已提交观战请求，等待游戏确认。请求 " + id.Text();
        }, true); join.IsEnabled = watchers.Length > 0;
        var follow = Action("跟随主账号当前对局", async () => {
            Preferences.AutoFollow = false; enabled.IsChecked = false; Preferences.Save(); autoFollow.Stop(); RefreshFollowStatus();
            var mainBinding = Preferences.Bindings.FirstOrDefault(b => b.Role == "MAIN");
            var main = Sessions.SingleOrDefault(s => mainBinding is not null && AutoFollowCoordinator.Identity(s, mainBinding));
            if (main.Get("matchContext").Text("matchState") != "MAIN_IN_MATCH") throw new InvalidOperationException("已绑定主账号尚未进入可观战的对局。");
            var watch = main.Get("matchContext").Text("spectatorCode");
            if (watch.Length == 0) throw new InvalidOperationException("主账号尚未提供可用观战码，请先进入对局。");
            code.Text = watch;
            var session = watchers.FirstOrDefault(s => s.Text("connectionId") == selectedWatcher);
            if (session.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("请先连接独立观战账号。");
            await Core.CallAsync("send_watch_target", new { connectionId = selectedWatcher, target = new { watchCode = watch, spectatorCode = watch, roomId = main.Get("matchContext").Number("internalRoomId") }, expectedSpectatorPlayerId = session.Number("livePlayerId") });
            status.Text = "已提交跟随请求，等待游戏确认。";
        }); follow.IsEnabled = watchers.Length > 0;
        body.Children.Add(UI.Card(UI.Stack(UI.Text("自动跟随", 18, "Ink", true), enabled, followStatus, retry,
            UI.Text("按渠道、玩家 ID 与连接身份核对。加入失败或超时后等待手动重试；不会自动切换账号或启动游戏。", 12, "Muted"))));
        body.Children.Add(UI.Card(UI.Stack(UI.Text("手动加入观战", 18, "Ink", true), UI.Row(picker, code), UI.Row(join, follow), status)));
        if (watchers.Length == 0) body.Children.Add(UI.Empty("等待独立观战账号", "在游戏账号页绑定 USER_SPECTATOR 身份，再启动该渠道游戏。主账号不会被用作观战执行账号。"));
        var hands = new StackPanel(); handPanel = hands; body.Children.Add(hands); hands.Unloaded += (_, _) => { if (handPanel == hands) handPanel = null; if (followStatus == thisStatus) followStatus = null; };
        picker.SelectionChanged += async (_, _) => { selectedWatcher = (picker.SelectedItem as Choice)?.Key ?? ""; handFence.Reset(); try { RenderHands(await Core.CallAsync("get_spectator_hand_payload")); } catch (Exception e) { status.Text = e.Message; } };
        RenderHands(await Core.CallAsync("get_spectator_hand_payload", cancellation: token));
    }
    internal void RenderHands(JsonElement payload)
    {
        if (handPanel is null || CurrentPage != "spectator") return;
        var session = Sessions.FirstOrDefault(s => s.Text("connectionId") == selectedWatcher && s.Text("boundRole") == "USER_SPECTATOR" && s.Text("connectionState") == "CONNECTED");
        if (session.ValueKind != JsonValueKind.Object || !HandSnapshotFence.Matches(payload, selectedWatcher, session.Text("platformId"), session.Number("livePlayerId")))
        { handPanel.Tag = null; handPanel.Children.Clear(); handPanel.Children.Add(UI.Empty("等待真实手牌快照", "观战进入对局且游戏返回手牌后显示；不使用示例卡牌，也不借用其他连接的数据。")); return; }
        if (Preferences.AutoFollow && (autoFollow.Active is null || (autoFollow.Active.RoomId > 0 && payload.Number("roomId") != autoFollow.Active.RoomId)))
        { handPanel.Tag = null; handPanel.Children.Clear(); handPanel.Children.Add(UI.Empty("等待当前对局快照", "已切换跟随目标，上一场手牌不会继续显示。")); return; }
        if (!handFence.Accept(payload, selectedWatcher, session.Text("platformId"), session.Number("livePlayerId"))) return;
        var revision = selectedWatcher + ":" + payload.Text("roomId") + ":" + payload.Text("spectatorSessionEpoch") + ":" + payload.Text("sequenceNumber");
        if (handPanel.Tag as string == revision) return;
        handPanel.Tag = revision;
        handPanel.Children.Clear();
        handPanel.Children.Add(UI.Row(UI.Badge("房间 " + payload.Text("roomId")), UI.Badge("快照 " + payload.Text("sequenceNumber")), UI.Badge(payload.Text("snapshotTimestamp"), "Muted")));
        var room = payload.Get("roomPlayers");
        var players = room.ValueKind == JsonValueKind.Object ? room.EnumerateObject().Select(p => p.Value).ToArray() : [];
        foreach (var player in players)
        {
            handPanel.Children.Add(UI.Text("玩家 " + player.Text("playerId") + " · " + player.Text("handSize") + " 张", 17, "Gold", true));
            var cards = new WrapPanel();
            foreach (var id in player.Get("cardIds").Rows())
            {
                var card = Catalog.Find("cards", id.Number());
                if (card.ValueKind == JsonValueKind.Object) cards.Children.Add(CatalogButton(card, "cards", [card]));
                else cards.Children.Add(UI.Card(UI.Text("未知卡牌 #" + id.Text())));
            }
            handPanel.Children.Add(cards);
        }
    }
}
