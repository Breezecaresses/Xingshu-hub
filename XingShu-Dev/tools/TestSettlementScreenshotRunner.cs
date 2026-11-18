using System;
using System.IO;
using XingShuRuntime.MatchPort;

internal static class SettlementScreenshotTests
{
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    public static int Main()
    {
        // 1. Desktop output folder
        string desktop = SettlementScreenshotPolicy.ResolveOutputFolder(@"C:\Users\Test\Pictures", @"C:\Persistent", @"C:\Temp", false);
        Check(desktop == Path.Combine(@"C:\Users\Test\Pictures", "XingShu", "Settlements"), "desktop resolves MyPictures/XingShu/Settlements");

        string desktopFallback = SettlementScreenshotPolicy.ResolveOutputFolder(string.Empty, @"C:\Persistent", @"C:\Temp", false);
        Check(desktopFallback == Path.Combine(@"C:\Persistent", "XingShu", "Settlements"), "desktop falls back to persistentDataPath when MyPictures empty");

        // 2. Android output folder
        string android = SettlementScreenshotPolicy.ResolveOutputFolder(@"C:\Users\Test\Pictures", "/data/user/0/app/files", "/data/user/0/app/cache", true);
        Check(android == Path.Combine("/data/user/0/app/files", "XingShu", "Settlements"), "android resolves persistentDataPath/XingShu/Settlements");

        string androidFallback = SettlementScreenshotPolicy.ResolveOutputFolder(null, null, "/data/user/0/app/cache", true);
        Check(androidFallback == Path.Combine("/data/user/0/app/cache", "XingShu", "Settlements"), "android falls back to temporaryCachePath");

        // 3. Filename & match keys
        var ts = new DateTime(2026, 11, 19, 1, 5, 30);
        Check(SettlementScreenshotPolicy.BuildFileName(ts, 42L) == "XingShu_Settlement_20261119_010530_Room42.png", "filename matches XingShu_Settlement_yyyyMMdd_HHmmss_Room{roomId}.png");
        Check(SettlementScreenshotPolicy.BuildFileName(ts, 0L) == "XingShu_Settlement_20261119_010530_Room0.png", "singleplayer or zero roomId clamps to Room0");
        Check(SettlementScreenshotPolicy.BuildMatchKey(42L, 1700001L, 3) == "42/1700001", "room match key uses roomId/StartTime");
        Check(SettlementScreenshotPolicy.BuildMatchKey(0L, 0L, 3) == "single/3", "singleplayer match key uses epoch");

        // 4. PNG validation
        Check(SettlementScreenshotPolicy.IsValidPng(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }), "valid PNG magic accepted");
        Check(!SettlementScreenshotPolicy.IsValidPng(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00 }), "non-PNG header rejected");
        Check(!SettlementScreenshotPolicy.IsValidPng(new byte[] { 0x89, 0x50 }), "truncated bytes rejected");
        Check(!SettlementScreenshotPolicy.IsValidPng(null), "null bytes rejected");

        // 5. Deduplication & bounded capacity
        var tracker = new SettlementScreenshotPolicy.CaptureTracker();
        Check(!tracker.HasCaptured("42/100"), "new match key not captured yet");
        tracker.MarkCaptured("42/100");
        Check(tracker.HasCaptured("42/100"), "same match key deduplicated");
        Check(!tracker.HasCaptured("42/200"), "rematch in same room with new StartTime is allowed");

        for (int i = 0; i < 70; i++) tracker.MarkCaptured("r/" + i);
        Check(!tracker.HasCaptured("42/100"), "bounded tracker evicts oldest keys beyond 64");
        Check(tracker.HasCaptured("r/69"), "recent keys retained in bounded tracker");

        Console.WriteLine("Settlement screenshot tests: " + _passed + "/17 passed");
        return _passed == 17 ? 0 : 1;
    }
}
