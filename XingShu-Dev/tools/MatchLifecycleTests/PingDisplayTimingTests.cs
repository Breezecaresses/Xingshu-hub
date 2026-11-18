using System;
using XingShuRuntime.Utilities;

internal static class PingDisplayTimingTests
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Test failed: " + name);
        _checks++;
    }

    public static void Main()
    {
        double now = 100.0;
        bool accelerated = false;
        float extra = 5.0f;
        int completes = 0, cancels = 0, stops = 0;

        // 1. Normal speed: ignores inflated accelerated carrier ticks and finishes at real 3.0s
        var state = new PingClockState(0f, 3f, () => completes++, () => cancels++, null, () => now, () => accelerated, () => extra);
        state.AttachStop(() => stops++);
        now = 102.9;
        state.Tick(29.0f);
        Check(completes == 0 && stops == 0, "Normal speed must not finish before 3 real seconds even when carrier reports 29s");
        now = 103.05;
        state.Tick(30.5f);
        Check(completes == 1 && stops == 1 && cancels == 0, "Normal speed finishes once at 3 real seconds");

        // 2. Accelerated mode (e.g. 10x): extends real lifetime by extra seconds (3 + 5 = 8s real)
        now = 200.0; accelerated = true; completes = 0; stops = 0; cancels = 0;
        state = new PingClockState(0f, 3f, () => completes++, () => cancels++, null, () => now, () => accelerated, () => extra);
        state.AttachStop(() => stops++);
        now = 203.0;
        state.Tick(30.0f);
        Check(completes == 0 && stops == 0, "Accelerated ping stays visible past base 3s real time");
        now = 207.9;
        state.Tick(79.0f);
        Check(completes == 0 && stops == 0, "Accelerated ping stays visible up to 3+5=8s real time");
        now = 208.05;
        state.Tick(80.5f);
        Check(completes == 1 && stops == 1, "Accelerated ping completes cleanly after extended real duration");

        // 3. Switching speed ON mid-ping latches extension seamlessly
        now = 300.0; accelerated = false; completes = 0; stops = 0; cancels = 0;
        state = new PingClockState(0f, 3f, () => completes++, () => cancels++, null, () => now, () => accelerated, () => extra);
        state.AttachStop(() => stops++);
        now = 301.0;
        state.Tick(1.0f);
        accelerated = true;
        now = 305.0;
        state.Tick(41.0f);
        Check(completes == 0, "Turning on acceleration mid-ping extends real lifetime");
        now = 308.1;
        state.Tick(72.0f);
        Check(completes == 1 && stops == 1, "Mid-ping acceleration completes at 3+5=8s real time");

        // 4. Cancellation stops any later ticks from firing completion
        now = 400.0; accelerated = true; completes = 0; stops = 0; cancels = 0;
        state = new PingClockState(0f, 3f, () => completes++, () => cancels++, null, () => now, () => accelerated, () => extra);
        state.AttachStop(() => stops++);
        state.Cancelled();
        now = 420.0;
        state.Tick(200.0f);
        Check(cancels == 1 && completes == 0 && stops == 0, "Cancelled ping never completes later");

        Console.WriteLine("PASS: " + _checks + " ping display timing checks");
    }
}
