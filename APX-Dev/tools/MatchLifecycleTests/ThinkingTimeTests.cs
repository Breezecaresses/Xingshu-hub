using System;
using APXRuntime.Core;
using APXRuntime.Utilities;
using GameLogic;
using Tools;
class ThinkingTimeTests {
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    static void Main() {
        var logic = SimpleSingletonProvider<GameLogicManager>.inst;
        Check(ThinkingTime.ScaleSeconds(30) == 300 && ThinkingTime.ScaleSeconds(12.5f) == 125f, "hero and operation timer values scale tenfold");
        Check(ThinkingTime.ScaleSeconds(0) == 0 && ThinkingTime.ScaleSeconds(-1) == -1, "disabled timer sentinels remain unchanged");
        APXConfigService.Enabled = false;
        Check(ThinkingTime.ScaleSeconds(30) == 30, "user can disable multiplier");
        APXConfigService.Enabled = true; logic.watch.Watching = true;
        Check(ThinkingTime.ScaleSeconds(30) == 30, "spectators keep official timers");
        logic.watch.Watching = false; logic.replay.Session.IsReplay = true;
        Check(ThinkingTime.ScaleSeconds(30) == 30, "replays keep official timers");
        ThinkingTime.Install(new UnityEngine.GameObject()); AssistBall.Action();
        Check(!APXConfigService.Enabled && APXConfigService.Saved, "registered toggle persists preference");
    }
}
namespace UnityEngine { public class GameObject {} }
namespace Tools { public static class SimpleSingletonProvider<T> where T:new() { public static T inst = new T(); } }
namespace GameLogic {
    public class Watch { public bool Watching; public bool PlayerIsWatcher() => Watching; }
    public class ReplaySession { public bool IsReplay; }
    public class Replay { public ReplaySession Session = new ReplaySession(); }
    public class GameLogicManager { public Watch watch = new Watch(); public Replay replay = new Replay(); }
}
namespace APXRuntime.Core {
    public static class APXConfigService {
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
