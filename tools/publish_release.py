"""Upload a verified local release, then publish it on GitHub Releases."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--body', type=Path, required=True)
    parser.add_argument('--repo', default='Breezecaresses/Xingshu-hub')
    parser.add_argument('--tag', default='v0.1.0-test.20261021.3')
    parser.add_argument('--android', '--extra-package', dest='extra_package', type=Path, action='append', required=True,
                        help='The single Android phone APK; no separate tool/manual ZIPs are published')
    parser.add_argument('--update-manifest', type=Path, help='Signed xingshu-update.json for both application updaters')
    args = parser.parse_args()
    if args.package.suffix.lower() != '.zip' or len(args.extra_package) != 1 or args.extra_package[0].suffix.lower() != '.apk':
        parser.error('Publish exactly one desktop ZIP and one Android APK')
    archive = args.package.resolve(strict=True)
    checksum = Path(str(archive) + '.sha256').resolve(strict=True)
    expected = checksum.read_text('utf-8').split()[0]
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == expected, 'ZIP checksum mismatch'
    upload_paths=[archive,checksum]
    for extra in args.extra_package:
        extra=extra.resolve(strict=True)
        extra_checksum=Path(str(extra)+'.sha256').resolve(strict=True)
        assert hashlib.sha256(extra.read_bytes()).hexdigest()==extra_checksum.read_text('utf8').split()[0], 'Android APK checksum mismatch'
        upload_paths.extend([extra,extra_checksum])
    if args.update_manifest:
        from prepare_update_release import verify_envelope, verify_assets
        manifest = args.update_manifest.resolve(strict=True)
        assert manifest.name == 'xingshu-update.json'
        metadata = verify_envelope(manifest)
        assert metadata['tag'] == args.tag, 'Signed update targets another release'
        verify_assets(metadata, upload_paths)
        upload_paths.append(manifest)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=args.source, text=True).strip()
    tag_commit = subprocess.check_output(['git', 'rev-parse', args.tag + '^{commit}'], cwd=args.source, text=True).strip()
    assert commit == tag_commit, 'Source and release tag differ'
    remote_tag = subprocess.check_output(['git', 'ls-remote', 'origin', 'refs/tags/' + args.tag], cwd=args.source, text=True).strip()
    assert remote_tag, 'Push the verified release tag first'
    credentials = subprocess.run(['git', 'credential', 'fill'], cwd=args.source,
        input='protocol=https\nhost=github.com\n\n', text=True, capture_output=True, check=True)
    credential = dict(line.split('=', 1) for line in credentials.stdout.splitlines() if '=' in line)
    token = credential['password']
    api = 'https://api.github.com/repos/' + args.repo

    def request(method, url, data=None, content_type='application/json'):
        if isinstance(data, dict):
            data = json.dumps(data, ensure_ascii=False).encode('utf-8')
        req = urllib.request.Request(url, data=data, method=method, headers={
            'Authorization': 'Bearer ' + token, 'User-Agent': 'XingShu-release',
            'Accept': 'application/vnd.github+json', 'Content-Type': content_type,
            'X-GitHub-Api-Version': '2022-11-28'})
        with urllib.request.urlopen(req, timeout=180) as response:
            return json.load(response)

    releases = request('GET', api + '/releases?per_page=100')
    existing = next((r for r in releases if r['tag_name'] == args.tag), None)
    if existing and not existing['draft']:
        raise SystemExit('This version is already published; no assets were replaced')
    release = existing or request('POST', api + '/releases', {
        'tag_name': args.tag, 'target_commitish': commit, 'name': '星枢群发测试版 ' + args.tag,
        'body': args.body.read_text('utf-8'), 'draft': True, 'prerelease': True})
    assets = request('GET', api + '/releases/' + str(release['id']) + '/assets')
    packages = {archive.name, args.extra_package[0].name}
    unexpected = [a['name'] for a in assets if Path(a['name']).suffix.lower() in {'.zip', '.apk'} and a['name'] not in packages]
    if unexpected:
        raise SystemExit('Draft contains extra packages; keep only the desktop ZIP and Android APK before publishing: ' + ', '.join(unexpected))
    for path in upload_paths:
        digest = 'sha256:' + hashlib.sha256(path.read_bytes()).hexdigest()
        asset = next((a for a in assets if a['name'] == path.name), None)
        if asset:
            assert asset.get('digest') == digest and asset['size'] == path.stat().st_size, 'Existing draft asset differs'
        else:
            upload = release['upload_url'].split('{', 1)[0] + '?name=' + urllib.parse.quote(path.name)
            content_types={'.zip':'application/zip','.apk':'application/vnd.android.package-archive','.json':'application/json'}
            asset = request('POST', upload, path.read_bytes(), content_types.get(path.suffix,'text/plain'))
            assert asset.get('digest') == digest and asset['size'] == path.stat().st_size, 'Uploaded asset mismatch'
        print('Verified upload: ' + path.name, flush=True)
    published = request('PATCH', api + '/releases/' + str(release['id']), {
        'body': args.body.read_text('utf-8'), 'draft': False, 'prerelease': True, 'make_latest': 'false'})
    confirmed = request('GET', api + '/releases/tags/' + urllib.parse.quote(args.tag, safe=''))
    assert not confirmed['draft'] and confirmed['prerelease']
    for path in upload_paths:
        asset = next(a for a in confirmed['assets'] if a['name'] == path.name)
        assert asset['state'] == 'uploaded' and asset['size'] == path.stat().st_size
    print('PUBLISHED: ' + published['html_url'], flush=True)


if __name__ == '__main__':
    main()
