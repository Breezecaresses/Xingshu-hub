using System;
using System.Collections.Generic;

namespace XingShuRuntime.MatchPort;

internal sealed class MovementTracker
{
	internal struct State
	{
		internal object Actor;

		internal int Land;

		internal int Steps;

		internal int? Next;

		internal bool Valid;
	}

	private sealed class Pending
	{
		internal long Player;

		internal long Created;

		internal int Target;

		internal State Before;
	}

	private readonly Dictionary<long, long> _totals = new Dictionary<long, long>();

	private readonly List<Pending> _pending = new List<Pending>();

	internal long Signals;

	internal long Confirmed;

	internal long Unconfirmed;

	internal bool Incomplete;

	internal IEnumerable<KeyValuePair<long, long>> Totals => _totals;

	internal int PendingCount => _pending.Count;

	internal void Register(long id)
	{
		if (id > 0 && !_totals.ContainsKey(id))
		{
			if (_totals.Count < 16)
			{
				_totals.Add(id, 0L);
			}
			else
			{
				Incomplete = true;
			}
		}
	}

	internal bool Knows(long id)
	{
		return _totals.ContainsKey(id);
	}

	internal void Observe(long id, int target, State before, long now)
	{
		if (!Knows(id))
		{
			return;
		}
		Signals++;
		if (!before.Valid)
		{
			Miss();
		}
		else
		{
			if (before.Next != target || before.Land == target || before.Steps <= 0)
			{
				return;
			}
			foreach (Pending item in _pending)
			{
				if (item.Player == id && item.Before.Actor == before.Actor && item.Target == target && item.Before.Land == before.Land && item.Before.Steps == before.Steps)
				{
					return;
				}
			}
			if (_pending.Count >= 16)
			{
				Miss();
				return;
			}
			_pending.Add(new Pending
			{
				Player = id,
				Target = target,
				Before = before,
				Created = now
			});
		}
	}

	internal void Pump(Func<long, State> read, long now)
	{
		for (int num = _pending.Count - 1; num >= 0; num--)
		{
			Pending pending = _pending[num];
			State state;
			try
			{
				state = read(pending.Player);
			}
			catch (Exception)
			{
				Miss();
				_pending.RemoveAt(num);
				continue;
			}
			if (!state.Valid || state.Actor != pending.Before.Actor)
			{
				Miss();
				_pending.RemoveAt(num);
				continue;
			}
			bool flag = state.Land == pending.Target && state.Steps == pending.Before.Steps - 1;
			if (flag && (pending.Before.Steps != 1 || !state.Next.HasValue))
			{
				try
				{
					_totals[pending.Player] = checked(_totals[pending.Player] + 1);
					Confirmed++;
				}
				catch (OverflowException)
				{
					Miss();
				}
				_pending.RemoveAt(num);
			}
			else if (now - pending.Created >= 10000 || (!flag && (state.Land != pending.Before.Land || state.Steps != pending.Before.Steps)))
			{
				Miss();
				_pending.RemoveAt(num);
			}
		}
	}

	internal void Miss()
	{
		Unconfirmed++;
		Incomplete = true;
	}

	internal void DropPending()
	{
		if (_pending.Count > 0)
		{
			Unconfirmed += _pending.Count;
			Incomplete = true;
		}
		_pending.Clear();
	}

	internal void Clear()
	{
		_totals.Clear();
		_pending.Clear();
		Signals = (Confirmed = (Unconfirmed = 0L));
		Incomplete = false;
	}
}
