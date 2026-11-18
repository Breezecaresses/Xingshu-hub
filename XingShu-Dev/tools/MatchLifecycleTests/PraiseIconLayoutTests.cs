using System;
using XingShuRuntime.Social;

internal static class PraiseIconLayoutTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .001f; }

    private static void Bounds(float width, float height, float scale, bool mobile)
    {
        PraiseIconLayout rect = PraiseIconLayout.Calculate(width, height, mobile, scale);
        float limit = Math.Min(width, height);
        Check(rect.Size > 0 && !float.IsNaN(rect.Size), "finite visible icon");
        Check(rect.X >= 0 && rect.Y >= 0, "nonnegative portrait coordinates");
        Check(rect.X + rect.Size <= limit + .001f && rect.Y + rect.Size <= limit + .001f, "icon stays inside portrait at every size");
    }

    private static void Main()
    {
        PraiseIconLayout desktop = PraiseIconLayout.Calculate(128, 128, false, 1);
        Check(Near(desktop.Size, 42) && Near(desktop.X, 69.5f) && Near(desktop.Y, 6), "desktop default preserves accepted position and size");
        PraiseIconLayout phone = PraiseIconLayout.Calculate(128, 128, true, 1);
        Check(phone.Size >= desktop.Size * 1.5f, "phone default is clearly larger than beta.1");
        Check(PraiseIconLayout.Calculate(192, 192, true, 1).Size > PraiseIconLayout.Calculate(128, 128, true, 1).Size, "phone default follows native portrait size");
        foreach (float width in new float[] { 1, 16, 32, 48, 72, 96, 128, 160, 192, 256 })
        foreach (float height in new float[] { 16, 48, 96, 144, 256 })
        foreach (bool mobile in new bool[] { false, true })
        {
            float previous = 0;
            for (int percent = 60; percent <= 200; percent += 5)
            {
                float scale = percent / 100f;
                Bounds(width, height, scale, mobile);
                float size = PraiseIconLayout.Calculate(width, height, mobile, scale).Size;
                Check(size + .001f >= previous, "increasing the setting never shrinks the icon");
                previous = size;
            }
        }
        foreach (float invalid in new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1, 0 })
        {
            Check(Near(PraiseIconLayout.Calculate(128, 128, true, invalid).Size, phone.Size), "invalid stored scale uses a usable default");
            Check(PraiseIconLayout.Calculate(invalid, 128, true, 1).Size == 0, "invalid portrait is hidden");
        }
        Check(PraiseIconLayout.Calculate(128, 128, true, 2).Size > phone.Size, "large setting has a visible effect");
        Check(Near(PraiseIconLayout.Calculate(128, 128, true, 1).Size, phone.Size), "reset returns to the enlarged phone default");
        Console.WriteLine("PASS: " + _checks + " praise layout checks (mobile defaults, size sweep, bounds, invalid values, desktop compatibility)");
    }
}
