"""Test desktop GroupTestInstaller conflict detection, removal, rollback & failure log export."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
CSC = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class DesktopConflictAndLogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(prefix='xingshu-desktop-test-')
        cls.pkg = Path(cls.tmp.name) / 'pkg'
        cls.pkg.mkdir()
        (cls.pkg / 'Hub').mkdir()
        hub_bytes = b'fake-hub'
        (cls.pkg / 'Hub/xingshu-hub.exe').write_bytes(hub_bytes)
        (cls.pkg / 'Runtime/STEAM_CN').mkdir(parents=True)
        cls.vanilla = b'UnityFS\x00' + b'O' * 256
        cls.payload = b'UnityFS\x00' + b'X' * 256
        cls.ga_bytes = b'MZ-game-assembly-3.3.0'
        (cls.pkg / 'Runtime/STEAM_CN/__data').write_bytes(cls.payload)
        cls.rel_bundle = 'AstralParty_CN_Data/com.unity.addressables/AssetBundles/' + '1' * 32 + '/' + '2' * 32 + '/__data'
        release = {
            'FormatVersion': 1,
            'Version': '0.1.2-test',
            'HubSha256': hashlib.sha256(hub_bytes).hexdigest(),
            'Platforms': [{
                'Id': 'STEAM_CN',
                'Label': 'Steam 国服',
                'ExeName': 'AstralParty_CN.exe',
                'GameAssemblySha256': hashlib.sha256(cls.ga_bytes).hexdigest(),
                'GameVersion': '3.3.0',
                'ResourceVersion': '152',
                'BundleRelativePath': cls.rel_bundle,
                'Payload': 'Runtime/STEAM_CN/__data',
                'PayloadSha256': hashlib.sha256(cls.payload).hexdigest(),
                'AcceptedHashes': [hashlib.sha256(cls.vanilla).hexdigest()],
            }]
        }
        manifest = cls.pkg / 'release.json'
        manifest.write_text(json.dumps(release, ensure_ascii=False, indent=2), encoding='utf8')
        identity = cls.pkg / 'PackageIdentity.cs'
        identity.write_text('internal static class PackageIdentity { internal const string ManifestSha256 = "' + sha(manifest) + '"; }', encoding='utf8')
        cls.exe = cls.pkg / '星枢安装助手.exe'
        subprocess.run([
            str(CSC), '/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/nowarn:0649',
            '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
            '/out:' + str(cls.exe), str(ROOT / 'tools/GroupTestInstaller.cs'), str(identity)
        ], check=True)
        identity.unlink()

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def make_game(self, name):
        game = Path(self.tmp.name) / name
        if game.exists():
            shutil.rmtree(game)
        game.mkdir()
        (game / 'AstralParty_CN.exe').write_bytes(b'game-exe')
        (game / 'GameAssembly.dll').write_bytes(self.ga_bytes)
        target = game / self.rel_bundle
        target.parent.mkdir(parents=True)
        target.write_bytes(self.vanilla)
        return game, target

    def run_cli(self, *args, ok=True):
        p = subprocess.run([str(self.exe), *args], capture_output=True, timeout=25)
        out = p.stdout.decode('utf8', errors='replace') + p.stderr.decode('utf8', errors='replace')
        self.assertEqual(p.returncode == 0, ok, out)
        return out

    def test_apx_conflict_requires_confirm_then_cleans_backs_up_and_replaces(self):
        game, target = self.make_game('game-apx')
        apx_bundle = b'UnityFS\x00' + b'APXLive2D' + b'A' * 256
        target.write_bytes(apx_bundle)
        apx_dll = game / 'AstralParty_CN_Data/Plugins/x86_64/APXLive2DBridge.dll'
        apx_dll.parent.mkdir(parents=True); apx_dll.write_bytes(b'MZ-apx')
        apx_ui = game / 'dependence/apx_modern_assist_ui.bundle'
        apx_ui.parent.mkdir(parents=True); apx_ui.write_bytes(b'apx-ui')
        apx_extra = game / 'AstralParty_CN_Data/com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__data'
        apx_extra.parent.mkdir(parents=True); apx_extra.write_bytes(b'apx-extra')
        speed_dll = game / 'version.dll'
        speed_dll.write_bytes(b'speed-mod-keep')

        check_out = self.run_cli('--check', 'STEAM_CN', str(game), ok=True)
        self.assertIn('APX 插件', check_out)

        fail_log = game / 'desktop-fail.log'
        denied = self.run_cli('--install', '--export-log', str(fail_log), 'STEAM_CN', str(game), ok=False)
        self.assertIn('CONFLICT_CONFIRM_REQUIRED', denied)
        self.assertTrue(fail_log.is_file())
        log_text = fail_log.read_text('utf8')
        self.assertIn('APXLive2DBridge.dll', log_text)
        self.assertIn('建议处理方式', log_text)

        replaced = self.run_cli('--install', '--replace-conflicts', 'STEAM_CN', str(game), ok=True)
        self.assertIn('已备份并删除原冲突插件', replaced)
        self.assertEqual(sha(target), sha(self.pkg / 'Runtime/STEAM_CN/__data'))
        self.assertFalse(apx_dll.exists())
        self.assertFalse(apx_ui.exists())
        self.assertFalse(apx_extra.exists())
        # Native speed DLL stays intact per Hub rules
        self.assertEqual(speed_dll.read_bytes(), b'speed-mod-keep')

        # Rollback preserves pre-install conflict file if user restores
        restored = self.run_cli('--restore', 'STEAM_CN', str(game), ok=True)
        self.assertIn('冲突插件版本', restored)
        self.assertEqual(target.read_bytes(), apx_bundle)

    def test_unknown_non_unityfs_rejected_even_with_replace_conflicts(self):
        game, target = self.make_game('game-corrupt')
        target.write_bytes(b'unknown game update')
        fail_log = game / 'corrupt.log'
        err = self.run_cli('--install', '--replace-conflicts', '--export-log', str(fail_log), 'STEAM_CN', str(game), ok=False)
        self.assertIn('当前资源与本发布包不匹配', err)
        self.assertTrue(fail_log.is_file())
        self.assertIn('当前资源与本发布包不匹配', fail_log.read_text('utf8'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
