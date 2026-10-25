"""Read-only check of original/SFW artwork in installed channel catalogs.

This checks the real Addressables entries and local bundles, not screenshots or
skin ownership. It never edits a game catalog, resource, preference or account.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import struct
import sys

import UnityPy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "XingShu-Dev/tools"))
from build_card_asset_inventory_v2 import parse_catalog
from export_game_catalog import decode


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class Catalog:
    def __init__(self, data):
        self.aa = data / "StreamingAssets/aa"
        self.path = self.aa / "catalog.bundle"
        self.keys, self.entries, self.buckets = parse_catalog(str(self.path))
        self.catalog = next(json.loads(obj.read().m_Script) for obj in UnityPy.load(str(self.path)).objects
                            if obj.type.name == "TextAsset")
        self.encoded = base64.b64decode(self.catalog["m_EntryDataString"])
        self.cached = {}
        for path in (data / "com.unity.addressables/AssetBundles").glob("*/*/__data"):
            self.cached.setdefault(path.parent.name.lower(), []).append(path)

    def bundles(self, key):
        result = set()
        for index in self.keys.get(key, ()):
            dependency = self.entries[index]["dep_key_idx"]
            if dependency < 0:
                continue
            for bundle_index in self.buckets[dependency][1]:
                internal_index = struct.unpack_from("<i", self.encoded, 4 + 28 * bundle_index)[0]
                internal = self.catalog["m_InternalIds"][internal_index]
                name = internal.replace("\\", "/").rsplit("/", 1)[-1]
                local = self.aa / "StandaloneWindows64" / name
                if local.is_file():
                    result.add(local)
                result.update(self.cached.get(Path(name).stem.lower(), ()))
        return sorted(result)

    def configuration(self):
        for path in self.bundles("GameData_CN"):
            tables = {}
            for obj in UnityPy.load(str(path)).objects:
                if obj.type.name != "TextAsset":
                    continue
                value = obj.read()
                if value.m_Name in ("Card", "Skin"):
                    tables[value.m_Name] = decode(value.m_Script.encode("utf8", "surrogateescape"), value.m_Name + "Configure")
            if len(tables) == 2:
                return tables, path, "channel_catalog_configuration"
        # The shipped catalog can refer to an older configuration hash after a
        # live resource update. Do not mistake that for absent artwork: compare
        # the same-version reference table with this channel's actual catalog,
        # and explicitly retain the weaker provenance in the result.
        reference = ROOT / "XingShu-Dev/build/catalog/tables.json"
        if reference.is_file():
            tables = json.loads(reference.read_text("utf8"))
            if "Card" in tables and "Skin" in tables:
                return tables, reference, "shared_official_reference_channel_config_not_resolved"
        raise FileNotFoundError("No channel or reference Card/Skin configuration is present locally")

    def sample(self, key):
        for path in self.bundles(key):
            for obj in UnityPy.load(str(path)).objects:
                if obj.type.name not in ("Texture2D", "Sprite"):
                    continue
                value = obj.read()
                if value.m_Name != key:
                    continue
                return {"key": key, "type": obj.type.name,
                        "bundleSha256": digest(path), "objectSha256": hashlib.sha256(obj.get_raw_data()).hexdigest()}
        return {"key": key, "decodedTextureFound": False}


def pairs(tables):
    found = set()

    def add(category, original, sfw):
        originals = original if isinstance(original, list) else [original]
        alternatives = sfw if isinstance(sfw, list) else [sfw]
        for original_key, sfw_key in zip(originals, alternatives):
            if original_key and sfw_key and original_key != sfw_key:
                found.add((category, original_key, sfw_key))

    for row in tables["Card"].get("Infos", []):
        add("card", row.get("Image"), row.get("ImageSfw"))
    for group in tables["Card"].get("AltArts", []):
        for row in group.get("CardAltArtConfigureItems", []):
            add("alternate_art", row.get("AccountBackground"), row.get("AccountBackgroundSfw"))
            add("alternate_video", row.get("AccountBackgroundVideo"), row.get("AccountBackgroundVideoSfw"))
    for group in tables["Skin"].get("StandingPaintings", []):
        for row in group.get("SkinStandingPaintingConfigureItems", []):
            for original, sfw in (("Character", "SfwCharacter"), ("InGameCharacter", "InGameCharacterSFW"),
                                  ("Bust", "SfwBust"), ("CharacterReady", "SfwCharacterReady"),
                                  ("CharacterThin", "SfwCharacterThin"), ("RolePhoto", "SfwRolePhoto")):
                add("skin", row.get(original), row.get(sfw))
    return sorted(found)


def inspect(profile):
    game = Path(profile["installRoot"])
    data = game / (Path(profile["processExecutable"]).stem + "_Data")
    catalog = Catalog(data)
    tables, config, config_source = catalog.configuration()
    rows = []
    for category, original, sfw in pairs(tables):
        rows.append({"category": category, "original": original, "sfw": sfw,
                     "originalCatalogEntry": original in catalog.keys, "sfwCatalogEntry": sfw in catalog.keys,
                     "originalLocalBundle": bool(catalog.bundles(original)), "sfwLocalBundle": bool(catalog.bundles(sfw))})
    # Verify actual texture object names for representative card and skin pairs;
    # an address being declared alone does not prove that its bytes are present.
    samples = []
    for category in ("card", "skin"):
        candidates = [row for row in rows if row["category"] == category and
                      row["originalLocalBundle"] and row["sfwLocalBundle"]]
        for row in candidates[:3]:
            samples.append({"category": category, "original": catalog.sample(row["original"]),
                            "sfw": catalog.sample(row["sfw"])})
    return {"platform": profile["platformId"], "catalogSha256": digest(catalog.path),
            "configurationSource": config_source,
            "configurationSha256": digest(config), "pairCount": len(rows),
            "originalEntries": sum(row["originalCatalogEntry"] for row in rows),
            "originalLocalBundles": sum(row["originalLocalBundle"] for row in rows),
            "sfwEntries": sum(row["sfwCatalogEntry"] for row in rows),
            "sfwLocalBundles": sum(row["sfwLocalBundle"] for row in rows),
            "pairs": rows, "decodedSamples": samples,
            "visualComparisonPassed": False}


def main():
    sys.stdout.reconfigure(encoding="utf8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    profiles = json.loads((ROOT / "AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json").read_text("utf8"))
    result = {"formatVersion": 1, "readOnly": True, "channels": [],
              "limitations": ["Local assets and selectors do not prove every panel refreshes correctly.",
                              "Android device catalog/texture validation requires an online authorized device.",
                              "Reference-table fallback is labeled and does not prove the active channel configuration matches.",
                              "No skin is unlocked and no resource or official saved setting is replaced."]}
    for profile in profiles:
        if profile["platformId"] not in ("TAPTAP", "BILIBILI", "WEGAME"):
            continue
        try:
            channel = inspect(profile)
        except Exception as error:
            channel = {"platform": profile["platformId"], "error": type(error).__name__ + ": " + str(error)}
        result["channels"].append(channel)
        print(json.dumps({key: value for key, value in channel.items() if key not in ("pairs", "decodedSamples")}, ensure_ascii=False), flush=True)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", "utf8")


if __name__ == "__main__":
    main()
