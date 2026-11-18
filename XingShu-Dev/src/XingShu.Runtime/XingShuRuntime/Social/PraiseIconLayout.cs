using System;

namespace XingShuRuntime.Social
{
    // Native portrait-local coordinates; resizing must never create inverted bounds.
    internal struct PraiseIconLayout
    {
        internal float X, Y, Size;

        internal static PraiseIconLayout Calculate(float width, float height, bool mobile, float scale, float offsetX = 0f, float offsetY = 0f)
        {
            if (!Finite(width) || !Finite(height) || width <= 0 || height <= 0)
                return new PraiseIconLayout();
            if (!Finite(scale) || scale <= 0) scale = 1f;
            if (!Finite(offsetX)) offsetX = 0f;
            if (!Finite(offsetY)) offsetY = 0f;
            float portrait = Math.Min(width, height);
            float margin = Math.Min(2f, portrait * .05f);
            float desktopBase = Clamp(portrait * .30f, 36f, 44f);
            float baseline = mobile ? Clamp(portrait * .48f, 64f, 92f) : desktopBase;
            float size = Math.Min(portrait - margin * 2f, Clamp(baseline * scale, 24f, 160f));
            // Sit in the upper border / inter-row gap between the 4 character frames rather than over the face.
            float center = portrait - desktopBase * .75f - 4f;
            return new PraiseIconLayout {
                Size = size,
                X = Clamp(center - size * .5f, margin, portrait - size - margin) + offsetX,
                Y = -size * .35f + offsetY
            };
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static float Clamp(float value, float min, float max) { return Math.Min(max, Math.Max(min, value)); }
    }
}
