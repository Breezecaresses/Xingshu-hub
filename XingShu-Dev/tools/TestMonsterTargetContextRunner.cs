using System;
using System.Collections.Generic;
using XingShuRuntime.Targeting;
using Core.Unit;
using GameLogic;

// Minimal read-only game doubles; compile the real context extractor against them.
namespace UnityEngine
{
    public static class Debug { public static void LogWarning(string message) { throw new Exception(message); } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float length = (float)Math.Sqrt(sqrMagnitude); return new Vector3(x / length, y / length, z / length); } }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public class Transform { public Vector3 localPosition; public Vector3 forward; }
    public class GameObject { public bool activeInHierarchy = true; }
    public class Component { public Transform transform = new Transform(); public GameObject gameObject = new GameObject(); }
}
namespace Core.Unit
{
    public enum CharacterType { Hero, Monster }
    public class UnitLand : UnityEngine.Component { public int Id; public List<int> AdjacencyLandIds = new List<int>(); }
    public class Effect : UnityEngine.Component { }
    public class CharacterMove
    {
        private readonly List<Effect> _walkDirections = new List<Effect>();
        public void Draw(params UnityEngine.Vector3[] directions)
        {
            _walkDirections.Clear();
            foreach (var direction in directions) { var effect = new Effect(); effect.transform.forward = direction; _walkDirections.Add(effect); }
        }
    }
    public class Character
    {
        public UnitLand standLand;
        private readonly CharacterMove move = new CharacterMove();
        public CharacterMove Movement => move;
    }
    public class LandManager { public static LandManager inst = new LandManager(); public Dictionary<int, UnitLand> NodeDict = new Dictionary<int, UnitLand>(); }
}
namespace GameLogic
{
    public class Number { public int Value = 1; }
    public class Property { public Number HP = new Number(); }
    public class Player { public long Id; public int NodeId; public int Slot; public int Progress = 99; public List<int> FrontIds = new List<int>(); }
    public class BattlePlayerData { public CharacterType characterType; public Player player; public Property Property = new Property(); public Character CharacterInst; }
    public class Battle { public List<BattlePlayerData> PlayerDatas = new List<BattlePlayerData>(); }
    public class GameLogicManager { public static GameLogicManager inst = new GameLogicManager(); public Battle battle = new Battle(); }
}
internal static class ContextTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) { Console.WriteLine("FAIL: " + name); throw new Exception("FAIL: " + name); }
        passed++;
        Console.WriteLine("PASS: " + name);
    }
    private static BattlePlayerData Monster(long id, int node) => new BattlePlayerData
    { characterType = CharacterType.Monster, player = new Player { Id = id, NodeId = node } };
    public static int Main()
    {
        var nodes = LandManager.inst.NodeDict;
        nodes[1] = new UnitLand { Id = 1, AdjacencyLandIds = new List<int> { 2 } };
        nodes[2] = new UnitLand { Id = 2, AdjacencyLandIds = new List<int> { 1 } };
        var players = GameLogicManager.inst.battle.PlayerDatas;
        players.Add(Monster(7, 1));
        players.Add(new BattlePlayerData { characterType = CharacterType.Hero, player = new Player { Id = 9, NodeId = 2, Slot = 1 } });
        players.Add(Monster(5000000000L, 2));
        var selected = MonsterTargetContext.ExtractInput(5000000000L);
        Check(selected.MonsterId == 5000000000L && selected.CurrentNode == 2, "exact 64-bit monster ID");
        var missing = MonsterTargetContext.ExtractInput(12345L);
        Check(missing.CurrentNode == -1 && !missing.MonsterNodeKnown, "missing monster never borrows another monster's position");
        var first = MonsterTargetContext.ExtractInput();
        Check(first.MonsterId == 7 && first.CurrentNode == 1, "implicit selection stays on first monster");
        Check(first.Players.Count == 1 && !first.Players[0].RouteProgressKnown && first.Players[0].SlotKnown, "rank progress is not route order");
        nodes[1].AdjacencyLandIds.Clear();
        Check(first.GraphEdges[1].Count == 1, "graph snapshot is independent from live neighbor lists");
        players[0].CharacterInst = new Character { standLand = new UnitLand { Id = 8 } };
        Check(MonsterTargetContext.ExtractInput(7).CurrentNode == 8, "visible stand-land takes precedence over destination node");
        players.Add(new BattlePlayerData { player = null });
        players.Add(Monster(-5000000000L, 2));
        Check(MonsterTargetContext.ExtractInput(-5000000000L).CurrentNode == 2, "null entries and signed 64-bit monster IDs");
        var graph = new Dictionary<int, List<int>>
        {
            { 1, new List<int>() }, { 2, new List<int> { 1, 3, 4 } },
            { 3, new List<int>() }, { 4, new List<int>() }
        };
        var facing = Monster(12, 2);
        facing.player.FrontIds.Add(3);
        var forward = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        Check(forward.Count == 1 && forward[0] == 3, "server arrow excludes other adjacent fork exits");
        facing.player.FrontIds[0] = 1;
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph)[0] == 1, "changed arrow replaces previous facing");
        facing.player.FrontIds.Clear();
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "empty arrow list never guesses all neighbors");
        facing.player.FrontIds.Add(99);
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "invalid arrow waits for synchronization");
        facing.player.FrontIds[0] = 3;
        facing.CharacterInst = new Character { standLand = new UnitLand { Id = 1 } };
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "moving character never combines old tile and destination arrows");
        facing.CharacterInst = null;
        facing.player.FrontIds.Add(3);
        var copied = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        facing.player.FrontIds.Clear();
        Check(copied.Count == 1 && copied[0] == 3, "arrows are deduplicated and independently snapshotted");
        facing.player.FrontIds = null;
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "missing arrow data stays unresolved");
        facing.player.FrontIds = new List<int> { 4 };
        facing.CharacterInst = null;
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph)[0] == 4, "server arrows remain usable before character creation");
        facing.player.FrontIds.Add(3);
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph).Count == 2, "multiple real arrows remain candidates");

        nodes[2].AdjacencyLandIds = new List<int>(graph[2]);
        nodes[1].transform.localPosition = new UnityEngine.Vector3(-1, 0, 0);
        nodes[3] = new UnitLand { Id = 3 };
        nodes[3].transform.localPosition = new UnityEngine.Vector3(1, 0, 0);
        nodes[4] = new UnitLand { Id = 4 };
        nodes[4].transform.localPosition = new UnityEngine.Vector3(0, 0, 1);
        facing.CharacterInst = new Character { standLand = nodes[2] };
        facing.player.NodeId = 1; facing.player.FrontIds = new List<int> { 99 };
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(1, 0, 0));
        forward = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        Check(forward != null && forward.Count == 1 && forward[0] == 3, "post-move visible arrow works despite stale server position and FrontIds");
        facing.player.NodeId = 2; facing.player.FrontIds = new List<int> { 1 };
        forward = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        Check(forward != null && forward[0] == 3, "visible forward route wins over an old but adjacent backward server arrow");
        facing.CharacterInst.Movement.Draw();
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "deleted movement arrows never reuse a stale server direction");
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(0, 0, 1));
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph)[0] == 4, "redrawn arrow replaces the previous visible facing");
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(1, 0, 0), new UnityEngine.Vector3(0, 0, 1));
        forward = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        Check(forward.Count == 2 && forward.Contains(3) && forward.Contains(4) && !forward.Contains(1), "visible fork keeps both forward exits and excludes the backward exit");
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(1, 0, 0), new UnityEngine.Vector3(1, 0, 0));
        copied = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(0, 0, 1));
        Check(copied.Count == 1 && copied[0] == 3, "visible arrows are independently snapshotted and deduplicated");
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(1, 0, 1));
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "arrow pointing between exits is not guessed");
        facing.CharacterInst.Movement.Draw(new UnityEngine.Vector3(1, 0, 0));
        nodes[4].transform.localPosition = new UnityEngine.Vector3(2, 0, 0);
        Check(MonsterTargetContext.ExtractForwardSteps(facing, graph) == null, "collinear map exits remain unresolved rather than picking the first");
        nodes[4].transform.localPosition = new UnityEngine.Vector3(0, 0, 1);
        facing.player.FrontIds.Clear();
        forward = MonsterTargetContext.ExtractForwardSteps(facing, graph);
        Check(forward.Count == 1 && forward[0] == 3 && facing.player.FrontIds.Count == 0 && facing.player.NodeId == 2,
            "visible route reads never mutate the server position or arrow list");
        Console.WriteLine("Context tests: " + passed + "/25 passed");
        return passed == 25 ? 0 : 1;
    }
}
