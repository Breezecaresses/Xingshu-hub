using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XingShuRuntime.MatchPort;

internal static class StatsWidgetPresenter
{
	internal static string Number(long value, bool ready, bool known)
	{
		if (!ready || !known || value < 0)
		{
			return "—";
		}
		if (value < 10000)
		{
			return value.ToString("N0", CultureInfo.InvariantCulture);
		}
		if (value >= 10000000000000000L)
		{
			return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
		}
		decimal num = ((value >= 1000000000000L) ? 1000000000000m : ((value >= 100000000) ? 100000000m : 10000m));
		string text = ((num == 10000m) ? "万" : ((num == 100000000m) ? "亿" : "万亿"));
		return (decimal.Truncate((decimal)value / num * 10m) / 10m).ToString("0.#", CultureInfo.InvariantCulture) + text;
	}

	internal static string Name(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "—";
		}
		name = name.Replace('\n', ' ').Replace('\r', ' ');
		int num = 0;
		int num2 = 0;
		int length = 0;
		while (num2 < name.Length)
		{
			char c = name[num2++];
			if (c >= '\ud800' && c <= '\udbff' && num2 < name.Length && name[num2] >= '\udc00' && name[num2] <= '\udfff')
			{
				num2++;
			}
			num++;
			if (num == 6)
			{
				length = num2;
			}
			if (num > 7)
			{
				return name.Substring(0, length) + "…";
			}
		}
		return name;
	}

	internal static StatsWidgetFrame Build(MatchLedger ledger, HashSet<long> live, bool enabled, DateTime since, string details)
	{
		StatsWidgetFrame statsWidgetFrame = new StatsWidgetFrame
		{
			Enabled = enabled,
			Partial = (ledger.Issues.Count > 0),
			Details = (details ?? "")
		};
		StringBuilder stringBuilder = new StringBuilder();
		var ratingInputs = new MatchRating.Input[4];
		bool settled = false;
		for (int i = 0; i < 4; i++)
		{
			MatchLedger.Row row = null;
			int num = 0;
			foreach (MatchLedger.Row value in ledger.Rows.Values)
			{
				if (value.Slot == i && live.Contains(value.Id))
				{
					row = value;
					num++;
				}
			}
			if (num != 1)
			{
				row = null;
			}
			statsWidgetFrame.Cells[i, 0] = ((row == null) ? "—" : Name(row.Name));
			statsWidgetFrame.Cells[i, 1] = ((row == null) ? "—" : Number(row.Kills, row.HpReady, row.KillsKnown));
			statsWidgetFrame.Cells[i, 2] = ((row == null) ? "—" : Number(row.Damage, row.HpReady, row.DamageKnown));
			statsWidgetFrame.Cells[i, 3] = ((row == null) ? "—" : Number(row.Movement, row.MovementReady, row.MovementKnown));
			statsWidgetFrame.Cells[i, 4] = ((row == null) ? "—" : Number(row.Transferred, row.TransferReady, row.TransferKnown));
			if (row != null)
			{
				settled |= row.Settled;
				var player = new StatsWidgetFrame.Player { Name = row.Name, Character = row.Character, HeadUrl = row.HeadUrl, State = row.Settled ? "已结算" : "实时参考" };
				long[] values = { row.Kills, row.Deaths, row.Damage, row.Taken, row.Healing, row.Coins, row.Transferred, row.Turns, row.Gold, row.Movement };
				bool[] known = { row.KillsKnown, true, row.DamageKnown, true, row.HealingKnown, true, row.TransferKnown, true, row.Settled, row.MovementKnown };
				for (int c = 0; c < values.Length; c++)
					player.Metrics[c] = Number(values[c], c == 9 ? row.MovementReady : row.MetricsReady, known[c]);
				statsWidgetFrame.Players[i] = player;
				if (row.MetricsReady && row.KillsKnown && row.DamageKnown && row.HealingKnown && row.TransferKnown)
					ratingInputs[i] = new MatchRating.Input { Kills = row.Kills, Deaths = row.Deaths, Damage = row.Damage, Healing = row.Healing, Coins = row.Coins, Transfer = row.Transferred };
			}
			for (int j = 1; j < 5; j++)
			{
				if (statsWidgetFrame.Cells[i, j] == "—")
				{
					statsWidgetFrame.Partial = true;
				}
			}
			if (row != null)
			{
				stringBuilder.Append("P").Append(i + 1).Append(' ')
					.Append(row.Name)
					.Append("\n");
				stringBuilder.Append(Exact(row.Kills, row.HpReady, row.KillsKnown)).Append(" / ").Append(Exact(row.Damage, row.HpReady, row.DamageKnown))
					.Append(" / ")
					.Append(Exact(row.Movement, row.MovementReady, row.MovementKnown))
					.Append(" / ")
					.Append(Exact(row.Transferred, row.TransferReady, row.TransferKnown))
					.Append("\n");
			}
		}
		var scores = MatchRating.Rate(ratingInputs);
		double best = 0;
		foreach (var score in scores) if (score.HasValue) best = Math.Max(best, score.Value);
		for (int i = 0; i < scores.Length; i++) if (statsWidgetFrame.Players[i] != null && scores[i].HasValue)
		{
			statsWidgetFrame.Players[i].Score = scores[i].Value.ToString("0.00", CultureInfo.InvariantCulture);
			statsWidgetFrame.Players[i].Mvp = best > 0 && scores[i].Value == best;
		}
		statsWidgetFrame.Status = !enabled ? "已关闭" : ledger.Key == null ? "等待对局" : settled ? "服务器结算 · 星枢评分 v2" : "实时参考 · 星枢评分 v2";
		statsWidgetFrame.Footer = !enabled ? "采集已关闭 · 设置中可重新开启" : ledger.Key == null ? "等待有效对局 · 缺失项显示 —" : settled ? "已按服务器结算校准 · 结算金币不参与评分" : since.ToString("HH:mm") + " 起 · 局内观察累计，结算后校准";
		statsWidgetFrame.Details = statsWidgetFrame.Details + "\n\n精确数值（击倒 / 伤害 / 步数 / 转出）\n" + stringBuilder;
		return statsWidgetFrame;
	}

	private static string Exact(long v, bool ready, bool known)
	{
		if (!(ready && known))
		{
			return "—";
		}
		return v.ToString(CultureInfo.InvariantCulture);
	}
}
