import unittest

from audit_monster_cooldowns import audit


class MonsterCooldownInventoryTests(unittest.TestCase):
    def tables(self):
        return {
            "Monster": {
                "Infos": [{"Id": 1, "ActiveSkill": 101}, {"Id": 2}],
                "Attributes": [{"Id": 1, "MonsterAttributeConfigureItems": [
                    {}, {"Index": 1, "PveActiveSkill": 102},
                ]}],
            },
            "Skill": {"Infos": [{"Id": 101, "Round": 3}, {"Id": 102, "Round": 99}]},
            "STRMonster": {"Locals": []}, "STRSkill": {"Locals": []},
        }

    def test_explicit_no_skill_difficulty_does_not_fall_back_to_default(self):
        result = audit(self.tables())
        variants = result["monsters"][0]["variants"]
        self.assertEqual([(v["skillId"], v["configuredCooldown"]) for v in variants], [(101, 3), (0, None), (102, 99)])
        self.assertEqual(result["difficultyChanges"], [1])
        self.assertEqual(result["monstersWithoutActiveSkill"], 1)
        self.assertFalse(result["errors"])

    def test_missing_skill_fails_instead_of_becoming_zero_cooldown(self):
        tables = self.tables()
        tables["Skill"]["Infos"].pop()
        self.assertIn("missing active skill 102", audit(tables)["errors"][0])

    def test_zero_round_is_distinct_from_missing_skill_definition(self):
        tables = self.tables()
        tables["Skill"]["Infos"][0].pop("Round")
        result = audit(tables)
        self.assertEqual(result["monsters"][0]["variants"][0]["configuredCooldown"], 0)
        self.assertFalse(result["errors"])

    def test_duplicate_difficulty_fails(self):
        tables = self.tables()
        tables["Monster"]["Attributes"][0]["MonsterAttributeConfigureItems"].append({"PveActiveSkill": 101})
        self.assertIn("duplicate difficulty 0", audit(tables)["errors"][0])


if __name__ == "__main__":
    unittest.main()
