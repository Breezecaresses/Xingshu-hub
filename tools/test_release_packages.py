"""Exercise the phone distribution ZIP and its direct update APK in isolated folders."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import package_android_preview as phone


class PhoneReleasePackages(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='xingshu-phone-zip-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.payload, self.vanilla = self.root / 'patch', self.root / 'vanilla'
        self.payload.write_bytes(b'isolated candidate')
        self.vanilla.write_bytes(b'isolated original')
        self.apk = self.root / 'phone.apk'
        with zipfile.ZipFile(self.apk, 'w') as apk:
            apk.writestr('assets/candidate.__data', self.payload.read_bytes())
            apk.writestr('assets/vanilla.__data', self.vanilla.read_bytes())
        self.manifest = self.root / 'candidate.json'
        self.manifest.write_text(json.dumps({'platform': 'ANDROID_CN', 'package': 'com.feimo.astralparty',
            'appVersion': '3.3.0', 'runtimeVersion': 'fixture', 'nativeLibraries': [],
            'candidate': str(self.payload), 'candidateSha256': phone.sha(self.payload),
            'originalSha256': phone.sha(self.vanilla), 'bundleRelativePath': 'fixture/__data'}), encoding='utf8')
        for name in ('LICENSE', 'ANDROID_PHONE_INSTALL.md', 'ANDROID_COMPATIBILITY.md'):
            (self.root / name).write_text('isolated documentation', encoding='utf8')
        self.name = 'XingShu_Android_0.0.0-fixture.1'
        self.archive = self.root / '发布包' / (self.name + '.zip')

    def build(self):
        args = ['package_android_preview.py', '--manifest', str(self.manifest), '--version', '0.0.0-fixture.1',
                '--source-commit', 'a' * 40, '--phone-apk', str(self.apk), '--vanilla', str(self.vanilla)]
        with patch.object(phone, 'ROOT', self.root), patch.object(phone, 'previous_hashes', return_value=[]), \
                patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()):
            phone.main()

    def modified_zip(self, replacements):
        destination = self.root / 'modified.zip'
        with zipfile.ZipFile(self.archive) as original, zipfile.ZipFile(destination, 'w') as output:
            files = {name: original.read(name) for name in original.namelist()}
            files.update(replacements)
            for name, data in files.items(): output.writestr(name, data)
        return destination

    def test_one_zip_contains_apk_manual_and_restore(self):
        self.build()
        direct_apk = self.archive.with_suffix('.apk')
        info = phone.verify_android_archive(self.archive, direct_apk)
        self.assertEqual(phone.sha(direct_apk), phone.sha(self.apk))
        self.assertEqual(info['releaseVersion'], '0.0.0-fixture.1')
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(archive.read(self.name + '/手动替换/__data'), self.payload.read_bytes())
            self.assertEqual(archive.read(self.name + '/手动替换/官方原版.__data'), self.vanilla.read_bytes())
        self.assertEqual(len(list(self.archive.parent.glob('*.zip'))), 1)
        self.assertEqual(Path(str(self.archive) + '.sha256').read_text('utf8').split()[0], phone.sha(self.archive))

    def test_manual_payload_tampering_is_rejected(self):
        self.build()
        changed = self.modified_zip({self.name + '/手动替换/__data': b'other payload'})
        with self.assertRaisesRegex(AssertionError, 'Manual payload differs'):
            phone.verify_android_archive(changed, self.apk)

    def test_another_apk_cannot_be_published_for_this_zip(self):
        self.build()
        other = self.root / 'different.apk'
        other.write_bytes(b'not the same APK')
        with self.assertRaisesRegex(AssertionError, 'ZIP APK differs'):
            phone.verify_android_archive(self.archive, other)

    def test_unapproved_or_traversal_entry_is_rejected_without_extraction(self):
        self.build()
        changed = self.modified_zip({self.name + '/../../escape': b'unapproved'})
        with self.assertRaisesRegex(AssertionError, 'Unexpected or missing'):
            phone.verify_android_archive(changed, self.apk)
        self.assertFalse((self.root.parent / 'escape').exists())


if __name__ == '__main__':
    unittest.main(verbosity=2)
