"""Export official offline encyclopedia from restored Unity configuration, never patched XingShu tables.

Run export_catalog_schema.ps1 first. Schemas come from Cecil metadata, not guessed field offsets.
"""
import hashlib
import argparse
import base64
import json
import re
import shutil
import struct
import subprocess
import sys
from functools import lru_cache
from pathlib import Path
import UnityPy

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = json.loads((ROOT / 'tools/catalog_schema.json').read_text('utf-8'))
TABLES = {
    'Card', 'STRCard', 'Character', 'STRCharacter', 'Skin', 'STRSkin',
    'Favor', 'Player', 'Item', 'STRItem', 'Skill', 'STRSkill', 'Mutator',
    'STRMutator', 'Map', 'STRMap', 'GameMode', 'STRGameMode',
    'Relic', 'STRRelic', 'PVENurturance', 'STRPVENurturance',
    'MapEvent', 'STRMapEvent', 'Monster', 'STRMonster', 'Buff', 'STRBuff',
}


def varint(data, pos):
    value = shift = 0
    while pos < len(data) and shift < 70:
        b = data[pos]; pos += 1
        value |= (b & 127) << shift
        if b < 128:
            return value, pos
        shift += 7
    raise ValueError('Invalid protobuf varint')


def fields(data):
    pos = 0
    while pos < len(data):
        tag, pos = varint(data, pos)
        wire = tag & 7
        if not tag:
            raise ValueError('Zero protobuf tag')
        if wire == 0:
            value, pos = varint(data, pos)
        elif wire == 2:
            size, pos = varint(data, pos)
            value = data[pos:pos+size]; pos += size
            if len(value) != size:
                raise ValueError('Truncated protobuf field')
        elif wire in (1, 5):
            size = 8 if wire == 1 else 4
            value = data[pos:pos+size]; pos += size
        else:
            raise ValueError(f'Unsupported wire {wire}')
        yield str(tag >> 3), wire, value


def scalar(value, typ, wire):
    if typ == 'System.String':
        return value.decode('utf-8')
    if typ == 'System.Boolean':
        return bool(value)
    if typ in ('System.Single', 'System.Double'):
        return struct.unpack('<f' if typ.endswith('Single') else '<d', value)[0]
    if typ in SCHEMA and 'fields' in SCHEMA[typ]:
        return decode(value, typ)
    if typ in ('System.Int32', 'System.Int64'):
        if isinstance(value, bytes):
            return int.from_bytes(value, 'little', signed=True)
        bits = 32 if typ.endswith('32') else 64
        return (value & ((1 << bits) - 1)) - (1 << bits) if value & (1 << (bits - 1)) else value
    if isinstance(value, bytes):
        if typ in ('System.UInt32', 'System.UInt64'):
            return int.from_bytes(value, 'little')
        return None
    return value


def decode(data, typ):
    result = {}
    for number, wire, value in fields(data):
        f = SCHEMA[typ]['fields'].get(number)
        if not f:
            continue
        name, ft = f['name'], f['type']
        if 'MapField' in ft:
            # Lookup tables are generated duplicates. Keep actual reward/material maps.
            if name.endswith('Dict'):
                continue
            entry = {n: scalar(v, f['args'][int(n)-1], w) for n, w, v in fields(value)}
            result.setdefault(name, {})[str(entry.get('1', 0))] = entry.get('2', 0)
            continue
        if 'RepeatedField' in ft:
            et = f['args'][0]
            arr = result.setdefault(name, [])
            if wire == 2 and et != 'System.String' and not (et in SCHEMA and 'fields' in SCHEMA[et]):
                p = 0
                while p < len(value):
                    enc = f.get('encoding', '')
                    if et in ('System.Single', 'System.Double') or 'Fixed' in enc:
                        size = 8 if et.endswith('Double') or enc.endswith('64') else 4
                        v = value[p:p+size]; p += size
                    else:
                        v, p = varint(value, p)
                        if enc.startswith('SInt'): v = (v >> 1) ^ -(v & 1)
                    arr.append(scalar(v, et, 0))
            else:
                arr.append(scalar(value, et, wire))
        else:
            try:
                result[name] = scalar(value, ft, wire)
            except Exception as exc:
                raise ValueError(f'{typ}.{name} wire={wire}: {exc}') from exc
    return result


def extract(config_bundle=None):
    candidates = [Path(config_bundle)] if config_bundle else sorted((ROOT / 'XingShu/CN_STEAM/3.3.0/restore').rglob('__data'))
    for path in candidates:
        if path.stat().st_size > 3_000_000:
            continue
        tables = {}
        for obj in UnityPy.load(str(path)).objects:
            if obj.type.name == 'TextAsset':
                asset = obj.read()
                typ = asset.m_Name + 'Configure'
                if typ in SCHEMA and asset.m_Name in TABLES:
                    tables[asset.m_Name] = decode(asset.m_Script.encode('utf-8', 'surrogateescape'), typ)
        if 'Card' in tables and 'Character' in tables:
            return tables, path
    raise FileNotFoundError('Original game configuration bundle missing')


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config-bundle', type=Path, help='Unmodified official configuration bundle')
    args = parser.parse_args()
    tables, source = extract(args.config_bundle)
    # Preview audits also retain referenced Buff text, rather than inventing a
    # complete skill description from an unlabelled parameter vector.
    if args.config_bundle:
        from audit_preview_characters import read_tables
        tables.update(read_tables(source))
    # Retain decoded source only in build cache, not a runtime dependency.
    cache = ROOT / 'XingShu-Dev/build/catalog'
    cache.mkdir(parents=True, exist_ok=True)
    (cache / 'tables.json').write_text(json.dumps(tables, ensure_ascii=False, indent=2), 'utf-8')
    loc = {k: {r['Id']: r.get('Simplified', '') for r in v.get('Locals', [])} for k, v in tables.items() if k.startswith('STR')}
    def text(table, key):
        return loc.get('STR'+table, {}).get(key, '')
    items = {r['Id']: r for r in tables['Item']['Infos']}
    skin_groups = {r['Id']: r['SkinStandingPaintingConfigureItems'] for r in tables['Skin']['StandingPaintings']}
    skills = {r['Id']: r for r in tables['Skill']['Infos']}
    rewards = {r['Id']: r for r in tables['Favor']['LevelRewards']}
    breaks = {r['Id']: r for r in tables['Favor']['Breakthroughs']}
    enhancements = {r['Id']: r.get('PVENurturanceEnhancementConfigureItems', []) for r in tables['PVENurturance']['Enhancements']}
    pve_breaks = {r['Id']: r for r in tables['PVENurturance']['Breaks']}
    expressions = {r['Id']: r.get('CharacterExpressionPackConfigureItems', []) for r in tables['Character']['ExpressionPacks']}
    wanted = set()
    videos = set()
    spines = set()
    def image(key):
        if key: wanted.add(key)
        return key or ''
    def original_asset(row, original, sfw):
        # The Hub displays the authored PC artwork. SFW is only a missing-source fallback.
        primary, fallback = row.get(original), row.get(sfw)
        return primary if primary and asset_bundles(primary) else fallback or primary or ''
    def skill(sid):
        row = skills.get(sid, {})
        return dict(id=sid, name=text('Skill', row.get('NameID')), description=text('Skill', row.get('DescID')), cooldown=row.get('Round', 0))
    def pve_potential(pid):
        row = pve_breaks.get(pid, {})
        if not row:
            return None
        return dict(id=pid, description=text('PVENurturance', row.get('DescriptionID')),
                    requiredLevel=row.get('UnlockNeedPVELevel', 0), materials=row.get('NeedMaterials', {}),
                    skills=[skill(i) for i in [row.get('ReplaceActiveSkill', 0)] + row.get('AddPassiveSkills', []) if i],
                    removedSkillIds=row.get('DelPassiveSkills', []))
    chars = []
    for c in tables['Character']['Infos']:
        skins = []
        for s in skin_groups.get(c.get('StandingPainting'), []):
            item = items.get(s.get('ItemID'), {})
            skins.append(dict(id=s.get('ItemID', 0), index=s.get('Index', 0), default=s.get('IsDefault', False), name=text('Item', item.get('NameID')) or ('原初装扮' if s.get('IsDefault') else '装扮 '+str(s.get('Index', 0))), description=text('Skin', s.get('LongTextID')) or text('Item', item.get('DescriptionID')), image=image(original_asset(s, 'Character', 'SfwCharacter'))))
        emotes = []
        for e in expressions.get(c.get('ExpressionPackID'), []):
            item = items.get(e.get('ItemID'), {})
            emotes.append(dict(id=e['ItemID'], name=text('Item', item.get('NameID')),
                               image=image(item.get('Icon')), default=e.get('IsDefault', False),
                               animated=bool(e.get('VideoKey'))))
        potentials = [pve_potential(pid) for pid in c.get('PveBreak', [])]
        potentials = [p for p in potentials if p]
        chars.append(dict(id=c['Id'], name=text('Character', c.get('NameID')), title=text('Character', c.get('NickID')), biography=text('Character', c.get('BiographyID')), comment=text('Character', c.get('CommentId')), gallery=c.get('IsGalleryShow', False), hasBond=c.get('HasKizuna', False), hp=c.get('Blood', 0), attack=c.get('Attack', 0), defense=c.get('Defense', 0), image=image(c.get('CharacterMap')), skills=[skill(i) for i in [c.get('ActiveSkill',0)]+c.get('PassiveSkills',[]) if i], pveSkills=[skill(i) for i in [c.get('PveActiveSkill',0)]+c.get('PvePassiveSkills',[]) if i], skins=skins, bondRewards=rewards.get(c.get('FavorLevelReward'),{}).get('FavorLevelRewardConfigureItems',[]), breakthrough=breaks.get(c.get('FavorBreakthroughReward'),{}),
                          pveProgression=[dict(level=e.get('Lv', 0), description=text('PVENurturance', e.get('DescriptionID'))) for e in enhancements.get(c['Id'], [])],
                          pvePotential=potentials[0] if potentials else None, pvePotentials=potentials, emotes=emotes))
    cards = []
    for c in tables['Card']['Infos']:
        cards.append(dict(id=c['Id'],name=text('Card',c.get('NameID')),number=text('Card',c.get('CardNumb')),description=text('Card',c.get('DescId')),comment=text('Card',c.get('CommentId')),image=image(original_asset(c, 'Image', 'ImageSfw')),type=c.get('CardType',0),cost=c.get('Cost',0),gallery=c.get('IsGalleryShow',False),dedicated=c.get('IsDedicated',False),playable=c.get('IsPlayable',False),target=c.get('CardTargetType',0),params=c.get('Params',[]),relatedCards=c.get('CardIds',[])))
    altarts=[]
    for group in tables['Card']['AltArts']:
        for a in group['CardAltArtConfigureItems']:
            item=items.get(a['AltArtId'],{})
            video = original_asset(a, 'AccountBackgroundVideo', 'AccountBackgroundVideoSfw')
            spine = original_asset(a, 'AccountBackgroundSpine', 'AccountBackgroundSpineSfw')
            if video: videos.add(video)
            if spine: spines.add(spine)
            altarts.append(dict(id=a['AltArtId'],cardId=group['CardId'],name=text('Item',item.get('NameID')),description=text('Item',item.get('DescriptionID')),image=image(original_asset(a, 'AccountBackground', 'AccountBackgroundSfw')),animated=bool(video or spine),video=video,spine=spine,front=image(a.get('CardFront')),animationKind='video' if video else 'spine' if spine else 'none'))
    mutators=[dict(id=r['Id'],name=text('Mutator',r.get('NameID')),description=text('Mutator',r.get('DescId')),image=image(r.get('Icon'))) for r in tables['Mutator']['Infos']]
    role_tags = {1: '通用', 2: '攻击', 3: '卡牌', 4: '支援', 5: '防御'}
    quality_names = {1: '蓝色', 2: '紫色', 3: '金色'}
    chips = [dict(id=r['Id'], name=text('Relic', r.get('NameID')),
                  description=text('Relic', r.get('DescID')), image=image(r.get('Icon')),
                  quality=r.get('RelicQualityType', 0), qualityName=quality_names.get(r.get('RelicQualityType'), ''),
                  type=r.get('KeyWordType', 0), tags=[role_tags.get(i, str(i)) for i in r.get('TagType', [])],
                  params=r.get('Params', []), mapLimit=r.get('MapLimit', []), order=r.get('OrderWeight', 0))
             for r in tables['Relic']['Infos']]
    mode_rows = tables['GameMode']['Infos']
    difficulties = {r['Id']: r.get('MapGameDifficultyConfigureItems', []) for r in tables['Map']['GameDifficultys']}
    monsters = {r['Id']: r for r in tables['Monster']['Infos']}
    def monster(mid):
        row = monsters.get(mid, {})
        return dict(id=mid, name=text('Monster', row.get('NameID')), title=text('Monster', row.get('NickID')))
    maps = []
    for r in tables['Map']['Infos']:
        mid = r['Id']
        ds = []
        for d in difficulties.get(mid, []):
            index = d.get('Index', 0)
            ds.append(dict(index=index, name=f'难度 {index + 1}', description=text('GameMode', 20001 + index), turnLimit=d.get('ProgressLimit', 0),
                           monsters=[monster(int(i)) for i in d.get('MonsterIds', {})],
                           missionIds=d.get('PveMissions', []), eventIds=d.get('ProgressEvents', []),
                           mutatorPool=d.get('MutatorPool', [])))
        maps.append(dict(id=mid, name=text('Map', r.get('MapName')),
                         description=text('Map', r.get('MapDescription')), galleryDescription=text('Map', r.get('MapGallery')),
                         image=image(r.get('MapImage')), sceneImage=image(r.get('MapSceneImage')), sceneIds=r.get('Mids', []),
                         modeIds=[m.get('MapModeType', 0) for m in mode_rows if mid in m.get('MapID', [])],
                         galleryTypes=[g.get('GalleryMapType', 0) for g in tables['GameMode']['GalleryMaps'] if mid in g.get('MapID', [])],
                         eventIds=r.get('MapeventIDs', []), specialCardIds=r.get('MapSpecialCard', []),
                         specialEventIds=r.get('MapSpecialEvent', []), specialChipIds=r.get('MapSpecialRelic', []), difficulties=ds))
    modes = [dict(id=r.get('MapModeType', 0), name=text('GameMode', r.get('NameID')),
                  description=text('GameMode', r.get('ModeDesID')), rules=text('GameMode', r.get('RulesID')),
                  mapIds=r.get('MapID', []), group=r.get('MapModeGroup', 0), visible=r.get('IsShow', False),
                  handLimit=r.get('CardInHandLimit', 0), battleCost=r.get('Battlecost', 0)) for r in mode_rows]
    assets = export_images(wanted)
    video_assets = export_videos(videos, cache)
    assets.update(video_assets)
    spine_assets = export_spines(spines)
    for alt in altarts:
        alt['animationAvailable'] = alt['video'] in video_assets
    out=dict(version='3.3.0',source='官方游戏原版配置',configSha256=hashlib.sha256(source.read_bytes()).hexdigest(),characters=chars,cards=cards,altArts=altarts,mutators=mutators,chips=chips,favorLevels=tables['Favor']['Levels'],playerLevels=tables['Player']['Levels'],pveLevels=[dict(level=r.get('Id', 0),exp=r.get('Exp', 0)) for r in tables['PVENurturance']['Levelups']],maps=maps,modes=modes,assets=assets,spineAssets=spine_assets)
    out['itemNames']={str(i):text('Item',r.get('NameID')) for i,r in items.items() if text('Item',r.get('NameID'))}
    from audit_preview_characters import apply_previews
    out = apply_previews(out, tables, assets)
    output=ROOT/'AstralParty-Hub/src/shared/gameCatalog.generated.json'
    output.write_text(json.dumps(out,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(f'Catalog: {len(chars)} characters, {sum(len(c["skins"]) for c in chars)} skins, {sum(len(c["emotes"]) for c in chars)} emotes, {len(cards)} cards, {len(altarts)} alternate arts, {len(chips)} chips, {len(maps)} maps; {len(assets)-len(video_assets)}/{len(wanted)} images, {len(video_assets)} videos, {len(spine_assets)} Spine sources')


@lru_cache(maxsize=1)
def addressable_catalog():
    sys.path.insert(0,str(ROOT/'XingShu-Dev/tools'))
    from build_card_asset_inventory_v2 import parse_catalog
    aa=Path('E:/steam/steamapps/common/Astral Party/8vJXn6CN/AstralParty_CN_Data/StreamingAssets/aa')
    keys,entries,buckets=parse_catalog(str(aa/'catalog.bundle'))
    cat = next(json.loads(o.read().m_Script) for o in UnityPy.load(str(aa/'catalog.bundle')).objects if o.type.name == 'TextAsset')
    encoded = base64.b64decode(cat['m_EntryDataString'])
    internal_ids = cat['m_InternalIds']
    for i, entry in enumerate(entries):
        internal_index = struct.unpack_from('<i', encoded, 4 + 28*i)[0]
        entry['internal_id'] = internal_ids[internal_index]
    return aa, keys, entries, buckets


@lru_cache(maxsize=None)
def asset_bundles(key):
    aa, keys, entries, buckets = addressable_catalog()
    paths = []
    for idx in keys.get(key, []):
        dep = entries[idx]['dep_key_idx']
        if dep < 0: continue
        # A dependency key can be an integer hash for several bundles, not a filename.
        for dep_idx in buckets[dep][1]:
            basename = entries[dep_idx]['internal_id'].replace('\\', '/').rsplit('/', 1)[-1]
            bundle = aa/'StandaloneWindows64'/basename
            if bundle.exists() and bundle not in paths:
                paths.append(bundle)
    return tuple(paths)


def export_images(wanted):
    dest=ROOT/'AstralParty-Hub/public/catalog'
    dest.mkdir(parents=True,exist_ok=True)
    assets={}
    for key in sorted(wanted):
        if not re.fullmatch(r'[A-Za-z0-9_]+',key): continue
        existing=ROOT/'AstralParty-Hub/public/cards'/f'{key}.png'
        target=dest/f'{key}.webp'
        if existing.exists():
            assets[key]=f'/cards/{key}.png'; continue
        if target.exists():
            assets[key]=f'/catalog/{key}.webp'; continue
        for bundle in asset_bundles(key):
            env=UnityPy.load(str(bundle))
            for obj in env.objects:
                if obj.type.name not in ('Texture2D','Sprite'): continue
                data=obj.read()
                if data.m_Name != key: continue
                try:
                    im=data.image
                    im.thumbnail((1400,1400))
                    im.save(target,'WEBP',quality=92,method=4)
                    assets[key]=f'/catalog/{key}.webp'
                except Exception as exc:
                    print(f'Image unavailable {key}: {type(exc).__name__}')
                break
            if key in assets: break
    return assets


def export_videos(wanted, cache):
    """Export bundled CRI USM bytes and transcode the original frames for HTML video."""
    assets = {}
    ffmpeg = shutil.which('ffmpeg')
    dest = ROOT/'AstralParty-Hub/public/catalog/video'
    dest.mkdir(parents=True, exist_ok=True)
    for key in sorted(wanted):
        if not re.fullmatch(r'[A-Za-z0-9_]+', key): continue
        usm = cache/f'{key}.usm'
        if not usm.exists():
            for bundle in asset_bundles(key):
                for obj in UnityPy.load(str(bundle)).objects:
                    if obj.type.name != 'MonoBehaviour': continue
                    tree = obj.read_typetree()
                    if tree.get('m_Name') != key: continue
                    for ref in tree.get('references', {}).get('RefIds', []):
                        data = ref.get('data', {}).get('data')
                        if isinstance(data, list) and data[:4] == [67, 82, 73, 68]:
                            usm.write_bytes(bytes(data))
                if usm.exists(): break
        if not usm.exists() or not ffmpeg:
            print(f'Video unavailable {key}: source or ffmpeg missing')
            continue
        ffprobe = Path(ffmpeg).with_name('ffprobe.exe' if sys.platform == 'win32' else 'ffprobe')
        alpha = False
        if ffprobe.exists():
            probe = subprocess.run([str(ffprobe), '-v', 'error', '-select_streams', 'v',
                                    '-show_entries', 'stream=index', '-of', 'json', str(usm)],
                                   capture_output=True, text=True, encoding='utf-8', errors='replace')
            if not probe.returncode:
                # These CRI assets carry an optional second grayscale alpha video stream.
                alpha = len(json.loads(probe.stdout).get('streams', [])) == 2
        target = dest/f'{key}.{ "webm" if alpha else "mp4" }'
        if target.exists():
            assets[key] = f'/catalog/video/{target.name}'
            continue
        # CRI's container reports unreliable timestamps; frame order and native frame rate
        # retain the authored loop duration. Browser decoders do not support USM directly.
        codec_args = (['-filter_complex', '[0:v:0]setpts=N/FRAME_RATE/TB[c];[0:v:1]setpts=N/FRAME_RATE/TB,format=gray[a];[c][a]alphamerge[out]',
                       '-map', '[out]', '-an', '-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', '24',
                       '-pix_fmt', 'yuva420p', '-auto-alt-ref', '0'] if alpha else
                      ['-map', '0:v:0', '-an', '-vf', 'setpts=N/FRAME_RATE/TB', '-c:v', 'libx264',
                       '-crf', '20', '-preset', 'medium', '-pix_fmt', 'yuv420p', '-movflags', '+faststart'])
        result = subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-y',
                                 '-i', str(usm)] + codec_args + [str(target)],
                                capture_output=True, text=True, encoding='utf-8', errors='replace')
        if result.returncode:
            print(f'Video unavailable {key}: {result.stderr[:180]}')
            continue
        assets[key] = f'/catalog/video/{target.name}'
    return assets


def export_spines(wanted):
    """Preserve real skeleton/atlas sources; availability is separate from playback."""
    sources = {}
    for key in sorted(wanted):
        if not re.fullmatch(r'[A-Za-z0-9_]+', key): continue
        dest = ROOT/'AstralParty-Hub/public/catalog/spine'/key
        for bundle in asset_bundles(key):
            env = UnityPy.load(str(bundle))
            found = {}
            textures = {}
            for obj in env.objects:
                if obj.type.name == 'TextAsset':
                    data = obj.read()
                    if data.m_Name == key+'.skel':
                        found['skeleton'] = data.m_Script.encode('utf-8', 'surrogateescape')
                    elif data.m_Name == key+'.atlas':
                        found['atlas'] = data.m_Script
                elif obj.type.name == 'Texture2D':
                    data = obj.read()
                    textures[data.m_Name] = data
            if 'skeleton' not in found or 'atlas' not in found: continue
            dest.mkdir(parents=True, exist_ok=True)
            (dest/'skeleton.skel').write_bytes(found['skeleton'])
            # Unity stores CRLF; default Windows text output would expand it to CRCRLF,
            # which makes Spine treat every metadata line as a new atlas page.
            (dest/'atlas.atlas').write_text(found['atlas'].replace('\r\n', '\n'), 'utf-8', newline='\n')
            pages = re.findall(r'^(\S+\.png)\s*$', found['atlas'], re.MULTILINE)
            exported = []
            for page in pages:
                name = Path(page).stem
                if name in textures:
                    textures[name].image.save(dest/page)
                    exported.append(page)
            base = f'/catalog/spine/{key}'
            sources[key] = dict(skeleton=base+'/skeleton.skel', atlas=base+'/atlas.atlas',
                                images=[base+'/'+p for p in exported],
                                runtimeVersion='4.3', animation='Idle', playbackAvailable=len(exported) == len(pages) and bool(pages))
            if (dest/'poster.png').exists():
                sources[key]['poster'] = base+'/poster.png'
            break
    return sources


if __name__ == '__main__':
    main()
