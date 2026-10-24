"""Exercise signed updates and portable rollback in isolated directories, never installed games."""
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]


class AppUpdates(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='xingshu-update-tests-')
        cls.root = Path(cls.temp.name)
        cls.key, cls.secret = cls.root / 'test.jks', cls.root / 'password'
        cls.secret.write_text('fixture-only-password', encoding='ascii')
        subprocess.run(['keytool', '-genkeypair', '-keystore', str(cls.key), '-alias', 'xingshu', '-keyalg', 'RSA', '-keysize', '2048',
                        '-validity', '2', '-dname', 'CN=Isolated Update Test', '-storepass:file', str(cls.secret), '-keypass:file', str(cls.secret)], check=True, capture_output=True)
        cls.cert = cls.root / 'certificate.txt'
        cls.cert.write_bytes(subprocess.check_output(['java', str(ROOT / 'tools/SignUpdate.java'), str(cls.key), str(cls.secret)]))
        harness = cls.root / 'Harness.cs'
        harness.write_text('''using System;using System.IO;using System.Collections.Generic;
class Harness { static int Main(string[] a) { try {
 if(a[0]=="wire") return (int)typeof(HubUpdater).GetMethod("Main",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{new[]{"--apply"}});
 else if(a[0]=="verify") HubUpdater.Verify(File.ReadAllText(a[1]),a[2]);
 else if(a[0]=="extract") { var m=HubUpdater.Verify(File.ReadAllText(a[3]),a[4]); HubUpdater.Extract(a[1],a[2],m); }
 else if(a[0]=="apply") { var files=HubUpdater.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(a[3])); HubUpdater.Apply(a[1],a[2],files,a[4],Int32.Parse(a[5])); }
 Console.WriteLine("PASS");return 0;
 }catch(Exception e){Console.WriteLine(e.Message);return 2;} } }
''', encoding='utf-8')
        cls.exe = cls.root / 'tests.exe'
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        subprocess.run([str(compiler), '/nologo', '/codepage:65001', '/main:Harness', '/reference:System.Web.Extensions.dll',
                        '/reference:System.Windows.Forms.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
                        '/resource:' + str(cls.cert) + ',update-cert', '/out:' + str(cls.exe), str(ROOT / 'tools/HubUpdater.cs'), str(harness)], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def fixture(self, extra=None, local=False):
        import uuid
        folder = self.root / uuid.uuid4().hex
        folder.mkdir()
        exe = b'MZ non-executable isolated fixture'
        version = '0.0.0-fixture.1'
        release = {'Version': version, 'HubSha256': hashlib.sha256(exe).hexdigest()}
        if local:
            release['LocalWorktreeCandidate'] = True
        archive = folder / 'desktop.zip'
        with zipfile.ZipFile(archive, 'w') as z:
            z.writestr('XingShu_fixture/Hub/xingshu-hub.exe', exe)
            z.writestr('XingShu_fixture/release.json', json.dumps(release))
            for path, content in (extra or {}).items():
                z.writestr(path, content)
        component = {'asset': 'desktop.zip', 'size': archive.stat().st_size, 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest()}
        m = {'schema': 1, 'repository': 'Breezecaresses/Xingshu-hub', 'tag': 'v' + version, 'version': version,
             'sequence': 1, 'notes': 'Isolated fixture', 'desktop': component,
             'android': {'asset': 'phone.apk', 'size': 1, 'sha256': 'a' * 64, 'versionCode': 1}}
        return folder, archive, m

    def sign(self, folder, m):
        payload, signature = folder / 'payload.json', folder / 'signature.txt'
        payload.write_text(json.dumps(m, ensure_ascii=False), encoding='utf-8')
        subprocess.run(['java', str(ROOT / 'tools/SignUpdate.java'), str(self.key), str(self.secret), str(payload), str(signature)], check=True, capture_output=True)
        envelope = folder / 'envelope.json'
        envelope.write_text(json.dumps({'payload': base64.b64encode(payload.read_bytes()).decode(), 'signature': signature.read_text()}), encoding='utf-8')
        return envelope

    def run_case(self, *args, ok=True):
        p = subprocess.run([str(self.exe), *map(str, args)], capture_output=True, text=True, encoding='utf-8', errors='replace')
        self.assertEqual(p.returncode == 0, ok, p.stdout + p.stderr)
        return p.stdout

    def test_signed_manifest_and_tamper(self):
        folder, archive, m = self.fixture()
        envelope = self.sign(folder, m)
        self.run_case('verify', envelope, m['tag'])
        e = json.loads(envelope.read_text())
        e['payload'] = base64.b64encode(b'{}').decode()
        envelope.write_text(json.dumps(e))
        self.run_case('verify', envelope, m['tag'], ok=False)

    def test_distribution_zip_does_not_change_signed_update_targets(self):
        from prepare_update_release import verify_assets
        folder, archive, m = self.fixture()
        phone_apk, phone_zip = folder / 'phone.apk', folder / 'android.zip'
        phone_apk.write_bytes(b'isolated APK download')
        with zipfile.ZipFile(phone_zip, 'w') as package:
            package.writestr('phone.apk', phone_apk.read_bytes())
            package.writestr('manual/__data', b'isolated manual payload')
        m['android'].update(size=phone_apk.stat().st_size, sha256=hashlib.sha256(phone_apk.read_bytes()).hexdigest())
        envelope = self.sign(folder, m)
        self.run_case('verify', envelope, m['tag'])
        verify_assets(m, [archive, phone_zip, phone_apk])
        self.assertEqual(m['desktop']['asset'], archive.name)
        self.assertEqual(m['android']['asset'], phone_apk.name)

    def test_repository_tag_size_and_downgrade_identity(self):
        for key, value in [('repository', 'other/repo'), ('tag', 'vother'), ('sequence', -1), ('version', 'other')]:
            folder, archive, m = self.fixture()
            m[key] = value
            envelope = self.sign(folder, m)
            self.run_case('verify', envelope, 'v0.0.0-fixture.1', ok=False)

    def test_valid_zip_and_corrupt_download(self):
        folder, archive, m = self.fixture()
        envelope = self.sign(folder, m)
        self.run_case('extract', archive, folder / 'stage', envelope, m['tag'])
        archive.write_bytes(archive.read_bytes() + b'bad')
        self.run_case('extract', archive, folder / 'bad', envelope, m['tag'], ok=False)

    def test_traversal_duplicates_unapproved_paths_and_local_candidate(self):
        for path in ['XingShu_fixture/Hub/../../escape.exe', 'XingShu_fixture/Hub/XINGSHU-HUB.EXE',
                     'XingShu_fixture/Android/CON.txt', 'XingShu_fixture/evil.exe', 'other/Android/x',
                     'XingShu_fixture/Android/bad:stream', 'XingShu_fixture/Android/trailing.']:
            folder, archive, m = self.fixture({path: b'bad'})
            envelope = self.sign(folder, m)
            self.run_case('extract', archive, folder / 'stage', envelope, m['tag'], ok=False)
            self.assertFalse((folder / 'escape.exe').exists())
        folder, archive, m = self.fixture(local=True)
        self.run_case('extract', archive, folder / 'stage', self.sign(folder, m), m['tag'], ok=False)

    def apply_fixture(self):
        folder, _, _ = self.fixture()
        current, stage, backup = folder / 'current', folder / 'stage', folder / 'backup'
        (current / 'Hub').mkdir(parents=True)
        stage.mkdir()
        (current / 'Hub/xingshu-hub.exe').write_bytes(b'old hub')
        (current / 'user-file.txt').write_bytes(b'keep')
        files = {}
        for path in ['Hub/xingshu-hub.exe', 'Android/new.apk', 'release.json']:
            target = stage / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b'new ' + path.encode())
            files[path] = hashlib.sha256(target.read_bytes()).hexdigest()
        manifest = folder / 'files.json'
        manifest.write_text(json.dumps(files))
        return current, stage, manifest, backup

    def test_apply_preserves_unmanaged_files_and_backup(self):
        current, stage, manifest, backup = self.apply_fixture()
        self.run_case('apply', current, stage, manifest, backup, 0)
        self.assertEqual((current / 'user-file.txt').read_bytes(), b'keep')
        self.assertEqual((backup / 'Hub/xingshu-hub.exe').read_bytes(), b'old hub')
        self.assertEqual((current / 'Hub/xingshu-hub.exe').read_bytes(), (stage / 'Hub/xingshu-hub.exe').read_bytes())

    def test_partial_failure_rolls_back(self):
        current, stage, manifest, backup = self.apply_fixture()
        self.run_case('apply', current, stage, manifest, backup, 2, ok=False)
        self.assertEqual((current / 'Hub/xingshu-hub.exe').read_bytes(), b'old hub')
        self.assertFalse((current / 'Android/new.apk').exists())
        self.assertFalse((current / 'release.json').exists())

    def test_corrupted_stage_does_not_write(self):
        current, stage, manifest, backup = self.apply_fixture()
        (stage / 'release.json').write_bytes(b'tampered')
        self.run_case('apply', current, stage, manifest, backup, 0, ok=False)
        self.assertEqual((current / 'Hub/xingshu-hub.exe').read_bytes(), b'old hub')
        self.assertFalse((current / 'Android/new.apk').exists())

    def test_gui_parent_uses_utf8_in_both_directions(self):
        # CREATE_NO_WINDOW reproduces a real GUI parent's GBK default. Exercise the actual
        # entry point with a missing plan: no network, no UI, no package file is modified.
        cache = self.root / '中文目录'
        request = json.dumps({'cache': str(cache), 'root': str(self.root)}, ensure_ascii=False) + '\n'
        result = subprocess.run([str(self.exe), 'wire'], input=request.encode('utf-8'), capture_output=True,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode, 1)
        error = json.loads(result.stdout.decode('utf-8'))['error']
        self.assertIn('中文目录', error)


if __name__ == '__main__':
    unittest.main(verbosity=2)
