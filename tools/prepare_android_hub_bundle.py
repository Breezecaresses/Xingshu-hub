"""Prepare the complete Android companion folder for the existing Windows Hub."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile
from android_payload import previous_hashes, sha
from project_paths import project_document

ROOT = Path(__file__).resolve().parents[1]


def verify(root):
    info = json.loads((root / 'bridge.json').read_text('utf8'))
    assert info['formatVersion'] == 1
    for name, digest in info['files'].items():
        path = (root / name).resolve()
        assert root.resolve() in path.parents and sha(path) == digest, name
    subprocess.run([str(root / '星枢安卓安装助手.exe'), '--verify-package'], check=True)
    result = subprocess.run([str(root / 'platform-tools/adb.exe'), 'version'], capture_output=True, text=True,
                            encoding='utf-8', errors='replace', check=True)
    assert 'Android Debug Bridge' in result.stdout
    return info


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--manifest', type=Path, required=True)
    p.add_argument('--vanilla', type=Path, required=True)
    p.add_argument('--phone-apk', type=Path, required=True)
    p.add_argument('--adb-dir', type=Path, default=Path('C:/Android/sdk/platform-tools'))
    p.add_argument('--out', type=Path, required=True)
    a = p.parse_args()
    m = json.loads(a.manifest.read_text('utf-8-sig'))
    assert m['package'] == 'com.feimo.astralparty' and m['platform'] == 'ANDROID_CN'
    assert m['appVersion'] == '3.3.0' and m['nativeLibraries'] == []
    assert sha(m['candidate']) == m['candidateSha256'] and sha(a.vanilla) == m['originalSha256']
    with zipfile.ZipFile(a.phone_apk) as apk:
        import hashlib
        assert hashlib.sha256(apk.read('assets/candidate.__data')).hexdigest() == m['candidateSha256']
        assert hashlib.sha256(apk.read('assets/vanilla.__data')).hexdigest() == m['originalSha256']
    root = a.out.resolve()
    if root.exists(): raise SystemExit('Choose a new output directory; existing bundles are preserved')
    root.mkdir(parents=True)
    shutil.copy2(m['candidate'], root / '__data')
    shutil.copy2(a.vanilla, root / 'vanilla.__data')
    shutil.copy2(a.phone_apk, root / '星枢手机安装助手.apk')
    m.update(candidate='__data', vanilla='vanilla.__data', supportedPreviousSha256=previous_hashes(m))
    (root / 'candidate.json').write_text(json.dumps(m, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    tools = root / 'platform-tools'
    tools.mkdir()
    for name in ('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'NOTICE.txt', 'source.properties'):
        shutil.copy2(a.adb_dir / name, tools / name)
    (tools / '来源.txt').write_text('Android SDK Platform-Tools (ADB components)\nhttps://developer.android.com/tools/releases/platform-tools\nhttps://android.googlesource.com/platform/packages/modules/adb/\n原始第三方许可见 NOTICE.txt。\n', encoding='utf8')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/optimize+', '/reference:System.Windows.Forms.dll',
                    '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
                    '/out:' + str(root / '星枢安卓安装助手.exe'), str(ROOT / 'tools/AndroidPreviewInstaller.cs')], check=True)
    shutil.copy2(project_document(ROOT, 'ANDROID_PHONE_INSTALL.md'), root / '手机独立安装说明.md')
    info = {'formatVersion': 1, 'runtimeVersion': m['runtimeVersion'], 'files': {
        f.relative_to(root).as_posix(): sha(f) for f in sorted(root.rglob('*')) if f.is_file()
    }}
    (root / 'bridge.json').write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    verify(root)
    print('READY:', root)
    print('Verified Android candidate, signed phone APK, desktop worker and bundled ADB')


if __name__ == '__main__': main()
