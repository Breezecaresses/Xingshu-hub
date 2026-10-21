"""Build the game-settings skin, including locally extracted official slider parts.

The source crops and their provenance live beside the generated assets. No game
screenshots or complete texture atlases are embedded in the plugin.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import subprocess
import sys
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "assets/vanilla-ui-r3"
S = 3
PINK = "#ff2794"
YELLOW = "#ffbf00"
INK = "#25252e"
WHITE = "#faf9f6"


def import_slider(atlas_path):
    """Extract user-provided atlas parts without copying unrelated game artwork."""
    source = Path(atlas_path)
    target = DEST / "official-sources"
    target.mkdir(parents=True, exist_ok=True)
    parts = {"handle": (682, 244, 786, 348), "fill": (1018, 13, 1642, 81)}
    with Image.open(source).convert("RGBA") as atlas:
        if atlas.size != (1864, 748):
            raise ValueError(f"Unexpected official atlas size: {atlas.size}")
        for part, box in parts.items():
            crop = atlas.crop(box)
            pixels = []
            for r,g,b,a in crop.getdata():
                # The manually packed atlas has an opaque background under these parts.
                # Color-key only that background; retain the official colored pixels.
                keep = (r > 150 and g > 85 and b < 115) if part == "handle" else (r > 160 and g < 135 and b > 70)
                pixels.append((r,g,b,a if keep else 0))
            crop.putdata(pixels)
            crop.save(target / f"slider_{part}.png", optimize=True)
    provenance = {
        "Project": "Astral Party / 吉星派对",
        "Attribution": "Official UI artwork belongs to its original game creators; XingShu is a community project.",
        "SourceFile": source.name,
        "SourceSHA256": hashlib.sha256(source.read_bytes()).hexdigest().upper(),
        "SourceSize": [1864,748],
        "CropsXYXY": parts,
        "Processing": "Local atlas crop and background color-key only; no AI redraw",
    }
    (target/"provenance.json").write_text(json.dumps(provenance,ensure_ascii=False,indent=2)+"\n","utf8")


def asset(name, w, h, radius, fill, stroke=None, pattern=False, kind=""):
    im = Image.new("RGBA", (w*S, h*S))
    d = ImageDraw.Draw(im)
    def rr(box, r, color, outline=None, line=1):
        d.rounded_rectangle(tuple(round(x*S) for x in box), radius=round(r*S), fill=color,
                            outline=outline, width=round(line*S))
    rr((2, 2, w-2, h-4), radius, fill, stroke, 2 if stroke else 1)
    if pattern:
        layer = Image.new("RGBA", im.size)
        p = ImageDraw.Draw(layer)
        c = (255, 255, 255, 10) if fill == INK else (50, 44, 66, 8)
        for y in range(12, h, 28):
            for x in range(10, w, 28):
                if (x//28+y//28) % 3 == 0:
                    p.ellipse((x*S, y*S, (x+7)*S, (y+7)*S), outline=c, width=2*S)
                else:
                    p.polygon([(x*S,(y+3)*S),((x+4)*S,(y+7)*S),((x+8)*S,(y+3)*S),((x+4)*S,(y-1)*S)], outline=c)
        mask = Image.new("L", im.size)
        ImageDraw.Draw(mask).rounded_rectangle((4*S,4*S,(w-4)*S,(h-6)*S),radius=radius*S,fill=255)
        layer.putalpha(Image.composite(layer.getchannel("A"), Image.new("L", im.size), mask))
        im = Image.alpha_composite(im, layer)
        d = ImageDraw.Draw(im)
    if kind == "tab":
        d.ellipse((14*S,18*S,38*S,42*S), fill="white")
        for x in range(8, 63, 6):
            for y in range(8, 55, 6):
                if (x-28)**2+(y-31)**2 > 17**2:
                    d.ellipse((x*S,y*S,(x+1)*S,(y+1)*S),fill=(255,255,255,80))
    elif kind == "handle":
        d.ellipse((6*S,6*S,(w-6)*S,(h-6)*S),fill=PINK)
    im = im.resize((w,h), Image.Resampling.LANCZOS)
    opacity = {"window_frame": 0.78, "header_bar": 0.82,
               "card_base": 0.93, "card_feature_header": 0.93}.get(name, 1.0)
    if opacity < 1.0:
        im.putalpha(im.getchannel("A").point(lambda a: round(a * opacity)))
    im.save(DEST / (name+".png"), optimize=True)


def floating(state):
    w = 144
    im = Image.new("RGBA", (w*S,w*S))
    d = ImageDraw.Draw(im)
    accent = YELLOW if state == "active" else PINK
    if state == "hover": accent = "#ff5caf"
    d.ellipse((7*S,11*S,137*S,141*S),fill=(0,0,0,65))
    d.ellipse((5*S,4*S,137*S,136*S),fill=INK)
    d.ellipse((10*S,9*S,132*S,131*S),fill="white")
    d.ellipse((15*S,14*S,127*S,126*S),fill=accent)
    # Off-centre halftone is part of the button rather than a glow halo.
    for y in range(23,111,9):
        for x in range(23,65,9):
            if (x-71)**2+(y-70)**2 < 49**2:
                d.ellipse((x*S,y*S,(x+2)*S,(y+2)*S),fill=(255,255,255,75))
    points=[]
    for i in range(10):
        a=-math.pi/2+i*math.pi/5
        r=37 if i%2==0 else 17
        points.append(((72+math.cos(a)*r)*S,(70+math.sin(a)*r)*S))
    d.polygon(points,fill=INK)
    points=[]
    for i in range(10):
        a=-math.pi/2+i*math.pi/5
        r=29 if i%2==0 else 13
        points.append(((72+math.cos(a)*r)*S,(69+math.sin(a)*r)*S))
    d.polygon(points,fill="white")
    if state == "notify":
        d.ellipse((109*S,7*S,138*S,36*S),fill=YELLOW,outline=INK,width=3*S)
    im.resize((w,w),Image.Resampling.LANCZOS).save(DEST/f"floating_entry_{state}.png",optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--import-slider-atlas", type=Path)
    args = parser.parse_args()
    DEST.mkdir(parents=True,exist_ok=True)
    if args.import_slider_atlas:
        import_slider(args.import_slider_atlas)
    specs = [
        ("window_frame",384,384,30,INK,None,False),
        ("header_bar",384,96,22,INK,None,False),
        ("sidebar_tab_normal",256,64,28,PINK,None,False,"tab"),
        ("sidebar_tab_hover",256,64,28,"#ff58aa",None,False,"tab"),
        ("sidebar_tab_selected",256,64,28,YELLOW,None,False,"tab"),
        ("card_base",256,160,22,WHITE,None,True),
        ("card_feature_header",384,128,22,WHITE,None,True),
        ("button_primary",192,64,22,PINK,INK),
        ("button_secondary",192,64,22,"#efedf2","#d9d5e0"),
        ("button_selected",192,64,22,YELLOW,INK),
        ("hotkey_badge",96,40,16,INK,None),
        ("toggle_track_off",80,40,19,"#cbc5d3",None),
        ("toggle_track_on",80,40,19,PINK,None),
        ("toggle_thumb",32,32,15,"#ffffff",None),
        ("slider_track",128,32,16,"#ffffff",None),
        ("divider",128,8,3,YELLOW,None),
        ("close_normal",48,48,15,PINK,None),
        ("close_hover",48,48,15,"#ff5caf",None),
    ]
    for args in specs: asset(*args)
    # Preserve the official four-point star and bar, including their edge pixels.
    sources = DEST / "official-sources"
    for part in ("fill", "handle"):
        with Image.open(sources / f"slider_{part}.png") as im:
            im.save(DEST / f"slider_{part}.png", optimize=True)
    for state in ("idle","hover","active","notify"): floating(state)
    manifest=json.loads((DEST/"manifest.json").read_text("utf8"))
    manifest.update(SkinName="XingShu Game Settings",Version="game-ui-r1")
    manifest["Tokens"]={"pink":PINK,"yellow":YELLOW,"ink":INK,"paper":WHITE}
    manifest["SurfaceOpacity"]={"window":0.78,"card":0.93}
    for item in manifest["Assets"]:
        path=DEST/item["RenderedPNG"]
        with Image.open(path) as im: item.update(Width=im.width,Height=im.height)
        item.update(Size=path.stat().st_size,SHA256=hashlib.sha256(path.read_bytes()).hexdigest().upper())
        # These are generated geometric assets; old SVG masters describe the old skin.
        item.pop("MasterSVG",None)
        if item["LogicalName"].startswith("sidebar_tab_"):
            item["SliceBorder"] = [60,30,30,30]
        if item["LogicalName"] == "window_frame.png":
            item["Usage"] = "Translucent body without stripes; entire navigation and content frame is 1440x860"
        if item["LogicalName"] in ("slider_track.png", "slider_fill.png"):
            item["SliceBorder"] = [32,16,32,16] if "fill" in item["LogicalName"] else [16,16,16,16]
        if item["LogicalName"] in ("slider_fill.png", "slider_handle.png"):
            item["Source"] = "Astral Party official UI atlas; see official-sources/provenance.json"
    manifest["MasterSVGCount"]=0
    manifest["TotalPNGBytes"]=sum(x["Size"] for x in manifest["Assets"])
    (DEST/"manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n","utf8")
    subprocess.run([sys.executable,str(ROOT/"tools/generate_ui_payloads.py")],cwd=ROOT,check=True)
    print("Built 24 game-settings assets and matching managed payloads")


if __name__ == "__main__": main()
