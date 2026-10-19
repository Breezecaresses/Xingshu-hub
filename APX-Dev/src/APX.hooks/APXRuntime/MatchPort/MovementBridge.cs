using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Core.Unit;
using GameLogic;
using Tools;
using party.model;

namespace APXRuntime.MatchPort;

internal static class MovementBridge
{
	private static readonly FieldInfo MoveField = typeof(Character).GetField("move", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo NextField = typeof(CharacterMove).GetField("nextStandLand", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MovementTracker Tracker = new MovementTracker();

	private static BattleLogic _battle;

	private static Signal<long, int> _signal;

	private static Action<long, int> _listener;

	private static long _room;

	private static long _start;

	private static long _generation;

	private static bool _installed;

	internal static string Status = "not_installed";

	private static long Now => (long)((double)Stopwatch.GetTimestamp() * (1000.0 / (double)Stopwatch.Frequency));

	internal static bool Available => _installed;

	internal static IEnumerable<KeyValuePair<long, long>> Totals => Tracker.Totals;

	internal static bool Incomplete => Tracker.Incomplete;

	internal static long Unconfirmed => Tracker.Unconfirmed;

	internal static bool Attach()
	{
		try
		{
			if (!Context(out var room, out var battle))
			{
				Detach();
				Status = "waiting_match";
				return false;
			}
			if (MoveField == null || MoveField.FieldType != typeof(CharacterMove) || NextField == null || NextField.FieldType != typeof(UnitLand))
			{
				Status = "movement_fields_unavailable";
				return false;
			}
			if (_room != room.info.Id || _start != room.info.StartTime)
			{
				Detach();
				Tracker.Clear();
				_room = room.info.Id;
				_start = room.info.StartTime;
			}
			foreach (RoomPlayer player in room.Players)
			{
				if (player != null && player.Slot >= 0 && player.Slot <= 3)
				{
					Tracker.Register(player.Id);
				}
			}
			Signal<long, int> signal = ((battle.signal == null) ? null : battle.signal.minimapMove);
			if (signal == null)
			{
				Detach();
				Status = "waiting_movement_signal";
				return false;
			}
			if (_installed && _signal == signal && _battle == battle)
			{
				signal.AddListener(_listener);
				Status = "installed";
				return true;
			}
			if (_installed)
			{
				Tracker.Incomplete = true;
			}
			Detach();
			_battle = battle;
			_signal = signal;
			long generation = ++_generation;
			_listener = delegate(long id, int land)
			{
				if (!_installed || generation != _generation || !Tracker.Knows(id))
				{
					return;
				}
				try
				{
					if (SameSession())
					{
						Tracker.Pump(ReadState, Now);
						Tracker.Observe(id, land, ReadState(id), Now);
					}
				}
				catch (Exception ex2)
				{
					Tracker.Miss();
					Status = "movement_callback_failed:" + Describe(ex2);
				}
			};
			_signal.AddListener(_listener);
			_installed = true;
			Status = "installed";
			return true;
		}
		catch (Exception ex)
		{
			Detach();
			Status = "movement_install_failed:" + Describe(ex);
			return false;
		}
	}

	internal static void Pump()
	{
		if (!_installed)
		{
			return;
		}
		try
		{
			if (!SameSession())
			{
				Detach();
				Status = "movement_context_changed";
			}
			else
			{
				Tracker.Pump(ReadState, Now);
			}
		}
		catch (Exception ex)
		{
			Tracker.Incomplete = true;
			Status = "movement_pump_failed:" + Describe(ex);
		}
	}

	private static bool Context(out RoomInfo room, out BattleLogic battle)
	{
		GameLogicManager inst = SimpleSingletonProvider<GameLogicManager>.inst;
		room = ((inst == null || inst.room == null) ? null : inst.room.curRoomInfo);
		battle = inst?.battle;
		if (room != null && room.info != null && room.State == Room.Types.State.Running && room.info.Id > 0 && room.info.StartTime > 0)
		{
			return battle != null;
		}
		return false;
	}

	private static bool SameSession()
	{
		if (Context(out var room, out var battle) && room.info.Id == _room && room.info.StartTime == _start && battle == _battle && battle.signal != null)
		{
			return battle.signal.minimapMove == _signal;
		}
		return false;
	}

	private static MovementTracker.State ReadState(long id)
	{
		foreach (BattlePlayerData playerData in _battle.PlayerDatas)
		{
			if (playerData != null && playerData.player != null && playerData.player.Id == id)
			{
				Character characterInst = playerData.CharacterInst;
				if (characterInst == null || characterInst.standLand == null)
				{
					return default(MovementTracker.State);
				}
				if (!(MoveField.GetValue(characterInst) is CharacterMove obj))
				{
					return default(MovementTracker.State);
				}
				UnitLand unitLand = NextField.GetValue(obj) as UnitLand;
				MovementTracker.State result = default(MovementTracker.State);
				result.Actor = characterInst;
				result.Land = characterInst.standLand.Id;
				result.Steps = characterInst.canStep;
				result.Next = ((unitLand == null) ? null : new int?(unitLand.Id));
				result.Valid = true;
				return result;
			}
		}
		return default(MovementTracker.State);
	}

	internal static void Reset()
	{
		Detach();
		Tracker.Clear();
		_room = (_start = 0L);
	}

	internal static string SnapshotJson()
	{
		Pump();
		StringBuilder stringBuilder = new StringBuilder(1024);
		stringBuilder.Append("{\"status\":\"").Append(Escape(Status)).Append("\",\"available\":")
			.Append(_installed ? "true" : "false")
			.Append(",\"roomId\":")
			.Append(N(_room))
			.Append(",\"startTime\":")
			.Append(N(_start))
			.Append(",\"signals\":")
			.Append(N(Tracker.Signals))
			.Append(",\"confirmed\":")
			.Append(N(Tracker.Confirmed))
			.Append(",\"unconfirmed\":")
			.Append(N(Tracker.Unconfirmed))
			.Append(",\"pending\":")
			.Append(N(Tracker.PendingCount))
			.Append(",\"incomplete\":")
			.Append(Tracker.Incomplete ? "true" : "false")
			.Append(",\"players\":[");
		bool flag = true;
		foreach (KeyValuePair<long, long> total in Tracker.Totals)
		{
			if (!flag)
			{
				stringBuilder.Append(',');
			}
			flag = false;
			stringBuilder.Append("{\"playerId\":").Append(N(total.Key)).Append(",\"steps\":")
				.Append(N(total.Value))
				.Append('}');
		}
		return stringBuilder.Append("]}").ToString();
	}

	internal static void Detach()
	{
		_installed = false;
		_generation++;
		if (_signal != null && _listener != null)
		{
			try
			{
				_signal.RemoveListener(_listener);
			}
			catch (Exception)
			{
				Tracker.Incomplete = true;
			}
		}
		_signal = null;
		_listener = null;
		_battle = null;
		Tracker.DropPending();
		Status = "detached";
	}

	private static string N(long value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}

	private static string Escape(string value)
	{
		return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ")
			.Replace("\n", " ");
	}

	private static string Describe(Exception ex)
	{
		Exception baseException = ex.GetBaseException();
		string text = baseException.GetType().Name + ":" + baseException.Message;
		if (text.Length > 240)
		{
			return text.Substring(0, 240);
		}
		return text;
	}
}
