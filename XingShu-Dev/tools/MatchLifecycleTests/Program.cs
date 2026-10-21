using System;
using Core.Scene;
using GameLogic;
using Tools;
using XingShu.Truth;
using XingShu.Spectator;

class Program {
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    static void Seed() { HandTruthSource.IsInSession = true; SpectatorStateStore.Active = true; SpectatorStateStore.RoomId = 71; SpectatorStateStore.SpectatorSessionEpoch = 500; }
    static void Main() {
        var manager = SimpleSingletonProvider<GameLogicManager>.inst;
        var scene = SimpleSingletonProvider<SceneManager>.inst;
        scene.currentType.Value = SceneType.Battle;
        manager.room.curRoomInfo = new Room { Id = 71, info = new Info { StartTime = 100 } };
        manager.room.roomController.roomStateType = RoomStateType.RUNNING;
        HandSessionLifecycle.Observe(); Seed(); HandSessionLifecycle.Observe();
        Check(HandTruthSource.IsInSession && SpectatorStateStore.Active, "same active match retains hands");
        manager.room.curRoomInfo.info.StartTime = 200;
        HandSessionLifecycle.Observe();
        Check(!HandTruthSource.IsInSession && !SpectatorStateStore.Active, "same room rematch clears both stores without Hub");
        Check(HandSessionLifecycle.RetiredMirrorEpoch == 500, "previous match epoch remains retired after clearing stores");
        Seed(); manager.room.curRoomInfo.Id = 72; HandSessionLifecycle.Observe();
        Check(!SpectatorStateStore.Active && HandSessionLifecycle.ActiveRoomId() == 72, "room switch clears old hands and changes authoritative room");
        Seed(); manager.room.roomController.roomStateType = RoomStateType.SETTLEMENT; HandSessionLifecycle.Observe();
        Check(!HandTruthSource.IsInSession && !SpectatorStateStore.Active && HandSessionLifecycle.ActiveRoomId() == 0, "settlement rejects mirrors and clears hands");
        manager.room.roomController.roomStateType = RoomStateType.RUNNING; scene.currentType.Value = SceneType.Home;
        Seed(); HandSessionLifecycle.Observe();
        Check(!SpectatorStateStore.Active && HandSessionLifecycle.ActiveRoomId() == 0, "cached room in lobby cannot revive previous match");
        manager.watch.Watching = true; Seed(); SpectatorStateStore.RoomId = 72; HandSessionLifecycle.Observe();
        Check(SpectatorStateStore.Active && HandSessionLifecycle.ActiveRoomId() == 72, "initial watch refresh survives Home to Battle transition");
        manager.watch.Watching = false; scene.currentType.Value = SceneType.Battle;
        int callbackCount = 0;
        Action reenter = () => {
            if (++callbackCount > 8) throw new Exception("reset callbacks recursively re-entered the lifecycle");
            HandSessionLifecycle.Observe();
        };
        HandTruthSource.OnReset = reenter;
        SpectatorStateStore.OnReset = reenter;
        Seed(); manager.room.curRoomInfo.info.StartTime = 300;
        HandSessionLifecycle.Observe();
        Check(callbackCount == 2 && !HandTruthSource.IsInSession && !SpectatorStateStore.Active,
            "rematch reset callbacks can inspect lifecycle without recursive reset");
        HandSessionLifecycle.Observe();
        Check(callbackCount == 2, "reentrant reset settles once and does not repeat next tick");
        Seed(); scene.currentType.Value = SceneType.Home;
        HandSessionLifecycle.Observe();
        Check(callbackCount == 4 && HandSessionLifecycle.ActiveRoomId() == 0,
            "exit cleanup callbacks cannot recurse while the other store is still active");
    }
}
namespace Tools { public static class SimpleSingletonProvider<T> where T:new() { public static T inst = new T(); } }
namespace Core.Scene { public enum SceneType { Home, Battle } public class CurrentScene { public SceneType Value; } public class SceneManager { public CurrentScene currentType = new CurrentScene(); } }
namespace GameLogic {
    public enum RoomStateType { RUNNING, SETTLEMENT }
    public class Info { public long StartTime; }
    public class Room { public long Id; public Info info; }
    public class Controller { public RoomStateType roomStateType; }
    public class RoomLogic { public Room curRoomInfo; public Controller roomController = new Controller(); }
    public class Watch { public bool Watching; public bool PlayerIsWatcher() => Watching; }
    public class GameLogicManager { public RoomLogic room = new RoomLogic(); public Watch watch = new Watch(); }
}
namespace XingShu.Truth { public static class HandTruthSource { public static bool IsInSession; public static Action OnReset; public static void ResetSession(string _) { IsInSession = false; OnReset?.Invoke(); } } }
namespace XingShu.Spectator { public static class SpectatorStateStore { public static bool Active; public static long RoomId; public static long SpectatorSessionEpoch; public static Action OnReset; public static bool IsInSpectatorSession() => Active; public static void Reset() { Active = false; RoomId = 0; SpectatorSessionEpoch = 0; OnReset?.Invoke(); } } }
