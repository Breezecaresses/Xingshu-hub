#!/usr/bin/env python3
"""
PROJECT XINGSHU — HS4-P1.4
Single Unified Code Generation Pipeline for In-Game Card Registries

Reads:
- card_metadata_official.json (69 cards)
- card_names_zh_cn_official.json (69 cards)
- CARD_ASSET_INVENTORY_V2.csv (69 cards)
- runtime_card_registry.generated.json (4 cards: 21026, 21027, 21028, 21029)

Generates:
- AstralParty-Hub/src/shared/card_registry.generated.json (strictly 69 static cards, 0 fallback)
- AstralParty-Hub/src/shared/runtime_card_registry.generated.json (4 runtime cards)
- APX-Dev/src/APX.hooks/XingShu/Cards/CardRegistry.Generated.cs (all 73 cards)
"""

import argparse
import csv
import io
import json
import os
import sys

def escape_cs_string(s: str) -> str:
    """Escapes string for C# string literal."""
    if s is None:
        return '""'
    return s.replace('\\', '\\\\').replace('"', '\\"').replace('\r', '\\r').replace('\n', '\\n')

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.abspath(os.path.join(script_dir, "..", ".."))

    default_meta = os.path.join(repo_root, "card_metadata_official.json")
    default_names = os.path.join(repo_root, "card_names_zh_cn_official.json")
    default_inv = os.path.join(repo_root, "CARD_ASSET_INVENTORY_V2.csv")
    default_runtime = os.path.join(repo_root, "AstralParty-Hub", "src", "shared", "runtime_card_registry.generated.json")
    if not os.path.isfile(default_runtime):
        default_runtime = os.path.join(repo_root, "runtime_card_registry.generated.json")

    default_hub_static = os.path.join(repo_root, "AstralParty-Hub", "src", "shared", "card_registry.generated.json")
    default_hub_runtime = os.path.join(repo_root, "AstralParty-Hub", "src", "shared", "runtime_card_registry.generated.json")
    default_cs = os.path.join(repo_root, "APX-Dev", "src", "APX.hooks", "XingShu", "Cards", "CardRegistry.Generated.cs")

    parser = argparse.ArgumentParser(description="Generate canonical card registries for Hub and C# APX.hooks")
    parser.add_argument("--metadata", default=default_meta, help="Path to card_metadata_official.json")
    parser.add_argument("--names", default=default_names, help="Path to card_names_zh_cn_official.json")
    parser.add_argument("--inventory", default=default_inv, help="Path to CARD_ASSET_INVENTORY_V2.csv")
    parser.add_argument("--runtime", default=default_runtime, help="Path to runtime_card_registry.generated.json")
    parser.add_argument("--output-hub-static", default=default_hub_static, help="Path to hub static card_registry.generated.json")
    parser.add_argument("--output-hub-runtime", default=default_hub_runtime, help="Path to hub runtime_card_registry.generated.json")
    parser.add_argument("--output-cs", default=default_cs, help="Path to C# CardRegistry.Generated.cs")

    args = parser.parse_args()

    print("[1/5] Loading canonical input files...")
    if not os.path.isfile(args.metadata):
        raise FileNotFoundError(f"Metadata file not found: {args.metadata}")
    if not os.path.isfile(args.names):
        raise FileNotFoundError(f"Names file not found: {args.names}")
    if not os.path.isfile(args.inventory):
        raise FileNotFoundError(f"Inventory file not found: {args.inventory}")
    if not os.path.isfile(args.runtime):
        raise FileNotFoundError(f"Runtime cards file not found: {args.runtime}")

    with open(args.metadata, "r", encoding="utf-8") as f:
        meta_list = json.load(f)
    print(f"  Loaded {len(meta_list)} static cards metadata.")

    with open(args.names, "r", encoding="utf-8") as f:
        names_dict = json.load(f)
    print(f"  Loaded {len(names_dict)} official names.")

    with open(args.inventory, "r", encoding="utf-8") as f:
        inv_rows = list(csv.DictReader(f))
    inv_map = {int(r["cardId"]): r for r in inv_rows}
    print(f"  Loaded {len(inv_rows)} inventory entries.")

    with open(args.runtime, "r", encoding="utf-8") as f:
        runtime_list = json.load(f)
    print(f"  Loaded {len(runtime_list)} runtime cards.")

    # Validate inputs
    if len(meta_list) != 69:
        raise ValueError(f"Expected exactly 69 static cards in metadata, got {len(meta_list)}")
    if len(names_dict) != 69:
        raise ValueError(f"Expected exactly 69 cards in names, got {len(names_dict)}")
    if len(inv_rows) != 69:
        raise ValueError(f"Expected exactly 69 cards in inventory, got {len(inv_rows)}")
    if len(runtime_list) != 4:
        raise ValueError(f"Expected exactly 4 runtime cards, got {len(runtime_list)}")

    # Sort static metadata by cardId ascending
    meta_list.sort(key=lambda m: int(m["cardId"]))

    print("[2/5] Building Hub static card registry (69 cards, strictly 0 fallback)...")
    hub_static_records = []
    seen_static_ids = set()

    for m in meta_list:
        cid = int(m["cardId"])
        if cid in seen_static_ids:
            raise ValueError(f"Duplicate cardId detected in static metadata: {cid}")
        seen_static_ids.add(cid)

        name_entry = names_dict.get(str(cid))
        if not name_entry or not name_entry.get("name"):
            raise ValueError(f"Missing verified Chinese name for cardId {cid}")
        name = name_entry["name"]

        inv_entry = inv_map.get(cid)
        if not inv_entry or not inv_entry.get("outputFile"):
            raise ValueError(f"Missing inventory entry/outputFile for cardId {cid}")
        icon = inv_entry["outputFile"]
        image_key = m.get("imageKey") or inv_entry.get("imageKey") or f"UT_HandCard_{cid}"

        card_type = int(m["cardType"])
        effect_type = int(m.get("effectType", 0))
        cost = int(m.get("cost", 0))

        hub_static_records.append({
            "cardId": cid,
            "name": name,
            "icon": icon,
            "imageKey": image_key,
            "cardType": card_type,
            "effectType": effect_type,
            "cost": cost,
            "provenance": {
                "metadata": "OFFICIAL_CONFIG",
                "name": "OFFICIAL_LOCALIZATION",
                "icon": "OFFICIAL_ASSET"
            }
        })

    print(f"  Built {len(hub_static_records)} verified static records.")

    print("[3/5] Validating Hub runtime cards registry (4 cards)...")
    runtime_records = []
    seen_runtime_ids = set()

    # Sort runtime cards by cardId ascending
    runtime_list_sorted = sorted(runtime_list, key=lambda r: int(r["cardId"]))

    for r in runtime_list_sorted:
        cid = int(r["cardId"])
        if cid in seen_runtime_ids:
            raise ValueError(f"Duplicate cardId in runtime cards: {cid}")
        if cid in seen_static_ids:
            raise ValueError(f"Conflict: runtime cardId {cid} overlaps with static cardId")
        seen_runtime_ids.add(cid)

        if not r.get("name"):
            raise ValueError(f"Missing official name for runtime card {cid}")

        runtime_records.append(r)

    print(f"  Validated {len(runtime_records)} runtime records (IDs: {[r['cardId'] for r in runtime_records]}).")

    print("[4/5] Writing Hub JSON outputs...")
    os.makedirs(os.path.dirname(os.path.abspath(args.output_hub_static)), exist_ok=True)
    with open(args.output_hub_static, "w", encoding="utf-8") as f:
        json.dump(hub_static_records, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"  Wrote: {args.output_hub_static} ({len(hub_static_records)} cards)")

    os.makedirs(os.path.dirname(os.path.abspath(args.output_hub_runtime)), exist_ok=True)
    with open(args.output_hub_runtime, "w", encoding="utf-8") as f:
        json.dump(runtime_records, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"  Wrote: {args.output_hub_runtime} ({len(runtime_records)} cards)")

    # Also keep root copy in sync if it exists
    root_runtime = os.path.join(repo_root, "runtime_card_registry.generated.json")
    if os.path.isfile(root_runtime) and os.path.abspath(root_runtime) != os.path.abspath(args.output_hub_runtime):
        with open(root_runtime, "w", encoding="utf-8") as f:
            json.dump(runtime_records, f, ensure_ascii=False, indent=2)
            f.write("\n")

    print("[5/5] Generating C# CardRegistry.Generated.cs (all 73 cards)...")
    all_73_cards = []

    for s in hub_static_records:
        all_73_cards.append({
            "cardId": s["cardId"],
            "name": s["name"],
            "cardType": s["cardType"],
            "cost": s["cost"],
            "imageKey": s["imageKey"],
            "isRuntimeCard": False
        })

    for r in runtime_records:
        all_73_cards.append({
            "cardId": r["cardId"],
            "name": r["name"],
            "cardType": int(r.get("cardType", 9)),
            "cost": int(r.get("cost", 0)),
            "imageKey": r.get("imageKey", f"UT_HandCard_{r['cardId']}"),
            "isRuntimeCard": True
        })

    all_73_cards.sort(key=lambda c: c["cardId"])

    if len(all_73_cards) != 73:
        raise ValueError(f"Expected exactly 73 combined cards, got {len(all_73_cards)}")

    # Check for duplicate IDs across all 73 cards
    all_ids = [c["cardId"] for c in all_73_cards]
    if len(all_ids) != len(set(all_ids)):
        raise ValueError("Duplicate cardId found in combined 73-card registry!")

    lines = []
    lines.append("// AUTO-GENERATED BY generate_canonical_card_registries.py — DO NOT EDIT")
    lines.append("// Project XingShu HS4-P1.4 Canonical In-Game Card Registry")
    lines.append("// Strictly 73 Official Cards (69 Static + 4 Runtime)")
    lines.append("")
    lines.append("using System;")
    lines.append("using System.Collections.Generic;")
    lines.append("")
    lines.append("namespace XingShu.Cards")
    lines.append("{")
    lines.append("    /// <summary>")
    lines.append("    /// Canonical card definition containing verified game metadata.")
    lines.append("    /// </summary>")
    lines.append("    public sealed class CardDefinition")
    lines.append("    {")
    lines.append("        public int CardId { get; set; }")
    lines.append("        public string Name { get; set; }")
    lines.append("        public int CardType { get; set; }")
    lines.append("        public int Cost { get; set; }")
    lines.append("        public string ImageKey { get; set; }")
    lines.append("        public bool IsRuntimeCard { get; set; }")
    lines.append("")
    lines.append("        public CardDefinition(int cardId, string name, int cardType, int cost, string imageKey, bool isRuntimeCard)")
    lines.append("        {")
    lines.append("            CardId = cardId;")
    lines.append("            Name = name;")
    lines.append("            CardType = cardType;")
    lines.append("            Cost = cost;")
    lines.append("            ImageKey = imageKey;")
    lines.append("            IsRuntimeCard = isRuntimeCard;")
    lines.append("        }")
    lines.append("")
    lines.append("        public override string ToString()")
    lines.append("        {")
    lines.append("            return $\"{Name} [{CardId}]\";")
    lines.append("        }")
    lines.append("    }")
    lines.append("")
    lines.append("    /// <summary>")
    lines.append("    /// Strongly-typed canonical registry for all 73 official cards (69 static + 4 runtime).")
    lines.append("    /// </summary>")
    lines.append("    public static class CardRegistryGenerated")
    lines.append("    {")
    lines.append("        public const int StaticCardCount = 69;")
    lines.append("        public const int RuntimeCardCount = 4;")
    lines.append("        public const int TotalCardCount = 73;")
    lines.append("")
    lines.append("        private static readonly Dictionary<int, CardDefinition> Registry = new Dictionary<int, CardDefinition>(73)")
    lines.append("        {")

    # Static cards entries
    lines.append("            // --- 69 Official Static Cards ---")
    for c in all_73_cards:
        if not c["isRuntimeCard"]:
            c_name = escape_cs_string(c["name"])
            c_img = escape_cs_string(c["imageKey"])
            lines.append(f'            {{ {c["cardId"]}, new CardDefinition({c["cardId"]}, "{c_name}", {c["cardType"]}, {c["cost"]}, "{c_img}", false) }},')

    # Runtime cards entries
    lines.append("            // --- 4 Official Runtime Cards ---")
    for c in all_73_cards:
        if c["isRuntimeCard"]:
            c_name = escape_cs_string(c["name"])
            c_img = escape_cs_string(c["imageKey"])
            lines.append(f'            {{ {c["cardId"]}, new CardDefinition({c["cardId"]}, "{c_name}", {c["cardType"]}, {c["cost"]}, "{c_img}", true) }},')

    lines.append("        };")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// Returns official Chinese name. For any unindexed/future unknown card, returns \"未知卡牌\".")
    lines.append("        /// </summary>")
    lines.append("        public static string GetCardName(int cardId)")
    lines.append("        {")
    lines.append("            if (Registry.TryGetValue(cardId, out CardDefinition def) && !string.IsNullOrEmpty(def.Name))")
    lines.append("            {")
    lines.append("                return def.Name;")
    lines.append("            }")
    lines.append("            return \"未知卡牌\";")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// Looks up card metadata definition by ID.")
    lines.append("        /// </summary>")
    lines.append("        public static bool TryGetCard(int cardId, out CardDefinition def)")
    lines.append("        {")
    lines.append("            return Registry.TryGetValue(cardId, out def);")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// Formats card for display as \"{cardName} [{cardId}]\", e.g. \"攻击(大) [10003]\", \"仙品杨桃 [21027]\", or \"未知卡牌 [{cardId}]\".")
    lines.append("        /// </summary>")
    lines.append("        public static string FormatCardDisplay(int cardId)")
    lines.append("        {")
    lines.append("            return $\"{GetCardName(cardId)} [{cardId}]\";")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// Checks whether the card ID is indexed in the canonical registry.")
    lines.append("        /// </summary>")
    lines.append("        public static bool IsRegistered(int cardId)")
    lines.append("        {")
    lines.append("            return Registry.ContainsKey(cardId);")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// Returns all 73 registered card definitions.")
    lines.append("        /// </summary>")
    lines.append("        public static IEnumerable<CardDefinition> GetAllCards()")
    lines.append("        {")
    lines.append("            return Registry.Values;")
    lines.append("        }")
    lines.append("    }")
    lines.append("")
    lines.append("    /// <summary>")
    lines.append("    /// Canonical CardRegistry alias for CardRegistryGenerated.")
    lines.append("    /// </summary>")
    lines.append("    public static class CardRegistry")
    lines.append("    {")
    lines.append("        public static string GetCardName(int cardId) => CardRegistryGenerated.GetCardName(cardId);")
    lines.append("        public static bool TryGetCard(int cardId, out CardDefinition def) => CardRegistryGenerated.TryGetCard(cardId, out def);")
    lines.append("        public static string FormatCardDisplay(int cardId) => CardRegistryGenerated.FormatCardDisplay(cardId);")
    lines.append("        public static bool IsRegistered(int cardId) => CardRegistryGenerated.IsRegistered(cardId);")
    lines.append("        public static IEnumerable<CardDefinition> GetAllCards() => CardRegistryGenerated.GetAllCards();")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    cs_content = "\r\n".join(lines)

    os.makedirs(os.path.dirname(os.path.abspath(args.output_cs)), exist_ok=True)
    with open(args.output_cs, "w", encoding="utf-8") as f:
        f.write(cs_content)

    print(f"  Wrote: {args.output_cs} ({len(all_73_cards)} cards)")
    print("\n[SUCCESS] Canonical card registries successfully generated!")
    print(f"  Static cards:  69 ({args.output_hub_static})")
    print(f"  Runtime cards:  4 ({args.output_hub_runtime})")
    print(f"  C# Registry:   73 ({args.output_cs})")

if __name__ == "__main__":
    main()
