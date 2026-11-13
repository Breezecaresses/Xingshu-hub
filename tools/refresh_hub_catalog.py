"""Refresh Hub's offline encyclopedia from the currently installed official resource.

Fetches only Feimo's dispatch/catalog; game installations and account sessions are read-only.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from pc_resource_audit import official_catalog, PROFILES, ROOT
import export_game_catalog as exporter


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--resolve-only', action='store_true')
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    profile = next(p for p in json.loads(PROFILES.read_text('utf-8')) if p['platformId'] == 'STEAM_CN')
    resource, url, raw = official_catalog(profile)
    catalog = args.out/'catalog_3.3.0.json'
    catalog.write_bytes(raw)
    data_dir = Path(profile['installRoot'])/Path(profile['bundleRelativePath']).parts[0]
    exporter.SOURCE_CATALOG, exporter.GAME_DATA = catalog, data_dir
    candidates = exporter.asset_bundles('GameData_CN')
    matches = []
    for path in candidates:
        try:
            tables, _ = exporter.extract(path)
            if 'Character' in tables and 'Card' in tables: matches.append(path)
        except (ValueError, FileNotFoundError, KeyError):
            continue
    if len(matches) != 1:
        raise ValueError('Current official config is unavailable/ambiguous; finish official game resource update')
    source = matches[0]
    report = dict(resourceVersion=resource, catalogUrl=url, catalogSha256=hashlib.sha256(raw).hexdigest(),
                  configPath=str(source), configSha256=hashlib.sha256(source.read_bytes()).hexdigest())
    (args.out/'catalog-source.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), 'utf-8')
    print(json.dumps(report, ensure_ascii=False), flush=True)
    if not args.resolve_only:
        subprocess.run([sys.executable, str(ROOT/'tools/export_game_catalog.py'), '--config-bundle', str(source),
                        '--catalog-json', str(catalog), '--game-data', str(data_dir), '--resource-version', resource], check=True)


if __name__ == '__main__':
    main()
