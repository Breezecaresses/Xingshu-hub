using System;

namespace XingShuRuntime.DataService;

/// <summary>
/// 纯 CLR 卡牌快照（不可变 DTO）。
/// 绝对不包含任何 UnityEngine / Protobuf 对象。
/// </summary>
public sealed class CardSnapshot
{
	public int CardId { get; }
	public string Name { get; }
	public int Cost { get; }
	public bool Unknown { get; }

	public CardSnapshot(int cardId, string name, int cost)
	{
		CardId = cardId;
		Name = name ?? "";
		Cost = cost;
		Unknown = false;
	}

	public static CardSnapshot CreateUnknown()
	{
		return new CardSnapshot();
	}

	private CardSnapshot()
	{
		CardId = -1;
		Name = "未知卡牌";
		Cost = 0;
		Unknown = true;
	}
}
