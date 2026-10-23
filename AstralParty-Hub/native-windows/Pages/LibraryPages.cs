using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XingShu.Native.Controls;
using XingShu.Native.Services;

namespace XingShu.Native;

public partial class MainWindow
{
    private static readonly string[] CardTypes = ["其他", "攻击", "防御", "效果", "反制", "事件", "幸运", "厄运", "诅咒", "建筑"];
    internal void BuildLibrary(string section, Panel body)
    {
        var query = UI.Input("搜索名称、效果或编号 (Ctrl+K)", width: 350); query.Tag = "search";
        var type = UI.Select(section == "cards" ? new[] { "全部类型" }.Concat(CardTypes) : section == "chips" ? ["全部品质", "蓝色", "紫色", "金色"] : section == "maps" ? ["全部地图", "合作挑战", "常规地图", "含实战攻略"] : ["全部资料"], "筛选内容", 150);
        var favorites = new CheckBox { Content = "仅收藏", Visibility = section == "cards" ? Visibility.Visible : Visibility.Collapsed };
        var arts = new CheckBox { Content = "异画版本", Visibility = section == "cards" ? Visibility.Visible : Visibility.Collapsed };
        var count = UI.Text("", 12, "Muted");
        body.Children.Add(UI.Row(query, type, favorites, arts)); body.Children.Add(count);
        var cards = new WrapPanel(); body.Children.Add(cards);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        void Render()
        {
            cards.Children.Clear();
            IEnumerable<JsonElement> rows = Catalog.Rows(section);
            if (section == "characters") rows = rows.Where(r => r.Flag("gallery") && r.Text("name").Length > 0);
            if (section == "cards") rows = rows.Where(r => r.Flag("gallery") || r.Flag("playable"));
            if (section == "maps")
            {
                rows = rows.Where(r => r.Text("image").Length > 0 && !r.Text("name").StartsWith("Map_"));
                if (type.SelectedIndex is 1 or 2) rows = rows.Where(r => (r.Get("difficulties").Rows().Length > 0) == (type.SelectedIndex == 1));
                if (type.SelectedIndex == 3) rows = rows.Where(r => Catalog.Guides.Get("maps").Rows().Any(g => g.Number("id") == r.Number("id") && g.Get("community").ValueKind == JsonValueKind.Object));
                rows = rows.OrderByDescending(r => Catalog.Guides.Get("maps").Rows().Any(g => g.Number("id") == r.Number("id") && g.Get("community").ValueKind == JsonValueKind.Object)).ThenByDescending(r => r.Get("difficulties").Rows().Length > 0);
            }
            if (section == "cards" && type.SelectedIndex > 0) rows = rows.Where(r => r.Number("type") == type.SelectedIndex - 1);
            if (section == "chips" && type.SelectedIndex > 0) rows = rows.Where(r => r.Text("qualityName") == type.SelectedItem?.ToString());
            if (favorites.IsChecked == true) rows = rows.Where(r => Preferences.Favorites.Contains(r.Number("id")));
            rows = rows.Where(r => (r.Text("name") + " " + CatalogStore.Plain(r.Text("description")) + " " + r.Text("id")).Contains(query.Text, StringComparison.OrdinalIgnoreCase));
            var all = rows.ToArray();
            if (section == "cards" && arts.IsChecked == true)
            {
                var versions = Catalog.Rows("altArts").Where(a => all.Any(c => c.Number("id") == a.Number("cardId"))).ToArray();
                foreach (var art in versions) { var c = Catalog.Find("cards", art.Number("cardId")); cards.Children.Add(CatalogButton(c, section, all, art)); }
                count.Text = $"{versions.Length} 个异画版本 · 静态卡面原生显示";
            }
            else
            {
                foreach (var item in all.Take(150)) cards.Children.Add(CatalogButton(item, section, all));
                count.Text = $"{all.Length} 条资料 · 官方配置 {Catalog.Data.Text("version")}";
                if (all.Length > 150) cards.Children.Add(UI.Empty("请缩小搜索范围", "当前显示前 150 条。"));
            }
            if (cards.Children.Count == 0) cards.Children.Add(UI.Empty("没有符合条件的内容", "可更换关键词或取消筛选。"));
        }
        timer.Tick += (_, _) => { timer.Stop(); Render(); };
        query.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        type.SelectionChanged += (_, _) => Render(); favorites.Click += (_, _) => Render(); arts.Click += (_, _) => Render();
        body.Unloaded += (_, _) => timer.Stop(); Render();
    }
    private Button CatalogButton(JsonElement item, string section, JsonElement[] all, JsonElement art = default)
    {
        UIElement content;
        if (section == "cards") content = FullCard(item, art);
        else
        {
            var width = section == "maps" ? 290 : 225;
            var image = UI.Image(Catalog.Image(item.Text("image"), 430), width - 28, section == "maps" ? 152 : 145, item.Text("name"));
            var text = section == "characters" ? item.Text("title") : section == "chips" ? item.Text("qualityName") + " · " + CatalogStore.Plain(item.Text("description")) : CatalogStore.Plain(item.Text("description"));
            var desc = UI.Text(text, 12, "Muted"); desc.MaxHeight = 65; desc.TextTrimming = TextTrimming.CharacterEllipsis;
            content = UI.Stack(image, UI.Text(item.Text("name"), 17, "Ink", true), desc);
            if (section == "maps")
            {
                var guide = Catalog.Guides.Get("maps").Rows().FirstOrDefault(g => g.Number("id") == item.Number("id"));
                ((StackPanel)content).Children.Add(UI.Badge(guide.Get("community").ValueKind == JsonValueKind.Object ? "实战速查 + 配置任务" : item.Get("difficulties").Rows().Length > 0 ? "合作机制与任务" : "地图资料", "Gold"));
            }
            ((FrameworkElement)content).Width = width - 28;
        }
        var button = UI.Button(item.Text("name"), () => { if (section == "maps") ShowMap(item); else ShowCatalogDetail(section, item, all, art); });
        button.Content = content; button.Padding = new Thickness(section == "cards" ? 0 : 13); button.Margin = new Thickness(0, 0, 16, 18);
        button.VerticalAlignment = VerticalAlignment.Top; button.ToolTip = item.Text("name") + "\n" + CatalogStore.Plain(item.Text("description"));
        return button;
    }
    internal Border FullCard(JsonElement card, JsonElement art = default, double width = 206)
    {
        var type = Math.Clamp((int)card.Number("type"), 0, CardTypes.Length - 1);
        var accent = type switch { 1 => "#EF6B87", 2 => "#67D5E7", 4 => "#D29BE9", _ => "#BDDE65" };
        var imageKey = art.ValueKind == JsonValueKind.Object ? art.Text("image") : card.Text("image");
        var name = art.ValueKind == JsonValueKind.Object ? art.Text("name") : card.Text("name");
        var stack = new StackPanel { Width = width - 12 };
        var picture = new Grid { Height = width - 6, ClipToBounds = true };
        picture.Children.Add(new Image { Source = Catalog.Image(imageKey, (int)(width * 2)), Stretch = Stretch.UniformToFill });
        var typeBadge = UI.Badge(CardTypes[type] + "  " + card.Number("cost") + "♦", type == 2 ? "Green" : "Pink");
        typeBadge.HorizontalAlignment = HorizontalAlignment.Left; typeBadge.VerticalAlignment = VerticalAlignment.Top; typeBadge.Margin = new Thickness(6);
        picture.Children.Add(typeBadge); stack.Children.Add(picture);
        stack.Children.Add(new Border { Background = (Brush)new BrushConverter().ConvertFrom(accent)!, Padding = new Thickness(7, 4, 7, 3),
            Child = new TextBlock { Text = name, Foreground = Brushes.Black, FontWeight = FontWeights.Bold, FontSize = 13, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap } });
        var desc = UI.Text(CatalogStore.Plain(card.Text("description")), width < 200 ? 10 : 12, "Ink", true);
        desc.Margin = new Thickness(9, 10, 9, 5); desc.Height = width * .51; stack.Children.Add(desc);
        var footer = UI.Text(card.Text("number", "#" + card.Text("id")) + "                      ✦", 9, "Muted"); footer.Margin = new Thickness(9, 0, 8, 6); stack.Children.Add(footer);
        return new Border { Child = stack, Background = UI.Brush("Bg"), CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(178, 151, 92)), BorderThickness = new Thickness(4), ClipToBounds = true };
    }
    internal void ShowCatalogDetail(string section, JsonElement item, JsonElement[] all, JsonElement art = default)
    {
        var panel = new StackPanel(); var index = Math.Max(0, Array.FindIndex(all, r => r.Number("id") == item.Number("id")));
        void Render()
        {
            panel.Children.Clear(); item = all[index];
            panel.Children.Add(UI.Text(art.ValueKind == JsonValueKind.Object ? art.Text("name") : item.Text("name"), 25, "Ink", true));
            if (section == "cards")
            {
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
                var view = FullCard(item, art, 240); view.HorizontalAlignment = HorizontalAlignment.Left; view.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(view);
                var details = UI.Stack(UI.Row(UI.Badge(CardTypes[Math.Clamp((int)item.Number("type"), 0, 9)]), UI.Badge("消耗 " + item.Text("cost"))),
                    UI.Text(CatalogStore.Plain(item.Text("description")), 16), UI.Text(CatalogStore.Plain(item.Text("comment")), 13, "Muted"));
                var favorite = UI.Button(Preferences.Favorites.Contains(item.Number("id")) ? "取消收藏" : "收藏卡牌", () => {
                    if (!Preferences.Favorites.Remove(item.Number("id"))) Preferences.Favorites.Add(item.Number("id")); Preferences.Save(); Render(); });
                details.Children.Add(favorite);
                var versions = new WrapPanel(); versions.Children.Add(UI.Button("原始卡面", () => { art = default; Render(); }));
                foreach (var a in Catalog.Rows("altArts").Where(a => a.Number("cardId") == item.Number("id"))) versions.Children.Add(UI.Button(a.Text("name"), () => { art = a; Render(); }));
                details.Children.Add(versions);
                if (art.Flag("animated"))
                {
                    var path = Catalog.ResourcePath(art.Text("video"));
                    if (path is not null && Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        var media = new MediaElement { Source = new Uri(path), Width = 320, Height = 230, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close };
                        media.MediaFailed += (_, _) => Notify("系统无法播放此视频，仍可查看完整静态卡面。");
                        details.Children.Add(UI.Row(UI.Button("播放原始动态卡面", () => media.Play()), UI.Button("暂停", () => media.Pause()))); details.Children.Add(media);
                    }
                    else details.Children.Add(UI.Text("此版本使用静态卡面；Spine 骨骼动画播放器尚未迁入原生版。", 12, "Muted"));
                }
                var related = new WrapPanel();
                foreach (var id in item.Get("relatedCards").Rows()) { var next = Catalog.Find("cards", id.Number()); if (next.ValueKind != JsonValueKind.Undefined) related.Children.Add(UI.Button(next.Text("name"), () => ShowCatalogDetail("cards", next, [next]))); }
                details.Children.Add(related); Grid.SetColumn(details, 1); grid.Children.Add(details); panel.Children.Add(grid);
            }
            else if (section == "characters")
            {
                var hero = Account.Get("snapshot").Get("heroes").Rows().FirstOrDefault(h => h.Number("id") == item.Number("id"));
                panel.Children.Add(UI.Row(UI.Image(Catalog.Image(item.Text("image")), 130, 130),
                    UI.Stack(UI.Text(item.Text("title"), 18, "Gold"), UI.Text($"生命 {item.Number("hp")}   攻击 {item.Number("attack")}   防御 {item.Number("defense")}", 14),
                    UI.Text(hero.ValueKind == JsonValueKind.Undefined ? "持有与养成：未同步" : $"羁绊等级 {hero.Number("level")} · 经验 {hero.Number("exp")} · PVE {hero.Metric("pveLevel")}", 13, "Muted"))));
                foreach (var key in new[] { "skills", "pveSkills" })
                {
                    var skills = new WrapPanel(); foreach (var skill in item.Get(key).Rows())
                        skills.Children.Add(UI.Card(UI.Stack(UI.Text(skill.Text("name") + (skill.Number("cooldown") > 0 ? " · 冷却 " + skill.Number("cooldown") : ""), 16, "Gold", true), UI.Text(CatalogStore.Plain(skill.Text("description"))))));
                    UI.Section(panel, key == "skills" ? "基础技能" : "合作挑战技能", skills);
                }
                var skins = new WrapPanel(); foreach (var skin in item.Get("skins").Rows())
                    skins.Children.Add(UI.Card(UI.Stack(UI.Image(Catalog.Image(skin.Text("image"), 300), 160, 190, skin.Text("name")), UI.Text(skin.Text("name"), 12), Limit(UI.Text(CatalogStore.Plain(skin.Text("description")), 11, "Muted"), 160, 100))));
                CharacterSection(panel, "皮肤", skins);
                var growth = new StackPanel();
                foreach (var p in item.Get("pveProgression").Rows())
                {
                    var reached = hero.Get("pveLevel").ValueKind == JsonValueKind.Number && hero.Number("pveLevel") >= p.Number("level");
                    growth.Children.Add(UI.Card(UI.Stack(UI.Text("Lv." + p.Number("level") + (reached ? " · 已达到" : ""), 15, reached ? "Green" : "Gold", true), UI.Text(CatalogStore.Plain(p.Text("description"))))));
                }
                if (growth.Children.Count > 0) CharacterSection(panel, "合作挑战成长", growth, true);
                var potentials = item.Get("pvePotentials").Rows();
                if (potentials.Length == 0 && item.Get("pvePotential").ValueKind == JsonValueKind.Object) potentials = [item.Get("pvePotential")];
                var potentialViews = new StackPanel();
                foreach (var p in potentials)
                {
                    var current = hero.ValueKind == JsonValueKind.Object ? hero.Get("potential").Rows().Any(v => v.Number() == p.Number("id")) ? "已激发" : "未激发" : "账号进度待同步";
                    var view = UI.Stack(UI.Row(UI.Badge("所需合作等级 " + p.Number("requiredLevel"), "Gold"), UI.Badge(current, "Green")), UI.Text(CatalogStore.Plain(p.Text("description"))));
                    foreach (var skill in p.Get("skills").Rows()) view.Children.Add(UI.Card(UI.Stack(UI.Text(skill.Text("name"), 16, "Ink", true), UI.Text(CatalogStore.Plain(skill.Text("description"))))));
                    if (p.Get("materials").ValueKind == JsonValueKind.Object) view.Children.Add(UI.Text("激发材料：" + Catalog.Items(p.Get("materials")), 12, "Muted"));
                    potentialViews.Children.Add(UI.Card(view));
                }
                if (potentialViews.Children.Count > 0) CharacterSection(panel, "潜能激发", potentialViews, true);
                var bonds = new WrapPanel();
                foreach (var b in item.Get("bondRewards").Rows())
                {
                    var reached = hero.ValueKind == JsonValueKind.Object && hero.Number("level") >= b.Number("FavorLevel");
                    bonds.Children.Add(UI.Card(UI.Stack(UI.Text("羁绊 Lv." + b.Number("FavorLevel") + (reached ? " · 已达到" : ""), 16, reached ? "Green" : "Gold", true), Limit(UI.Text(Catalog.Items(b.Get("Rewards")), 13), 325, 100))));
                }
                if (bonds.Children.Count > 0) CharacterSection(panel, "羁绊奖励", bonds);
                var breakthrough = item.Get("breakthrough");
                if (breakthrough.Get("NeedMaterials").ValueKind == JsonValueKind.Object) CharacterSection(panel, "羁绊突破", UI.Card(UI.Stack(
                    UI.Text(hero.ValueKind == JsonValueKind.Object ? hero.Flag("breakthrough") ? "已突破" : "未突破" : "账号进度待同步", 15, "Gold", true),
                    UI.Text("突破材料：" + Catalog.Items(breakthrough.Get("NeedMaterials"))), UI.Text("突破奖励：" + Catalog.Items(breakthrough.Get("Rewards"))))));
                var emotes = new WrapPanel(); foreach (var emote in item.Get("emotes").Rows())
                    emotes.Children.Add(UI.Card(UI.Stack(UI.Image(Catalog.Image(emote.Text("image"), 160), 86, 86, emote.Text("name")), Limit(UI.Text(emote.Text("name"), 11, "Muted"), 100, 40))));
                if (emotes.Children.Count > 0) CharacterSection(panel, "角色表情 · 静态预览", emotes);
                CharacterSection(panel, "角色档案", UI.Card(UI.Stack(UI.Text(item.Text("biography")), UI.Text(item.Text("comment"), 12, "Muted"))));
            }
            else
            {
                panel.Children.Add(UI.Image(Catalog.Image(item.Text("image"), 500), 230, 210, item.Text("name")));
                panel.Children.Add(UI.Row(UI.Badge(item.Text("qualityName")), UI.Badge("#" + item.Text("id"))));
                panel.Children.Add(UI.Text(CatalogStore.Plain(item.Text("description")), 17));
                var maps = item.Get("mapLimit").Rows(); if (maps.Length > 0) panel.Children.Add(UI.Text("适用地图：" + string.Join("、", maps.Select(m => Catalog.Name("maps", m.Number()))), 13, "Muted"));
            }
            panel.Children.Add(UI.Row(UI.Button("← 上一项", () => { index = (index - 1 + all.Length) % all.Length; art = default; Render(); }),
                UI.Button("下一项 →", () => { index = (index + 1) % all.Length; art = default; Render(); }), UI.Badge($"{index + 1} / {all.Length}")));
        }
        Render(); ShowDetail(item.Text("name"), panel);
    }
    private static void CharacterSection(Panel panel, string title, UIElement content, bool expanded = false) => panel.Children.Add(new Expander {
        Header = UI.Text(title, 18, "Ink", true), Content = content, IsExpanded = expanded, Margin = new Thickness(0, 8, 0, 8), Foreground = UI.Brush("Ink") });
    private static TextBlock Limit(TextBlock block, double width, double height) { block.Width = width; block.MaxHeight = height; block.TextTrimming = TextTrimming.CharacterEllipsis; return block; }
    internal void ShowMap(JsonElement map) => ShowDetail(map.Text("name") + " · 地图攻略", BuildMapGuide(map));
    internal UIElement BuildMapGuide(JsonElement map)
    {
        var root = new StackPanel(); var content = new StackPanel();
        var guide = Catalog.Guides.Get("maps").Rows().FirstOrDefault(g => g.Number("id") == map.Number("id"));
        var community = guide.Get("community"); var diffs = guide.Get("difficulties").Rows();
        root.Children.Add(UI.Text(map.Text("name"), 27, "Ink", true));
        root.Children.Add(UI.Text(CatalogStore.Plain(map.Text("galleryDescription", map.Text("description"))), 13, "Muted"));
        var difficulty = UI.Select(diffs.Select(d => d.Text("name")).DefaultIfEmpty("常规地图"), "地图难度", 180);
        if (community.ValueKind == JsonValueKind.Object) difficulty.SelectedIndex = Math.Clamp((int)community.Number("difficultyIndex"), 0, Math.Max(0, diffs.Length - 1));
        root.Children.Add(UI.Row(difficulty, UI.Badge("官方配置 " + Catalog.Guides.Text("gameVersion")), UI.Badge("攻略按来源标注", "Green")));
        var nav = new WrapPanel(); root.Children.Add(nav); root.Children.Add(content);
        string active = community.ValueKind == JsonValueKind.Object ? "quick" : "official";
        var buttons = new Dictionary<string, Button>();
        void Render()
        {
            content.Children.Clear();
            foreach (var (key, button) in buttons) button.Background = key == active ? new SolidColorBrush(Color.FromRgb(77, 61, 133)) : UI.Brush("Raised");
            var d = diffs.ElementAtOrDefault(Math.Max(0, difficulty.SelectedIndex));
            if (active == "map")
            {
                var preview = UI.Image(Catalog.Image(map.Text("sceneImage", map.Text("image")), 1400), 760, 330, map.Text("name")); preview.Width = double.NaN; preview.MaxWidth = 760;
                content.Children.Add(UI.Card(UI.Stack(preview, UI.Text("游戏地图预览 · 未校准的参考图编号不会标为实际格子 ID。", 12, "Muted"))));
            }
            else if (active == "quick")
            {
                if (community.ValueKind == JsonValueKind.Object && difficulty.SelectedIndex == community.Number("difficultyIndex")) RenderCommunityGuide(content, community);
                else content.Children.Add(UI.Empty("该难度暂无实战路线", "可在“配置与任务”查看当前难度的机制和奖励。不同难度的数值分别保存。"));
            }
            else
            {
                if (d.ValueKind == JsonValueKind.Object)
                {
                    content.Children.Add(UI.Card(UI.Stack(UI.Text(d.Text("name") + " · 进度上限 " + d.Number("progressLimit"), 20, "Gold", true), UI.Text(CatalogStore.Plain(d.Text("description")), 13, "Muted"))));
                    var missions = new WrapPanel();
                    foreach (var mission in d.Get("missions").Rows())
                    {
                        var task = UI.Stack(UI.Text(mission.Text("name"), 17, "Ink", true), Limit(UI.Text(CatalogStore.Plain(mission.Text("reward")), 13, "Green"), 325, 125));
                        if (mission.Text("failure").Length > 0) task.Children.Add(Limit(UI.Text(CatalogStore.Plain(mission.Text("failure")), 12, "Pink"), 325, 85));
                        task.Children.Add(UI.Text("原始配置 · 任务 #" + mission.Text("id"), 10, "Muted"));
                        missions.Children.Add(UI.Card(task));
                    }
                    if (missions.Children.Count > 0) UI.Section(content, "全队任务与奖励", missions);
                    var monsters = new WrapPanel();
                    foreach (var monster in d.Get("monsters").Rows()) monsters.Children.Add(UI.Card(UI.Stack(UI.Text(monster.Text("name"), 17, "Gold", true),
                        UI.Text($"基础生命 {monster.Number("baseHp")} · 攻击 {monster.Number("baseAttack")} · 防御 {monster.Number("baseDefense")}", 12, "Muted"),
                        Limit(UI.Text(CatalogStore.Plain(monster.Text("mechanism"))), 325, 200))));
                    if (monsters.Children.Count > 0) UI.Section(content, "常规刷怪池 · 基础属性不含难度与局内加成", monsters);
                    var events = new WrapPanel();
                    foreach (var e in d.Get("events").Rows()) events.Children.Add(UI.Card(UI.Stack(UI.Text(e.Text("name", "事件 #" + e.Text("id")), 17, "Gold", true), Limit(UI.Text(CatalogStore.Plain(e.Text("description"))), 325, 220))));
                    if (events.Children.Count > 0) UI.Section(content, "进度事件", events);
                }
                else content.Children.Add(UI.Empty("本图暂无合作难度配置", "可查看地图预览及现有专属卡牌。"));
                var special = new WrapPanel();
                foreach (var id in map.Get("specialCardIds").Rows()) { var card = Catalog.Find("cards", id.Number()); if (card.ValueKind == JsonValueKind.Object) special.Children.Add(CatalogButton(card, "cards", [card])); }
                if (special.Children.Count > 0) UI.Section(content, "专属卡牌与建筑", special);
                content.Children.Add(UI.Text("路线、落点及行动顺序需要逐图核对；不会沿用森秋幻境的格子标记。", 12, "Muted"));
            }
        }
        foreach (var (key, title) in new[] { ("quick", "实战速查"), ("official", "配置与任务"), ("map", "地图预览") })
        { var button = UI.Button(title, () => { active = key; Render(); }); buttons[key] = button; nav.Children.Add(button); }
        difficulty.SelectionChanged += (_, _) => Render(); Render(); return root;
    }
    private static void RenderCommunityGuide(Panel content, JsonElement guide)
    {
        content.Children.Add(UI.Card(UI.Stack(UI.Text("实战速查 · " + guide.Text("version"), 20, "Gold", true),
            UI.Text(guide.Text("source") + " · " + guide.Text("asOf"), 12, "Muted"), UI.Text(string.Join("\n", guide.Get("notes").Rows().Select(r => r.Text())), 12, "Muted"))));
        var order = new WrapPanel(); foreach (var m in guide.Get("actionOrder").Rows()) order.Children.Add(UI.Badge(m.Text("priority") + "  " + m.Text("name") + "  →", "Green"));
        UI.Section(content, "怪物行动顺序 · 数值越小越先动", order);
        var timeline = new WrapPanel(); foreach (var e in guide.Get("timeline").Rows()) timeline.Children.Add(UI.Card(UI.Stack(UI.Badge("进度 " + e.Text("progress"), "Pink"), UI.Text(e.Text("name"), 19, "Ink", true), Limit(UI.Text(e.Text("description")), 325, 170))));
        UI.Section(content, "团队进度与刷怪", timeline);
        var buildings = new WrapPanel(); foreach (var b in guide.Get("buildings").Rows()) buildings.Children.Add(UI.Card(UI.Stack(UI.Text(b.Text("name") + "  " + b.Text("chance") + "%", 17, "Green", true), Limit(UI.Text(b.Text("effect")), 160, 110))));
        UI.Section(content, "建筑牌速查", buildings);
        var tasks = new WrapPanel(); foreach (var task in guide.Get("missions").Rows()) tasks.Children.Add(UI.Card(UI.Stack(UI.Text(task.Text("name"), 17, "Ink", true), Limit(UI.Text(task.Text("reward")), 325, 100),
            UI.Text("蓝 / 紫 / 金：" + string.Join(" / ", task.Get("rarities").Rows().Select(r => r.Text() + "%")), 12, "Gold"))));
        UI.Section(content, "全队任务与奖励", tasks);
        UI.Section(content, "召唤与行动提示", UI.Card(UI.Text(string.Join("\n", guide.Get("summoning").Rows().Select(r => "• " + r.Text())))));
        UI.Section(content, "路线要点", UI.Card(UI.Text(string.Join("\n", guide.Get("routes").Rows().Select(r => "• " + r.Text())))));
    }
}
