"""Exercise the real Windows Android worker against an isolated fake ADB device.

No connected phone, emulator or game installation is accessed by these tests.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
CSC = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
FAKE = r'''
using System; using System.IO; using System.Linq; using System.Collections.Generic;
using System.Security.Cryptography; using System.Text.RegularExpressions; using System.Web.Script.Serialization;
class FakeAdb {
 static JavaScriptSerializer json=new JavaScriptSerializer(); static string stateFile; static Dictionary<string,object> data;
 static string S(string k) { return Convert.ToString(data[k]); }
 static string Resolve(string path) { return path.StartsWith("/sdcard/")?S("target"):Path.Combine(S("root"),Path.GetFileName(path)); }
 static string Quoted(string command,int index) { return Regex.Matches(command,"'([^']*)'")[index].Groups[1].Value; }
 static string Hash(string path) { using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
 static int Main(string[] args) {
  try {
   stateFile=Environment.GetEnvironmentVariable("XINGSHU_ADB_FIXTURE"); data=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(stateFile));
   File.AppendAllText(Path.Combine(S("root"),"calls.txt"),String.Join(" ",args)+"\n");
   if(args.Length<3 || args[0]!="-s" || args[1]!="TEST_DEVICE")throw new Exception("invalid device selection");
   var a=args.Skip(2).ToArray();
   if(a[0]=="get-state") {Console.WriteLine("device");return 0;}
   if(a[0]=="pull") {File.Copy(Resolve(a[1]),a[2],true);return 0;}
   if(a[0]=="push") {File.Copy(a[1],Resolve(a[2]),true);return 0;}
   string c=a[1];
   if(c.StartsWith("dumpsys package "))Console.WriteLine(" versionName="+S("version"));
   else if(c=="ps -A")Console.WriteLine(S("running"));
   else if(c.StartsWith("sha256sum ")) {string p=Quoted(c,0);Console.WriteLine(Hash(Resolve(p))+"  "+p);}
   else if(c.StartsWith("cp ")) {
    string source=Resolve(Quoted(c,0)), target=Resolve(Quoted(c,1));
    if(S("failWrite")=="yes") {data["failWrite"]="no";File.WriteAllText(stateFile,json.Serialize(data));File.WriteAllText(target,"interrupted");throw new Exception("simulated write failure");}
    File.Copy(source,target,true);
   } else if(!c.StartsWith("rm -f "))throw new Exception("Unexpected ADB command: "+c);
   return 0;
  }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
 }
}
'''


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()


class WorkerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = tempfile.TemporaryDirectory(prefix='xingshu-android-worker-')
        cls.root = Path(cls.workspace.name)
        cls.worker = cls.root / 'worker.exe'
        cls.adb = cls.root / 'fake-adb.exe'
        (cls.root / 'FakeAdb.cs').write_text(FAKE, encoding='utf8')
        common = [str(CSC), '/nologo', '/reference:System.Web.Extensions.dll']
        subprocess.run(common + ['/target:exe', '/out:' + str(cls.adb), str(cls.root / 'FakeAdb.cs')], check=True)
        subprocess.run(common + ['/target:winexe', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
                               '/out:' + str(cls.worker), str(ROOT / 'tools/AndroidPreviewInstaller.cs')], check=True)

    @classmethod
    def tearDownClass(cls): cls.workspace.cleanup()

    def setUp(self):
        self.fixture = tempfile.TemporaryDirectory(prefix='device-', dir=self.root)
        self.home = Path(self.fixture.name)
        for name, content in [('__data', b'new plugin'), ('vanilla.__data', b'official'), ('target', b'official'), ('old', b'old verified plugin')]:
            (self.home / name).write_bytes(content)
        self.manifest = {'platform': 'ANDROID_CN', 'package': 'com.feimo.astralparty', 'appVersion': '3.3.0',
                         'bundleRelativePath': 'com.unity.addressables/AssetBundles/' + 'a' * 32 + '/' + 'b' * 32 + '/__data',
                         'candidate': '__data', 'vanilla': 'vanilla.__data', 'candidateSha256': sha(self.home / '__data'),
                         'originalSha256': sha(self.home / 'vanilla.__data'), 'supportedPreviousSha256': [sha(self.home / 'old')]}
        (self.home / 'candidate.json').write_text(json.dumps(self.manifest), encoding='utf8')
        self.state = {'target': str(self.home / 'target'), 'root': str(self.home), 'version': '3.3.0', 'running': '', 'failWrite': 'no'}
        self.save()

    def tearDown(self): self.fixture.cleanup()

    def save(self): (self.home / 'state.json').write_text(json.dumps(self.state), encoding='utf8')

    def run_worker(self, action, success=True):
        env = os.environ.copy(); env['XINGSHU_ADB_FIXTURE'] = str(self.home / 'state.json')
        p = subprocess.run([str(self.worker), action, '--manifest', str(self.home / 'candidate.json'), '--adb', str(self.adb),
                            '--serial', 'TEST_DEVICE', '--backup-dir', str(self.home / 'backups')], capture_output=True, env=env, timeout=25)
        self.assertEqual(p.returncode == 0, success, p.stdout.decode('utf8', errors='replace') + p.stderr.decode('utf8', errors='replace'))
        return p

    def test_fresh_install_repeat_and_restore(self):
        self.run_worker('--check'); installed = self.run_worker('--install')
        self.assertIn('SHA256 校验通过', installed.stdout.decode('utf8'))
        self.assertEqual(sha(self.home / 'target'), self.manifest['candidateSha256'])
        calls = (self.home / 'calls.txt').read_text().count('push ')
        self.run_worker('--install')
        self.assertEqual((self.home / 'calls.txt').read_text().count('push '), calls)
        self.run_worker('--restore')
        self.assertEqual(sha(self.home / 'target'), self.manifest['originalSha256'])
        self.assertEqual(sha(self.home / 'backups/assembly.vanilla.__data'), self.manifest['originalSha256'])

    def test_upgrade_phone_installed_preview_without_pc_receipt(self):
        (self.home / 'target').write_bytes((self.home / 'old').read_bytes())
        self.run_worker('--check'); self.run_worker('--install')
        self.assertEqual(sha(self.home / 'target'), self.manifest['candidateSha256'])
        self.assertTrue((self.home / ('backups/before-' + sha(self.home / 'old') + '.__data')).is_file())
        self.run_worker('--restore')
        self.assertEqual(sha(self.home / 'target'), self.manifest['originalSha256'])

    def test_restore_known_phone_preview_with_bundled_official_recovery(self):
        (self.home / 'target').write_bytes((self.home / 'old').read_bytes())
        self.run_worker('--restore')
        self.assertEqual(sha(self.home / 'target'), self.manifest['originalSha256'])

    def test_unknown_resources_rejected_without_backup_or_write(self):
        (self.home / 'target').write_bytes(b'unknown game update')
        before = sha(self.home / 'target')
        self.run_worker('--check', False); self.run_worker('--install', False)
        self.assertEqual(sha(self.home / 'target'), before)
        self.assertFalse((self.home / 'backups').exists())

    def test_corrupt_payload_rejected_before_device_access(self):
        (self.home / '__data').write_bytes(b'corrupt')
        self.run_worker('--install', False)
        self.assertFalse((self.home / 'calls.txt').exists())

    def test_running_game_subprocess_blocks_write(self):
        self.state['running'] = 'u0_a100 123 1 0 com.feimo.astralparty:service'; self.save()
        self.run_worker('--install', False)
        self.assertEqual(sha(self.home / 'target'), self.manifest['originalSha256'])
        self.assertNotIn('push ', (self.home / 'calls.txt').read_text())

    def test_failed_upgrade_restores_exact_previous_plugin(self):
        (self.home / 'target').write_bytes((self.home / 'old').read_bytes())
        self.state['failWrite'] = 'yes'; self.save()
        self.run_worker('--install', False)
        self.assertEqual(sha(self.home / 'target'), sha(self.home / 'old'))
        self.assertFalse((self.home / 'backups/deployment.json').exists())


MULTI_FILE_FAKE = r'''
using System; using System.IO; using System.Linq; using System.Collections.Generic;
using System.Security.Cryptography; using System.Text.RegularExpressions; using System.Web.Script.Serialization;
class MultiFakeAdb {
 static JavaScriptSerializer json=new JavaScriptSerializer(); static string stateFile; static Dictionary<string,object> data;
 static string S(string k) { return Convert.ToString(data[k]); }
 static string Resolve(string path) {
  const string prefix = "/sdcard/Android/data/com.feimo.astralparty/files/";
  if (path.StartsWith(prefix)) return Path.Combine(S("filesRoot"), path.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar));
  return Path.Combine(S("root"), Path.GetFileName(path));
 }
 static string Quoted(string command,int index) { return Regex.Matches(command,"'([^']*)'")[index].Groups[1].Value; }
 static string Hash(string path) { using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
 static int Main(string[] args) {
  try {
   stateFile=Environment.GetEnvironmentVariable("XINGSHU_ADB_FIXTURE"); data=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(stateFile));
   if(args.Length<3 || args[0]!="-s" || args[1]!="TEST_DEVICE") throw new Exception("invalid device");
   var a=args.Skip(2).ToArray();
   if(a[0]=="get-state") { Console.WriteLine("device"); return 0; }
   if(a[0]=="pull") { string src=Resolve(a[1]); if(!File.Exists(src)) throw new Exception("No such file"); File.Copy(src,a[2],true); return 0; }
   if(a[0]=="push") { File.Copy(a[1],Resolve(a[2]),true); return 0; }
   string c=a[1];
   if(c.StartsWith("dumpsys package ")) Console.WriteLine(" versionName="+S("version"));
   else if(c=="ps -A") Console.WriteLine(S("running"));
   else if(c.StartsWith("sha256sum ")) {
    string p=Quoted(c,0); string f=Resolve(p);
    if(!File.Exists(f)) { Console.Error.WriteLine("sha256sum: "+p+": No such file or directory"); return 1; }
    Console.WriteLine(Hash(f)+"  "+p);
   }
   else if(c.StartsWith("cp ")) File.Copy(Resolve(Quoted(c,0)),Resolve(Quoted(c,1)),true);
   else if(c.StartsWith("rm -f ")) { foreach(Match m in Regex.Matches(c,"'([^']*)'")) { string f=Resolve(m.Groups[1].Value); if(File.Exists(f)) File.Delete(f); } }
   else throw new Exception("Unexpected ADB command: "+c);
   return 0;
  } catch(Exception e) { Console.Error.WriteLine(e.Message); return 1; }
 }
}
'''


class ConflictAndLogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = tempfile.TemporaryDirectory(prefix='xingshu-android-conflict-')
        cls.root = Path(cls.workspace.name)
        cls.worker = cls.root / 'worker.exe'
        cls.adb = cls.root / 'multi-adb.exe'
        (cls.root / 'MultiFakeAdb.cs').write_text(MULTI_FILE_FAKE, encoding='utf8')
        common = [str(CSC), '/nologo', '/reference:System.Web.Extensions.dll']
        subprocess.run(common + ['/target:exe', '/out:' + str(cls.adb), str(cls.root / 'MultiFakeAdb.cs')], check=True)
        subprocess.run(common + ['/target:winexe', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
                               '/out:' + str(cls.worker), str(ROOT / 'tools/AndroidPreviewInstaller.cs')], check=True)

    @classmethod
    def tearDownClass(cls): cls.workspace.cleanup()

    def setUp(self):
        self.fixture = tempfile.TemporaryDirectory(prefix='android-device-', dir=self.root)
        self.home = Path(self.fixture.name)
        self.files = self.home / 'phone_files'
        self.rel_bundle = 'com.unity.addressables/AssetBundles/' + 'a' * 32 + '/' + 'b' * 32 + '/__data'
        vanilla_bytes = b'UnityFS\x00' + b'V' * 256
        candidate_bytes = b'UnityFS\x00' + b'X' * 256
        catalog_bytes = b'{"m_BuildResultHash":"official-3.3.0"}'
        (self.home / '__data').write_bytes(candidate_bytes)
        (self.home / 'vanilla.__data').write_bytes(vanilla_bytes)
        cat_path = self.files / 'com.unity.addressables/catalog_3.3.0.json'
        cat_path.parent.mkdir(parents=True)
        cat_path.write_bytes(catalog_bytes)
        self.target = self.files / self.rel_bundle
        self.target.parent.mkdir(parents=True)
        self.target.write_bytes(vanilla_bytes)
        self.manifest = {
            'platform': 'ANDROID_CN', 'package': 'com.feimo.astralparty', 'appVersion': '3.3.0',
            'bundleRelativePath': self.rel_bundle, 'candidate': '__data', 'vanilla': 'vanilla.__data',
            'candidateSha256': sha(self.home / '__data'), 'originalSha256': sha(self.home / 'vanilla.__data'),
            'catalogSha256': sha(cat_path), 'supportedPreviousSha256': []
        }
        (self.home / 'candidate.json').write_text(json.dumps(self.manifest), encoding='utf8')
        (self.home / 'state.json').write_text(json.dumps({'root': str(self.home), 'filesRoot': str(self.files), 'version': '3.3.0', 'running': ''}), encoding='utf8')

    def tearDown(self): self.fixture.cleanup()

    def run_worker(self, *extra, success=True):
        env = os.environ.copy(); env['XINGSHU_ADB_FIXTURE'] = str(self.home / 'state.json')
        p = subprocess.run([str(self.worker), *extra, '--manifest', str(self.home / 'candidate.json'),
                            '--adb', str(self.adb), '--serial', 'TEST_DEVICE', '--backup-dir', str(self.home / 'backups')],
                           capture_output=True, env=env, timeout=60)
        out = p.stdout.decode('utf8', errors='replace') + p.stderr.decode('utf8', errors='replace')
        self.assertEqual(p.returncode == 0, success, out)
        return out

    def test_apx_conflict_detected_prompted_backed_up_deleted_and_replaced(self):
        apx_bundle = b'UnityFS\x00' + b'APXLive2DBridge' + b'A' * 256
        self.target.write_bytes(apx_bundle)
        apx_rt = self.files / 'runtime_dependencies.json'; apx_rt.write_text('{"apx":true}', encoding='utf8')
        apx_dll = self.files / 'Plugins/x86_64/APXLive2DBridge.dll'; apx_dll.parent.mkdir(parents=True); apx_dll.write_bytes(b'MZ-apx')
        unrelated_mod = self.files / 'mods/custom-skin/readme.txt'; unrelated_mod.parent.mkdir(parents=True); unrelated_mod.write_text('keep me', encoding='utf8')

        check_out = self.run_worker('--check', success=True)
        self.assertIn('CONFLICT_DETECTED=APX', check_out)

        fail_log = self.home / 'android-fail.log'
        denied = self.run_worker('--install', '--export-log', str(fail_log), success=False)
        self.assertIn('CONFLICT_CONFIRM_REQUIRED', denied)
        self.assertTrue(fail_log.is_file())
        log_text = fail_log.read_text('utf8')
        self.assertIn('APX 插件', log_text)
        self.assertIn('runtime_dependencies.json', log_text)

        replaced = self.run_worker('--install', '--replace-conflicts', success=True)
        self.assertIn('已备份并删除原冲突插件', replaced)
        self.assertEqual(sha(self.target), self.manifest['candidateSha256'])
        self.assertFalse(apx_rt.exists())
        self.assertFalse(apx_dll.exists())
        self.assertEqual(unrelated_mod.read_text('utf8'), 'keep me')
        self.assertEqual(sha(self.home / 'backups/assembly.vanilla.__data'), self.manifest['originalSha256'])

    def test_corrupt_non_unityfs_rejected_even_with_replace_conflicts_and_exports_log(self):
        self.target.write_bytes(b'NOT_UNITY_FS_' + b'Z' * 256)
        fail_log = self.home / 'corrupt-fail.log'
        err = self.run_worker('--install', '--replace-conflicts', '--export-log', str(fail_log), success=False)
        self.assertIn('缓存不是原版或已验证的星枢版本', err)
        self.assertTrue(fail_log.is_file())
        self.assertIn('非UnityFS', fail_log.read_text('utf8'))


if __name__ == '__main__': unittest.main(verbosity=2)
