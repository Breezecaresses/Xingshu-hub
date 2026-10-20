using System;
using System.Collections.Generic;
using APXRuntime.Targeting;
using Core.Unit;
using GameLogic;

// Minimal read-only game doubles; compile the real context extractor against them.
namespace UnityEngine { public static class Debug { public static void LogWarning(string message) { throw new Exception(message); } } }
namespace Core.Unit
{
    public enum CharacterType { Hero, Monster }
    public class UnitLand { public int Id; public List<int> AdjacencyLandIds = new List<int>(); }
    public class LandManager { public static LandManager inst = new LandManager(); public Dictionary<int, UnitLand> NodeDict = new Dictionary<int, UnitLand>(); }
}
namespace GameLogic
{
    public class Number { public int Value = 1; }
    public class Property { public Number HP = new Number(); }
    public class Character { public UnitLand standLand; }
    public class Player { public long Id; public int NodeId; public int Slot; public int Progress = 99; }
    public class BattlePlayerData { public CharacterType characterType; public Player player; public Property Property = new Property(); public Character CharacterInst; }
    public class Battle { public List<BattlePlayerData> PlayerDatas = new List<BattlePlayerData>(); }
    public class GameLogicManager { public static GameLogicManager inst = new GameLogicManager(); public Battle battle = new Battle(); }
}
internal static class ContextTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
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
        Check(missing.CurrentNode == 0 && !missing.MonsterNodeKnown, "missing monster never borrows another monster's position");
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
        Console.WriteLine("Context tests: " + passed + "/7 passed");
        return passed == 7 ? 0 : 1;
    }
}
