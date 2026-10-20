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
    parser.add_argument('--tag', default='v0.1.0-test.20261021.2')
    args = parser.parse_args()
    archive = args.package.resolve(strict=True)
    checksum = Path(str(archive) + '.sha256').resolve(strict=True)
    expected = checksum.read_text('utf-8').split()[0]
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == expected, 'ZIP checksum mismatch'
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
    for path in (archive, checksum):
        digest = 'sha256:' + hashlib.sha256(path.read_bytes()).hexdigest()
        asset = next((a for a in assets if a['name'] == path.name), None)
        if asset:
            assert asset.get('digest') == digest and asset['size'] == path.stat().st_size, 'Existing draft asset differs'
        else:
            upload = release['upload_url'].split('{', 1)[0] + '?name=' + urllib.parse.quote(path.name)
            asset = request('POST', upload, path.read_bytes(), 'application/zip' if path.suffix == '.zip' else 'text/plain')
            assert asset.get('digest') == digest and asset['size'] == path.stat().st_size, 'Uploaded asset mismatch'
        print('Verified upload: ' + path.name, flush=True)
    published = request('PATCH', api + '/releases/' + str(release['id']), {
        'body': args.body.read_text('utf-8'), 'draft': False, 'prerelease': True, 'make_latest': 'false'})
    confirmed = request('GET', api + '/releases/tags/' + urllib.parse.quote(args.tag, safe=''))
    assert not confirmed['draft'] and confirmed['prerelease']
    for path in (archive, checksum):
        asset = next(a for a in confirmed['assets'] if a['name'] == path.name)
        assert asset['state'] == 'uploaded' and asset['size'] == path.stat().st_size
    print('PUBLISHED: ' + published['html_url'], flush=True)


if __name__ == '__main__':
    main()
