using System;

namespace APXRuntime.DataService;

/// <summary>
/// 纯 CLR 玩家单体统计快照（不可变 DTO）。
/// 包含伤害、击倒、步数、转账与来源可信度标志。
/// </summary>
public sealed class PlayerStatsSnapshot
{
	public long PlayerId { get; }
	public int Slot { get; }
	public string Name { get; }
	public string Character { get; }
	public long Damage { get; }
	public long Kills { get; }
	public long Movement { get; }
	public long Transferred { get; }
	public bool HpReady { get; }
	public bool MovementReady { get; }
	public bool TransferReady { get; }
	public bool DamageKnown { get; }
	public bool KillsKnown { get; }
	public bool MovementKnown { get; }
	public bool TransferKnown { get; }

	public PlayerStatsSnapshot(
		long playerId,
		int slot,
		string name,
		string character,
		long damage,
		long kills,
		long movement,
		long transferred,
		bool hpReady,
		bool movementReady,
		bool transferReady,
		bool damageKnown,
		bool killsKnown,
		bool movementKnown,
		bool transferKnown)
	{
		PlayerId = playerId;
		Slot = slot;
		Name = name ?? "";
		Character = character ?? "";
		Damage = damage;
		Kills = kills;
		Movement = movement;
		Transferred = transferred;
		HpReady = hpReady;
		MovementReady = movementReady;
		TransferReady = transferReady;
		DamageKnown = damageKnown;
		KillsKnown = killsKnown;
		MovementKnown = movementKnown;
		TransferKnown = transferKnown;
	}
}
