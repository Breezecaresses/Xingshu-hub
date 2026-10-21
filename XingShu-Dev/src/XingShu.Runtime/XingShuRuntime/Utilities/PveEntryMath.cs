using System;
using System.Collections.Generic;

namespace XingShuRuntime.Utilities;

public static class PveEntryMath
{
	public sealed class Projection
	{
		public KillProbabilityMath.Distribution Attack;

		public int Cure;

		public int MarkBefore;

		public int Knives;

		public int KnifeBonus;

		public int DamageBonus;

		public int FixedKnifeBonus;

		public int HpBefore;

		public int HpAfter;

		public int GoldBefore;

		public int GoldAfter;

		public int HammerBonus;

		public long MarkAfter;

		public bool FullHealth;

		public bool OrdinaryScope;

		public bool EagleScope;

		public bool OrderEstimated;

		public bool TurnForecast;

		public bool Hammer;

		public bool HammerActive;

		public bool HammerRoundingEstimated;

		public bool IsEstimated
		{
			get
			{
				if (!OrderEstimated && !TurnForecast)
				{
					return HammerRoundingEstimated;
				}
				return true;
			}
		}
	}

	public static bool ShouldForecastTurn(long attackerId, long currentPlayerId)
	{
		if (attackerId > 0 && currentPlayerId > 0)
		{
			return attackerId != currentPlayerId;
		}
		return false;
	}

	public static Projection Project(int panelAttack, bool fullHealth, int cure, int mark, IEnumerable<int> relics, int gold = 0, bool beforeOwnTurn = false, int hp = 0, int maxHp = 0)
	{
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		bool flag5 = false;
		if (relics != null)
		{
			using IEnumerator<int> enumerator = relics.GetEnumerator();
			while (enumerator.MoveNext())
			{
				switch (enumerator.Current)
				{
				case 50010:
					flag = true;
					break;
				case 50011:
					flag2 = true;
					break;
				case 50052:
					flag3 = true;
					break;
				case 50044:
					flag4 = true;
					break;
				case 50043:
					flag5 = true;
					break;
				}
			}
		}
		Projection projection = new Projection();
		projection.FullHealth = fullHealth;
		projection.Cure = Math.Max(0, cure);
		projection.MarkBefore = Math.Max(0, mark);
		projection.HpBefore = hp;
		projection.HpAfter = hp;
		projection.TurnForecast = beforeOwnTurn && hp > 0 && maxHp > 0 && hp <= maxHp;
		if (projection.TurnForecast)
		{
			projection.HpAfter = (int)Math.Min(maxHp, (long)hp + (long)projection.Cure);
			if (!fullHealth && hp < maxHp && projection.HpAfter == maxHp)
			{
				projection.FullHealth = true;
				projection.FixedKnifeBonus = (flag ? 2 : 0) + (flag2 ? 4 : 0);
			}
		}
		projection.Knives = (flag ? 1 : 0) + (flag2 ? 1 : 0);
		projection.KnifeBonus = (projection.FullHealth ? checked(projection.Knives * projection.Cure) : 0);
		projection.Hammer = flag5;
		projection.GoldBefore = Math.Max(0, gold);
		projection.GoldAfter = projection.GoldBefore;
		projection.HammerActive = flag5 && projection.GoldBefore > 20;
		if (projection.HammerActive)
		{
			long num = (long)projection.GoldBefore * 30L;
			projection.HammerBonus = (int)(num / 100);
			projection.HammerRoundingEstimated = num % 100 != 0;
			projection.GoldAfter -= 6;
		}
		projection.OrdinaryScope = flag3;
		projection.EagleScope = flag4;
		projection.MarkAfter = (long)projection.MarkBefore + (long)(flag3 ? 1 : 0) + (flag4 ? 1 : 0);
		projection.DamageBonus = ((projection.MarkAfter > 0) ? 1 : 0);
		long num2 = (flag4 ? (2 * ((long)projection.MarkBefore + 1L)) : 0);
		int num3;
		checked
		{
			num3 = (int)(unchecked((long)panelAttack) + unchecked((long)projection.FixedKnifeBonus) + projection.KnifeBonus + projection.HammerBonus + num2);
		}
		projection.OrderEstimated = flag3 && flag4;
		if (projection.OrderEstimated)
		{
			projection.Attack = new KillProbabilityMath.Distribution(num3, new double[3] { 0.5, 0.0, 0.5 });
		}
		else
		{
			projection.Attack = KillProbabilityMath.Fixed(num3);
		}
		return projection;
	}
}
