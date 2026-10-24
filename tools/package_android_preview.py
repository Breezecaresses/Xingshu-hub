"""Build one phone ZIP with its APK/manual files, plus the identical APK for existing updaters."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import tempfile
import zipfile
from android_payload import previous_hashes
from project_paths import project_document

ROOT = Path(__file__).resolve().parents[1]
APK_NAME = '星枢手机安装助手.apk'
MANUAL = '手动替换'
PACKAGE_FILES = {'candidate.json', APK_NAME, f'{MANUAL}/__data', f'{MANUAL}/官方原版.__data',
                 f'{MANUAL}/文件校验.json', '手机安装与手动替换.md', '兼容性说明.md',
                 'LICENSE.txt', '先读我.txt', 'SHA256SUMS.txt'}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify_android_archive(archive, phone_apk):
    """Bind the distribution ZIP, manual payloads and updater APK without extracting anything."""
    with zipfile.ZipFile(archive) as package:
        names = package.namelist()
        assert names and len(names) == len(set(names)), 'Duplicate Android ZIP entries'
        roots = {name.split('/')[0] for name in names}
        assert len(roots) == 1, 'Android ZIP must have one root directory'
        root = roots.pop()
        assert re.fullmatch(r'XingShu_Android_[0-9A-Za-z][0-9A-Za-z.\-]{0,79}', root), 'Invalid Android ZIP root'
        assert set(names) == {root + '/' + name for name in PACKAGE_FILES}, 'Unexpected or missing Android ZIP files'
        assert sum(entry.file_size for entry in package.infolist()) <= 1024**3, 'Android ZIP is too large'
        assert package.testzip() is None, 'Corrupt Android ZIP'
        read = lambda name: package.read(root + '/' + name)
        manifest = json.loads(read('candidate.json'))
        assert root == 'XingShu_Android_' + manifest['releaseVersion'], 'Android ZIP version differs'
        assert manifest['platform'] == 'ANDROID_CN' and manifest['package'] == 'com.feimo.astralparty'
        assert manifest['nativeLibraries'] == []
        assert manifest['candidate'] == f'{MANUAL}/__data' and manifest['vanilla'] == f'{MANUAL}/官方原版.__data'
        assert hashlib.sha256(read(APK_NAME)).hexdigest() == sha(phone_apk), 'ZIP APK differs from updater APK'
        for name, key in [(manifest['candidate'], 'candidateSha256'), (manifest['vanilla'], 'originalSha256')]:
            assert hashlib.sha256(read(name)).hexdigest() == manifest[key], 'Manual payload differs: ' + name
        manual = json.loads(read(f'{MANUAL}/文件校验.json'))
        assert manual == {**manifest, 'candidate': '__data', 'vanilla': '官方原版.__data'}, 'Manual metadata differs'
        with zipfile.ZipFile(phone_apk) as apk:
            assert hashlib.sha256(apk.read('assets/candidate.__data')).hexdigest() == manifest['candidateSha256']
            assert hashlib.sha256(apk.read('assets/vanilla.__data')).hexdigest() == manifest['originalSha256']
        checksums = {}
        for line in read('SHA256SUMS.txt').decode('utf8').splitlines():
            digest, name = line.split('  ', 1)
            assert name not in checksums, 'Duplicate checksum entry'
            checksums[name] = digest
        assert set(checksums) == PACKAGE_FILES - {'SHA256SUMS.txt'}, 'Incomplete Android ZIP checksums'
        for name, digest in checksums.items():
            assert hashlib.sha256(read(name)).hexdigest() == digest, 'Android ZIP checksum mismatch: ' + name
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--phone-apk', type=Path, required=True)
    parser.add_argument('--vanilla', type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch('[a-f0-9]{40}', args.source_commit): raise SystemExit('Expected full source commit')
    if not re.fullmatch(r'[0-9A-Za-z][0-9A-Za-z.\-]{0,79}', args.version): raise SystemExit('Invalid version')
    manifest = json.loads(args.manifest.read_text('utf8'))
    candidate = Path(manifest['candidate'])
    assert sha(candidate) == manifest['candidateSha256'] and sha(args.vanilla) == manifest['originalSha256']
    assert manifest['platform'] == 'ANDROID_CN' and manifest['nativeLibraries'] == []
    name = 'XingShu_Android_' + args.version
    publish = (ROOT / '发布包').resolve()
    publish.mkdir(exist_ok=True)
    destination, archive, phone_asset = publish / name, publish / (name + '.zip'), publish / (name + '.apk')
    outputs = [destination, archive, phone_asset, Path(str(archive) + '.sha256'), Path(str(phone_asset) + '.sha256')]
    if any(path.exists() for path in outputs): raise SystemExit('This phone package already exists; choose a new version')
    with tempfile.TemporaryDirectory(prefix='.building-' + name + '-', dir=publish) as temp:
        stage = Path(temp).resolve()
        assert stage.parent == publish
        package = stage / name
        manual = package / MANUAL
        manual.mkdir(parents=True)
        shutil.copy2(args.phone_apk, package / APK_NAME)
        shutil.copy2(candidate, manual / '__data')
        shutil.copy2(args.vanilla, manual / '官方原版.__data')
        manifest.update(candidate=f'{MANUAL}/__data', vanilla=f'{MANUAL}/官方原版.__data',
                        sourceCommit=args.source_commit, releaseVersion=args.version,
                        supportedPreviousSha256=previous_hashes(manifest))
        (package / 'candidate.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
        (manual / '文件校验.json').write_text(json.dumps({**manifest, 'candidate': '__data', 'vanilla': '官方原版.__data'}, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
        shutil.copy2(project_document(ROOT, 'ANDROID_PHONE_INSTALL.md'), package / '手机安装与手动替换.md')
        shutil.copy2(project_document(ROOT, 'ANDROID_COMPATIBILITY.md'), package / '兼容性说明.md')
        shutil.copy2(ROOT / 'LICENSE', package / 'LICENSE.txt')
        (package / '先读我.txt').write_text(f'''星枢安卓手机包 {args.version}
适配游戏：{manifest['package']} {manifest['appVersion']}；插件：{manifest['runtimeVersion']}。

推荐安装：解压后安装“{APK_NAME}”。在官方游戏更新资源、进入过大厅并正常退出后，按“手机安装与手动替换.md”完成本机无线调试配对、连接及安装。
已有助手可直接覆盖安装，保留数据与配对身份。APK 是安装工具，不是修改后的游戏 APK。

手动替换：“{MANUAL}/__data”为插件文件，“{MANUAL}/官方原版.__data”用于还原；具体目标路径和 SHA256 见 candidate.json、文件校验.json 及安装说明。先正常退出游戏并保留备份；未知资源版本不要强行覆盖。普通文件管理器可能无权访问 Android/data，推荐使用助手完成授权安装。

电脑连接手机的安装工具已包含在另一个电脑端 ZIP 内，本包不再附带 Windows EXE。
GitHub 会另外保留与包内完全相同的 APK 和签名更新清单，供“检查助手更新”直接下载，无需手机先下载并解压整个 ZIP。
源码：https://github.com/Breezecaresses/Xingshu-hub
提交：{args.source_commit}
''', encoding='utf-8-sig')
        entries = sorted(path for path in package.rglob('*') if path.is_file())
        (package / 'SHA256SUMS.txt').write_text(''.join(f'{sha(path)}  {path.relative_to(package).as_posix()}\n' for path in entries), encoding='utf8')
        staged_archive = stage / archive.name
        with zipfile.ZipFile(staged_archive, 'x', zipfile.ZIP_DEFLATED, compresslevel=9) as output:
            for path in sorted(package.rglob('*')):
                if path.is_file(): output.write(path, Path(name) / path.relative_to(package))
        verify_android_archive(staged_archive, args.phone_apk)
        package.rename(destination)
        staged_archive.rename(archive)
        shutil.copy2(destination / APK_NAME, phone_asset)
        for asset in (archive, phone_asset):
            Path(str(asset) + '.sha256').write_text(sha(asset) + '  ' + asset.name + '\n', encoding='utf8')
    print(f'PHONE ZIP: {archive}\nSIZE: {archive.stat().st_size / 1024 / 1024:.2f} MiB\nSHA256: {sha(archive)}')
    print(f'UPDATER APK: {phone_asset}\nSHA256: {sha(phone_asset)}')


if __name__ == '__main__':
    main()
