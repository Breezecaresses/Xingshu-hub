using System;
using System.Globalization;
using System.Text;

namespace APXRuntime.DataService;

/// <summary>
/// 针对 .NET Framework 4.0 的零依赖、线程安全、高性能 JSON 输出器。
/// 严格处理 Unicode、控制字符、引号转义，杜绝未转义字符串注入。
/// </summary>
public static class SimpleJsonWriter
{
	public static void EscapeString(string s, StringBuilder sb)
	{
		if (s == null)
		{
			sb.Append("null");
			return;
		}

		sb.Append('"');
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			switch (c)
			{
				case '\\':
					sb.Append("\\\\");
					break;
				case '\"':
					sb.Append("\\\"");
					break;
				case '\b':
					sb.Append("\\b");
					break;
				case '\f':
					sb.Append("\\f");
					break;
				case '\n':
					sb.Append("\\n");
					break;
				case '\r':
					sb.Append("\\r");
					break;
				case '\t':
					sb.Append("\\t");
					break;
				default:
					if (c < 32 || c == 127)
					{
						sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
					}
					else
					{
						sb.Append(c);
					}
					break;
			}
		}
		sb.Append('"');
	}

	public static string WriteEnvelopeSuccess(string dataJson, string source, bool authoritative, string updatedAt)
	{
		StringBuilder sb = new StringBuilder(dataJson.Length + 160);
		sb.Append("{\"ok\":true,\"data\":");
		sb.Append(dataJson);
		sb.Append(",\"meta\":{\"updatedAt\":");
		EscapeString(updatedAt, sb);
		sb.Append(",\"source\":");
		EscapeString(source, sb);
		sb.Append(",\"authoritative\":");
		sb.Append(authoritative ? "true" : "false");
		sb.Append("}}");
		return sb.ToString();
	}

	public static string WriteEnvelopeError(string code, string message)
	{
		StringBuilder sb = new StringBuilder(256);
		sb.Append("{\"ok\":false,\"error\":{\"code\":");
		EscapeString(code, sb);
		sb.Append(",\"message\":");
		EscapeString(message, sb);
		sb.Append("}}");
		return sb.ToString();
	}

	public static void WriteStatus(MatchDataSnapshot s, StringBuilder sb)
	{
		sb.Append("{\"gameRunning\":");
		sb.Append(s.GameRunning ? "true" : "false");
		sb.Append(",\"inMatch\":");
		sb.Append(s.InMatch ? "true" : "false");
		sb.Append(",\"roomId\":");
		EscapeString(s.RoomId, sb);
		sb.Append(",\"startTime\":");
		sb.Append(s.StartTime.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"round\":");
		sb.Append(s.Round.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"matchKey\":");
		EscapeString(s.MatchKey, sb);
		sb.Append(",\"playerCount\":");
		sb.Append((s.Players != null ? s.Players.Length : 0).ToString(CultureInfo.InvariantCulture));
		sb.Append('}');
	}

	public static void WritePlayers(PlayerSnapshot[] players, StringBuilder sb)
	{
		sb.Append('[');
		if (players != null)
		{
			for (int i = 0; i < players.Length; i++)
			{
				if (i > 0) sb.Append(',');
				WritePlayerBrief(players[i], sb);
			}
		}
		sb.Append(']');
	}

	public static void WritePlayerBrief(PlayerSnapshot p, StringBuilder sb)
	{
		sb.Append("{\"playerId\":");
		sb.Append(p.PlayerId.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"slot\":");
		sb.Append(p.Slot.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"name\":");
		EscapeString(p.Name, sb);
		sb.Append(",\"character\":");
		EscapeString(p.Character, sb);
		sb.Append(",\"alive\":");
		sb.Append(p.Alive ? "true" : "false");
		sb.Append(",\"hp\":");
		sb.Append(p.Hp.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"maxHp\":");
		sb.Append(p.MaxHp.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"isSelf\":");
		sb.Append(p.IsSelf ? "true" : "false");
		sb.Append(",\"isBot\":");
		sb.Append(p.IsBot ? "true" : "false");
		sb.Append('}');
	}

	public static void WritePlayerDetail(PlayerSnapshot p, StringBuilder sb)
	{
		sb.Append("{\"playerId\":");
		sb.Append(p.PlayerId.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"slot\":");
		sb.Append(p.Slot.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"name\":");
		EscapeString(p.Name, sb);
		sb.Append(",\"character\":");
		EscapeString(p.Character, sb);
		sb.Append(",\"alive\":");
		sb.Append(p.Alive ? "true" : "false");
		sb.Append(",\"hp\":");
		sb.Append(p.Hp.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"maxHp\":");
		sb.Append(p.MaxHp.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"isSelf\":");
		sb.Append(p.IsSelf ? "true" : "false");
		sb.Append(",\"isBot\":");
		sb.Append(p.IsBot ? "true" : "false");
		sb.Append(",\"damage\":");
		sb.Append(p.Stats != null ? p.Stats.Damage.ToString(CultureInfo.InvariantCulture) : "0");
		sb.Append(",\"kills\":");
		sb.Append(p.Stats != null ? p.Stats.Kills.ToString(CultureInfo.InvariantCulture) : "0");
		sb.Append(",\"movement\":");
		sb.Append(p.Stats != null ? p.Stats.Movement.ToString(CultureInfo.InvariantCulture) : "0");
		sb.Append(",\"transferred\":");
		sb.Append(p.Stats != null ? p.Stats.Transferred.ToString(CultureInfo.InvariantCulture) : "0");
		sb.Append(",\"cardCount\":");
		if (p.Hand != null && p.Hand.CardCount.HasValue)
		{
			sb.Append(p.Hand.CardCount.Value.ToString(CultureInfo.InvariantCulture));
		}
		else
		{
			sb.Append("null");
		}
		sb.Append(",\"handAuthoritative\":");
		sb.Append((p.Hand != null && p.Hand.Authoritative) ? "true" : "false");
		sb.Append('}');
	}

	public static void WriteHand(HandSnapshot h, StringBuilder sb)
	{
		sb.Append("{\"playerId\":");
		sb.Append(h.PlayerId.ToString(CultureInfo.InvariantCulture));
		sb.Append(",\"source\":");
		EscapeString(h.Source, sb);
		sb.Append(",\"authoritative\":");
		sb.Append(h.Authoritative ? "true" : "false");
		sb.Append(",\"status\":");
		EscapeString(h.Status, sb);
		sb.Append(",\"cardCount\":");
		if (h.CardCount.HasValue)
		{
			sb.Append(h.CardCount.Value.ToString(CultureInfo.InvariantCulture));
		}
		else
		{
			sb.Append("null");
		}
		sb.Append(",\"cards\":[");
		if (h.Cards != null)
		{
			for (int i = 0; i < h.Cards.Length; i++)
			{
				if (i > 0) sb.Append(',');
				CardSnapshot c = h.Cards[i];
				if (c.Unknown)
				{
					sb.Append("{\"unknown\":true}");
				}
				else
				{
					sb.Append("{\"cardId\":");
					sb.Append(c.CardId.ToString(CultureInfo.InvariantCulture));
					sb.Append(",\"name\":");
					EscapeString(c.Name, sb);
					sb.Append(",\"cost\":");
					sb.Append(c.Cost.ToString(CultureInfo.InvariantCulture));
					sb.Append(",\"unknown\":false}");
				}
			}
		}
		sb.Append("]}");
	}

	public static void WriteStats(StatsSnapshot s, StringBuilder sb)
	{
		sb.Append("{\"matchKey\":");
		EscapeString(s.MatchKey, sb);
		sb.Append(",\"issues\":[");
		if (s.Issues != null)
		{
			for (int i = 0; i < s.Issues.Length; i++)
			{
				if (i > 0) sb.Append(',');
				EscapeString(s.Issues[i], sb);
			}
		}
		sb.Append("],\"players\":[");
		if (s.PlayerStats != null)
		{
			for (int i = 0; i < s.PlayerStats.Length; i++)
			{
				if (i > 0) sb.Append(',');
				PlayerStatsSnapshot row = s.PlayerStats[i];
				sb.Append("{\"playerId\":");
				sb.Append(row.PlayerId.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"slot\":");
				sb.Append(row.Slot.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"name\":");
				EscapeString(row.Name, sb);
				sb.Append(",\"character\":");
				EscapeString(row.Character, sb);
				sb.Append(",\"damage\":");
				sb.Append(row.Damage.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"kills\":");
				sb.Append(row.Kills.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"movement\":");
				sb.Append(row.Movement.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"transferred\":");
				sb.Append(row.Transferred.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"hpReady\":");
				sb.Append(row.HpReady ? "true" : "false");
				sb.Append(",\"movementReady\":");
				sb.Append(row.MovementReady ? "true" : "false");
				sb.Append(",\"transferReady\":");
				sb.Append(row.TransferReady ? "true" : "false");
				sb.Append(",\"damageKnown\":");
				sb.Append(row.DamageKnown ? "true" : "false");
				sb.Append(",\"killsKnown\":");
				sb.Append(row.KillsKnown ? "true" : "false");
				sb.Append(",\"movementKnown\":");
				sb.Append(row.MovementKnown ? "true" : "false");
				sb.Append(",\"transferKnown\":");
				sb.Append(row.TransferKnown ? "true" : "false");
				sb.Append('}');
			}
		}
		sb.Append("]}");
	}

	public static void WriteDamageRankings(PlayerStatsSnapshot[] rankings, StringBuilder sb)
	{
		sb.Append('[');
		if (rankings != null)
		{
			for (int i = 0; i < rankings.Length; i++)
			{
				if (i > 0) sb.Append(',');
				PlayerStatsSnapshot r = rankings[i];
				sb.Append("{\"rank\":");
				sb.Append((i + 1).ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"playerId\":");
				sb.Append(r.PlayerId.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"slot\":");
				sb.Append(r.Slot.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"name\":");
				EscapeString(r.Name, sb);
				sb.Append(",\"character\":");
				EscapeString(r.Character, sb);
				sb.Append(",\"damage\":");
				sb.Append(r.Damage.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"damageKnown\":");
				sb.Append(r.DamageKnown ? "true" : "false");
				sb.Append('}');
			}
		}
		sb.Append(']');
	}

	public static void WriteKnockdownRankings(PlayerStatsSnapshot[] rankings, StringBuilder sb)
	{
		sb.Append('[');
		if (rankings != null)
		{
			for (int i = 0; i < rankings.Length; i++)
			{
				if (i > 0) sb.Append(',');
				PlayerStatsSnapshot r = rankings[i];
				sb.Append("{\"rank\":");
				sb.Append((i + 1).ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"playerId\":");
				sb.Append(r.PlayerId.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"slot\":");
				sb.Append(r.Slot.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"name\":");
				EscapeString(r.Name, sb);
				sb.Append(",\"character\":");
				EscapeString(r.Character, sb);
				sb.Append(",\"kills\":");
				sb.Append(r.Kills.ToString(CultureInfo.InvariantCulture));
				sb.Append(",\"killsKnown\":");
				sb.Append(r.KillsKnown ? "true" : "false");
				sb.Append('}');
			}
		}
		sb.Append(']');
	}
}
