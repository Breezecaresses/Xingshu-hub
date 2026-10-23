"""Inspect local third-party archives without executing them; map texture bundles to the current CN catalog."""
import argparse
from collections import defaultdict
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys

import UnityPy
from export_game_catalog import addressable_catalog, asset_bundles

ROOT = Path(__file__).resolve().parents[1]
HEX32 = re.compile(r'^[a-f0-9]{32}$')


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inspect_archives(source, extraction, seven_zip):
    rows = []
    for index, archive in enumerate(sorted(source.iterdir())):
        if archive.suffix.lower() not in ('.zip', '.rar', '.7z'):
            continue
        listing = subprocess.run([str(seven_zip), 'l', '-slt', '-sccUTF-8', '--', str(archive)],
                                 capture_output=True, encoding='utf-8', check=True).stdout
        total = 0
        for block in listing.split('----------\n')[-1].split('\n\n'):
            fields = dict(line.split(' = ', 1) for line in block.splitlines() if ' = ' in line)
            if 'Path' not in fields:
                continue
            path = PurePosixPath(fields['Path'].replace('\\', '/'))
            if (path.is_absolute() or any(x in ('.', '..') or ':' in x for x in path.parts)
                    or any(fields.get(x) for x in ('Symbolic Link', 'Hard Link', 'Copy Link'))):
                raise ValueError(f'Unsafe archive entry: {fields["Path"]}')
            total += int(fields.get('Size', 0))
        if total > 2_000_000_000:
            raise ValueError('Archive expands beyond the local inspection limit')
        dest = extraction / f'{index:02d}'
        dest.mkdir(parents=True, exist_ok=True)
        subprocess.run([str(seven_zip), 'x', '-y', '-sccUTF-8', '-o'+str(dest), '--', str(archive)],
                       capture_output=True, check=True)
        rows.append((archive, dest))
    return rows


def inspect_bundle(path):
    env = UnityPy.load(str(path))
    assets, types, externals = [], set(), []
    for obj in env.objects:
        types.add(obj.type.name)
        if obj.type.name in ('Texture2D', 'TextAsset', 'AudioClip', 'Material', 'GameObject', 'Mesh', 'AnimationClip'):
            name = getattr(obj.read(), 'm_Name', '')
            if name:
                assets.append({'name': name, 'type': obj.type.name,
                               'targets': [p.name for p in asset_bundles(name)]})
    for serialized in env.assets:
        externals.extend(str(item.path) for item in serialized.externals)
    return assets, types, externals


def build(source, extraction, output, seven_zip):
    if output.exists():
        raise ValueError('Use a new library output directory')
    output.mkdir(parents=True)
    aa, _, _, _ = addressable_catalog()
    game = aa.parents[2]
    items, raw = [], []
    for archive, directory in inspect_archives(source, extraction, seven_zip):
        groups = defaultdict(lambda: {'files': [], 'unmapped': []})
        for path in sorted(directory.rglob('*')):
            if not path.is_file() or (path.suffix != '.bundle' and path.name != '__data'):
                continue
            relative = path.relative_to(directory)
            # Bundled original copies are documentation, never an alternate mod payload.
            if '原' in relative.parts:
                continue
            if '黑旗袍' in archive.name:
                group = relative.parts[0]
            elif '游戏王' in archive.name:
                group = relative.parts[0]
            else:
                group = '立绘与卡面'
            # Some authors ship two portraits for the same resource, one as a loose bundle
            # and one as a cached bundle. Keep these as mutually exclusive variants.
            if path.name == '__data' and ('修女' in archive.name or '黑旗袍' in archive.name):
                group += ' · 缓存立绘方案'
            record = {'source': str(relative), 'sha256': sha(path), 'size': path.stat().st_size}
            try:
                assets, types, externals = inspect_bundle(path)
                targets = sorted({t for asset in assets for t in asset['targets']})
                record.update(assets=assets, types=sorted(types), externals=externals, targetCandidates=targets)
                # Never rename model/audio bundles whose external references still belong to another build.
                if len(targets) != 1 or externals or not types <= {'AssetBundle', 'Texture2D', 'Sprite'}:
                    raise ValueError('模型/语音或资源依赖待适配' if externals or 'AnimationClip' in types else '未在当前目录唯一定位资源')
                target = aa/'StandaloneWindows64'/targets[0]
                original_assets, original_types, original_external = inspect_bundle(target)
                names = sorted({a['name'] for a in assets})
                if (original_external or not original_types <= {'AssetBundle', 'Texture2D', 'Sprite'}
                        or set(names) != {a['name'] for a in original_assets}):
                    raise ValueError('当前原版的资源结构不同')
                payload = output/'payload'/f'{record["sha256"]}.bundle'
                payload.parent.mkdir(exist_ok=True)
                if not payload.exists():
                    shutil.copy2(path, payload)
                assert sha(payload) == record['sha256']
                groups[group]['files'].append({'source': payload.relative_to(output).as_posix(),
                    'target': target.relative_to(game).as_posix(), 'sha256': record['sha256'],
                    'originalSha256': sha(target), 'size': record['size'], 'assets': names})
            except Exception as exc:
                record['reason'] = str(exc)
                groups[group]['unmapped'].append(str(relative))
            raw.append({'archive': archive.name, **record})
        for group, component in groups.items():
            identifier = 'local.'+hashlib.sha256((archive.name+'/'+group).encode()).hexdigest()[:16]
            targets = [f['target'] for f in component['files']]
            if len(set(targets)) != len(targets):
                raise ValueError(f'Duplicate target in {archive.name}: {group}')
            name = archive.stem + (f' · {group}' if group != '立绘与卡面' else '')
            if component['unmapped'] and component['files']:
                name += '（立绘/界面部分）'
            items.append({'id': identifier, 'name': name, 'archive': archive.name, 'archiveSha256': sha(archive),
                'author': '花环飞鸟' if '花环飞鸟' in archive.name else '阿底' if 'by阿底' in archive.name else 'Foxe（狐e）' if '魔女审判' in archive.name or '修女' in archive.name or '小叶子' in archive.name else '原包作者（见原包说明）',
                'platforms': ['STEAM_CN'], 'files': component['files'], 'unmappedFiles': component['unmapped'], 'conflicts': []})
        print('Inspected:', archive.name, flush=True)
    for item in items:
        paths = {f['target']: f['sha256'] for f in item['files']}
        item['conflicts'] = [other['id'] for other in items if other['id'] != item['id'] and
            any(f['target'] in paths and paths[f['target']] != f['sha256'] for f in other['files'])]
    catalog = {'formatVersion': 1, 'gameVersion': '3.3.0', 'gameAssemblySha256': sha(game/'GameAssembly.dll'),
               'catalogSha256': sha(aa/'catalog.bundle'), 'items': items}
    encoded = json.dumps(catalog, ensure_ascii=False, indent=2)+'\n'
    (output/'catalog.json').write_text(encoded, encoding='utf-8')
    (ROOT/'AstralParty-Hub/src-tauri/src/mod_library.generated.json').write_text(encoded, encoding='utf-8')
    (extraction/'mapping.json').write_text(json.dumps(raw,ensure_ascii=False,indent=2),encoding='utf-8')
    lines = ['# 本地模组适配记录', '', '仅用于本机管理，保留原作者署名与下载原包。不修改原包，不把第三方模组重新授权。', '',
             '仅将无外部依赖且与当前原版资源名称/类型结构相同的纹理映射至 Steam 国服 3.3.0；模型、动画、语音和其他渠道不按文件名盲目替换。实战显示仍待验证。', '']
    for item in items:
        lines.extend([f'## {item["name"]}', '', f'作者：{item["author"]}；来源：{item["archive"]}', '',
                      f'可映射纹理 {len(item["files"])} 个；待适配文件 {len(item["unmappedFiles"])} 个。', ''])
        for f in item['files']:
            lines.append(f'- {", ".join(f["assets"])} → `{f["target"]}`')
        for f in item['unmappedFiles']:
            lines.append(f'- 待适配：`{f}`')
        if item['conflicts']:
            lines.append('- 与其他条目存在相同目标、不同内容；不可同时启用。')
        lines.append('')
    (output/'适配说明.md').write_text('\n'.join(lines),encoding='utf-8')
    print('Library:', len(items), 'components;', sum(len(x['files']) for x in items), 'mapped textures;',
          sum(len(x['unmappedFiles']) for x in items), 'unmapped files')


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT/'星引擎mod')
    parser.add_argument('--extraction', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--seven-zip', type=Path, default=Path('C:/Program Files/7-Zip/7z.exe'))
    args = parser.parse_args()
    build(args.source.resolve(), args.extraction.resolve(), args.out.resolve(), args.seven_zip)
