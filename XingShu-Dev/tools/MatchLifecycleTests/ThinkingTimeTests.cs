using System;
using XingShuRuntime.Core;
using XingShuRuntime.Utilities;
using GameLogic;
using Tools;
class ThinkingTimeTests {
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    static void Main() {
        var logic = SimpleSingletonProvider<GameLogicManager>.inst;
        Check(ThinkingTime.ScaleSeconds(30) == 300 && ThinkingTime.ScaleSeconds(12.5f) == 125f, "hero and operation timer values scale tenfold");
        Check(ThinkingTime.ScaleSeconds(0) == 0 && ThinkingTime.ScaleSeconds(-1) == -1, "disabled timer sentinels remain unchanged");
        XingShuConfigService.Enabled = false;
        Check(ThinkingTime.ScaleSeconds(30) == 30, "user can disable multiplier");
        XingShuConfigService.Enabled = true; logic.watch.Watching = true;
        Check(ThinkingTime.ScaleSeconds(30) == 30, "spectators keep official timers");
        logic.watch.Watching = false; logic.replay.Session.IsReplay = true;
        Check(ThinkingTime.ScaleSeconds(30) == 30, "replays keep official timers");
        logic.replay.Session.IsReplay = false;
        var window = new UI.OperateTimeWindow();
        object first = new object(), second = new object();
        ThinkingTime.BeginProgress(first, 300);
        ThinkingTime.UpdateProgress(window, 300, 15, "first", false, first);
        Check(window.Calls == 1 && window.Total == 300 && window.Elapsed == 15 && !ThinkingTime.AllowRemoteProgress(),
            "local real-clock progress excludes remote repainting");
        ThinkingTime.BeginProgress(second, 450);
        ThinkingTime.UpdateProgress(window, 450, 7, "second", false, second);
        ThinkingTime.UpdateProgress(window, 300, 290, "old first", false, first);
        Check(window.Calls == 2 && window.Total == 450 && window.Elapsed == 7,
            "old operation callback cannot repaint the current window");
        ThinkingTime.UpdateProgress(window, 450, 3, "second", false, second);
        Check(window.Elapsed == 7, "same operation cannot move displayed progress backwards");
        ThinkingTime.UpdateProgress(window, 0, 0, "old first ended", false, first);
        Check(window.Calls == 3 && !ThinkingTime.AllowRemoteProgress(), "old operation completion cannot clear a newer progress bar");
        ThinkingTime.UpdateProgress(window, 0, 0, "second ended", false, second);
        Check(window.Calls == 4 && window.Total == 0 && ThinkingTime.AllowRemoteProgress(), "current operation completion releases progress ownership");
        ThinkingTime.BeginProgress(first, 300);
        ThinkingTime.ClearProgress();
        ThinkingTime.UpdateProgress(window, 300, 20, "disposed", false, first);
        Check(window.Calls == 4 && ThinkingTime.AllowRemoteProgress(), "room disposal blocks late local callbacks");
        logic.watch.Watching = true;
        ThinkingTime.BeginProgress(first, 30);
        ThinkingTime.UpdateProgress(window, 30, 2, "official spectator", false, second);
        Check(window.Calls == 5 && window.Total == 30 && ThinkingTime.AllowRemoteProgress(), "spectator progress remains owned by official timers");
        logic.watch.Watching = false; XingShuConfigService.Enabled = false;
        ThinkingTime.BeginProgress(first, 30);
        ThinkingTime.UpdateProgress(window, 30, 3, "official disabled", false, second);
        Check(window.Calls == 6 && window.Elapsed == 3 && ThinkingTime.AllowRemoteProgress(), "disabling tenfold thinking restores official progress updates");
        XingShuConfigService.Enabled = true;
        ThinkingTime.Install(new UnityEngine.GameObject()); AssistBall.Action();
        Check(!XingShuConfigService.Enabled && XingShuConfigService.Saved, "registered toggle persists preference");
    }
}
namespace UnityEngine { public class GameObject {} }
namespace UI {
    public class OperateTimeWindow {
        public int Calls, Total; public float Elapsed;
        public void UpdateOperationProgress(int total, float elapsed, string head, bool warning) { Calls++; Total = total; Elapsed = elapsed; }
    }
}
namespace Tools { public static class SimpleSingletonProvider<T> where T:new() { public static T inst = new T(); } }
namespace GameLogic {
    public class Watch { public bool Watching; public bool PlayerIsWatcher() => Watching; }
    public class ReplaySession { public bool IsReplay; }
    public class Replay { public ReplaySession Session = new ReplaySession(); }
    public class GameLogicManager { public Watch watch = new Watch(); public Replay replay = new Replay(); }
}
namespace XingShuRuntime.Core {
    public static class XingShuConfigService {
        public static bool Enabled = true, Saved;
        public static bool GetBool(string key, bool fallback) => Enabled;
        public static void SetBool(string key, bool value) { Enabled = value; }
        public static void Save() { Saved = true; }
    }
    public static class AssistBall {
        public static Action Action;
        public static void RegisterAction(string id, string name, Action action, int order, string hint, string icon, Func<bool> state) { Action = action; }
    }
}
