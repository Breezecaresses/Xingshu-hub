using System;
using System.Reflection;
using GameLogic;
using Tools;

namespace XingShuRuntime.Utilities;

public static class PveTurnContext
{
	private static readonly FieldInfo TurnPlayerField = typeof(BattleLogic).GetField("CurPlayerId", BindingFlags.Instance | BindingFlags.NonPublic);

	public static long CurrentPlayerId()
	{
		try
		{
			BattleLogic battleLogic = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
			if (battleLogic == null || TurnPlayerField == null || battleLogic.PlayerDatas == null)
			{
				return 0L;
			}
			object value = TurnPlayerField.GetValue(battleLogic);
			long num = ((value is long) ? ((long)value) : 0);
			if (num <= 0)
			{
				return 0L;
			}
			foreach (BattlePlayerData playerData in battleLogic.PlayerDatas)
			{
				if (playerData != null && playerData.player != null && playerData.player.Id == num)
				{
					return num;
				}
			}
		}
		catch (Exception)
		{
		}
		return 0L;
	}
}
