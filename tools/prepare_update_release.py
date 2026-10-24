"""Generate the signed update asset after the desktop ZIP and phone APK are verified.

No upload is performed. The existing APK signing key stays in the user's signing directory.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPO = 'Breezecaresses/Xingshu-hub'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_envelope(path):
    e = json.loads(path.read_text('utf-8'))
    data = base64.b64decode(e['payload'], validate=True)
    with tempfile.TemporaryDirectory(prefix='xingshu-signature-') as temp:
        payload, signature = Path(temp) / 'payload.json', Path(temp) / 'signature.txt'
        payload.write_bytes(data)
        signature.write_text(e['signature'], encoding='ascii')
        subprocess.run(['java', str(ROOT / 'tools/SignUpdate.java'), '--verify', str(ROOT / 'tools/update-public-cert.txt'),
                        str(payload), str(signature)], check=True)
    m = json.loads(data)
    assert m['schema'] == 1 and m['repository'] == REPO
    assert re.fullmatch(r'v[0-9A-Za-z][0-9A-Za-z.\-]{0,79}', m['tag']) and m['tag'][1:] == m['version']
    assert 0 < m['sequence'] <= 2100000000 and m['android']['versionCode'] == m['sequence']
    for key, ext in [('desktop', '.zip'), ('android', '.apk')]:
        a = m[key]
        assert re.fullmatch(r'[A-Za-z0-9_.\-]{1,150}', a['asset']) and a['asset'].endswith(ext)
        assert re.fullmatch('[0-9a-f]{64}', a['sha256']) and 0 < a['size'] <= 1024**3
    return m


def verify_assets(m, paths):
    by_name = {p.name: p for p in paths}
    for key in ('desktop', 'android'):
        a = m[key]
        p = by_name[a['asset']]
        assert sha(p) == a['sha256'] and p.stat().st_size == a['size'], 'Signed update asset mismatch: ' + p.name


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--desktop', type=Path, required=True)
    p.add_argument('--android', type=Path, required=True)
    p.add_argument('--android-package', type=Path, required=True, help='Distribution ZIP; its APK must match the direct updater asset')
    p.add_argument('--notes', type=Path, required=True)
    p.add_argument('--out', type=Path, required=True)
    p.add_argument('--aapt', type=Path, default=Path('C:/Android/sdk/build-tools/35.0.0/aapt2.exe'))
    args = p.parse_args()
    build = json.loads((ROOT / 'AstralParty-Hub/src-tauri/src/update_build.json').read_text('utf-8'))
    version, sequence = build['version'], build['sequence']
    from package_android_preview import verify_android_archive
    phone_package = verify_android_archive(args.android_package, args.android)
    assert phone_package['releaseVersion'] == version, 'Android ZIP build identity differs'
    with zipfile.ZipFile(args.desktop) as z:
        names = [n for n in z.namelist() if n.endswith('/release.json') and n.count('/') == 1]
        assert len(names) == 1, 'Not a complete desktop release'
        release = json.loads(z.read(names[0]))
        assert release['Version'] == version and not release.get('LocalWorktreeCandidate'), 'Do not publish local adaptation candidates'
        assert release['HubBuildVersion'] == version and release['HubBuildSequence'] == sequence
        assert hashlib.sha256(z.read(names[0].replace('release.json', 'Hub/xingshu-hub.exe'))).hexdigest() == release['HubSha256']
    badging = subprocess.check_output([str(args.aapt), 'dump', 'badging', str(args.android)], encoding='utf-8')
    identity = re.search(r"package: name='([^']+)' versionCode='(\d+)' versionName='([^']+)'", badging)
    assert identity and identity.groups() == ('app.xingshu.installer', str(sequence), version), 'APK build identity differs'
    signer = Path(os.environ['LOCALAPPDATA']) / 'XingShu/Signing'
    public = subprocess.check_output(['java', str(ROOT / 'tools/SignUpdate.java'), str(signer / 'android-installer.jks'), str(signer / 'android-installer.password')], text=True).strip()
    assert public == (ROOT / 'tools/update-public-cert.txt').read_text('ascii').strip(), 'Signing identity changed; do not rotate it silently'
    cert_digest = hashlib.sha256(base64.b64decode(public)).hexdigest()
    certs = subprocess.check_output([str(args.aapt.parent / 'apksigner.bat'), 'verify', '--print-certs', str(args.android)], encoding='utf-8')
    assert 'Signer #1 certificate SHA-256 digest: ' + cert_digest in certs, 'APK signing identity differs'
    notes = args.notes.read_text('utf-8-sig').strip()
    assert len(notes) <= 6000
    m = {'schema': 1, 'repository': REPO, 'tag': 'v' + version, 'version': version, 'sequence': sequence, 'notes': notes}
    for key, asset in [('desktop', args.desktop), ('android', args.android)]:
        m[key] = {'asset': asset.name, 'size': asset.stat().st_size, 'sha256': sha(asset)}
    m['android']['versionCode'] = sequence
    with tempfile.TemporaryDirectory(prefix='xingshu-sign-') as temp:
        payload, signature = Path(temp) / 'payload.json', Path(temp) / 'signature.txt'
        payload.write_text(json.dumps(m, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
        subprocess.run(['java', str(ROOT / 'tools/SignUpdate.java'), str(signer / 'android-installer.jks'), str(signer / 'android-installer.password'), str(payload), str(signature)], check=True)
        envelope = {'payload': base64.b64encode(payload.read_bytes()).decode('ascii'), 'signature': signature.read_text('ascii')}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open('x', encoding='utf-8') as out:
        json.dump(envelope, out, separators=(',', ':'))
    assert verify_envelope(args.out) == m
    verify_assets(m, [args.desktop, args.android])
    Path(str(args.out) + '.sha256').write_text(sha(args.out) + '  ' + args.out.name + '\n', encoding='utf-8')
    print('Verified signed update metadata:', args.out)


if __name__ == '__main__':
    main()
