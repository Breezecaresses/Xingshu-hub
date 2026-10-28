"""Audit incomplete official character rows without guessing unreleased effects.

The input is an unmodified local Unity configuration bundle.  Only unnamed
characters are projected; released characters and the source bundle are never
changed.  Raw references remain in the audit, separate from display labels.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ROLE_NAMES = {0: "未分类", 1: "通用", 2: "攻击", 3: "卡牌", 4: "支援", 5: "防御"}


def index(tables, name, group="Infos"):
    return {r["Id"]: r for r in tables.get(name, {}).get(group, [])}


def local(tables, table, identifier):
    return index(tables, "STR" + table, "Locals").get(identifier, {}).get("Simplified", "")


def project_preview(character, tables, available_assets):
    """Return a safe display row plus evidence, retaining exact numeric values."""
    row = index(tables, "Character")[character["id"]]
    if local(tables, "Character", row.get("NameID")).strip():
        return copy.deepcopy(character)
    result = copy.deepcopy(character)
    cid = row["Id"]
    references = []
    missing_text = []
    items = index(tables, "Item")
    skills = index(tables, "Skill")
    buffs = index(tables, "Buff")
    all_characters = index(tables, "Character")
    available_assets = set(available_assets)

    def text(table, identifier):
        value = local(tables, table, identifier)
        if identifier and not value:
            entry = {"table": "STR" + table, "id": identifier}
            if entry not in missing_text:
                missing_text.append(entry)
        return value

    def reference(field, value, reason):
        references.append({"field": field, "value": value, "reason": reason})

    result.update(
        name=text("Character", row.get("NameID")),
        title=text("Character", row.get("NickID")),
        biography=text("Character", row.get("BiographyID")),
        comment=text("Character", row.get("CommentId")),
        hp=row.get("Blood", 0), attack=row.get("Attack", 0), defense=row.get("Defense", 0),
        hasBond=row.get("HasKizuna", False),
        isLinkage=row.get("IsLinkage", False),
        category=ROLE_NAMES.get(row.get("TagType", 0), "未分类"),
    )
    # A valid asset for another released character is still a placeholder here.
    borrowed_image = next((c["Id"] for c in all_characters.values()
                           if c["Id"] != cid and c.get("CharacterMap") == row.get("CharacterMap")), None)
    if borrowed_image:
        reference("角色头像", row["CharacterMap"], f"与角色 #{borrowed_image} 共用，不能作为该角色的正式形象。")
        result["image"] = ""
    character_item = items.get(cid, {})
    if character_item.get("NameID") and character_item["NameID"] != cid:
        reference("角色物品名称", f"STRItem:{character_item['NameID']} · {local(tables, 'Item', character_item['NameID'])}",
                  "角色物品沿用了其他角色的名称；图鉴和奖励物品显示名均改为预更新角色编号。")

    def skill(sid):
        source = skills.get(sid, {})
        state_rows = []
        for bid in source.get("BuffId", []):
            state = buffs.get(bid, {})
            state_rows.append({
                "id": bid, "name": text("Buff", state.get("NameId")),
                "description": text("Buff", state.get("DescId")),
                "visible": state.get("IsShow", False),
                "clearedOnDeath": state.get("IsDeathClear", False),
                "duration": state.get("KeepRound", 0),
                "durationType": state.get("BuffRoundCountType", 0),
                "cardIds": state.get("EffectCardIds", []),
            })
        return {
            "id": sid, "name": text("Skill", source.get("NameID")),
            "description": text("Skill", source.get("DescID")),
            "cooldown": source.get("Round", 0),
            "kind": "active" if source.get("SkillType") == 1 else "passive",
            "params": source.get("Params", []), "buffs": state_rows,
            "relatedCharacterIds": sorted({v for v in source.get("Params", []) if v in all_characters}),
        }

    result["skills"] = [skill(sid) for sid in [row.get("ActiveSkill", 0)] + row.get("PassiveSkills", []) if sid]
    result["pveSkills"] = [skill(sid) for sid in [row.get("PveActiveSkill", 0)] + row.get("PvePassiveSkills", []) if sid]
    skin_group = index(tables, "Skin", "StandingPaintings").get(row.get("StandingPainting"), {})
    source_skins = {s.get("ItemID"): s for s in skin_group.get("SkinStandingPaintingConfigureItems", [])}
    for skin in result["skins"]:
        source = source_skins.get(skin["id"], {})
        item = items.get(skin["id"], {})
        own_text_id = skin["id"]
        name_id = item.get("NameID", 0)
        source_name = local(tables, "Item", name_id)
        if name_id and name_id != own_text_id and name_id in items:
            reference("初始装扮名称", f"STRItem:{name_id} · {source_name}", "物品引用了其他装扮的名称；显示中改为中性占位名称。")
            skin["name"] = "初始装扮（名称待更新）" if skin.get("default") else "装扮（名称待更新）"
        elif not source_name:
            skin["name"] = "初始装扮（名称待更新）" if skin.get("default") else "装扮（名称待更新）"
        for field, label in (("CharacterBattlleRes", "战斗资源"), ("CharacterLabel", "模型标签"),
                             ("PreviewVideo", "装扮预览视频"), ("Voice", "语音组")):
            value = source.get(field)
            if value and ((field == "CharacterBattlleRes" and value != skin["id"])
                          or (field == "CharacterLabel" and value != f"Hero{cid}")
                          or (field == "PreviewVideo" and str(skin["id"]) not in value)
                          or (field == "Voice" and value // 1000 % 1000 != cid)):
                reference(label, value, "引用了其他角色的资源，不能据此确认该角色的身份或正式表现。")
    emote_group = index(tables, "Character", "ExpressionPacks").get(row.get("ExpressionPackID"), {})
    for ordinal, emote in enumerate(result.get("emotes", []), 1):
        item = items.get(emote["id"], {})
        name_id = item.get("NameID", 0)
        if name_id != emote["id"]:
            reference(f"表情 {ordinal} 名称", f"STRItem:{name_id} · {local(tables, 'Item', name_id)}", "沿用了其他表情的名称；不视为新角色姓名证据。")
            emote["name"] = f"表情 {ordinal}（名称待更新）"
    if not result["hasBond"]:
        if row.get("FavorLevelReward") or row.get("FavorBreakthroughReward"):
            reference("羁绊奖励与突破", f"Favor:{row.get('FavorLevelReward', 0)} / {row.get('FavorBreakthroughReward', 0)}", "HasKizuna 未启用；共享模板不作为该角色的可用羁绊或奖励展示。")
        result["bondRewards"] = []
        result["breakthrough"] = {}
    enhancements = index(tables, "PVENurturance", "Enhancements").get(cid, {})
    result["pveProgression"] = [
        {"level": e.get("Lv", 0), "description": text("PVENurturance", e.get("DescriptionID")),
         "descriptionId": e.get("DescriptionID", 0)}
        for e in enhancements.get("PVENurturanceEnhancementConfigureItems", [])
    ]
    declared_assets = sorted({s["image"] for s in result["skins"] if s.get("image")} |
                             {e["image"] for e in result.get("emotes", []) if e.get("image")})
    absent = [key for key in declared_assets if key not in available_assets]
    result["preview"] = {
        "status": "incomplete_official_configuration",
        "missing": [label for label, present in (
            ("角色名", result["name"]), ("称号", result["title"]), ("档案", result["biography"]),
            ("技能名称与说明", all(s["name"] and s["description"] for s in result["skills"] + result["pveSkills"])),
            ("立绘与表情图片", not absent), ("潜能", bool(row.get("PveBreak"))),
        ) if not present],
        "missingTextIds": missing_text,
        "placeholderReferences": references,
        "unavailableAssets": absent,
        "skinSlots": len(source_skins),
        "emoteSlots": len(emote_group.get("CharacterExpressionPackConfigureItems", [])),
        "growthDescriptionIds": [e.get("DescriptionID", 0) for e in enhancements.get("PVENurturanceEnhancementConfigureItems", [])],
        "notes": ["数值为当前预装配置，可能在上线前调整。", "参数顺序不等于效果含义，空白文案不按猜测补写。",
                  "装扮和表情条目已声明，不代表相应图片已随本机资源提供。"],
    }
    return result


def apply_previews(catalog, tables, available_assets):
    result = copy.deepcopy(catalog)
    result["characters"] = [project_preview(c, tables, available_assets) for c in catalog["characters"]]
    for character in result["characters"]:
        if not character.get("preview"):
            continue
        names = result.setdefault("itemNames", {})
        names[str(character["id"])] = f"预更新角色 #{character['id']}"
        for item in character["skins"] + character.get("emotes", []):
            names[str(item["id"])] = item["name"]
    return result


def read_tables(bundle):
    import UnityPy
    from export_game_catalog import decode, SCHEMA
    result = {}
    for obj in UnityPy.load(str(bundle)).objects:
        if obj.type.name != "TextAsset":
            continue
        value = obj.read()
        if value.m_Name + "Configure" in SCHEMA:
            result[value.m_Name] = decode(value.m_Script.encode("utf8", "surrogateescape"), value.m_Name + "Configure")
    if "Character" not in result or "Skill" not in result:
        raise ValueError("Not an official character/skill configuration bundle")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config-bundle", required=True, type=Path)
    parser.add_argument("--catalog", type=Path, default=ROOT / "AstralParty-Hub/src/shared/gameCatalog.generated.json")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text("utf8"))
    source_hash = hashlib.sha256(args.config_bundle.read_bytes()).hexdigest()
    if source_hash != catalog["configSha256"]:
        raise ValueError("Configuration SHA256 differs from the current encyclopedia; export its matching version first")
    tables = read_tables(args.config_bundle)
    result = apply_previews(catalog, tables, catalog["assets"])
    previews = [c for c in result["characters"] if c.get("preview")]
    raw = {name: tables.get(name, {}) for name in ("Character", "Skill", "Buff", "Skin", "PVENurturance", "BattleResource")}
    report = {
        "formatVersion": 1, "configurationSha256": source_hash,
        "sourceBundle": str(args.config_bundle.resolve()),
        "sourceUnmodified": True, "changedReleasedCharacters": [],
        "characters": previews,
    }
    before = {c["id"]: c for c in catalog["characters"]}
    report["changedReleasedCharacters"] = [c["id"] for c in result["characters"] if not c.get("preview") and c != before[c["id"]]]
    if report["changedReleasedCharacters"]:
        raise AssertionError("Released characters must be unchanged")
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "preview-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", "utf8")
    # Complete source rows are local research evidence, not shipped runtime data.
    (args.out / "preview-source-tables.json").write_text(json.dumps(raw, ensure_ascii=False, indent=2) + "\n", "utf8")
    if args.apply:
        args.catalog.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", "utf8")
    print(json.dumps({"previewIds": [c["id"] for c in previews], "releasedUnchanged": True, "applied": args.apply}, ensure_ascii=False))


if __name__ == "__main__":
    main()
