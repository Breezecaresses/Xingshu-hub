using System;

namespace APXRuntime.DataService;

/// <summary>
/// 纯 CLR 对局总账本快照（不可变 DTO）。
/// 包含全员统计明细、排位榜单与诊断异常列表。
/// </summary>
public sealed class StatsSnapshot
{
	private static readonly PlayerStatsSnapshot[] EmptyPlayerStats = new PlayerStatsSnapshot[0];
	private static readonly string[] EmptyIssues = new string[0];

	public string MatchKey { get; }
	public PlayerStatsSnapshot[] PlayerStats { get; }
	public string[] Issues { get; }
	public PlayerStatsSnapshot[] DamageRankings { get; }
	public PlayerStatsSnapshot[] KnockdownRankings { get; }

	public StatsSnapshot(string matchKey, PlayerStatsSnapshot[] playerStats, string[] issues)
	{
		MatchKey = matchKey ?? "";
		PlayerStats = playerStats ?? EmptyPlayerStats;
		Issues = issues ?? EmptyIssues;

		// 构造伤害降序排行榜不可变数组
		PlayerStatsSnapshot[] dmg = new PlayerStatsSnapshot[PlayerStats.Length];
		Array.Copy(PlayerStats, dmg, PlayerStats.Length);
		Array.Sort(dmg, (a, b) => b.Damage.CompareTo(a.Damage));
		DamageRankings = dmg;

		// 构造击倒降序排行榜不可变数组
		PlayerStatsSnapshot[] kds = new PlayerStatsSnapshot[PlayerStats.Length];
		Array.Copy(PlayerStats, kds, PlayerStats.Length);
		Array.Sort(kds, (a, b) => b.Kills.CompareTo(a.Kills));
		KnockdownRankings = kds;
	}

	public static StatsSnapshot CreateEmpty()
	{
		return new StatsSnapshot("", EmptyPlayerStats, EmptyIssues);
	}
}
