using System;
using System.Globalization;

namespace APXRuntime.DataService;

/// <summary>
/// 顶级对局数据全量快照（不可变 DTO）。
/// 严格遵守 5.1 & 5.2 规范：
/// 1. 绝对不包含任何 UnityEngine / MonoBehaviour / IL2CPP 对象。
/// 2. 一旦由主线程发布，生产线程绝不再修改内部任何对象与集合。
/// 3. 所有集合均为只读 Array，HTTP 线程并发读取绝对线程安全。
/// </summary>
public sealed class MatchDataSnapshot
{
	private static readonly PlayerSnapshot[] EmptyPlayers = new PlayerSnapshot[0];

	public bool GameRunning { get; }
	public bool InMatch { get; }
	public string RoomId { get; }
	public long StartTime { get; }
	public int Round { get; }
	public string SnapshotTime { get; }
	public string MatchKey { get; }
	public PlayerSnapshot[] Players { get; }
	public StatsSnapshot Stats { get; }

	public MatchDataSnapshot(
		bool gameRunning,
		bool inMatch,
		string roomId,
		long startTime,
		int round,
		string snapshotTime,
		string matchKey,
		PlayerSnapshot[] players,
		StatsSnapshot stats)
	{
		GameRunning = gameRunning;
		InMatch = inMatch;
		RoomId = roomId;
		StartTime = startTime;
		Round = round;
		SnapshotTime = snapshotTime ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
		MatchKey = matchKey;
		Players = players ?? EmptyPlayers;
		Stats = stats ?? StatsSnapshot.CreateEmpty();
	}

	public static MatchDataSnapshot CreateNoMatch()
	{
		string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
		return new MatchDataSnapshot(
			gameRunning: true,
			inMatch: false,
			roomId: null,
			startTime: 0L,
			round: 0,
			snapshotTime: now,
			matchKey: null,
			players: EmptyPlayers,
			stats: StatsSnapshot.CreateEmpty()
		);
	}

	public PlayerSnapshot FindPlayer(long playerId)
	{
		if (Players == null || Players.Length == 0)
		{
			return null;
		}

		for (int i = 0; i < Players.Length; i++)
		{
			if (Players[i] != null && Players[i].PlayerId == playerId)
			{
				return Players[i];
			}
		}

		return null;
	}
}
