using System;

namespace XingShuRuntime.MatchPort;

internal sealed class StatsWidgetPreferences
{
	internal float Background = 0.66f;

	internal float TextAlpha = 0.96f;

	internal float Scale = 1f;

	internal float X = 1f;

	internal float Y = 0.12f;

	internal bool Locked;

	internal const float Width = 360f;

	internal const float Height = 328f;

	internal static float Limit(float v, float min, float max, float fallback)
	{
		if (!float.IsNaN(v) && !float.IsInfinity(v))
		{
			return Math.Max(min, Math.Min(max, v));
		}
		return fallback;
	}

	internal void Normalize()
	{
		Background = Limit(Background, 0.15f, 1f, 0.66f);
		TextAlpha = Limit(TextAlpha, 0.55f, 1f, 0.96f);
		Scale = Limit(Scale, 0.65f, 1.6f, 1f);
		X = Limit(X, 0f, 1f, 1f);
		Y = Limit(Y, 0f, 1f, 0.12f);
	}

	internal void ResetAppearance()
	{
		Background = 0.66f;
		TextAlpha = 0.96f;
		Scale = 1f;
	}

	internal void ResetPosition()
	{
		X = 1f;
		Y = 0.12f;
	}

	internal StatsWidgetBounds Layout(float x, float y, float width, float height, float baseScale)
	{
		Normalize();
		width = Math.Max(1f, width);
		height = Math.Max(1f, height);
		float num = Math.Min(12f, Math.Min(width, height) * 0.025f);
		float num2 = Math.Max(1f, width - 2f * num);
		float num3 = Math.Max(1f, height - 2f * num);
		float num4 = Math.Min(Limit(baseScale, 0.5f, 3f, 1f) * Scale, Math.Min(num2 / Width, num3 / Height));
		StatsWidgetBounds result = default(StatsWidgetBounds);
		result.X = x + num + X * Math.Max(0f, num2 - 360f * num4);
		result.Y = y + num + Y * Math.Max(0f, num3 - Height * num4);
		result.Width = 360f * num4;
		result.Height = Height * num4;
		result.Scale = num4;
		return result;
	}

	internal void Move(float dx, float dy, float safeW, float safeH, float baseScale)
	{
		if (!Locked)
		{
			StatsWidgetBounds statsWidgetBounds = Layout(0f, 0f, safeW, safeH, baseScale);
			float num = Math.Min(12f, Math.Min(safeW, safeH) * 0.025f);
			float num2 = Math.Max(0f, safeW - 2f * num - statsWidgetBounds.Width);
			float num3 = Math.Max(0f, safeH - 2f * num - statsWidgetBounds.Height);
			if (num2 > 0.01f)
			{
				X = Limit(X + dx / num2, 0f, 1f, 1f);
			}
			if (num3 > 0.01f)
			{
				Y = Limit(Y + dy / num3, 0f, 1f, 0.12f);
			}
		}
	}
}
