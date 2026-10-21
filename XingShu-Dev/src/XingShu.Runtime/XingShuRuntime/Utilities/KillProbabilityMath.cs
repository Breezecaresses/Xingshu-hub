using System;
using System.Globalization;

namespace XingShuRuntime.Utilities;

public static class KillProbabilityMath
{
	public sealed class Distribution
	{
		public readonly int Min;

		public readonly double[] Weights;

		public int Max => checked(Min + (Weights.Length - 1));

		public Distribution(int min, double[] weights)
		{
			if (weights == null || weights.Length == 0 || weights.Length > 257)
			{
				throw new ArgumentException("Invalid distribution span");
			}
			double num = 0.0;
			foreach (double num2 in weights)
			{
				if (num2 < 0.0 || double.IsNaN(num2) || double.IsInfinity(num2))
				{
					throw new ArgumentException("Invalid weight");
				}
				num += num2;
			}
			if (num <= 0.0 || double.IsInfinity(num))
			{
				throw new ArgumentException("Invalid total weight");
			}
			checked
			{
				_ = min + (weights.Length - 1);
				Min = min;
				Weights = (double[])weights.Clone();
			}
			for (int j = 0; j < Weights.Length; j++)
			{
				Weights[j] /= num;
			}
		}
	}

	public sealed class Result
	{
		public readonly double Expected;

		public readonly bool IsEstimated;

		public string Text => Percent(Expected);

		public Result(double expected, bool isEstimated = false)
		{
			if (double.IsNaN(expected) || double.IsInfinity(expected) || expected < 0.0 || expected > 1.0)
			{
				throw new ArgumentOutOfRangeException("expected");
			}
			Expected = expected;
			IsEstimated = isEstimated;
		}
	}

	public const int MaxSpan = 256;

	public static string Percent(double p)
	{
		if (p <= 0.0)
		{
			return "0%";
		}
		if (p >= 1.0)
		{
			return "100%";
		}
		return Math.Max(0.1, Math.Min(99.9, p * 100.0)).ToString("0.#", CultureInfo.InvariantCulture) + "%";
	}

	public static Distribution Fixed(int value)
	{
		return new Distribution(value, new double[1] { 1.0 });
	}

	public static Distribution Uniform(int min, int max)
	{
		long num = (long)max - (long)min;
		if (num < 0 || num > 256)
		{
			throw new ArgumentException("Invalid card range");
		}
		double[] array = new double[(int)num + 1];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = 1.0;
		}
		return new Distribution(min, array);
	}

	public static Distribution Add(Distribution a, Distribution b)
	{
		int num = checked(a.Weights.Length + b.Weights.Length - 1);
		if (num > 257)
		{
			throw new ArgumentException("Distribution too wide");
		}
		double[] array = new double[num];
		for (int i = 0; i < a.Weights.Length; i++)
		{
			for (int j = 0; j < b.Weights.Length; j++)
			{
				array[i + j] += a.Weights[i] * b.Weights[j];
			}
		}
		return new Distribution(checked(a.Min + b.Min), array);
	}

	public static double Defend(Distribution attack, Distribution defense, int hp, int attackPoint = 0, int damageBonus = 0)
	{
		if (hp <= 0)
		{
			throw new ArgumentOutOfRangeException("hp", "Dead targets are not predictions");
		}
		if (attackPoint < 0 || attackPoint > 6)
		{
			throw new ArgumentOutOfRangeException("attackPoint");
		}
		if (damageBonus < 0)
		{
			throw new ArgumentOutOfRangeException("damageBonus");
		}
		if (hp <= 1L + (long)damageBonus)
		{
			return 1.0;
		}
		double num = 0.0;
		int num2 = ((attackPoint == 0) ? 1 : attackPoint);
		int num3 = ((attackPoint == 0) ? 6 : attackPoint);
		for (int i = 0; i < attack.Weights.Length; i++)
		{
			for (int j = 0; j < defense.Weights.Length; j++)
			{
				double num4 = 0.0;
				for (int k = num2; k <= num3; k++)
				{
					long val = (long)attack.Min + (long)i + k - defense.Min - j - hp + damageBonus;
					num4 += (double)Math.Max(0L, Math.Min(6L, val));
				}
				num += attack.Weights[i] * defense.Weights[j] * num4 / ((double)(num3 - num2 + 1) * 6.0);
			}
		}
		return Math.Max(0.0, Math.Min(1.0, num));
	}

	private static double UniformWin(int minAtk, int maxAtk, int minDef, int maxDef, long threshold)
	{
		long num = Math.Max(minAtk, minDef + threshold);
		long num2 = Math.Min(maxAtk, maxDef + threshold - 1);
		double num3 = 0.0;
		if (num2 >= num)
		{
			long num4 = num - threshold - minDef + 1;
			long num5 = num2 - threshold - minDef + 1;
			num3 = (double)(num2 - num + 1) * (double)(num4 + num5) / 2.0;
		}
		long num6 = Math.Max(minAtk, maxDef + threshold);
		long num7 = (long)maxDef - (long)minDef + 1;
		if (num6 <= maxAtk)
		{
			num3 += (double)(maxAtk - num6 + 1) * (double)num7;
		}
		return num3 / (((double)maxAtk - (double)minAtk + 1.0) * (double)num7);
	}

	public static Result Evaluate(int minAtk, int maxAtk, int minDef, int maxDef, int hp, Distribution attack, Distribution defense, int point = 0, bool estimated = false, int damageBonus = 0)
	{
		if (minAtk > maxAtk || minDef > maxDef)
		{
			throw new ArgumentException("Reversed range");
		}
		if (attack != null && (attack.Min != minAtk || attack.Max != maxAtk))
		{
			throw new ArgumentException("Attack snapshot mismatch");
		}
		if (defense != null && (defense.Min != minDef || defense.Max != maxDef))
		{
			throw new ArgumentException("Defense snapshot mismatch");
		}
		if (hp <= 0)
		{
			throw new ArgumentOutOfRangeException("hp");
		}
		if (point < 0 || point > 6)
		{
			throw new ArgumentOutOfRangeException("point");
		}
		if (damageBonus < 0)
		{
			throw new ArgumentOutOfRangeException("damageBonus");
		}
		estimated = estimated || (attack == null && minAtk != maxAtk) || (defense == null && minDef != maxDef);
		if (attack != null && defense != null)
		{
			return new Result(Defend(attack, defense, hp, point, damageBonus), estimated);
		}
		if (hp <= 1L + (long)damageBonus)
		{
			return new Result(1.0, estimated);
		}
		double num = 0.0;
		int num2 = ((point == 0) ? 1 : point);
		int num3 = ((point == 0) ? 6 : point);
		for (int i = 0; i < ((attack == null) ? 1 : attack.Weights.Length); i++)
		{
			for (int j = 0; j < ((defense == null) ? 1 : defense.Weights.Length); j++)
			{
				double num4 = ((attack == null) ? 1.0 : attack.Weights[i]) * ((defense == null) ? 1.0 : defense.Weights[j]);
				int num5 = ((attack == null) ? minAtk : (attack.Min + i));
				int maxAtk2 = ((attack == null) ? maxAtk : num5);
				int num6 = ((defense == null) ? minDef : (defense.Min + j));
				int maxDef2 = ((defense == null) ? maxDef : num6);
				for (int k = num2; k <= num3; k++)
				{
					for (int l = 1; l <= 6; l++)
					{
						num += num4 * UniformWin(num5, maxAtk2, num6, maxDef2, (long)hp + (long)l - k - damageBonus) / ((double)(num3 - num2 + 1) * 6.0);
					}
				}
			}
		}
		return new Result(Math.Max(0.0, Math.Min(1.0, num)), estimated);
	}
}
