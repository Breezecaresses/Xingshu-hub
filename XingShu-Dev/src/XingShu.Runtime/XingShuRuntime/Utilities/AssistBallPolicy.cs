using System;

namespace XingShuRuntime.Utilities;

// Shared by the UI paths. No input API here: a touch release must not become
// a permanent mouse hover, and the idle clock must remain independent of speed.
internal static class AssistBallPolicy
{
	internal static float Size(float stored, bool mobile, bool migrate)
	{
		float fallback = 64f;
		if (float.IsNaN(stored) || float.IsInfinity(stored)) stored = fallback;
		if (mobile && migrate && (Math.Abs(stored - 44f) < .01f || Math.Abs(stored - 64f) < .01f)) stored = fallback;
		return Math.Max(mobile ? 48f : 36f, Math.Min(200f, stored));
	}

	internal static bool ShouldHide(bool enabled, bool docked, bool mobile, bool pressed,
		bool hover, bool menuOpen, float now, float lastInteraction)
	{
		return enabled && docked && !pressed && !menuOpen && !(hover && !mobile)
			&& (!mobile || now - lastInteraction >= 2f);
	}

	internal static float Reveal(float size, float scale, bool mobile)
	{
		return mobile ? Math.Min(size, Math.Max(32f / scale, size * .35f)) : 20f / scale;
	}
}
