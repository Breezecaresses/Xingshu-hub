"""Export official board grid layouts and UT_Platform tile icons from local Steam game data.

Outputs:
  1. E:\\工作流\\星引擎插件\\XingShu-Server\\xingshu_server\\replay_boards.json
  2. E:\\工作流\\星枢源码\\AstralParty-Hub\\public\\platform-tiles\\UT_Platform_*.webp (128x128)
"""

from __future__ import annotations

import json
import math
import pathlib
import re
import sys

import UnityPy
from PIL import Image, ImageDraw

TOOLS_DIR = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS_DIR))
import export_game_catalog as egc

CONFIG_BUNDLE = pathlib.Path(
    r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\com.unity.addressables\AssetBundles\be8a6f7fa87357b26096b5a0ada25e0b\f148363ad19a61a9fc5b4ec4e0f55f93\__data"
)
BOARDS_JSON_OUT = pathlib.Path(
    r"E:\工作流\星引擎插件\XingShu-Server\xingshu_server\replay_boards.json"
)
ICONS_OUT_DIR = pathlib.Path(
    r"E:\工作流\星枢源码\AstralParty-Hub\public\platform-tiles"
)
PREVIEW_OUT_DIR = pathlib.Path(
    r"C:\Users\76501\.gemini\antigravity\brain\10a504f3-78b6-4d6b-b5f0-996af2e8e4f7\scratch\board-preview-final"
)

# Scenes whose official top-down diagram uses (+X, -Z) rather than 90 deg CW (+Z, +X).
ROT_ZERO_SCENES = {"Map_017"}

# Fallback icon key by LandType for entries whose icon name is missing or aliased.
LAND_ICON_OVERRIDES = {
    2: "UT_Platform_Start",
    5: "UT_Platform_Event",
    28: "UT_Platform_Shop",
}

SPAWN_COLORS = {
    0: (228, 68, 68),    # P1 red
    1: (136, 200, 60),   # P2 lime
    2: (59, 130, 246),   # P3 blue
    3: (245, 158, 11),   # P4 amber
}


def quat_to_mat(q):
    x, y, z, w = q["x"], q["y"], q["z"], q["w"]
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    return [
        [1 - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy)],
        [2 * (xy + wz), 1 - 2 * (xx + zz), 2 * (yz - wx)],
        [2 * (xz - wy), 2 * (yz + wx), 1 - 2 * (xx + yy)],
    ]


def mat_mul(a, b):
    return [
        [sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)]
        for i in range(3)
    ]


def mat_vec(m, v):
    return [
        m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
        m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
        m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2],
    ]


def world_position(tf_id: int, transforms: dict) -> list[float]:
    chain = []
    cur = tf_id
    while cur and cur in transforms:
        chain.append(transforms[cur])
        cur = (transforms[cur].get("m_Father") or {}).get("m_PathID", 0)
    chain.reverse()
    pos = [0.0, 0.0, 0.0]
    rot = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    scale = [1.0, 1.0, 1.0]
    for t in chain:
        lp, ls, lq = t["m_LocalPosition"], t["m_LocalScale"], t["m_LocalRotation"]
        rv = mat_vec(rot, [lp["x"] * scale[0], lp["y"] * scale[1], lp["z"] * scale[2]])
        pos = [pos[0] + rv[0], pos[1] + rv[1], pos[2] + rv[2]]
        rot = mat_mul(rot, quat_to_mat(lq))
        scale = [scale[0] * ls["x"], scale[1] * ls["y"], scale[2] * ls["z"]]
    return pos


def decode_configs():
    env = UnityPy.load(str(CONFIG_BUNDLE))
    tas = {
        o.read().m_Name: o.read().m_Script.encode("utf-8", "surrogateescape")
        for o in env.objects
        if o.type.name == "TextAsset"
    }
    lstrs = {x["Id"]: x["Simplified"] for x in egc.decode(tas["STRLand"], "STRLandConfigure")["Locals"]}
    mstrs = {x["Id"]: x["Simplified"] for x in egc.decode(tas["STRMap"], "STRMapConfigure")["Locals"]}

    land_types = {}
    for x in egc.decode(tas["Land"], "LandConfigure")["Infos"]:
        lt = int(x["LandType"])
        icon = LAND_ICON_OVERRIDES.get(lt) or x.get("LandIcon") or ""
        if icon and not egc.asset_bundles(icon):
            icon = LAND_ICON_OVERRIDES.get(lt, "") if egc.asset_bundles(LAND_ICON_OVERRIDES.get(lt, "")) else ""
        land_types[str(lt)] = {
            "type": lt,
            "name": lstrs.get(x.get("NameID"), f"格型{lt}"),
            "description": lstrs.get(x.get("DescriptionID"), ""),
            "icon": icon,
        }
    if "5" not in land_types:
        land_types["5"] = {
            "type": 5,
            "name": "悬赏",
            "description": "",
            "icon": LAND_ICON_OVERRIDES[5],
        }

    map_to_scenes = {}
    map_names = {}
    for x in egc.decode(tas["Map"], "MapConfigure")["Infos"]:
        mid = int(x["Id"])
        mids = [int(m) for m in (x.get("Mids") or [])]
        name = mstrs.get(x.get("MapName"), "")
        if name:
            map_names[str(mid)] = name
        scenes = [f"Map_{m:03d}" if m < 1000 else f"Map_{m}" for m in mids]
        if scenes:
            map_to_scenes[str(mid)] = scenes
    return land_types, map_to_scenes, map_names


def export_platform_icons(land_types: dict):
    ICONS_OUT_DIR.mkdir(parents=True, exist_ok=True)
    needed_icons = sorted({v["icon"] for v in land_types.values() if v.get("icon")})
    exported = {}
    for icon_key in needed_icons:
        bundles = egc.asset_bundles(icon_key)
        img = None
        for b in bundles:
            env = UnityPy.load(str(b))
            for obj in env.objects:
                if obj.type.name == "Texture2D":
                    tex = obj.read()
                    if tex.m_Name == icon_key or img is None:
                        img = tex.image.convert("RGBA")
                        break
            if img is not None:
                break
        if img is None:
            continue
        resized = img.resize((128, 128), Image.Resampling.LANCZOS)
        out_file = ICONS_OUT_DIR / f"{icon_key}.webp"
        resized.save(out_file, format="WEBP", quality=92, method=6)
        exported[icon_key] = resized
    print(f"Exported {len(exported)} platform WebP icons to {ICONS_OUT_DIR}")
    return exported


def extract_scene_nodes(scene_key: str):
    bundles = egc.asset_bundles(scene_key)
    if not bundles:
        return []
    gameobjects = {}
    transforms = {}
    monos = []
    for b in bundles:
        env = UnityPy.load(str(b))
        for obj in env.objects:
            tname = obj.type.name
            if tname == "GameObject":
                gameobjects[obj.path_id] = obj.read_typetree()
            elif tname == "Transform":
                transforms[obj.path_id] = obj.read_typetree()
            elif tname == "MonoBehaviour":
                monos.append(obj.read_typetree())
    go_to_tf = {
        (tf.get("m_GameObject") or {}).get("m_PathID", 0): tf_id
        for tf_id, tf in transforms.items()
    }
    nodes = []
    for mb in monos:
        info = mb.get("nodeInfo") if isinstance(mb, dict) else None
        if not isinstance(info, dict):
            continue
        go_id = (mb.get("m_GameObject") or {}).get("m_PathID", 0)
        tf_id = go_to_tf.get(go_id)
        pos = world_position(tf_id, transforms) if tf_id else [0.0, 0.0, 0.0]
        nodes.append(
            {
                "id": int(info.get("_id", 0)),
                "type": int(info.get("_landType", 0)),
                "spawn": int(info.get("_playerSerialNumber", 0)),
                "initDir": int(info.get("_initDirLandId", 0)),
                "jumpTo": int(info.get("_JumpNodeId", 0)),
                "neighbors": [int(n) for n in (info.get("_neighborLandIds") or [])],
                "disabled": bool(mb.get("Disable", 0)),
                "pos": pos,
            }
        )
    return nodes


def project_raw(scene_key: str, pos: list[float]) -> tuple[float, float]:
    x, _, z = pos
    if scene_key in ROT_ZERO_SCENES:
        return (x, -z)
    return (z, x)


def fundamental_step(steps: list[float], fallback: float) -> float:
    """Estimate primary tile pitch without letting wider bridge gaps inflate it."""
    if not steps:
        return fallback
    s = sorted(steps)
    lo = s[0]
    band = [v for v in s if v <= lo * 1.25]
    if len(band) >= max(3, len(s) // 4):
        return band[len(band) // 2]
    return s[len(s) // 2]


def normalize_grid(scene_key: str, raw_nodes: list[dict]):
    pts = {n["id"]: project_raw(scene_key, n["pos"]) for n in raw_nodes}
    by_id = {n["id"]: n for n in raw_nodes}

    # Deskew scenes rotated off cardinal axes (e.g. Map_009 at ~15 deg)
    angles = []
    for n in raw_nodes:
        nid = n["id"]
        x1, y1 = pts[nid]
        for nb in n["neighbors"]:
            if nb in pts and nb > nid:
                x2, y2 = pts[nb]
                dx, dy = x2 - x1, y2 - y1
                if math.hypot(dx, dy) > 1e-3:
                    a = math.atan2(dy, dx) % (math.pi / 2.0)
                    if a > math.pi / 4.0:
                        a -= math.pi / 2.0
                    angles.append(a)
    angles.sort()
    dom_ang = angles[len(angles) // 2] if angles else 0.0
    if abs(math.degrees(dom_ang)) > 2.5:
        ca, sa = math.cos(-dom_ang), math.sin(-dom_ang)
        pts = {nid: (x * ca - y * sa, x * sa + y * ca) for nid, (x, y) in pts.items()}

    h_steps, v_steps, all_steps = [], [], []
    for n in raw_nodes:
        nid = n["id"]
        x1, y1 = pts[nid]
        for nb in n["neighbors"]:
            if nb in pts and nb > nid:
                x2, y2 = pts[nb]
                adx, ady = abs(x2 - x1), abs(y2 - y1)
                dist = math.hypot(adx, ady)
                if dist > 1e-3:
                    all_steps.append(dist)
                if adx > ady * 3.0 and adx > 5.0:
                    h_steps.append(adx)
                elif ady > adx * 3.0 and ady > 5.0:
                    v_steps.append(ady)

    all_step = fundamental_step(all_steps, 30.0)
    sx = fundamental_step(h_steps, all_step)
    sy = fundamental_step(v_steps, all_step)
    if not (0.70 <= sx / sy <= 1.35):
        sx = sy = all_step

    norm = {nid: (x / sx, y / sy) for nid, (x, y) in pts.items()}

    def align_axis(vals_dict: dict[int, float], thresh: float = 0.16) -> dict[int, float]:
        items = sorted(vals_dict.items(), key=lambda kv: kv[1])
        groups = []
        for nid, val in items:
            if not groups or val - groups[-1][-1][1] > thresh:
                groups.append([(nid, val)])
            else:
                groups[-1].append((nid, val))
        out = {}
        for g in groups:
            avg = sum(v for _, v in g) / len(g)
            for nid, _ in g:
                out[nid] = avg
        return out

    ax = align_axis({nid: p[0] for nid, p in norm.items()})
    ay = align_axis({nid: p[1] for nid, p in norm.items()})
    min_x, min_y = min(ax.values()), min(ay.values())

    coords = {
        nid: (round(ax[nid] - min_x, 3), round(ay[nid] - min_y, 3))
        for nid in norm
    }
    ids = list(coords.keys())
    min_euclid = min(
        (
            math.hypot(coords[ids[j]][0] - coords[ids[i]][0], coords[ids[j]][1] - coords[ids[i]][1])
            for i in range(len(ids))
            for j in range(i + 1, len(ids))
        ),
        default=1.0,
    )
    width = round(max((c[0] for c in coords.values()), default=0.0) + 1.0, 3)
    height = round(max((c[1] for c in coords.values()), default=0.0) + 1.0, 3)

    out_nodes = []
    edge_set = set()
    for n in sorted(raw_nodes, key=lambda x: x["id"]):
        nid = n["id"]
        gx, gy = coords[nid]
        for nb in n["neighbors"]:
            if nb in by_id and nb != nid:
                a, b = min(nid, nb), max(nid, nb)
                edge_set.add((a, b))
        row = {
            "id": nid,
            "type": n["type"],
            "gx": gx,
            "gy": gy,
            "neighbors": n["neighbors"],
        }
        if n["type"] == 1:
            row["spawn"] = n["spawn"]
        if n["initDir"]:
            row["initDir"] = n["initDir"]
        if n["jumpTo"]:
            row["jumpTo"] = n["jumpTo"]
        if n["disabled"]:
            row["disabled"] = True
        out_nodes.append(row)

    return {
        "scene": scene_key,
        "nodeCount": len(out_nodes),
        "gridWidth": width,
        "gridHeight": height,
        "minSpacing": round(min_euclid, 3),
        "nodes": out_nodes,
        "edges": [[u, v] for u, v in sorted(edge_set)],
    }


def render_verification_png(scene_obj: dict, icons: dict, land_types: dict, out_path: pathlib.Path):
    cell = 58
    pad = 44
    w = int(math.ceil(scene_obj["gridWidth"] * cell + 2 * pad))
    h = int(math.ceil(scene_obj["gridHeight"] * cell + 2 * pad))
    img = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    draw = ImageDraw.Draw(img)

    for gx in range(int(math.ceil(scene_obj["gridWidth"])) + 2):
        x = pad + gx * cell - cell * 0.5
        draw.line([(x, pad - cell * 0.5), (x, h - pad + cell * 0.5)], fill=(230, 234, 240, 255), width=1)
    for gy in range(int(math.ceil(scene_obj["gridHeight"])) + 2):
        y = pad + gy * cell - cell * 0.5
        draw.line([(pad - cell * 0.5, y), (w - pad + cell * 0.5, y)], fill=(230, 234, 240, 255), width=1)

    by_id = {n["id"]: n for n in scene_obj["nodes"]}
    for u, v in scene_obj["edges"]:
        nu, nv = by_id[u], by_id[v]
        draw.line(
            [(pad + nu["gx"] * cell, pad + nu["gy"] * cell), (pad + nv["gx"] * cell, pad + nv["gy"] * cell)],
            fill=(115, 120, 132, 255),
            width=10,
        )

    tile_half = min(cell * 0.47, max(cell * 0.36, scene_obj["minSpacing"] * cell * 0.47))
    icon_sz = int(tile_half * 1.65)
    for n in scene_obj["nodes"]:
        cx = pad + n["gx"] * cell
        cy = pad + n["gy"] * cell
        tile_fill = (108, 112, 124, 255) if not n.get("disabled") else (165, 170, 180, 190)
        draw.rectangle([cx - tile_half, cy - tile_half, cx + tile_half, cy + tile_half], fill=tile_fill, outline=(255, 255, 255, 255), width=2)
        if n["type"] == 1:
            sn = n.get("spawn", 0)
            c = SPAWN_COLORS.get(sn, (228, 68, 68))
            r = icon_sz * 0.5
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=c, outline=(255, 255, 255, 255), width=2)
            draw.text((cx - 7, cy - 6), f"P{sn+1}", fill=(255, 255, 255, 255))
        else:
            lt_meta = land_types.get(str(n["type"]), {})
            icon_img = icons.get(lt_meta.get("icon", ""))
            if icon_img is not None:
                thumb = icon_img.resize((icon_sz, icon_sz), Image.Resampling.LANCZOS)
                img.paste(thumb, (int(cx - icon_sz / 2), int(cy - icon_sz / 2)), thumb)

    draw.text((10, 6), f"{scene_obj['scene']} ({scene_obj['nodeCount']}) min={scene_obj['minSpacing']}", fill=(20, 20, 20, 255))
    img.convert("RGB").save(out_path)


def main():
    land_types, map_to_scenes, map_names = decode_configs()
    icons = export_platform_icons(land_types)

    _, cats = egc.addressable_catalog()
    scene_keys = sorted({k for ks, _, _ in cats for k in ks if re.fullmatch(r"Map_\d+", str(k))})

    scenes = {}
    PREVIEW_OUT_DIR.mkdir(parents=True, exist_ok=True)
    preview_targets = {
        "Map_007", "Map_008", "Map_010", "Map_012", "Map_013",
        "Map_014", "Map_015", "Map_016", "Map_017", "Map_023", "Map_024",
    }

    for sk in scene_keys:
        raw_nodes = extract_scene_nodes(sk)
        if not raw_nodes:
            continue
        scene_obj = normalize_grid(sk, raw_nodes)
        scenes[sk] = scene_obj
        print(
            f"  {sk:9s} nodes={scene_obj['nodeCount']:2d} grid={scene_obj['gridWidth']:4.1f}x{scene_obj['gridHeight']:4.1f} minSpacing={scene_obj['minSpacing']:.3f}"
        )
        if sk in preview_targets:
            render_verification_png(scene_obj, icons, land_types, PREVIEW_OUT_DIR / f"{sk}.png")

    payload = {
        "schemaVersion": 1,
        "landTypes": land_types,
        "mapNames": map_names,
        "mapToScenes": map_to_scenes,
        "scenes": scenes,
    }
    BOARDS_JSON_OUT.write_text(
        json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    print(f"Wrote {BOARDS_JSON_OUT} ({BOARDS_JSON_OUT.stat().st_size / 1024:.1f} KB)")


if __name__ == "__main__":
    main()
