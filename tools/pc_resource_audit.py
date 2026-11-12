"""Discover and check the current official PC resource without pinning a resource number.

Read-only against game installations. Outputs an audited profile/copy for the build
tools; it never applies an old patched game assembly to an unknown official one.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
PROFILES = ROOT / 'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json'
CHANNELS = {'STEAM_CN': 'CN_STEAM', 'TAPTAP': 'CN_TAPTAP',
            'BILIBILI': 'CN_BILIBILI', 'WEGAME': 'CN_WEGAME'}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def managed_bundle_id(catalog):
    """Resolve the runtime's actual Addressables dependency, not a recent cache file."""
    try:
        keys, buckets_raw, entries_raw = [base64.b64decode(catalog[k], validate=True) for k in
            ('m_KeyDataString', 'm_BucketDataString', 'm_EntryDataString')]
        internal = catalog['m_InternalIds']
        if not isinstance(internal, list) or not all(isinstance(s, str) for s in internal):
            raise ValueError('Invalid internal IDs')

        def ints(blob, pos, count):
            if pos < 0 or count < 0 or count > 200000 or pos + count * 4 > len(blob):
                raise ValueError('Catalog integer range exceeds buffer')
            return struct.unpack_from('<' + str(count) + 'i', blob, pos)

        entry_count = ints(entries_raw, 0, 1)[0]
        if not 0 < entry_count <= 200000 or len(entries_raw) != 4 + entry_count * 28:
            raise ValueError('Invalid catalog entry table')
        bucket_count = ints(buckets_raw, 0, 1)[0]
        if not 0 < bucket_count <= 200000:
            raise ValueError('Invalid catalog bucket count')
        buckets, pos = [], 4
        for _ in range(bucket_count):
            key_offset, count = ints(buckets_raw, pos, 2)
            pos += 8
            indexes = ints(buckets_raw, pos, count)
            pos += count * 4
            if any(i < 0 or i >= entry_count for i in indexes) or not 0 <= key_offset < len(keys):
                raise ValueError('Invalid catalog index')
            key = None
            if keys[key_offset] == 0:  # SerializationUtilities.AsciiString
                size = ints(keys, key_offset + 1, 1)[0]
                if size < 0 or key_offset + 5 + size > len(keys):
                    raise ValueError('Invalid catalog string')
                key = keys[key_offset + 5:key_offset + 5 + size].decode('ascii')
            buckets.append((key, indexes))
        if pos != len(buckets_raw):
            raise ValueError('Trailing catalog bucket data')
        runtime = [b for b in buckets if b[0] == 'AstralParty.Runtime.dll']
        if len(runtime) != 1 or len(runtime[0][1]) != 1:
            raise ValueError('Expected one runtime address')

        found, visited, pending = set(), set(), list(runtime[0][1])
        while pending:
            index = pending.pop()
            if index in visited:
                continue
            visited.add(index)
            if len(visited) > 256:
                raise ValueError('Excessive runtime dependencies')
            entry = ints(entries_raw, 4 + index * 28, 7)
            if not 0 <= entry[0] < len(internal):
                raise ValueError('Invalid internal ID index')
            location = internal[entry[0]]
            match = re.fullmatch(r'\{App\.WebServerConfig\.Path\}[\\/]([0-9a-f]{32})\.bundle', location)
            if match:
                found.add(match[1])
            dependency = entry[2]
            if dependency != -1:
                if not 0 <= dependency < len(buckets):
                    raise ValueError('Invalid dependency index')
                pending.extend(buckets[dependency][1])
        if len(found) != 1:
            raise ValueError('Expected one managed runtime bundle dependency')
        return found.pop()
    except (KeyError, TypeError, UnicodeError, struct.error) as exc:
        raise ValueError('Unsupported or damaged Addressables catalog') from exc


def get(url, limit):
    request = urllib.request.Request(url, headers={'User-Agent': 'XingShu-Compatibility-Audit'})
    with urllib.request.urlopen(request, timeout=25) as response:
        raw = response.read(limit + 1)
    if len(raw) > limit:
        raise ValueError('Official configuration exceeds size limit')
    return raw


def official_catalog(profile):
    route, version = CHANNELS[profile['platformId']], profile['gameVersion']
    config = json.loads(get(f'https://serescn.feimogames.com/{route}/{version}/RemoteConfig/RemoteConfig.json', 65536))
    if config.get('EnableHttps') is not True:
        raise ValueError('Official HTTPS configuration needs review')
    query = urllib.parse.urlencode({'route': route, 'version': version})
    dispatch = json.loads(get('https://se-web-cn.feimogames.com:7878/api/hotaddressExtend/get?' + query, 65536))
    if dispatch.get('route') != route or dispatch.get('version') != version:
        raise ValueError('Official route/version mismatch')
    endpoint = urllib.parse.urlsplit(dispatch['sourceUrl'])
    prefix = f'/{route}/{version}/'
    resource = endpoint.path[len(prefix):] if endpoint.path.startswith(prefix) else ''
    if (endpoint.scheme not in ('http', 'https') or endpoint.netloc != 'serescn.feimogames.com' or
            endpoint.query or endpoint.fragment or not re.fullmatch(r'[A-Za-z0-9_.-]+', resource)):
        raise ValueError('Unrecognized official resource location')
    url = 'https://' + endpoint.netloc + endpoint.path + '/catalog_' + version + '.json'
    raw = get(url, 16 * 1024 * 1024)
    return resource, url, raw


def audit(profile, out, hooks_dll):
    import UnityPy
    profile = dict(profile)
    root = Path(profile['installRoot']).resolve(strict=True)
    native = root / profile['gameRuntimeDllPath']
    native_sha = digest(native.read_bytes())
    if native_sha.upper() != profile['gameRuntimeDllSha256'].upper():
        raise ValueError('Native executable changed; separate ABI review is required')
    resource, url, raw_catalog = official_catalog(profile)
    catalog = json.loads(raw_catalog.decode('utf-8-sig'))
    bundle = managed_bundle_id(catalog)
    data_dir = root / Path(profile['bundleRelativePath']).parts[0]
    matches = list((data_dir / 'com.unity.addressables/AssetBundles').glob('*/' + bundle + '/__data'))
    if len(matches) != 1:
        raise ValueError('Current official runtime has not downloaded, or cache is ambiguous')
    target = matches[0].resolve(strict=True)
    if root not in target.parents or not re.fullmatch('[0-9a-f]{32}', target.parent.parent.name):
        raise ValueError('Invalid runtime cache location')
    original = target.read_bytes()
    if not original.startswith(b'UnityFS\x00'):
        raise ValueError('Invalid official runtime container')
    folder = out / 'official' / profile['platformId']
    folder.mkdir(parents=True, exist_ok=True)
    assemblies = []
    for obj in UnityPy.load(original).objects:
        if obj.type.name != 'TextAsset':
            continue
        asset = obj.read()
        name = asset.m_Name
        if (not re.fullmatch(r'[A-Za-z0-9_.-]+\.dll', name) or name.startswith(('XingShu', 'APX'))):
            raise ValueError('Selected official container contains an unexpected/modified assembly')
        data = asset.m_Script.encode('utf-8', errors='surrogateescape')
        if not data.startswith(b'MZ') or any(a['name'] == name for a in assemblies):
            raise ValueError('Invalid or duplicate managed assembly')
        (folder / name).write_bytes(data)
        assemblies.append({'name': name, 'pathId': obj.path_id, 'size': len(data), 'sha256': digest(data)})
    if sum(a['name'] == 'AstralParty.Runtime.dll' for a in assemblies) != 1:
        raise ValueError('Expected exactly one game runtime')
    result = subprocess.run(['pwsh', '-NoProfile', '-File', str(ROOT / 'tools/check_runtime_compatibility.ps1'),
        '-RuntimeDll', str(folder / 'AstralParty.Runtime.dll'), '-HooksDll', str(hooks_dll)],
        capture_output=True, text=True)
    (folder / 'compatibility.log').write_text(result.stdout + result.stderr, 'utf-8')
    if result.returncode:
        raise ValueError('Game member compatibility failed; see compatibility.log')
    if digest(target.read_bytes()) != digest(original) or digest(native.read_bytes()) != native_sha:
        raise ValueError('Game files changed while auditing; finish the official update first')
    (folder / 'vanilla.__data').write_bytes(original)
    (folder / 'catalog_3.3.0.json').write_bytes(raw_catalog)
    profile.update(bundleId=bundle, bundleRelativePath=str(target.relative_to(root)),
        containerSha256=digest(original).upper(), resourceVersion=resource,
        compatibilityStatus='PARTIALLY_AUDITED', lastVerifiedAt=None)
    if 'containerHash' in profile:
        profile['containerHash'] = profile['containerSha256']
    if 'status' in profile:
        profile['status'] = 'PARTIALLY_AUDITED'
    report = {'platform': profile['platformId'], 'resourceVersion': resource, 'catalogUrl': url,
        'catalogSha256': digest(raw_catalog), 'catalogBuildHash': catalog.get('m_BuildResultHash'),
        'bundleId': bundle, 'bundleRelativePath': profile['bundleRelativePath'], 'target': str(target),
        'containerSha256': digest(original), 'nativeSha256': native_sha, 'assemblies': assemblies,
        'memberCompatibility': result.stdout.strip(), 'gameFilesModified': False}
    (folder / 'audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', 'utf-8')
    print(f"{profile['platformId']}: resource {resource}; catalog dependency resolved; {len(assemblies)} DLLs; game interfaces PASS", flush=True)
    return profile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profiles', type=Path, default=PROFILES)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--hooks-dll', type=Path, required=True)
    parser.add_argument('--platform', choices=CHANNELS, action='append')
    args = parser.parse_args()
    profiles = json.loads(args.profiles.read_text('utf-8'))
    args.out.mkdir(parents=True, exist_ok=True)
    selected = args.platform or list(CHANNELS)
    for index, profile in enumerate(profiles):
        if profile['platformId'] in selected:
            profiles[index] = audit(profile, args.out.resolve(), args.hooks_dll.resolve(strict=True))
    (args.out / 'platform_profiles.json').write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + '\n', 'utf-8')


if __name__ == '__main__':
    main()
