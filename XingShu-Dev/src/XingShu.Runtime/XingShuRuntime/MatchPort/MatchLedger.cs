using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace XingShuRuntime.MatchPort;

internal sealed class MatchLedger
{
	internal sealed class Row
	{
		internal long Id;

		internal long Kills;

		internal long Damage;

		internal long Deaths, Taken, Healing, Coins, Turns, Gold;
		internal string Character;
		internal string HeadUrl;
		internal bool MetricsReady, HealingKnown = true, Settled;

		internal long Movement;

		internal long Transferred;

		internal int Slot;

		internal string Name;

		internal bool HpReady;

		internal bool MovementReady;

		internal bool TransferReady;

		internal bool KillsKnown = true;

		internal bool DamageKnown = true;

		internal bool MovementKnown = true;

		internal bool TransferKnown = true;
	}

	private sealed class Identity : IEqualityComparer<object>
	{
		public new bool Equals(object a, object b)
		{
			return a == b;
		}

		public int GetHashCode(object value)
		{
			return RuntimeHelpers.GetHashCode(value);
		}
	}

	internal readonly Dictionary<long, Row> Rows = new Dictionary<long, Row>();

	internal readonly HashSet<string> Issues = new HashSet<string>();

	private readonly HashSet<object> _hpSeen = new HashSet<object>(new Identity());

	private readonly Queue<object> _hpOrder = new Queue<object>();

	private readonly HashSet<string> _transfers = new HashSet<string>();

	internal string Key { get; private set; }

	internal void Clear(string key)
	{
		Key = key;
		Rows.Clear();
		Issues.Clear();
		_hpSeen.Clear();
		_hpOrder.Clear();
		_transfers.Clear();
	}

	internal void Issue(string text)
	{
		if (Issues.Count < 32)
		{
			Issues.Add(text);
		}
	}

	internal void Register(long id, int slot, string name)
	{
		if (slot < 0 || slot > 3)
		{
			Issue("席位异常");
			return;
		}
		if (!Rows.TryGetValue(id, out var value))
		{
			if (Rows.Count >= 16)
			{
				Issue("身份数量超限");
				return;
			}
			value = new Row
			{
				Id = id
			};
			Rows[id] = value;
		}
		value.Slot = slot;
		value.Name = name;
	}

	internal void Hp(string key, object token, long victim, long killer, int actualDelta, int oldHp, int? currentHp)
	{
		if (Key == null || key != Key || token == null || _hpSeen.Contains(token))
		{
			return;
		}
		_hpSeen.Add(token);
		_hpOrder.Enqueue(token);
		if (_hpOrder.Count > 128)
		{
			_hpSeen.Remove(_hpOrder.Dequeue());
		}
		Row target;
		if (Rows.TryGetValue(victim, out target))
		{
			if (actualDelta < 0) target.Taken += -(long)actualDelta;
			if (oldHp > 0 && currentHp == 0) target.Deaths++;
		}
		if (actualDelta > 0)
		{
			Row healer;
			if (Rows.TryGetValue(killer, out healer)) healer.Healing += actualDelta;
			else foreach (var row in Rows.Values) row.HealingKnown = false;
			return;
		}
		if (actualDelta == 0 || killer == victim || !Rows.TryGetValue(killer, out var value))
		{
			return;
		}
		checked
		{
			try
			{
				value.Damage -= actualDelta;
			}
			catch (OverflowException)
			{
				value.DamageKnown = false;
				Issue("伤害溢出");
			}
			if (!currentHp.HasValue)
			{
				value.KillsKnown = false;
				Issue("击倒时目标 HP 不可确认");
			}
			else if (oldHp > 0 && currentHp.Value == 0)
			{
				try
				{
					value.Kills++;
				}
				catch (OverflowException)
				{
					value.KillsKnown = false;
					Issue("击倒计数溢出");
				}
			}
		}
	}

	internal void Movement(string key, long id, long total)
	{
		if (Key != null && !(Key != key) && total >= 0 && Rows.TryGetValue(id, out var value))
		{
			if (total < value.Movement)
			{
				Issue("步数来源回退");
				value.MovementKnown = false;
			}
			else
			{
				value.Movement = total;
				value.MovementReady = true;
			}
		}
	}

	internal void Transfer(string key, string uniqueId, long sender, long recipient, long amount)
	{
		if (Key == null || key != Key || string.IsNullOrEmpty(uniqueId) || uniqueId.Length > 160 || amount <= 0 || sender == recipient || !Rows.TryGetValue(sender, out var value) || !Rows.ContainsKey(recipient) || _transfers.Contains(uniqueId))
		{
			return;
		}
		if (_transfers.Count >= 4096)
		{
			value.TransferKnown = false;
			Issue("转账去重容量已满");
			return;
		}
		_transfers.Add(uniqueId);
		value.TransferReady = true;
		checked
		{
			try
			{
				value.Transferred += amount;
			}
			catch (OverflowException)
			{
				value.TransferKnown = false;
				Issue("转账金额溢出");
			}
		}
	}
}
