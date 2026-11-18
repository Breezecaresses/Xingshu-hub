using System;
using System.Collections.Generic;
using Core.Scene;
using Core.Unit;
using GameLogic;
using party.model;
using UnityEngine;
using XingShuRuntime.Targeting;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public static readonly Vector3 one = new Vector3(1f, 1f, 1f);
        public static readonly Vector3 zero = new Vector3(0f, 0f, 0f);
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public class Transform
    {
        public Vector3 localScale = Vector3.one;
        public Vector3 localPosition = Vector3.zero;
        public Vector3 forward = new Vector3(0f, 0f, 1f);
    }

    public class GameObject
    {
        public bool activeInHierarchy = true;
    }

    public class Component
    {
        private static int _nextId = 100;
        private readonly int _id = _nextId++;
        public Transform transform = new Transform();
        public GameObject gameObject = new GameObject();
        public int GetInstanceID() => _id;
    }
}

namespace Core.Scene
{
    public enum SceneType { None, Home, Battle }
}

namespace Core.Unit
{
    public class Effect : Component { }

    public class CharacterMove
    {
        private readonly List<Effect> _walkDirections = new List<Effect>();

        public void Draw(params Effect[] effects)
        {
            _walkDirections.Clear();
            foreach (var effect in effects) _walkDirections.Add(effect);
        }
    }

    public class Character
    {
        private readonly CharacterMove move = new CharacterMove();
        public CharacterMove Movement => move;
    }
}

namespace party.model
{
    public static class Hero
    {
        public static class Types
        {
            public enum MonsterType { Normal, Boss, PveAllyMonster }
        }
    }

    public class HeroInfo
    {
        public Hero.Types.MonsterType MonsterType = Hero.Types.MonsterType.Normal;
    }
}

namespace GameLogic
{
    public enum CharacterType { Hero, Monster }

    public class RoomPlayer
    {
        public long Id;
        public HeroInfo Hero = new HeroInfo();
    }

    public class Number { public int Value = 10; }
    public class Property { public Number HP = new Number(); }

    public class BattlePlayerData
    {
        public CharacterType characterType;
        public RoomPlayer player;
        public Property Property = new Property();
        public Character CharacterInst;
    }

    public class RoomInfo { public long Id = 1; }
    public class RoomLogic { public RoomInfo curRoomInfo = new RoomInfo(); }
    public class BattleLogic { public List<BattlePlayerData> PlayerDatas = new List<BattlePlayerData>(); }

    public class GameLogicManager
    {
        public RoomLogic room = new RoomLogic();
        public BattleLogic battle = new BattleLogic();
    }
}

internal static class MonsterArrowBoostTests
{
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static bool Approx(Vector3 a, Vector3 b) => MonsterWalkArrowScaleMath.Approximately(a, b);

    public static int Main()
    {
        var boost = new MonsterWalkArrowBoost();
        var logic = new GameLogicManager();

        var hostileArrow = new Effect();
        hostileArrow.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        hostileArrow.transform.forward = new Vector3(1f, 0f, 0f);
        hostileArrow.transform.localPosition = new Vector3(2f, 0f, -3f);

        var hostile = new BattlePlayerData
        {
            characterType = CharacterType.Monster,
            player = new RoomPlayer { Id = 101 },
            CharacterInst = new Character()
        };
        hostile.CharacterInst.Movement.Draw(hostileArrow);

        var allyArrow = new Effect();
        var ally = new BattlePlayerData
        {
            characterType = CharacterType.Monster,
            player = new RoomPlayer { Id = 102, Hero = new HeroInfo { MonsterType = Hero.Types.MonsterType.PveAllyMonster } },
            CharacterInst = new Character()
        };
        ally.CharacterInst.Movement.Draw(allyArrow);

        var heroArrow = new Effect();
        var hero = new BattlePlayerData
        {
            characterType = CharacterType.Hero,
            player = new RoomPlayer { Id = 103 },
            CharacterInst = new Character()
        };
        hero.CharacterInst.Movement.Draw(heroArrow);

        logic.battle.PlayerDatas.Add(hostile);
        logic.battle.PlayerDatas.Add(ally);
        logic.battle.PlayerDatas.Add(hero);

        // 1. Hostile monster arrow gets scaled 2.2x; ally and hero stay untouched
        boost.Sync(true, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(3.3f, 3.3f, 3.3f)), "hostile monster arrow boosted 2.2x from native baseline");
        Check(Approx(hostileArrow.transform.forward, new Vector3(1f, 0f, 0f)), "arrow forward stays untouched");
        Check(Approx(hostileArrow.transform.localPosition, new Vector3(2f, 0f, -3f)), "arrow position stays untouched");
        Check(Approx(allyArrow.transform.localScale, Vector3.one), "ally monster arrow stays untouched");
        Check(Approx(heroArrow.transform.localScale, Vector3.one), "hero arrow stays untouched");

        // 2. Repeated frame sync is strictly idempotent (never compounds)
        boost.Sync(true, logic, SceneType.Battle, true);
        boost.Sync(true, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(3.3f, 3.3f, 3.3f)), "repeated syncs do not compound scale");

        // 3. Pooled arrow reset by game to native scale re-applies cleanly once
        hostileArrow.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);
        boost.Sync(true, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(4.4f, 4.4f, 4.4f)), "re-pooled arrow with new baseline scales accurately");

        // 4. Monster death restores arrow scale
        hostile.Property.HP.Value = 0;
        boost.Sync(true, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(2.0f, 2.0f, 2.0f)), "dead monster restores baseline scale");

        // 5. Alive again -> boosted; disabled -> restored; non-PVE -> restored
        hostile.Property.HP.Value = 5;
        boost.Sync(true, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(4.4f, 4.4f, 4.4f)), "alive hostile monster re-boosted");

        boost.Sync(false, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(2.0f, 2.0f, 2.0f)), "disabling feature restores baseline scale");

        boost.Sync(true, logic, SceneType.Battle, true);
        boost.Sync(true, logic, SceneType.Battle, false);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(2.0f, 2.0f, 2.0f)), "leaving PVE restores baseline scale");

        // 6. Pooled arrow already reset by another owner is NOT clobbered on restore
        boost.Sync(true, logic, SceneType.Battle, true);
        hostile.CharacterInst.Movement.Draw(); // arrow released to pool
        hostileArrow.transform.localScale = new Vector3(0.75f, 0.75f, 0.75f); // reused elsewhere
        boost.Sync(true, logic, SceneType.Battle, true);
        Check(Approx(hostileArrow.transform.localScale, new Vector3(0.75f, 0.75f, 0.75f)), "pooled arrow reused by another owner is not overwritten");

        Console.WriteLine("Arrow boost tests: " + _passed + "/12 passed");
        return _passed == 12 ? 0 : 1;
    }
}
