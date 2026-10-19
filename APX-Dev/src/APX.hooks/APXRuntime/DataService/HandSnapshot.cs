using System;
using System.Globalization;

namespace APXRuntime.DataService;

/// <summary>
/// 纯 CLR 手牌快照（不可变 DTO）。
/// 严格遵守生产硬约束：区分数据来源（runtime-live / server-authoritative / unknown），
/// 远端服务失效时强制降级为 unknown，绝不允许陈旧快照冒充实时手牌。
/// </summary>
public sealed class HandSnapshot
{
	private static readonly CardSnapshot[] EmptyCards = new CardSnapshot[0];

	public long PlayerId { get; }
	public string Source { get; }
	public bool Authoritative { get; }
	public string Status { get; }
	public int? CardCount { get; }
	public CardSnapshot[] Cards { get; }
	public string UpdatedAt { get; }

	public HandSnapshot(long playerId, string source, bool authoritative, string status, int? cardCount, CardSnapshot[] cards, string updatedAt)
	{
		PlayerId = playerId;
		Source = source ?? "unknown";
		Authoritative = authoritative;
		Status = status ?? "unknown";
		CardCount = cardCount;
		Cards = cards ?? EmptyCards;
		UpdatedAt = updatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
	}

	public static HandSnapshot CreateSelfLive(long playerId, CardSnapshot[] cards, string updatedAt)
	{
		int count = cards != null ? cards.Length : 0;
		return new HandSnapshot(playerId, "runtime-live", true, "ready", count, cards, updatedAt);
	}

	public static HandSnapshot CreateServerAuthoritative(long playerId, CardSnapshot[] cards, string updatedAt)
	{
		int count = cards != null ? cards.Length : 0;
		return new HandSnapshot(playerId, "server-authoritative", true, "ready", count, cards, updatedAt);
	}

	public static HandSnapshot CreateUnknown(long playerId, int? count, string status, string updatedAt)
	{
		CardSnapshot[] cards;
		if (count.HasValue && count.Value > 0)
		{
			cards = new CardSnapshot[count.Value];
			for (int i = 0; i < count.Value; i++)
			{
				cards[i] = CardSnapshot.CreateUnknown();
			}
		}
		else
		{
			cards = EmptyCards;
		}

		return new HandSnapshot(playerId, "unknown", false, status ?? "unavailable", count, cards, updatedAt);
	}
}
