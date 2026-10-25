using System;
using Core;
using UI;
using XingShu.Bridge;
using XingShuRuntime.Core;
using XingShuRuntime.Utilities;

class Program
{
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    static OriginalArtworkService Reset(string platform, bool mode = true)
    {
        XingShuBridgeClient.Platform = platform;
        GameSettings.APP_VERSION = "3.3.0"; GameSettings.RES_VERSION = "026";
        GameSettings.angelMode = mode;
        XingShuConfigService.Saved = null; XingShuConfigService.SaveCount = 0;
        Tools.SimpleSingletonProvider<UIManager>.hasInstance = true;
        Tools.SimpleSingletonProvider<UIManager>.inst = new UIManager();
        UnityEngine.Time.unscaledTime = 10;
        return new OriginalArtworkService();
    }
    static void Main()
    {
        foreach (string platform in new[] {"TAPTAP", "BILIBILI", "WEGAME", "ANDROID_CN"})
        {
            var service = Reset(platform);
            service.Tick();
            Check(service.Enabled && !GameSettings.angelMode && XingShuConfigService.SaveCount == 0,
                platform + " defaults to original artwork without changing official saved preference");
            Check(Tools.SimpleSingletonProvider<UIManager>.inst.Refreshes == 1,
                platform + " refreshes existing panels once");
            UnityEngine.Time.unscaledTime += 2;
            service.Tick();
            Check(Tools.SimpleSingletonProvider<UIManager>.inst.Refreshes == 1, platform + " stable state avoids repeated UI refresh");
            service.SetEnabled(false);
            Check(GameSettings.angelMode && XingShuConfigService.Saved == false && XingShuConfigService.SaveCount == 1,
                platform + " disabling restores prior channel state and persists the plugin choice");
        }
        foreach (string platform in new[] {"STEAM_CN", "STEAM_GLOBAL", "UNKNOWN", "ANDROID"})
        {
            var service = Reset(platform);
            service.Tick(); service.SetEnabled(true);
            Check(!service.Supported && GameSettings.angelMode && XingShuConfigService.SaveCount == 0,
                platform + " preserves game settings");
        }
        var waiting = Reset("TAPTAP");
        GameSettings.APP_VERSION = null;
        waiting.Tick();
        Check(GameSettings.angelMode, "waits for official initialization before capturing restore value");
        GameSettings.APP_VERSION = "3.3.0";
        UnityEngine.Time.unscaledTime += 2;
        waiting.Tick(); waiting.SetEnabled(false);
        Check(GameSettings.angelMode, "delayed initialization still restores true original baseline");

        var original = Reset("TAPTAP", false);
        original.Tick(); original.SetEnabled(false);
        Check(!GameSettings.angelMode, "disabling never forces censorship when channel was already original");

        var disabled = Reset("TAPTAP");
        XingShuConfigService.Saved = false;
        disabled.Tick();
        Check(GameSettings.angelMode && !disabled.Enabled, "saved opt-out survives a new service instance");

        var deferred = Reset("ANDROID_CN");
        Tools.SimpleSingletonProvider<UIManager>.hasInstance = false;
        deferred.Tick();
        Check(!GameSettings.angelMode, "asset selection works before UI manager is available");
        Tools.SimpleSingletonProvider<UIManager>.hasInstance = true;
        UnityEngine.Time.unscaledTime += 2;
        deferred.Tick();
        Check(Tools.SimpleSingletonProvider<UIManager>.inst.Refreshes == 1, "pending refresh runs once UI is ready");

        var retry = Reset("WEGAME");
        Tools.SimpleSingletonProvider<UIManager>.inst.Fail = true;
        retry.Tick();
        Check(!GameSettings.angelMode, "closing panel cannot prevent original asset selection");
        Tools.SimpleSingletonProvider<UIManager>.inst.Fail = false;
        UnityEngine.Time.unscaledTime += 2; retry.Tick();
        Check(Tools.SimpleSingletonProvider<UIManager>.inst.Refreshes == 1, "failed panel refresh is retried");
        Console.WriteLine(checks + " checks passed");
    }
}

// Test doubles only for the engine boundary; the production service is linked.
namespace Core { static class GameSettings { public static bool angelMode; public static string APP_VERSION, RES_VERSION; } }
namespace UnityEngine { static class Time { public static float unscaledTime; } }
namespace Tools { static class SimpleSingletonProvider<T> { public static bool hasInstance; public static T inst; } }
namespace UI { class UIManager { public int Refreshes; public bool Fail; public void SwitchAdultMode(bool value) { if (Fail) throw new Exception(); Refreshes++; } } }
namespace XingShu.Bridge { static class XingShuBridgeClient { public static string Platform; public static string DetectPlatform() => Platform; } }
namespace XingShuRuntime.Core
{
    static class XingShuConfigKeys { public const string OriginalArtworkEnabled = "XINGSHU_ORIGINAL_ARTWORK_ENABLED"; public const bool OriginalArtworkEnabledDefault = true; }
    static class XingShuConfigService
    {
        public static bool? Saved; public static int SaveCount;
        public static bool GetBool(string key, bool fallback) => Saved ?? fallback;
        public static void SetBool(string key, bool value) { Saved = value; }
        public static void Save() { SaveCount++; }
    }
}
