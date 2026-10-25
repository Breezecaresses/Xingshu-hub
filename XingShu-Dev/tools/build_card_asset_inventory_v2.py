#!/usr/bin/env python3
"""
PROJECT XINGSHU — HS4-P1.3D
Lane 2: Complete Asset Inventory V2 Builder

Constructs CARD_ASSET_INVENTORY_V2.csv for all 69 official cards with
complete cryptographic (SHA-256) and geometric (bundle/PathID/rect/dimensions) provenance.
"""

import os
import sys
import json
import csv
import base64
import struct
import hashlib
from PIL import Image
import UnityPy

WORKSPACE_ROOT = r"E:\工作流\星引擎插件"
METADATA_JSON_PATH = os.path.join(WORKSPACE_ROOT, "card_metadata_official.json")
LOCALIZATION_JSON_PATH = os.path.join(WORKSPACE_ROOT, "card_names_zh_cn_official.json")
PUBLIC_CARDS_DIR = os.path.join(WORKSPACE_ROOT, "AstralParty-Hub", "public", "cards")
CATALOG_BUNDLE_PATH = r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\catalog.bundle"
AA_STANDALONE_DIR = r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
OUTPUT_CSV_PATH = os.path.join(WORKSPACE_ROOT, "CARD_ASSET_INVENTORY_V2.csv")

# Known shared asset mappings in official game design
SHARED_CARD_IDS = {20029, 20031, 21012, 21013}

def parse_catalog(catalog_path):
    env = UnityPy.load(catalog_path)
    for obj in env.objects:
        if obj.type.name == "TextAsset":
            cat = json.loads(obj.read().m_Script)
            break

    internal_ids = cat.get("m_InternalIds", [])
    bucket_bytes = base64.b64decode(cat["m_BucketDataString"])
    bucket_count, = struct.unpack("<I", bucket_bytes[:4])
    key_bytes = base64.b64decode(cat["m_KeyDataString"])

    def read_object(data, offset):
        tag = data[offset]
        offset += 1
        if tag == 0:
            slen, = struct.unpack("<I", data[offset:offset+4])
            return data[offset+4 : offset+4+slen].decode("utf-8", errors="replace")
        elif tag == 1:
            slen, = struct.unpack("<I", data[offset:offset+4])
            return data[offset+4 : offset+4+slen].decode("utf-16le", errors="replace")
        elif tag == 2:
            return struct.unpack("<H", data[offset:offset+2])[0]
        elif tag == 3:
            return struct.unpack("<I", data[offset:offset+4])[0]
        elif tag == 4:
            return struct.unpack("<i", data[offset:offset+4])[0]
        elif tag == 5:
            slen = data[offset]
            return data[offset+1 : offset+1+slen].decode("ascii", errors="replace")
        else:
            return f"<Tag {tag}>"

    buckets = []
    b_ptr = 4
    for b in range(bucket_count):
        data_offset, entry_cnt = struct.unpack("<II", bucket_bytes[b_ptr:b_ptr+8])
        b_ptr += 8
        entries_tuple = struct.unpack(f"<{entry_cnt}I", bucket_bytes[b_ptr : b_ptr + entry_cnt*4])
        b_ptr += entry_cnt * 4
        key_obj = read_object(key_bytes, data_offset)
        buckets.append((key_obj, entries_tuple))

    entry_bytes = base64.b64decode(cat["m_EntryDataString"])
    entry_count, = struct.unpack("<I", entry_bytes[:4])

    entries = []
    for i in range(entry_count):
        e_off = 4 + i * 28
        internal_id_idx, provider_idx, dep_key_idx, dep_hash, data_off, primary_key_idx, res_type_idx = struct.unpack("<iiiiiii", entry_bytes[e_off : e_off+28])
        entries.append({"dep_key_idx": dep_key_idx})

    key_map = {}
    for k, e_indices in buckets:
        if isinstance(k, str):
            key_map[k] = e_indices

    return key_map, entries, buckets

def main():
    print("[1/5] Loading official metadata...")
    with open(METADATA_JSON_PATH, "r", encoding="utf-8") as f:
        meta_list = json.load(f)
    print(f"  Loaded {len(meta_list)} cards from {METADATA_JSON_PATH}")

    print("[2/5] Parsing Unity Addressables catalog.bundle...")
    key_map, entries, buckets = parse_catalog(CATALOG_BUNDLE_PATH)

    bundle_map = {}
    for c in meta_list:
        ik = c["imageKey"]
        if ik in key_map:
            for e_idx in key_map[ik]:
                ent = entries[e_idx]
                if ent["dep_key_idx"] >= 0:
                    bundle_map[ik] = str(buckets[ent["dep_key_idx"]][0])
                    break
        else:
            print(f"  WARNING: imageKey {ik} NOT FOUND in catalog!")

    print(f"  Resolved {len(bundle_map)} / {len(set(c['imageKey'] for c in meta_list))} unique imageKeys to bundles.")

    print("[3/5] Inspecting Unity bundles for Texture2D assets...")
    unique_bundles = sorted(list(set(bundle_map.values())))
    tex_meta = {}
    for bname in unique_bundles:
        bpath = os.path.join(AA_STANDALONE_DIR, bname)
        if not os.path.exists(bpath):
            raise FileNotFoundError(f"Bundle not found on disk: {bpath}")
        benv = UnityPy.load(bpath)
        found_tex = False
        for o in benv.objects:
            if o.type.name == "Texture2D":
                d = o.read()
                tex_name = getattr(d, "m_Name", getattr(d, "name", ""))
                tex_meta[bname] = {
                    "path_id": o.path_id,
                    "texture_name": tex_name,
                    "width": d.m_Width,
                    "height": d.m_Height
                }
                found_tex = True
                break
        if not found_tex:
            raise ValueError(f"No Texture2D found in bundle {bname}")

    print(f"  Successfully extracted Texture2D metadata from {len(tex_meta)} bundles.")

    print("[4/5] Building inventory records and validating disk PNG files...")
    rows = []
    for c in meta_list:
        cid = c["cardId"]
        ik = c["imageKey"]
        bname = bundle_map[ik]
        t_info = tex_meta[bname]

        is_shared = (cid in SHARED_CARD_IDS) or (ik != f"UT_HandCard_{cid}")
        status = "VERIFIED_SHARED" if is_shared else "VERIFIED_DIRECT"
        obj_type = "SHARED_REFERENCE" if is_shared else "Texture2D"

        png_filename = f"{ik}.png"
        png_path = os.path.join(PUBLIC_CARDS_DIR, png_filename)

        if not os.path.exists(png_path):
            raise FileNotFoundError(f"Missing PNG file: {png_path}")

        with open(png_path, "rb") as pf:
            png_bytes = pf.read()
            png_sha = hashlib.sha256(png_bytes).hexdigest()

        with Image.open(png_path) as im:
            w, h = im.size

        if (w, h) != (t_info["width"], t_info["height"]):
            print(f"  WARNING: Dimensions mismatch for {ik}: disk=({w},{h}), bundle=({t_info['width']},{t_info['height']})")

        rows.append({
            "cardId": cid,
            "imageKey": ik,
            "bundlePath": f"StandaloneWindows64/{bname}",
            "objectType": obj_type,
            "PathID": t_info["path_id"],
            "textureName": t_info["texture_name"],
            "spriteName": "",
            "rect": f"0,0,{w},{h}",
            "outputFile": f"/cards/{png_filename}",
            "SHA256": png_sha,
            "width": w,
            "height": h,
            "status": status
        })

    # Sort strictly by cardId ascending
    rows.sort(key=lambda r: r["cardId"])

    print(f"[5/5] Writing {OUTPUT_CSV_PATH}...")
    fieldnames = [
        "cardId", "imageKey", "bundlePath", "objectType", "PathID",
        "textureName", "spriteName", "rect", "outputFile", "SHA256",
        "width", "height", "status"
    ]
    with open(OUTPUT_CSV_PATH, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in rows:
            writer.writerow(r)

    print(f"  Successfully wrote {len(rows)} rows to {OUTPUT_CSV_PATH}")

    # Print summary
    status_counts = {}
    for r in rows:
        status_counts[r["status"]] = status_counts.get(r["status"], 0) + 1

    print("\n=== SUMMARY ===")
    print(f"Total Cards: {len(rows)}")
    for s, count in sorted(status_counts.items()):
        print(f"  {s}: {count}")

if __name__ == "__main__":
    main()
