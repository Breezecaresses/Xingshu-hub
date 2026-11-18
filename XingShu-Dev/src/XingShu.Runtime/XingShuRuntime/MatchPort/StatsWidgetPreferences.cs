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

	internal const float Width = 532f;

	internal const float HeaderHeight = 38f;

	internal const float Height = 230f;

	internal static float PanelWidth(int visibleMetricCount)
	{
		int clamped = Math.Max(1, Math.Min(8, visibleMetricCount));
		float metricColWidth = clamped <= 4 ? 56f : 46f;
		return Math.Max(316f, 146f + clamped * metricColWidth + 18f);
	}

	internal static float PanelHeight(int visibleMetricCount, bool collapsed = false)
	{
		if (collapsed)
		{
			return HeaderHeight;
		}
		return Height;
	}

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
		return Layout(x, y, width, height, baseScale, Width, Height);
	}

	internal StatsWidgetBounds Layout(float x, float y, float width, float height, float baseScale, float contentHeight)
	{
		return Layout(x, y, width, height, baseScale, Width, contentHeight);
	}

	internal StatsWidgetBounds Layout(float x, float y, float width, float height, float baseScale, float contentWidth, float contentHeight)
	{
		Normalize();
		width = Math.Max(1f, width);
		height = Math.Max(1f, height);
		if (float.IsNaN(contentWidth) || float.IsInfinity(contentWidth))
		{
			contentWidth = Width;
		}
		if (float.IsNaN(contentHeight) || float.IsInfinity(contentHeight))
		{
			contentHeight = Height;
		}
		contentWidth = Math.Max(260f, contentWidth);
		contentHeight = Math.Max(HeaderHeight, contentHeight);
		float num = Math.Min(12f, Math.Min(width, height) * 0.025f);
		float num2 = Math.Max(1f, width - 2f * num);
		float num3 = Math.Max(1f, height - 2f * num);
		float num4 = Math.Min(Limit(baseScale, 0.5f, 3f, 1f) * Scale, Math.Min(num2 / contentWidth, num3 / contentHeight));
		StatsWidgetBounds result = default(StatsWidgetBounds);
		result.X = x + num + X * Math.Max(0f, num2 - contentWidth * num4);
		result.Y = y + num + Y * Math.Max(0f, num3 - contentHeight * num4);
		result.Width = contentWidth * num4;
		result.Height = contentHeight * num4;
		result.Scale = num4;
		return result;
	}

	internal void Move(float dx, float dy, float safeW, float safeH, float baseScale)
	{
		Move(dx, dy, safeW, safeH, baseScale, Width, Height);
	}

	internal void Move(float dx, float dy, float safeW, float safeH, float baseScale, float contentHeight)
	{
		Move(dx, dy, safeW, safeH, baseScale, Width, contentHeight);
	}

	internal void Move(float dx, float dy, float safeW, float safeH, float baseScale, float contentWidth, float contentHeight)
	{
		if (!Locked)
		{
			StatsWidgetBounds statsWidgetBounds = Layout(0f, 0f, safeW, safeH, baseScale, contentWidth, contentHeight);
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
