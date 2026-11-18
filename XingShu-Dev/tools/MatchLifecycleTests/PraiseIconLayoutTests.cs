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
        Check(rect.X >= 0 && rect.X + rect.Size <= limit + .001f, "icon stays horizontally inside portrait at every size");
        Check(rect.Y < 0 && rect.Y >= -rect.Size * .45f - .001f, "icon sits at upper portrait border / inter-row gap");
    }

    private static void Main()
    {
        PraiseIconLayout desktop = PraiseIconLayout.Calculate(128, 128, false, 1);
        Check(desktop.Size >= 36f && desktop.Size <= 44f, "desktop default size is compact (36..44px)");
        Check(Near(desktop.Y, -desktop.Size * .35f) && desktop.X >= 0, "desktop default sits at upper gap above face");
        PraiseIconLayout phone = PraiseIconLayout.Calculate(128, 128, true, 1);
        Check(phone.Size >= desktop.Size * 1.5f, "phone default is clearly larger than desktop");
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

        // PraiseSelection default-all-selected + click-to-cancel behavior
        PraiseSelection sel = new PraiseSelection();
        Check(sel.Begin("room/1", 100), "begin match session");
        Check(sel.EnsureDefaultSelected(201, true, true), "first teammate defaults to praised ON");
        Check(sel.EnsureDefaultSelected(202, true, true), "second teammate defaults to praised ON");
        Check(!sel.EnsureDefaultSelected(100, true, true), "self is never selected");
        Check(!sel.EnsureDefaultSelected(203, false, true), "ineligible bot is never selected");
        Check(sel.IsSelected(201) && sel.IsSelected(202) && sel.PendingCount == 2, "all eligible teammates start selected");
        Check(sel.Toggle(201, true) && !sel.IsSelected(201) && sel.IsSelected(202) && sel.PendingCount == 1, "clicking colored icon cancels praise for that teammate only");
        Check(!sel.EnsureDefaultSelected(201, true, true) && !sel.IsSelected(201), "subsequent ticks do not re-enable manually cancelled teammate");
        Check(sel.Toggle(201, true) && sel.IsSelected(201) && sel.PendingCount == 2, "clicking blue icon re-enables praise");
        Check(sel.Begin("room/2", 100) && sel.PendingCount == 0, "new match resets selection state");
        Check(!sel.EnsureDefaultSelected(201, true, false) && !sel.IsSelected(201), "defaultOn=false keeps teammates unselected initially");

        Console.WriteLine("PASS: " + _checks + " praise layout & selection checks");
    }
}
