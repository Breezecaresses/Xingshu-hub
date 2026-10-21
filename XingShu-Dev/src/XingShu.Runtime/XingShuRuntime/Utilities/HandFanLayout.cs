using System;

namespace XingShuRuntime.Utilities
{
    internal struct HandFanPose { internal float X, Y, Angle; }
    internal static class HandFanLayout
    {
        internal const float Headroom = 52;
        internal const float Bottomroom = 32;
        internal static HandFanPose[] Arrange(int count, float width, float cardWidth, float cardHeight)
        {
            var poses = new HandFanPose[Math.Max(0, count)];
            if (count <= 0) return poses;
            float angle = count <= 6 ? 9 : 6;
            float margin = (float)Math.Sin(angle * Math.PI / 180) * cardHeight + cardWidth * .08f + 8;
            float usable = Math.Max(cardWidth, width - margin * 2);
            float step = count <= 1 ? 0 : Math.Max(0, Math.Min(cardWidth * .82f, (usable - cardWidth) / (count - 1)));
            float start = (width - (cardWidth + step * (count - 1))) * .5f + cardWidth * .5f;
            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0 : (float)i * 2 / (count - 1) - 1;
                poses[i] = new HandFanPose { X = start + i * step, Y = Headroom + cardHeight + 14 * t * t, Angle = -t * angle };
            }
            return poses;
        }
        // Test the resting fan, not the raised card. Changing draw order must not
        // make a stationary pointer oscillate between two overlapping cards.
        internal static int Hit(HandFanPose[] poses, float x, float y, float width, float height)
        {
            for (int i = poses.Length - 1; i >= 0; i--)
                if (Contains(poses[i], x, y, width, height)) return i;
            return -1;
        }
        internal static bool Contains(HandFanPose pose, float x, float y, float width, float height)
        {
            double radians = pose.Angle * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
            double dx = x - pose.X, dy = pose.Y - y;
            double localX = cos * dx + sin * dy, localY = -sin * dx + cos * dy;
            return Math.Abs(localX) <= width * .5 && localY >= 0 && localY <= height;
        }
    }
}
