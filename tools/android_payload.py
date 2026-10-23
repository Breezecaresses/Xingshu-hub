"""Shared, verified Android payload history for the phone and desktop helpers."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def previous_hashes(manifest):
    # Only intact candidates for this exact game/resource build are accepted.
    found = {'3d94697f4dc6d64c2cb5fcca56339090f54e920cd7dab6de7bcc1912347e5a0f'}
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
