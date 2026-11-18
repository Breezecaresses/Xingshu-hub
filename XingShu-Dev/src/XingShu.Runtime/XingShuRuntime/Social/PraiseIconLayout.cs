using System;

namespace XingShuRuntime.Social
{
    // Native portrait-local coordinates; resizing must never create inverted bounds.
    internal struct PraiseIconLayout
    {
        internal float X, Y, Size;

        internal static PraiseIconLayout Calculate(float width, float height, bool mobile, float scale)
        {
            if (!Finite(width) || !Finite(height) || width <= 0 || height <= 0)
                return new PraiseIconLayout();
            if (!Finite(scale) || scale <= 0) scale = 1f;
            float portrait = Math.Min(width, height);
            float margin = Math.Min(2f, portrait * .05f);
            float desktopBase = Clamp(portrait * .32f, 42f, 52f);
            float baseline = mobile ? Clamp(portrait * .52f, 72f, 104f) : desktopBase;
            float size = Math.Min(portrait - margin * 2f, Clamp(baseline * scale, 24f, 160f));
            // Keep the accepted forehead anchor, expanding towards the inside of the portrait.
            float center = portrait - desktopBase * .75f - 6f;
            return new PraiseIconLayout {
                Size = size,
                X = Clamp(center - size * .5f, margin, portrait - size - margin),
                Y = Clamp(6f, margin, portrait - size - margin)
            };
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static float Clamp(float value, float min, float max) { return Math.Min(max, Math.Max(min, value)); }
    }
}
