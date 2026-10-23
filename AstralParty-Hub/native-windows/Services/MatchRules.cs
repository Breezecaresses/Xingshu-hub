using System.Text.Json;

namespace XingShu.Native.Services;

public static class MatchRules
{
    public const string RatingMethod = "星枢评分 v2：仅比较同一局玩家。取综合分、输出分、辅助分中的最高分，再除以（1＋死亡次数 × 0.15）。综合分权重：输出 30%、治疗 20%、击杀 15%、转账 30%、累计金币 5%；输出分：输出 75%、击杀 25%。全局为零的项目移除权重。辅助分：min（转账 ÷ 20，1）×（6＋2 × 转账比例＋2 × 治疗比例）。结算金币与移动步数只展示，不参与评分。缺少完整回放数据时不评分。";
    public static double?[] Ratings(JsonElement[] players)
    {
        string[] required = ["kills", "deaths", "damage", "healing", "coins", "transfer"];
        if (players.Length == 0 || players.Any(p => required.Any(k => p.Get(k).ValueKind != JsonValueKind.Number || !p.Get(k).TryGetDouble(out var v) || !double.IsFinite(v) || v < 0))) return players.Select(_ => (double?)null).ToArray();
        string[] keys = ["damage", "healing", "kills", "transfer", "coins"];
        var rows = players.Select(p => keys.Select(k => p.Get(k).GetDouble()).ToArray()).ToArray();
        var max = Enumerable.Range(0, 5).Select(i => rows.Max(r => r[i])).ToArray();
        double[] weights = [.3, .2, .15, .3, .05];
        var active = Enumerable.Range(0, 5).Sum(i => max[i] > 0 ? weights[i] : 0);
        if (active == 0) return players.Select(_ => (double?)null).ToArray();
        var offenseActive = (max[0] > 0 ? .75 : 0) + (max[2] > 0 ? .25 : 0);
        return rows.Select((r, j) => {
            var ratios = Enumerable.Range(0, 5).Select(i => max[i] > 0 ? r[i] / max[i] : 0).ToArray();
            var balanced = 10 * Enumerable.Range(0, 5).Sum(i => weights[i] * ratios[i]) / active;
            var offense = offenseActive > 0 ? 10 * (.75 * ratios[0] + .25 * ratios[2]) / offenseActive : 0;
            var support = Math.Min(players[j].Get("transfer").GetDouble() / 20, 1) * (6 + 2 * ratios[3] + 2 * ratios[1]);
            return (double?)Math.Round(Math.Max(balanced, Math.Max(offense, support)) / (1 + players[j].Get("deaths").GetDouble() * .15), 2, MidpointRounding.AwayFromZero);
        }).ToArray();
    }
    public static string Result(JsonElement record, CatalogStore catalog)
    {
        var group = catalog.Find("modes", record.Number("mapId")).Number("group"); var rank = record.Number("rank");
        return rank <= 0 || group is not (1 or 2) ? "—" : group == 1 ? rank == 1 ? "胜利" : "失败" : "#" + rank;
    }
    public static bool IsKnown(JsonElement record, CatalogStore catalog) => catalog.Find("modes", record.Number("mapId")).Number("group") is 1 or 2 && record.Number("rank") > 0;
}
