using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Core;
using Core.Net;
using Cysharp.Threading.Tasks;
using GameLogic;
using Tools;
using party.model;
using party.protocol;

namespace APXRuntime.MatchPort;

public static class TransferBridge
{
	private sealed class Candidate
	{
		internal long Player;

		internal long Sn;

		internal int Round;

		internal party.model.Action Value;
	}

	private sealed class Transfer
	{
		internal long Room;

		internal long Start;

		internal long Sender;

		internal long Recipient;

		internal long ShopSn;

		internal int Amount;
	}

	private sealed class Subscription
	{
		internal bool Active = true;

		internal RPCMsgManager Rpc;

		internal PredictActionS2CRPC.OnPredictActionS2CServerDelegate PreviousPredict;

		internal PredictActionS2CRPC.OnPredictActionS2CServerDelegate Predict;

		internal PVEShopBuyS2CRPC.OnPVEShopBuyS2CServerDelegate PreviousBuy;

		internal PVEShopBuyS2CRPC.OnPVEShopBuyS2CServerDelegate Buy;
	}

	internal delegate void TransferObserver(string key, string id, long sender, long recipient, long amount);

	private static Subscription _subscription;

	private static readonly Queue<Transfer> Events = new Queue<Transfer>();

	private static readonly TransferTransactions Transactions = new TransferTransactions();

	private static readonly FieldInfo QueuedActions = typeof(ActionListener).GetField("actionsList", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo QueuedMessages = typeof(RPCMsgManager).GetField("_OnPushRPCMsg", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo MessageLock = typeof(RPCMsgManager).GetField("_lockObj", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly Type MessageType = typeof(RPCMsgManager).Assembly.GetType("Core.Net.OnPushRPCMsgData");

	private static readonly PropertyInfo MessageCommand = MessageType?.GetProperty("CMDID");

	private static readonly PropertyInfo MessageDownSn = MessageType?.GetProperty("DOWNSN");

	private static readonly PropertyInfo MessageFrame = MessageType?.GetProperty("frame");

	private static long _room;

	private static long _start;

	private static long _generation;

	private static int _incomplete;

	public static string Status = "not_installed";

	internal static bool Incomplete
	{
		get
		{
			if (_incomplete <= 0)
			{
				return Transactions.Incomplete > 0;
			}
			return true;
		}
	}

	internal static bool Attach()
	{
		try
		{
			NetManager inst = MonoSingletonProvider<NetManager>.inst;
			RPCMsgManager rPCMsgManager = ((inst == null) ? null : inst.RPC);
			if (rPCMsgManager == null || rPCMsgManager.PredictActionS2C == null || rPCMsgManager.PVEShopBuyS2C == null)
			{
				Status = "waiting_rpc";
				return false;
			}
			if (!RefreshSession())
			{
				Detach();
				Status = "waiting_match";
				return false;
			}
			if (_subscription != null && _subscription.Active && _subscription.Rpc == rPCMsgManager && rPCMsgManager.PredictActionS2C.OnPredictActionS2CServerCallBackAsync == _subscription.Predict && rPCMsgManager.PVEShopBuyS2C.OnPVEShopBuyS2CServerCallBackAsync == _subscription.Buy)
			{
				Status = "installed";
				return true;
			}
			if (_subscription != null && _subscription.Rpc == rPCMsgManager)
			{
				_subscription.Active = false;
				_incomplete++;
				Status = "handler_ownership_lost";
				return false;
			}
			Detach();
			Subscription s = new Subscription
			{
				Rpc = rPCMsgManager,
				PreviousPredict = rPCMsgManager.PredictActionS2C.OnPredictActionS2CServerCallBackAsync,
				PreviousBuy = rPCMsgManager.PVEShopBuyS2C.OnPVEShopBuyS2CServerCallBackAsync
			};
			if (s.PreviousPredict == null || s.PreviousBuy == null)
			{
				Status = "waiting_game_handlers";
				return false;
			}
			s.Predict = (PredictActionS2C model, int err, bool dispatch) => ObservePredict(s, model, err, dispatch);
			s.Buy = (PVEShopBuyS2C model, int err, bool dispatch) => ObserveBuy(s, model, err, dispatch);
			_subscription = s;
			rPCMsgManager.PredictActionS2C.OnPredictActionS2CServerCallBackAsync = s.Predict;
			rPCMsgManager.PVEShopBuyS2C.OnPVEShopBuyS2CServerCallBackAsync = s.Buy;
			Status = "installed";
			return true;
		}
		catch (Exception)
		{
			Status = "install_failed";
			return false;
		}
	}

	private static UniTask ObservePredict(Subscription s, PredictActionS2C model, int err, bool dispatch)
	{
		if (!s.Active)
		{
			return s.PreviousPredict(model, err, dispatch);
		}
		bool valid = false;
		long generation = 0L;
		List<Candidate> candidates = null;
		try
		{
			valid = RefreshSession();
			generation = _generation;
			if (valid && err == 0 && model != null)
			{
				candidates = SnapshotCandidates(model);
			}
		}
		catch (Exception)
		{
			_incomplete++;
			Status = "observer_context_failed";
		}
		return After(s.PreviousPredict(model, err, dispatch), delegate
		{
			if (valid && s.Active && _subscription == s && generation == _generation && RefreshSession() && generation == _generation && candidates != null)
			{
				foreach (Candidate item in candidates)
				{
					Transactions.Register(item.Player, item.Sn, item.Round, item.Value);
				}
			}
		});
	}

	private static UniTask ObserveBuy(Subscription s, PVEShopBuyS2C model, int err, bool dispatch)
	{
		if (!s.Active)
		{
			return s.PreviousBuy(model, err, dispatch);
		}
		bool valid = false;
		long generation = 0L;
		long downSn = 0L;
		TransferTransactions.Context context = null;
		try
		{
			valid = RefreshSession();
			generation = _generation;
			if (valid && err == 0 && model != null)
			{
				RoomInfo curRoomInfo = SimpleSingletonProvider<GameLogicManager>.inst.room.curRoomInfo;
				context = Transactions.Begin(model.PlayerId, curRoomInfo.Round, SnapshotQueuedActions());
				downSn = CurrentBuyDownSn(s.Rpc);
			}
		}
		catch (Exception)
		{
			_incomplete++;
			Status = "observer_context_failed";
		}
		return After(s.PreviousBuy(model, err, dispatch), delegate
		{
			if (valid && s.Active && _subscription == s && generation == _generation && RefreshSession() && generation == _generation)
			{
				Confirm(model, err, context, downSn);
			}
		});
	}

	private static UniTask After(UniTask original, System.Action observer)
	{
		return original.ContinueWith(delegate
		{
			try
			{
				observer();
			}
			catch (Exception)
			{
				_incomplete++;
				Status = "observer_event_failed";
			}
		});
	}

	public static void Detach()
	{
		Subscription subscription = _subscription;
		if (subscription != null)
		{
			subscription.Active = false;
			if (subscription.Rpc.PredictActionS2C.OnPredictActionS2CServerCallBackAsync == subscription.Predict)
			{
				subscription.Rpc.PredictActionS2C.OnPredictActionS2CServerCallBackAsync = subscription.PreviousPredict;
			}
			if (subscription.Rpc.PVEShopBuyS2C.OnPVEShopBuyS2CServerCallBackAsync == subscription.Buy)
			{
				subscription.Rpc.PVEShopBuyS2C.OnPVEShopBuyS2CServerCallBackAsync = subscription.PreviousBuy;
			}
		}
		_subscription = null;
		Transactions.DropPending();
		Status = "detached";
	}

	private static bool RefreshSession()
	{
		GameLogicManager inst = SimpleSingletonProvider<GameLogicManager>.inst;
		RoomInfo roomInfo = ((inst == null || inst.room == null) ? null : inst.room.curRoomInfo);
		if (roomInfo == null || roomInfo.info == null || roomInfo.State != Room.Types.State.Running)
		{
			return false;
		}
		long id = roomInfo.info.Id;
		long startTime = roomInfo.info.StartTime;
		if (id <= 0 || startTime <= 0)
		{
			return false;
		}
		if (id != _room || startTime != _start)
		{
			_room = id;
			_start = startTime;
			_generation++;
			Transactions.Clear();
			Events.Clear();
			_incomplete = 0;
		}
		return true;
	}

	private static List<Candidate> SnapshotCandidates(PredictActionS2C model)
	{
		List<Candidate> list = new List<Candidate>();
		int round = SimpleSingletonProvider<GameLogicManager>.inst.room.curRoomInfo.Round;
		foreach (party.model.Action action in model.Actions)
		{
			if (action != null && action.Id == 5215 && !action.NotBro && action.PlayerId > 0 && action.Sn > 0)
			{
				if (list.Count >= 64)
				{
					_incomplete++;
					break;
				}
				list.Add(new Candidate
				{
					Player = action.PlayerId,
					Sn = action.Sn,
					Round = round,
					Value = action
				});
			}
		}
		return list;
	}

	private static HashSet<long> SnapshotQueuedActions()
	{
		ActionListener inst = SimpleSingletonProvider<ActionListener>.inst;
		List<party.model.Action> list = ((inst == null || QueuedActions == null) ? null : (QueuedActions.GetValue(inst) as List<party.model.Action>));
		if (list == null || list.Count > 4096)
		{
			return null;
		}
		HashSet<long> hashSet = new HashSet<long>();
		foreach (party.model.Action item in list)
		{
			if (item != null && item.Sn > 0)
			{
				hashSet.Add(item.Sn);
			}
		}
		return hashSet;
	}

	private static long CurrentBuyDownSn(RPCMsgManager rpc)
	{
		if (QueuedMessages == null || MessageLock == null || MessageCommand == null || MessageDownSn == null || MessageFrame == null)
		{
			return 0L;
		}
		object value = MessageLock.GetValue(rpc);
		if (value == null)
		{
			return 0L;
		}
		lock (value)
		{
			if (!(QueuedMessages.GetValue(rpc) is IList { Count: not 0 } list))
			{
				return 0L;
			}
			object obj = list[0];
			if (obj == null || (int)MessageCommand.GetValue(obj) != 5216)
			{
				return 0L;
			}
			return (MessageFrame.GetValue(obj) is Frame { IsConsumed: false }) ? ((long)MessageDownSn.GetValue(obj)) : 0;
		}
	}

	private static bool ReadConfirmedFields(object payload, out int amount, out long assisted)
	{
		if (!(payload is party.model.Action action) || action.Data == null)
		{
			amount = 0;
			assisted = 0L;
			return false;
		}
		return TransferWire.ReadTransferFields(action.Data.ToByteArray(), out amount, out assisted);
	}

	private static void Confirm(PVEShopBuyS2C model, int err, TransferTransactions.Context context, long downSn)
	{
		if (err != 0 || model == null || !RefreshSession() || model.PlayerId <= 0)
		{
			return;
		}
		long shopSn;
		int amount;
		TransferTransactions.Result num = Transactions.Acknowledge(context, model, downSn, model.AssistPlayer, model.IsClose, model.BuyCards.Count != 0, ReadConfirmedFields, out shopSn, out amount);
		if (num == TransferTransactions.Result.Unknown)
		{
			Status = "transfer_context_unconfirmed";
		}
		if (num == TransferTransactions.Result.Confirmed)
		{
			if (Events.Count >= 256)
			{
				Events.Dequeue();
				_incomplete++;
			}
			Events.Enqueue(new Transfer
			{
				Room = _room,
				Start = _start,
				Sender = model.PlayerId,
				Recipient = model.AssistPlayer,
				Amount = amount,
				ShopSn = shopSn
			});
		}
	}

	internal static void Reset()
	{
		_generation++;
		Detach();
		Transactions.Clear();
		Events.Clear();
		_room = (_start = 0L);
		_incomplete = 0;
	}

	internal static void Drain(TransferObserver observer)
	{
		while (Events.Count > 0)
		{
			Transfer transfer = Events.Dequeue();
			string text = transfer.Room.ToString(CultureInfo.InvariantCulture) + "/" + transfer.Start.ToString(CultureInfo.InvariantCulture);
			observer(text, text + ":" + transfer.ShopSn, transfer.Sender, transfer.Recipient, transfer.Amount);
		}
	}

	public static string DrainJson()
	{
		StringBuilder stringBuilder = new StringBuilder();
		long num = (long)_incomplete + (long)Transactions.Incomplete;
		stringBuilder.Append("{\"status\":\"").Append(Status).Append("\",\"incomplete\":")
			.Append((num > 0) ? "true" : "false")
			.Append(",\"incompleteCount\":")
			.Append(num)
			.Append(",\"events\":[");
		bool flag = true;
		while (Events.Count > 0)
		{
			Transfer transfer = Events.Dequeue();
			if (!flag)
			{
				stringBuilder.Append(',');
			}
			flag = false;
			stringBuilder.Append("{\"id\":\"transfer:").Append(transfer.Room).Append(':')
				.Append(transfer.Start)
				.Append(':')
				.Append(transfer.ShopSn)
				.Append("\",\"roomId\":")
				.Append(transfer.Room)
				.Append(",\"startTime\":")
				.Append(transfer.Start)
				.Append(",\"sender\":")
				.Append(transfer.Sender)
				.Append(",\"recipient\":")
				.Append(transfer.Recipient)
				.Append(",\"amount\":")
				.Append(transfer.Amount)
				.Append('}');
		}
		return stringBuilder.Append("]}").ToString();
	}
}
