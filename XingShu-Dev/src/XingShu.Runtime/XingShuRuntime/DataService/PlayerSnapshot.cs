using System;

namespace XingShuRuntime.DataService;

/// <summary>
/// 纯 CLR 玩家综合快照（不可变 DTO）。
/// 包含基础信息、存活 HP、手牌快照及单体统计快照。
/// </summary>
public sealed class PlayerSnapshot
{
	public long PlayerId { get; }
	public int Slot { get; }
	public string Name { get; }
	public string Character { get; }
	public bool Alive { get; }
	public int Hp { get; }
	public int MaxHp { get; }
	public bool IsSelf { get; }
	public bool IsBot { get; }
	public HandSnapshot Hand { get; }
	public PlayerStatsSnapshot Stats { get; }

	public PlayerSnapshot(
		long playerId,
		int slot,
		string name,
		string character,
		bool alive,
		int hp,
		int maxHp,
		bool isSelf,
		bool isBot,
		HandSnapshot hand,
		PlayerStatsSnapshot stats)
	{
		PlayerId = playerId;
		Slot = slot;
		Name = name ?? "";
		Character = character ?? "";
		Alive = alive;
		Hp = hp;
		MaxHp = maxHp;
		IsSelf = isSelf;
		IsBot = isBot;
		Hand = hand;
		Stats = stats;
	}
}
