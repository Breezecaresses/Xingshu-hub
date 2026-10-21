using System;
using System.Collections.Generic;
using System.Globalization;
using XingShuRuntime.MatchPort;
using XingShuRuntime.Utilities;
using GameLogic;
using party.model;
using Tools;
using UI;

namespace XingShuRuntime.DataService;

/// <summary>
/// 运行在游戏主线程上的快照构造器。
/// 负责从游戏运行时实体提取只读数据，严格单向构造不可变 DTO。
/// 严格执行：
/// 1. Self 手牌来自 cardContainer (runtime-live)。
/// 2. Remote/Bot 严禁读取本地 _HandCards，仅读取 HandCardDisplayManager 权威缓存；未同步时严格降级为 Unknown。
/// 3. 所有返回对象完全与 Unity 引擎解耦。
/// </summary>
internal static class SnapshotBuilder
{
	public static MatchDataSnapshot Build()
	{
		try
		{
			if (!BattleStatsOverlay.Context(out RoomInfo room, out BattleLogic battle, out string matchKey))
			{
				return MatchDataSnapshot.CreateNoMatch();
			}

			long roomIdNum = room.info.Id;
			string roomId = roomIdNum.ToString(CultureInfo.InvariantCulture);
			long startTime = room.info.StartTime;
			int round = room.Round;
			string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

			MatchLedger ledger = BattleStatsOverlay.Instance?.Ledger;
			bool sameLedger = (ledger != null && ledger.Key == matchKey);

			List<PlayerSnapshot> playerList = new List<PlayerSnapshot>(4);
			List<PlayerStatsSnapshot> statsList = new List<PlayerStatsSnapshot>(4);

			GameLogicManager logicMgr = SimpleSingletonProvider<GameLogicManager>.inst;
			AccountLogic accountLogic = logicMgr?.account;

			if (room.Players != null)
			{
				foreach (RoomPlayer player in room.Players)
				{
					if (player == null || player.Slot < 0 || player.Slot > 3)
					{
						continue;
					}

					long pId = player.Id;
					int slot = player.Slot;
					bool isBot = player.IsBot;
					bool isSelf = (accountLogic != null && accountLogic.IsSelf(pId));
					string playerName = null;
					try
					{
						playerName = player.GetNick();
					}
					catch
					{
					}
					if (string.IsNullOrEmpty(playerName))
					{
						playerName = "P" + (slot + 1);
					}

					string charName = "未知角色";
					if (player.characterConfig != null)
					{
						try
						{
							charName = CharacterHandle.GetCharacterName(player.characterConfig.Id, player.characterConfig.CharacterType);
							if (string.IsNullOrEmpty(charName))
							{
								charName = "角色_" + player.characterConfig.Id;
							}
						}
						catch
						{
							charName = "角色_" + player.characterConfig.Id;
						}
					}

					// 匹配 BattlePlayerData 获取实时 HP 与存活
					BattlePlayerData bpd = null;
					if (battle.PlayerDatas != null)
					{
						for (int b = 0; b < battle.PlayerDatas.Count; b++)
						{
							BattlePlayerData candidate = battle.PlayerDatas[b];
							if (candidate != null && candidate.player != null && candidate.player.Id == pId)
							{
								bpd = candidate;
								break;
							}
						}
					}

					int hp = 0;
					int maxHp = 0;
					bool alive = false;
					if (bpd != null && bpd.Property != null)
					{
						maxHp = bpd.Property.maxHP;
						if (bpd.Property.HP != null)
						{
							hp = bpd.Property.HP.Value;
							alive = (hp > 0);
						}
					}

					// 构造手牌快照 (严格遵循 Self Live 与 Remote 权威/降级规范)
					HandSnapshot handSnapshot = BuildHandSnapshot(pId, isSelf, roomIdNum, bpd, nowIso);

					// 构造单体战绩快照
					PlayerStatsSnapshot pStats;
					if (sameLedger && ledger.Rows.TryGetValue(pId, out MatchLedger.Row row) && row != null)
					{
						pStats = new PlayerStatsSnapshot(
							pId,
							slot,
							playerName,
							charName,
							row.Damage,
							row.Kills,
							row.Movement,
							row.Transferred,
							row.HpReady,
							row.MovementReady,
							row.TransferReady,
							row.DamageKnown,
							row.KillsKnown,
							row.MovementKnown,
							row.TransferKnown
						);
					}
					else
					{
						pStats = new PlayerStatsSnapshot(
							pId,
							slot,
							playerName,
							charName,
							0L,
							0L,
							0L,
							0L,
							false,
							false,
							false,
							true,
							true,
							true,
							true
						);
					}

					statsList.Add(pStats);

					PlayerSnapshot pSnapshot = new PlayerSnapshot(
						pId,
						slot,
						playerName,
						charName,
						alive,
						hp,
						maxHp,
						isSelf,
						isBot,
						handSnapshot,
						pStats
					);

					playerList.Add(pSnapshot);
				}
			}

			// 按席位 0~3 排序
			playerList.Sort((a, b) => a.Slot.CompareTo(b.Slot));
			statsList.Sort((a, b) => a.Slot.CompareTo(b.Slot));

			// 收集 issues
			string[] issues;
			if (sameLedger && ledger.Issues != null && ledger.Issues.Count > 0)
			{
				issues = new string[ledger.Issues.Count];
				ledger.Issues.CopyTo(issues);
			}
			else
			{
				issues = new string[0];
			}

			StatsSnapshot statsSnapshot = new StatsSnapshot(matchKey, statsList.ToArray(), issues);

			return new MatchDataSnapshot(
				gameRunning: true,
				inMatch: true,
				roomId: roomId,
				startTime: startTime,
				round: round,
				snapshotTime: nowIso,
				matchKey: matchKey,
				players: playerList.ToArray(),
				stats: statsSnapshot
			);
		}
		catch
		{
			// 发生任何意外未受检异常时安全降级
			return MatchDataSnapshot.CreateNoMatch();
		}
	}

	private static HandSnapshot BuildHandSnapshot(long playerId, bool isSelf, long currentRoomId, BattlePlayerData bpd, string nowIso)
	{
		if (isSelf)
		{
			// 本地玩家：直接从客户端 cardContainer 提取实时手牌
			List<CardSnapshot> cardList = new List<CardSnapshot>();
			if (bpd != null && bpd.cardContainer != null && bpd.cardContainer._HandCards != null)
			{
				for (int i = 0; i < bpd.cardContainer._HandCards.Count; i++)
				{
					HandCardData hc = bpd.cardContainer._HandCards[i];
					if (hc != null && hc.CardId > 0)
					{
						string cardName = ResolveCardName(hc.CardId);
						int cost = ResolveCardCost(hc.CardId);
						cardList.Add(new CardSnapshot(hc.CardId, cardName, cost));
					}
				}
			}

			return HandSnapshot.CreateSelfLive(playerId, cardList.ToArray(), nowIso);
		}
		else
		{
			// 远端/Bot 玩家：严禁读本地 _HandCards！仅读权威服务同步缓存
			if (HandCardDisplayManager.TryGetAuthoritativeRemoteHand(currentRoomId, playerId, out List<TrackedCard> remoteCards, out HandSyncState syncState))
			{
				List<CardSnapshot> cardList = new List<CardSnapshot>();
				if (remoteCards != null)
				{
					for (int i = 0; i < remoteCards.Count; i++)
					{
						TrackedCard tc = remoteCards[i];
						if (tc != null && tc.CardId > 0)
						{
							string cardName = ResolveCardName(tc.CardId);
							int cost = ResolveCardCost(tc.CardId);
							cardList.Add(new CardSnapshot(tc.CardId, cardName, cost));
						}
					}
				}

				return HandSnapshot.CreateServerAuthoritative(playerId, cardList.ToArray(), nowIso);
			}

			// 权威服务未返回、超时、或仍在拉取中：严格降级为 unknown 掩码
			int? cardCount = null;
			if (bpd != null && bpd.cardContainer != null)
			{
				cardCount = bpd.cardContainer.CardCount;
			}

			string status = (syncState == HandSyncState.Pending) ? "loading" : "unavailable";
			return HandSnapshot.CreateUnknown(playerId, cardCount, status, nowIso);
		}
	}

	private static string ResolveCardName(int cardId)
	{
		if (cardId <= 0) return "未知卡牌";
		try
		{
			CardInfoConfigure config = cardId.GetCardConfigure();
			if (config != null)
			{
				string name = config.NameID.GetLocal(UIStringType.Card);
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
			}
		}
		catch
		{
		}
		return "卡牌_" + cardId;
	}

	private static int ResolveCardCost(int cardId)
	{
		if (cardId <= 0) return 0;
		try
		{
			CardInfoConfigure config = cardId.GetCardConfigure();
			if (config != null)
			{
				return config.Cost;
			}
		}
		catch
		{
		}
		return 0;
	}
}
