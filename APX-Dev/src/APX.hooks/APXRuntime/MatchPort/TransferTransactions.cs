using System.Collections.Generic;

namespace APXRuntime.MatchPort;

internal sealed class TransferTransactions
{
	internal sealed class Candidate
	{
		internal long Player;

		internal long Sn;

		internal long Order;

		internal int Round;

		internal object Payload;

		internal bool PurchaseAcknowledged;
	}

	internal sealed class Context
	{
		internal readonly List<Candidate> Executed = new List<Candidate>();

		internal Candidate Only
		{
			get
			{
				if (Executed.Count != 1)
				{
					return null;
				}
				return Executed[0];
			}
		}
	}

	internal enum Result
	{
		Ignored,
		Duplicate,
		Unknown,
		Confirmed
	}

	internal delegate bool ReadFields(object payload, out int amount, out long assisted);

	private readonly List<Candidate> _pending = new List<Candidate>();

	private readonly HashSet<long> _consumed = new HashSet<long>();

	private readonly HashSet<long> _seenDownSn = new HashSet<long>();

	private readonly Queue<object> _seenObjects = new Queue<object>();

	private long _order;

	internal int Incomplete { get; private set; }

	internal int PendingCount => _pending.Count;

	internal void Clear()
	{
		_pending.Clear();
		_consumed.Clear();
		_seenDownSn.Clear();
		_seenObjects.Clear();
		_order = 0L;
		Incomplete = 0;
	}

	internal void DropPending()
	{
		_pending.Clear();
	}

	internal void Register(long player, long sn, int round, object payload)
	{
		if (player <= 0 || sn <= 0 || payload == null || _consumed.Contains(sn))
		{
			return;
		}
		foreach (Candidate item in _pending)
		{
			if (item.Sn == sn)
			{
				return;
			}
		}
		if (_pending.Count >= 64)
		{
			_pending.RemoveAt(0);
			Incomplete++;
		}
		_pending.Add(new Candidate
		{
			Player = player,
			Sn = sn,
			Round = round,
			Payload = payload,
			Order = ++_order
		});
	}

	internal Context Begin(long player, int round, HashSet<long> queued)
	{
		Context result = new Context();
		if (queued == null)
		{
			return result;
		}
		_pending.RemoveAll((Candidate c) => c.Round < round);
		foreach (Candidate item in _pending)
		{
			if (item.Player == player && item.Round == round && !queued.Contains(item.Sn) && !_consumed.Contains(item.Sn))
			{
				result.Executed.Add(item);
			}
		}
		if (result.Executed.Count > 1)
		{
			long latestOrder = 0L;
			foreach (Candidate item2 in result.Executed)
			{
				if (item2.Order > latestOrder)
				{
					latestOrder = item2.Order;
				}
			}
			_pending.RemoveAll((Candidate c) => result.Executed.Contains(c) && c.PurchaseAcknowledged && c.Order < latestOrder);
			result.Executed.RemoveAll((Candidate c) => c.PurchaseAcknowledged && c.Order < latestOrder);
		}
		return result;
	}

	internal Result Acknowledge(Context context, object ack, long downSn, long recipient, bool isClose, bool hasBoughtCards, ReadFields read, out long shopSn, out int amount)
	{
		shopSn = 0L;
		amount = 0;
		foreach (object seenObject in _seenObjects)
		{
			if (seenObject == ack)
			{
				return Result.Duplicate;
			}
		}
		if (downSn > 0 && _seenDownSn.Contains(downSn))
		{
			return Result.Duplicate;
		}
		if (_seenDownSn.Count >= 4096 || _consumed.Count >= 4096)
		{
			Incomplete++;
			return Result.Unknown;
		}
		if (_seenObjects.Count >= 256)
		{
			_seenObjects.Dequeue();
		}
		_seenObjects.Enqueue(ack);
		if (downSn > 0)
		{
			_seenDownSn.Add(downSn);
		}
		else
		{
			Incomplete++;
		}
		if (context == null)
		{
			Incomplete++;
			return Result.Unknown;
		}
		if (isClose)
		{
			foreach (Candidate item in context.Executed)
			{
				_pending.Remove(item);
			}
			return Result.Ignored;
		}
		Candidate only = context.Only;
		if (recipient <= 0)
		{
			if (hasBoughtCards && only != null)
			{
				only.PurchaseAcknowledged = true;
			}
			return Result.Ignored;
		}
		if (hasBoughtCards || only == null || only.Player == recipient)
		{
			Incomplete++;
			return Result.Unknown;
		}
		if (_consumed.Contains(only.Sn))
		{
			return Result.Duplicate;
		}
		if (!read(only.Payload, out amount, out var assisted) || amount <= 0 || assisted != 0L)
		{
			amount = 0;
			Incomplete++;
			return Result.Unknown;
		}
		shopSn = only.Sn;
		_consumed.Add(only.Sn);
		_pending.Remove(only);
		return Result.Confirmed;
	}
}
