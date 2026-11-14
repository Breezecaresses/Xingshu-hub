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

        // CloudMirrorStore tests
        var store = new CloudMirrorStore();
        var t0 = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        System.Collections.Generic.IReadOnlyDictionary<long, System.Collections.Generic.IReadOnlyList<int>> MakeHands(params int[] cards)
            => new System.Collections.Generic.Dictionary<long, System.Collections.Generic.IReadOnlyList<int>> { [202] = new System.Collections.Generic.List<int>(cards).AsReadOnly() };

        Check(store.Record(71, 1, 1, t0.AddSeconds(60), MakeHands(101, 102), t0),
            "cloud mirror accepts modest future clock skew (+60s)");
        Check(!store.Record(71, 1, 2, t0.AddMinutes(10), MakeHands(103), t0),
            "cloud mirror rejects excessive future clock skew (+10m)");
        Check(!store.Record(71, 1, 3, t0.AddSeconds(-150), MakeHands(103), t0),
            "cloud mirror rejects stale observedAt (>=120s)");
        Check(!store.Record(71, 1, 4, t0.AddSeconds(50), MakeHands(999), t0.AddSeconds(5)),
            "cloud mirror rejects older remote observedAt on same clock domain");
        Check(store.Record(71, 1, 5, t0.AddSeconds(60), MakeHands(101, 102, 103, 104, 105), t0.AddSeconds(45)),
            "cloud mirror heartbeat at same observedAt refreshes receipt time during slow turns");
        Check(store.TryGetPlayerCards(71, 202, out var updatedCards, t0.AddSeconds(80)) && updatedCards.Count == 5,
            "cloud mirror survives 80s total idle elapsed when refreshed within 60s lifetime");
        Check(!store.TryGetPlayerCards(71, 202, out _, t0.AddSeconds(110)),
            "cloud mirror expires when unrefreshed beyond 60s lifetime");
        Check(store.Record(71, 2, 1, t0.AddSeconds(40), MakeHands(), t0.AddSeconds(110)),
            "cloud mirror rematch epoch accepts updated empty hand");
        Check(store.TryGetPlayerCards(71, 202, out var emptyCards, t0.AddSeconds(115)) && emptyCards.Count == 0,
            "cloud mirror preserves empty teammate hand");
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
