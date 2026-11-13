"""Exercise signed updates and portable rollback in isolated directories, never installed games."""
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
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
        harness.write_text('''using System;using System.IO;using System.Text;using System.Linq;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;
class FakeMetadata {
 internal Dictionary<string,object> Config; internal int Canceled,Active,MaxActive; internal List<string> Calls=new List<string>(),Downloads=new List<string>();
 internal async Task Probe(string url,long size,CancellationToken cancellation) { await Fetch(url,65536,cancellation); }
 internal void Download(string url,string destination,long size,string hash) {
  string host=new Uri(url).Host;Downloads.Add(host);File.WriteAllText(destination,"partial");
  if(Config.ContainsKey("failDownloadHost")&&HubUpdater.S(Config,"failDownloadHost")==host)throw new HubUpdater.TransportException("fixture download interrupted",0,null);
  if(Config.ContainsKey("integrityFailure")&&Convert.ToBoolean(Config["integrityFailure"]))throw new Exception("更新文件 SHA256 校验失败");
  File.WriteAllText(destination,"complete");
 }
 internal async Task<byte[]> Fetch(string url,long limit,CancellationToken cancellation) {
  Dictionary<string,object> rule=null;
  lock(Calls)Calls.Add(url);
  foreach(Dictionary<string,object> r in (System.Collections.IEnumerable)Config["rules"]) {
   if(url.Contains(HubUpdater.S(r,"match"))&&(!r.ContainsKey("host")||new Uri(url).Host==HubUpdater.S(r,"host"))) { rule=r;break; }
  }
  if(rule==null)rule=HubUpdater.Object("{\\"delay\\":5,\\"status\\":503}");
  bool track=rule.ContainsKey("track")&&Convert.ToBoolean(rule["track"]);
  if(track)lock(Calls) { Active++;MaxActive=Math.Max(Active,MaxActive); }
  try {
   await Task.Delay(Convert.ToInt32(rule["delay"]),rule.ContainsKey("ignoreCancellation")&&Convert.ToBoolean(rule["ignoreCancellation"])?CancellationToken.None:cancellation);
   if(rule.ContainsKey("status"))throw new HubUpdater.TransportException("fixture network failure",Convert.ToInt32(rule["status"]),null);
   byte[] bytes=Encoding.UTF8.GetBytes(HubUpdater.S(rule,"body"));
   if(bytes.Length>limit)throw new Exception("fixture response limit");
   return bytes;
  } catch(OperationCanceledException) { Interlocked.Increment(ref Canceled);throw; }
  finally { if(track)lock(Calls)Active--; }
 }
}
class Harness { static int Main(string[] a) { try {
 Console.OutputEncoding=new UTF8Encoding(false);
 if(a[0]=="check") {
  var fake=new FakeMetadata { Config=HubUpdater.Object(File.ReadAllText(a[1])) };var timer=Stopwatch.StartNew();
  var result=HubUpdater.Check(HubUpdater.N(fake.Config,"current"),Convert.ToBoolean(fake.Config["preview"]),fake.Fetch,Convert.ToInt32(fake.Config["timeout"]));
  Console.WriteLine(HubUpdater.Json.Serialize(new { result=result,elapsedMs=timer.ElapsedMilliseconds,canceled=fake.Canceled,maxActive=fake.MaxActive,calls=fake.Calls.ToArray() }));return 0;
 }
 if(a[0]=="download") {
  var fake=new FakeMetadata { Config=HubUpdater.Object(File.ReadAllText(a[1])) };var timer=Stopwatch.StartNew();
  var urls=((System.Collections.IEnumerable)fake.Config["urls"]).Cast<string>().ToList();
  string destination=Path.Combine(Path.GetDirectoryName(a[1]),"download.zip");
  try {
   string host=HubUpdater.DownloadWithFallback(urls,destination,100000,new String('a',64),fake.Probe,fake.Download);
   Console.WriteLine(HubUpdater.Json.Serialize(new { host=host,elapsedMs=timer.ElapsedMilliseconds,canceled=fake.Canceled,downloads=fake.Downloads,content=File.ReadAllText(destination) }));return 0;
  }catch(Exception e){Console.WriteLine(HubUpdater.Json.Serialize(new { error=e.Message,downloads=fake.Downloads }));return 2;}
 }
 if(a[0]=="probe-header")HubUpdater.ValidateDownloadProbe(Int32.Parse(a[1]),Int64.Parse(a[2]),a[3]=="-"?null:a[3],Int64.Parse(a[4]));
 if(a[0]=="wire") return (int)typeof(HubUpdater).GetMethod("Main",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{new[]{"--apply"}});
 else if(a[0]=="verify") HubUpdater.Verify(File.ReadAllText(a[1]),a[2]);
 else if(a[0]=="manifest") { Console.WriteLine(HubUpdater.ManifestName(a[1].Split(','))??"NONE");return 0; }
 else if(a[0]=="hosts") {
  Console.WriteLine((HubUpdater.IsAllowedUpdateHost("api.github.com")&&HubUpdater.IsAllowedUpdateHost("github.com")&&HubUpdater.IsAllowedUpdateHost("edgeone.gh-proxy.org")&&HubUpdater.IsAllowedUpdateHost("gh-proxy.org")&&HubUpdater.IsAllowedUpdateHost("ghfast.top")&&!HubUpdater.IsAllowedUpdateHost("evil.example"))?"ALLOW_OK":"ALLOW_BAD");
  var c1=HubUpdater.CandidateUrls("https://github.com/Breezecaresses/Xingshu-hub/releases/download/v0.1.3/a.zip",42,false);
  var c2=HubUpdater.CandidateUrls("https://github.com/Breezecaresses/Xingshu-hub/releases/download/v0.1.3/a.zip",42,true);
  Console.WriteLine(String.Join("|",c1));
  Console.WriteLine(String.Join("|",c2));
  Console.WriteLine(String.Join(",",HubUpdater.ParseAtomTags("<a href='/Breezecaresses/Xingshu-hub/releases/tag/v0.1.3'/><a href='/Breezecaresses/Xingshu-hub/releases/tag/v0.1.3-test.1'/>",false,8)));
  return 0;
 }
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

    def test_desktop_only_manifest_and_extraction(self):
        from prepare_update_release import verify_assets
        folder, archive, m = self.fixture()
        del m['android']
        envelope = self.sign(folder, m)
        self.run_case('verify', envelope, m['tag'])
        self.run_case('extract', archive, folder / 'stage', envelope, m['tag'])
        verify_assets(m, [archive])
        # Desktop-only updates must not use the legacy name read by old Android installers.
        self.assertEqual(self.run_case('manifest', 'xingshu-desktop-update.json'), 'xingshu-desktop-update.json\n')
        self.assertEqual(self.run_case('manifest', 'xingshu-update.json'), 'xingshu-update.json\n')
        self.assertEqual(self.run_case('manifest', 'xingshu-update.json,xingshu-desktop-update.json'), 'xingshu-desktop-update.json\n')
        self.assertEqual(self.run_case('manifest', 'XingShu_Desktop.zip'), 'NONE\n')

    def test_present_android_component_still_requires_matching_identity(self):
        folder, _, m = self.fixture()
        m['android']['versionCode'] = 2
        self.run_case('verify', self.sign(folder, m), m['tag'], ok=False)

    def test_missing_desktop_component_is_rejected(self):
        folder, _, m = self.fixture()
        del m['desktop']
        self.run_case('verify', self.sign(folder, m), m['tag'], ok=False)

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

    def test_domestic_mirror_allowlist_and_atom_tags(self):
        out = self.run_case('hosts').splitlines()
        self.assertEqual(out[0], 'ALLOW_OK')
        c1 = out[1].split('|')
        c2 = out[2].split('|')
        self.assertTrue(c1[0].startswith('https://api.github.com/repos/Breezecaresses/Xingshu-hub/releases/assets/42'))
        self.assertIn('https://edgeone.gh-proxy.org/https://github.com/Breezecaresses/Xingshu-hub/releases/download/v0.1.3/a.zip', c1)
        self.assertTrue(c2[0].startswith('https://edgeone.gh-proxy.org/'))
        self.assertEqual(out[3], 'v0.1.3')

    def release_fixtures(self, count=1):
        releases, rules, manifests = [], [], []
        for i in range(count):
            folder, _, m = self.fixture()
            m.update(version=f'0.0.{i + 1}', tag=f'v0.0.{i + 1}', sequence=i + 1)
            m['android']['versionCode'] = i + 1
            body = self.sign(folder, m).read_text()
            manifests.append(body)
            releases.append({'tag_name': m['tag'], 'draft': False, 'prerelease': False,
                             'assets': [{'name': 'xingshu-update.json', 'id': (i + 1) * 10},
                                        {'name': m['desktop']['asset'], 'id': (i + 1) * 10 + 1}]})
            rules.append({'match': '/download/' + m['tag'] + '/', 'host': 'edgeone.gh-proxy.org',
                          'delay': 30, 'body': body, 'track': True})
        return releases, rules, manifests

    def check_fixture(self, rules, *, current=0, preview=True, timeout=2500, ok=True):
        import uuid
        config = self.root / (uuid.uuid4().hex + '.json')
        config.write_text(json.dumps({'rules': rules, 'current': current, 'preview': preview, 'timeout': timeout}), encoding='utf-8')
        started = time.monotonic()
        output = self.run_case('check', config, ok=ok)
        self.assertLess(time.monotonic() - started, timeout / 1000 + 1.5, output)
        return json.loads(output) if ok else output

    def test_slow_official_route_does_not_delay_fast_domestic_route(self):
        releases, manifests, _ = self.release_fixtures()
        result = self.check_fixture([
            {'match': '/releases?', 'host': 'api.github.com', 'delay': 5000, 'body': json.dumps(releases)},
            {'match': '/releases?', 'host': 'edgeone.gh-proxy.org', 'delay': 20, 'body': json.dumps(releases)},
            *manifests,
        ])
        self.assertTrue(result['result']['available'])
        self.assertIsNone(result['result']['checkWarning'])
        self.assertEqual(result['result']['release']['sequence'], 1)
        self.assertIn('edgeone.gh-proxy.org', result['result']['downloadSource'])
        self.assertGreaterEqual(result['canceled'], 1)
        self.assertLess(result['elapsedMs'], 1500)

    def test_slow_first_mirror_does_not_block_other_mirrors(self):
        releases, manifests, _ = self.release_fixtures()
        manifests[0]['host'] = 'gh-proxy.org'
        result = self.check_fixture([
            {'match': '', 'host': 'edgeone.gh-proxy.org', 'delay': 5000, 'status': 503},
            {'match': '/releases?', 'host': 'gh-proxy.org', 'delay': 20, 'body': json.dumps(releases)},
            *manifests,
        ])
        self.assertIn('gh-proxy.org', result['result']['downloadSource'])
        self.assertGreaterEqual(result['canceled'], 1)
        self.assertLess(result['elapsedMs'], 1500)

    def test_manifests_run_in_parallel_and_select_greatest_signed_sequence(self):
        releases, manifests, _ = self.release_fixtures(6)
        for rule in manifests:
            rule['delay'] = 200
        # API order deliberately disagrees with signed sequence order.
        releases = [releases[2], releases[5], releases[1], releases[4], releases[0], releases[3]]
        result = self.check_fixture([
            {'match': '/releases?', 'host': 'api.github.com', 'delay': 20, 'body': json.dumps(releases)},
            *manifests,
        ])
        self.assertEqual(result['result']['release']['sequence'], 6)
        self.assertEqual(result['maxActive'], 4)
        self.assertLess(result['elapsedMs'], 1100)

    def test_overall_deadline_also_bounds_uncooperative_transport(self):
        output = self.check_fixture([{'match': '', 'delay': 5000, 'body': '[]', 'ignoreCancellation': True}],
                                    timeout=350, ok=False)
        self.assertIn('超时', output)

    def test_incomplete_check_never_reports_no_updates(self):
        releases, manifests, _ = self.release_fixtures(2)
        result = self.check_fixture([
            {'match': '/releases?', 'host': 'api.github.com', 'delay': 20, 'body': json.dumps(releases)},
            manifests[0],
        ], current=1)
        self.assertFalse(result['result']['available'])
        self.assertTrue(result['result']['checkWarning'])
        self.assertIn('尚未完成', result['result']['message'])
        self.assertNotIn('没有比当前版本更新', result['result']['message'])

    def test_tampered_fast_manifest_cannot_fall_back_to_older_release(self):
        releases, manifests, _ = self.release_fixtures(2)
        envelope = json.loads(manifests[1]['body'])
        envelope['payload'] = base64.b64encode(b'{}').decode()
        manifests[1]['body'] = json.dumps(envelope)
        output = self.check_fixture([
            {'match': '/releases?', 'host': 'api.github.com', 'delay': 20, 'body': json.dumps(releases)},
            *manifests,
        ], ok=False)
        self.assertIn('签名校验失败', output)

    def test_api_preview_and_draft_filtering_is_preserved(self):
        releases, manifests, _ = self.release_fixtures(3)
        releases[1]['prerelease'] = True
        releases[2]['draft'] = True
        rules = [{'match': '/releases?', 'host': 'api.github.com', 'delay': 20, 'body': json.dumps(releases)}, *manifests]
        stable = self.check_fixture(rules, preview=False)
        self.assertEqual(stable['result']['release']['sequence'], 1)
        self.assertFalse(any('/v0.0.2/' in u or '/v0.0.3/' in u for u in stable['calls']))
        preview = self.check_fixture(rules)
        self.assertEqual(preview['result']['release']['sequence'], 2)
        self.assertFalse(any('/v0.0.3/' in u for u in preview['calls']))

    def test_atom_fallback_retains_legacy_manifest_compatibility(self):
        _, manifests, _ = self.release_fixtures()
        atom = '<feed xmlns="http://www.w3.org/2005/Atom"><entry><link href="https://github.com/Breezecaresses/Xingshu-hub/releases/tag/v0.0.1"/></entry></feed>'
        result = self.check_fixture([
            {'match': '/releases.atom', 'host': 'edgeone.gh-proxy.org', 'delay': 20, 'body': atom},
            {'match': '/xingshu-desktop-update.json', 'delay': 10, 'status': 404},
            *manifests,
        ])
        self.assertEqual(result['result']['release']['sequence'], 1)
        self.assertIsNone(result['result']['checkWarning'])
        self.assertTrue(any('/xingshu-desktop-update.json' in u for u in result['calls']))

    def test_invalid_api_response_does_not_win_over_valid_route(self):
        releases, manifests, _ = self.release_fixtures()
        result = self.check_fixture([
            {'match': '/releases?', 'host': 'api.github.com', 'delay': 1, 'body': '<html>proxy error</html>'},
            {'match': '/releases?', 'host': 'edgeone.gh-proxy.org', 'delay': 40, 'body': json.dumps(releases)},
            *manifests,
        ])
        self.assertEqual(result['result']['release']['sequence'], 1)

    def test_invalid_atom_response_never_reports_no_updates(self):
        output = self.check_fixture([
            {'match': '/releases.atom', 'delay': 5, 'body': '<html>not a release feed</html>'},
        ], ok=False)
        self.assertIn('格式无效', output)

    def download_fixture(self, rules, *, ok=True, **config):
        import uuid
        direct = 'https://github.com/Breezecaresses/Xingshu-hub/releases/download/v9.8.7/XingShu_Desktop_9.8.7.zip'
        folder = self.root / uuid.uuid4().hex
        folder.mkdir()
        path = folder / 'download.json'
        path.write_text(json.dumps({'rules': rules, 'urls': ['https://edgeone.gh-proxy.org/' + direct,
                        'https://gh-proxy.org/' + direct, direct], **config}), encoding='utf-8')
        return json.loads(self.run_case('download', path, ok=ok))

    def test_future_release_download_selects_fast_mirror_and_cancels_slow_routes(self):
        result = self.download_fixture([
            {'match': '', 'host': 'edgeone.gh-proxy.org', 'delay': 5000, 'body': 'ready'},
            {'match': '', 'host': 'gh-proxy.org', 'delay': 25, 'body': 'ready'},
            {'match': '', 'host': 'github.com', 'delay': 5000, 'body': 'ready'},
        ])
        self.assertEqual(result['host'], 'gh-proxy.org')
        self.assertEqual(result['downloads'], ['gh-proxy.org'])
        self.assertEqual(result['content'], 'complete')
        self.assertGreaterEqual(result['canceled'], 1)
        self.assertLess(result['elapsedMs'], 1500)

    def test_interrupted_download_reselects_remaining_sources(self):
        result = self.download_fixture([
            {'match': '', 'host': 'edgeone.gh-proxy.org', 'delay': 10, 'body': 'ready'},
            {'match': '', 'host': 'gh-proxy.org', 'delay': 50, 'body': 'ready'},
        ], failDownloadHost='edgeone.gh-proxy.org')
        self.assertEqual(result['downloads'], ['edgeone.gh-proxy.org', 'gh-proxy.org'])
        self.assertEqual(result['content'], 'complete')

    def test_download_integrity_failure_is_fatal(self):
        result = self.download_fixture([
            {'match': '', 'host': 'edgeone.gh-proxy.org', 'delay': 10, 'body': 'ready'},
            {'match': '', 'host': 'gh-proxy.org', 'delay': 50, 'body': 'ready'},
        ], integrityFailure=True, ok=False)
        self.assertIn('SHA256', result['error'])
        self.assertEqual(result['downloads'], ['edgeone.gh-proxy.org'])

    def test_download_probe_checks_full_size_and_range(self):
        self.run_case('probe-header', 206, 65536, 'bytes 0-65535/100000', 100000)
        self.run_case('probe-header', 200, 100000, '-', 100000)  # Range ignored, use the first chunk only.
        self.run_case('probe-header', 206, 7, 'bytes 0-6/7', 7)
        self.run_case('probe-header', 206, 65536, 'bytes 0-65535/99999', 100000, ok=False)
        self.run_case('probe-header', 206, 65536, 'bytes 1-65536/100000', 100000, ok=False)
        self.run_case('probe-header', 200, 500, '-', 100000, ok=False)


if __name__ == '__main__':
    unittest.main(verbosity=2)
