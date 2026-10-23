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


if __name__ == '__main__': unittest.main(verbosity=2)
