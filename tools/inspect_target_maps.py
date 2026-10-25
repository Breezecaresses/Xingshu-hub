"""Render previously captured graph IDs beside reference map directions for auditing."""
import csv
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'XingShu-Dev/build/inspection/sync-targeting-20261024'
BASE = Path.home() / 'AppData/LocalLow/feimo/AstralParty_CN/APX/diagnostics'
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 16)
for mid in (82007, 82008, 82010, 82013, 82015):
    source = sorted(BASE.glob(f'*/monster-maps/map_{mid}_nodes.csv'))[-1]
    rows = list(csv.DictReader(source.open(encoding='utf-8-sig')))
    nodes = {int(r['NodeId']): (float(r['WorldX']), float(r['WorldZ']), [int(n) for n in r['Adjacency'].split(';') if n]) for r in rows}
    for flip in (False, True):
        image = Image.new('RGB', (1000, 1000), 'white'); draw = ImageDraw.Draw(image)
        xs = [n[0] for n in nodes.values()]; zs = [n[1] for n in nodes.values()]
        scale = min(850/(max(xs)-min(xs)), 850/(max(zs)-min(zs)))
        xy = {n: (75+(v[0]-min(xs))*scale, 75+((max(zs)-v[1]) if not flip else (v[1]-min(zs)))*scale) for n,v in nodes.items()}
        for n,v in nodes.items():
            for to in v[2]:
                if to in xy: draw.line((*xy[n], *xy[to]), fill='#444444', width=3)
        for n,v in nodes.items():
            x,y=xy[n]; draw.ellipse((x-14,y-14,x+14,y+14), fill='orange' if len(v[2])>2 else '#aaccff', outline='black')
            draw.text((x,y),str(n),font=font,anchor='mm',fill='black')
        draw.text((20,15),f'Map {mid} / {source.parent.parent.name} / flip={flip}', font=font, fill='black')
        image.save(OUT/f'map_{mid}_{int(flip)}.png')

for source in (OUT/'maps').glob('scene_*.json'):
    nodes=json.loads(source.read_text())['nodes']
    nodes={n['id']:n for n in nodes if n['position'] and not n['disabled']}
    image=Image.new('RGB',(1000,1000),'white'); draw=ImageDraw.Draw(image)
    xs=[-n['position']['z'] for n in nodes.values()]; ys=[n['position']['x'] for n in nodes.values()]
    scale=min(850/max(1,max(xs)-min(xs)),850/max(1,max(ys)-min(ys)))
    xy={i:(75+(-n['position']['z']-min(xs))*scale,75+(n['position']['x']-min(ys))*scale) for i,n in nodes.items()}
    for i,n in nodes.items():
        for to in n['next']:
            if to in xy: draw.line((*xy[i],*xy[to]),fill='#555555',width=2)
    for i,n in nodes.items():
        x,y=xy[i];draw.ellipse((x-13,y-13,x+13,y+13),fill='orange' if len(n['next'])>2 else '#bbddff',outline='black')
        draw.text((x,y),str(i),font=font,anchor='mm',fill='black')
        if n['spawn']:draw.text((x+16,y-16),f"P{n['spawn']+1}",font=font,fill='red')
    draw.text((20,15),source.stem+' official local (-Z,X)',font=font,fill='black')
    image.save(OUT/(source.stem+'.png'))
