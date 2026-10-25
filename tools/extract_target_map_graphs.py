"""Read local official map nodes for route-priority audits. Never modify game assets."""
import hashlib
import json
from pathlib import Path
import UnityPy
from export_game_catalog import asset_bundles

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'XingShu-Dev/build/inspection/sync-targeting-20261024/maps'
OUT.mkdir(parents=True, exist_ok=True)
for scene in (7, 8, 10, 12, 13, 14, 15, 16, 17):
    nodes = []
    sources = []
    for path in asset_bundles(f'Map_{scene:03}'):
        env = UnityPy.load(str(path))
        transforms = {}
        for obj in env.objects:
            if obj.type.name == 'Transform':
                t = obj.read_typetree(); transforms[t['m_GameObject']['m_PathID']] = t['m_LocalPosition']
        for obj in env.objects:
            if obj.type.name != 'MonoBehaviour': continue
            try: tree = obj.read_typetree()
            except Exception: continue
            n = tree.get('nodeInfo')
            if not isinstance(n, dict) or '_neighborLandIds' not in n: continue
            nodes.append(dict(id=n['_id'], type=n['_landType'], spawn=n['_playerSerialNumber'], initial=n['_initDirLandId'],
                next=n['_neighborLandIds'], position=transforms.get(tree['m_GameObject']['m_PathID']), disabled=tree.get('Disable',0)))
        if nodes: sources.append(dict(bundle=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    nodes.sort(key=lambda n:n['id'])
    (OUT/f'scene_{scene}.json').write_text(json.dumps(dict(scene=scene,sources=sources,nodes=nodes),indent=2),encoding='utf-8')
    print(f'Scene {scene}: {len(nodes)} nodes, forks '+str([(n['id'],n['next']) for n in nodes if len(n['next'])>2])+', spawns '+str([(n['id'],n['spawn']) for n in nodes if n['spawn']]))
