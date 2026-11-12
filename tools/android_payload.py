"""Shared, verified Android payload history for the phone and desktop helpers."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASELINES = {
    '01ee8b4332fdb024fd4c5b9bfdb38ce25f9d42e0b07941765bc7cd06efdaaaa4': {
        'resourceVersion': '026',
        'bundleRelativePath': 'com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__data',
    },
    '2ea82144e02b301a07a236a42706611df82a83b85a92c1b1fdd0d7a1e93a91e2': {
        'resourceVersion': '150',
        'bundleRelativePath': 'com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/7a5e751e708c075b7272acfd3dbe1249/__data',
    },
}


def verified_baseline(original_sha):
    baseline = BASELINES.get(original_sha)
    if baseline is None:
        raise ValueError('Unknown Android baseline; verify version and compatibility before patching')
    return baseline


def validate_baseline_manifest(manifest):
    baseline = verified_baseline(manifest['originalSha256'])
    if (manifest['bundleRelativePath'] != baseline['bundleRelativePath']
            or manifest.get('resourceVersion', baseline['resourceVersion']) != baseline['resourceVersion']):
        raise ValueError('Android resource path/version does not match its verified baseline')
    return baseline


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def previous_hashes(manifest):
    # Only intact candidates for this exact game/resource build are accepted.
    validate_baseline_manifest(manifest)
    found = set()
    if manifest['originalSha256'] == '01ee8b4332fdb024fd4c5b9bfdb38ce25f9d42e0b07941765bc7cd06efdaaaa4':
        found.add('3d94697f4dc6d64c2cb5fcca56339090f54e920cd7dab6de7bcc1912347e5a0f')
    releases = ROOT / '发布包'
    manifests = set(releases.glob('XingShu_Android_*/candidate.json'))
    manifests.update(releases.glob('XingShu_*/Android/candidate.json'))
    for path in sorted(manifests):
        old = json.loads(path.read_text(encoding='utf-8-sig'))
        if any(old.get(key) != manifest.get(key) for key in ('platform', 'package', 'appVersion', 'originalSha256', 'bundleRelativePath')):
            continue
        payload = (path.parent / old['candidate']).resolve()
        if path.parent.resolve() not in payload.parents:
            raise ValueError('Previous Android payload escapes its package')
        if payload.is_file() and sha(payload) == old['candidateSha256']:
            found.add(old['candidateSha256'])
    return sorted(found - {manifest['candidateSha256'], manifest['originalSha256']})
