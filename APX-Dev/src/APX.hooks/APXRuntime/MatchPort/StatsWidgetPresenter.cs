using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace APXRuntime.MatchPort;

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
		statsWidgetFrame.Status = ((!enabled) ? "已关闭" : ((ledger.Key == null) ? "等待对局" : (statsWidgetFrame.Partial ? "部分数据" : "观察中")));
		statsWidgetFrame.Footer = ((!enabled) ? "采集已关闭 · 设置中可重新开启" : ((ledger.Key == null) ? "等待有效对局 · 仅观察公开事件" : (since.ToString("HH:mm") + " 起 · 观察累计，非服务器结算")));
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
