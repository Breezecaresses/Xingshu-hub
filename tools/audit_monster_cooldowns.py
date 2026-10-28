"""Inventory every monster active cooldown from an unmodified official config.

PVE difficulty rows override even with an explicit zero skill, exactly as
MonsterInfoConfigure.GetBattleActiveSkillId does. A configured maximum is not
evidence of a live actor's remaining cooldown.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def audit(tables):
    monsters = tables["Monster"]["Infos"]
    attributes = {row["Id"]: row for row in tables["Monster"].get("Attributes", [])}
    skills = {row["Id"]: row for row in tables["Skill"]["Infos"]}
    texts = {
        table: {row["Id"]: row.get("Simplified", "") for row in tables[table].get("Locals", [])}
        for table in ("STRMonster", "STRSkill")
    }
    result, errors = [], []
    for monster in monsters:
        variants = [{"mode": "default", "difficulty": -1, "skillId": monster.get("ActiveSkill", 0)}]
        seen = set()
        for row in attributes.get(monster["Id"], {}).get("MonsterAttributeConfigureItems", []):
            difficulty = row.get("Index", 0)
            if difficulty in seen:
                errors.append(f"Monster {monster['Id']}: duplicate difficulty {difficulty}")
            seen.add(difficulty)
            variants.append({"mode": "pve", "difficulty": difficulty, "skillId": row.get("PveActiveSkill", 0)})
        for variant in variants:
            sid = variant["skillId"]
            skill = skills.get(sid)
            variant["hasActiveSkill"] = sid > 0
            variant["configuredCooldown"] = skill.get("Round", 0) if skill is not None else None
            variant["skillName"] = texts["STRSkill"].get(skill.get("NameID"), "") if skill else ""
            if sid and skill is None:
                errors.append(f"Monster {monster['Id']}: missing active skill {sid}")
            if skill and variant["configuredCooldown"] < 0:
                errors.append(f"Monster {monster['Id']}: negative configured cooldown {sid}")
        result.append({
            "id": monster["Id"], "name": texts["STRMonster"].get(monster.get("NameID"), ""),
            "variants": variants,
            "hasActiveSkill": any(row["hasActiveSkill"] for row in variants),
            "difficultyChanges": len({row["skillId"] for row in variants if row["mode"] == "pve"}) > 1,
        })
    active = [row for monster in result for row in monster["variants"] if row["hasActiveSkill"]]
    return {
        "monsterCount": len(result),
        "monstersWithActiveSkill": sum(row["hasActiveSkill"] for row in result),
        "monstersWithoutActiveSkill": sum(not row["hasActiveSkill"] for row in result),
        "activeSkillCount": len({row["skillId"] for row in active}),
        "activeVariantCount": len(active),
        "difficultyChanges": [row["id"] for row in result if row["difficultyChanges"]],
        "configuredCooldowns": sorted({row["configuredCooldown"] for row in active if row["configuredCooldown"] is not None}),
        "errors": errors, "monsters": result,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--config-bundle", type=Path)
    source.add_argument("--tables", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.config_bundle:
        from audit_preview_characters import read_tables
        tables = read_tables(args.config_bundle)
        identity = {"configSha256": hashlib.sha256(args.config_bundle.read_bytes()).hexdigest()}
    else:
        tables = json.loads(args.tables.read_text("utf8"))
        identity = {"tablesSha256": hashlib.sha256(args.tables.read_bytes()).hexdigest()}
    result = {"formatVersion": 1, **identity, **audit(tables)}
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "all-monster-cooldowns.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", "utf8")
    # Numeric-only fixtures are consumed by the independent ledger regression.
    with (args.out / "cooldown-variants.csv").open("w", newline="", encoding="utf8") as stream:
        writer = csv.writer(stream)
        writer.writerow(["monsterId", "skillId", "cooldown", "difficulty"])
        for monster in result["monsters"]:
            for row in monster["variants"]:
                if row["hasActiveSkill"] and row["configuredCooldown"] is not None:
                    writer.writerow([monster["id"], row["skillId"], row["configuredCooldown"], row["difficulty"]])
    lines = ["| 怪物 | 默认技能 / CD | 合作难度技能 / CD |", "| --- | --- | --- |"]
    for monster in result["monsters"]:
        def label(row):
            return f"{row['skillId']} / {row['configuredCooldown']}" if row["hasActiveSkill"] else "无主动技能"
        pve = [f"{row['difficulty']}：{label(row)}" for row in monster["variants"] if row["mode"] == "pve"]
        lines.append(f"| {monster['id']} {monster['name']} | {label(monster['variants'][0])} | {'；'.join(pve) or '沿用默认'} |")
    (args.out / "all-monster-cooldowns.md").write_text("\n".join(lines) + "\n", "utf8")
    print(json.dumps({key: value for key, value in result.items() if key != "monsters"}, ensure_ascii=False))
    if result["errors"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
