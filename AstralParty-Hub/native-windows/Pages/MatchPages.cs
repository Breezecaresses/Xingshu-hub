using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using XingShu.Native.Controls;
using XingShu.Native.Services;

namespace XingShu.Native;

public partial class MainWindow
{
    private void BuildMatches(Panel body)
    {
        var records = Account.Get("snapshot").Get("matches").Rows(); var known = records.Where(r => MatchRules.IsKnown(r, Catalog)).ToArray();
        var pvp = known.Where(r => Catalog.Find("modes", r.Number("mapId")).Number("group") == 2).ToArray();
        var wins = known.Count(r => r.Number("rank") == 1);
        body.Children.Add(UI.Row(UI.Card(UI.Stack(UI.Text(records.Length.ToString(), 28, "Gold", true), UI.Text("已保存对局"))),
            UI.Card(UI.Stack(UI.Text(known.Length == 0 ? "—" : $"{(double)wins / known.Length:P0}", 28, "Green", true), UI.Text("已知结果胜率"))),
            UI.Card(UI.Stack(UI.Text(pvp.Length == 0 ? "—" : pvp.Average(r => r.Number("rank")).ToString("F2"), 28, "Pink", true), UI.Text("PVP 平均名次")))));
        AddSync(body);
        var modeIds = records.Select(r => r.Number("mapId")).Distinct().ToArray();
        var mode = UI.Select(new[] { "全部模式" }.Concat(modeIds.Select(id => Catalog.Name("modes", id))), "对局模式");
        var win = new CheckBox { Content = "仅胜利" }; var search = UI.Input("搜索角色或时间", width: 260); search.Tag = "search";
        body.Children.Add(UI.Row(mode, search, win));
        var list = new StackPanel(); var pagination = new WrapPanel(); body.Children.Add(list); body.Children.Add(pagination); var page = 0;
        void Render()
        {
            list.Children.Clear(); pagination.Children.Clear();
            var visible = records.Where(r => (mode.SelectedIndex == 0 || r.Number("mapId") == modeIds[mode.SelectedIndex - 1]) &&
                (win.IsChecked != true || MatchRules.IsKnown(r, Catalog) && r.Number("rank") == 1) &&
                (Catalog.Name("characters", r.Number("heroId")) + Date(r.Number("time"))).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            page = Math.Clamp(page, 0, Math.Max(0, (visible.Length - 1) / 20));
            foreach (var record in visible.Skip(page * 20).Take(20))
            {
                var hero = Catalog.Find("characters", record.Number("heroId"));
                var row = UI.Row(UI.Image(Catalog.Image(hero.Text("image"), 100), 58, 58, hero.Text("name")),
                    UI.Stack(UI.Text(MatchRules.Result(record, Catalog) + "   " + hero.Text("name"), 17, "Gold", true), UI.Text(Catalog.Name("modes", record.Number("mapId")) + " · " + Date(record.Number("time")), 12, "Muted")),
                    UI.Badge(record.Flag("detailAvailable") ? "已保存详情" : "查看详情"));
                var button = Action(hero.Text("name"), () => ShowMatch(record)); button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; list.Children.Add(button);
            }
            if (visible.Length == 0) list.Children.Add(UI.Empty("暂无符合条件的对局", Account.ValueKind == JsonValueKind.Undefined ? "先在右上角选择账号；首次读取需要账号在游戏大厅在线。" : "可更换筛选，或同步游戏数据。"));
            if (visible.Length > 20)
            {
                var prev = UI.Button("上一页", () => { page--; Render(); }); prev.IsEnabled = page > 0;
                var next = UI.Button("下一页", () => { page++; Render(); }); next.IsEnabled = (page + 1) * 20 < visible.Length;
                pagination.Children.Add(prev); pagination.Children.Add(UI.Badge($"{page + 1} / {(visible.Length + 19) / 20}")); pagination.Children.Add(next);
            }
        }
        mode.SelectionChanged += (_, _) => { page = 0; Render(); }; win.Click += (_, _) => { page = 0; Render(); }; search.TextChanged += (_, _) => { page = 0; Render(); }; Render();
    }
    private async Task ShowMatch(JsonElement record)
    {
        var platform = Account.Text("platform"); var playerId = Account.Get("snapshot").Number("playerId"); var session = Session;
        var args = new { platform, playerId, time = record.Number("time"), heroId = record.Number("heroId"), mapId = record.Number("mapId") };
        var detail = await Core.CallAsync("get_game_match_detail", args);
        var root = new StackPanel(); var content = new StackPanel(); var status = UI.Text("", 12, "Muted");
        var lifetime = new CancellationTokenSource(); root.Unloaded += (_, _) => lifetime.Cancel();
        root.Children.Add(UI.Text(Catalog.Name("characters", record.Number("heroId")) + " · " + MatchRules.Result(record, Catalog), 26, "Ink", true));
        root.Children.Add(UI.Text(Catalog.Name("modes", record.Number("mapId")) + " · " + Date(record.Number("time")), 13, "Muted"));
        var read = Action("重新读取本场详情", async () => {
            if (session.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("所选账号需要在游戏大厅在线。");
            await Core.CallAsync("request_game_match_detail", new { connectionId = session.Text("connectionId"), playerId, args.time, args.heroId, args.mapId }, lifetime.Token);
            status.Text = "正在等待游戏读取本场回放结算…";
            var stop = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < stop)
            {
                lifetime.Token.ThrowIfCancellationRequested(); detail = await Core.CallAsync("get_game_match_detail", args, lifetime.Token); Render();
                if (detail.Text("status") is "ready" or "error") return;
                await Task.Delay(800, lifetime.Token);
            }
            status.Text = "读取超时，已保留现有详情；可稍后重试。";
        }); read.IsEnabled = session.ValueKind == JsonValueKind.Object;
        root.Children.Add(UI.Row(read)); root.Children.Add(status); root.Children.Add(content);
        void Render()
        {
            content.Children.Clear(); var participants = detail.Get("participants").Rows();
            status.Text = detail.Text("status") switch { "ready" => "详情已保存 · " + detail.Text("observedAt"), "error" => detail.Text("message", detail.Text("error")), "loading" => "正在读取本场回放结算…", _ => "尚未保存详情；在线读取后可离线查看。" };
            if (detail.Text("statsError").Length > 0) content.Children.Add(UI.Text("回放统计未完整提供：" + detail.Text("statsError") + "。缺失指标显示 —。", 12, "Gold"));
            var ratings = detail.Text("statsSource") == "replay_settlement" ? MatchRules.Ratings(participants) : participants.Select(_ => (double?)null).ToArray();
            var best = ratings.Where(r => r.HasValue).DefaultIfEmpty(null).Max();
            for (var i = 0; i < participants.Length; i++)
            {
                var p = participants[i]; var hero = Catalog.Find("characters", p.Number("heroId"));
                var heading = UI.Row(UI.Image(Catalog.Image(hero.Text("image"), 96), 54, 54), UI.Stack(UI.Text(p.Text("name") + (p.Number("playerId") == playerId ? " · 本人" : ""), 18, "Ink", true), UI.Text(hero.Text("name") + " · 对局星级 " + p.Text("level"), 12, "Muted")),
                    UI.Badge(ratings[i]?.ToString("F2") ?? "—", "Pink"));
                if (ratings[i].HasValue && ratings[i] == best) heading.Children.Add(UI.Badge("MVP", "Gold"));
                var metrics = new WrapPanel();
                foreach (var (key, name) in new[] { ("kills", "击杀"), ("deaths", "死亡"), ("damage", "输出"), ("taken", "承伤"), ("healing", "治疗"), ("coins", "累计金币"), ("transfer", "转账"), ("turns", "行动回合"), ("gold", "结算金币"), ("movement", "移动步数") })
                    metrics.Children.Add(new Border { Width = 115, Padding = new Thickness(8), Child = UI.Stack(UI.Text(name, 11, "Muted"), UI.Text(p.Metric(key), 17, "Ink", true)) });
                content.Children.Add(UI.Card(UI.Stack(heading, metrics)));
            }
            if (participants.Length == 0) content.Children.Add(UI.Empty("暂未取得参战数据", "首次读取需要对应账号在线，服务器已过期的回放可能无法补全。"));
            content.Children.Add(new Expander { Header = "评分规则与数据来源", Content = UI.Text(MatchRules.RatingMethod, 12, "Muted") });
        }
        Render(); ShowDetail("历史对局详情", root); lifetime.Dispose();
    }
}
