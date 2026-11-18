using System.Collections.Generic;

namespace XingShuRuntime.MatchPort;

internal static class StatsWidgetMetricCatalog
{
	internal const int Count = 8;

	internal const int DefaultMask = (1 << Count) - 1;

	internal static readonly string[] Labels =
	{
		"击杀", "死亡", "输出", "承伤", "治疗", "累计金币", "转账", "移动步数"
	};

	// Maps display metric index (0..7) to StatsWidgetFrame.Player.Metrics index (skipping index 7 = Turns).
	internal static readonly int[] FrameMetricIndices = { 0, 1, 2, 3, 4, 5, 6, 8 };

	internal static int NormalizeMask(int mask)
	{
		int valid = mask & DefaultMask;
		return valid == 0 ? DefaultMask : valid;
	}

	internal static bool IsEnabled(int mask, int index)
	{
		if (index < 0 || index >= Count)
		{
			return false;
		}
		return (NormalizeMask(mask) & (1 << index)) != 0;
	}

	internal static int SetEnabled(int mask, int index, bool enabled)
	{
		int normalized = NormalizeMask(mask);
		if (index < 0 || index >= Count)
		{
			return normalized;
		}
		int bit = 1 << index;
		if (enabled)
		{
			return normalized | bit;
		}
		int next = normalized & ~bit;
		// Enforce at least 1 metric remains enabled.
		return next == 0 ? normalized : next;
	}

	internal static int CountEnabled(int mask)
	{
		int normalized = NormalizeMask(mask);
		int count = 0;
		for (int i = 0; i < Count; i++)
		{
			if ((normalized & (1 << i)) != 0)
			{
				count++;
			}
		}
		return count;
	}

	internal static int[] GetEnabledIndices(int mask)
	{
		int normalized = NormalizeMask(mask);
		var result = new List<int>(Count);
		for (int i = 0; i < Count; i++)
		{
			if ((normalized & (1 << i)) != 0)
			{
				result.Add(i);
			}
		}
		return result.ToArray();
	}
}
