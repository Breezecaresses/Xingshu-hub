"""Verify library packaging with isolated fixtures, never installed game files."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import package_group_test as packaging


class LibraryPackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='xingshu-mod-package-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'library'
        self.source.mkdir()
        self.sha = hashlib.sha256(b'texture').hexdigest()
        self.relative = f'payload/{self.sha}.bundle'
        self.file = self.source / self.relative
        self.file.parent.mkdir()
        self.file.write_bytes(b'texture')
        self.catalog = {'items': [{'files': [{'source': self.relative, 'sha256': self.sha, 'size': 7}]}]}
        embedded = self.root / 'AstralParty-Hub/src-tauri/src/mod_library.generated.json'
        embedded.parent.mkdir(parents=True)
        embedded.write_text(json.dumps(self.catalog), encoding='utf-8')
        (self.source / 'catalog.json').write_text(json.dumps(self.catalog), encoding='utf-8')
        (self.source / '适配说明.md').write_text('author and target records', encoding='utf-8')
        self.root_patch = patch.object(packaging, 'ROOT', self.root)
        self.root_patch.start()
        self.addCleanup(self.root_patch.stop)

    def test_only_catalogued_files_are_copied(self):
        (self.source / 'unrelated.exe').write_bytes(b'do not distribute')
        destination = self.root / 'output'
        result = packaging.stage_mod_library(self.source, destination)
        self.assertEqual(result['ItemCount'], 1)
        self.assertEqual(result['PayloadCount'], 1)
        self.assertEqual((destination / self.relative).read_bytes(), b'texture')
        self.assertFalse((destination / 'unrelated.exe').exists())

    def test_corrupt_payload_is_rejected_before_staging(self):
        self.file.write_bytes(b'changed')
        with self.assertRaises(ValueError):
            packaging.stage_mod_library(self.source, self.root / 'output')
        self.assertFalse((self.root / 'output').exists())

    def test_replaced_catalog_is_rejected(self):
        (self.source / 'catalog.json').write_text('{"items": []}', encoding='utf-8')
        with self.assertRaises(ValueError):
            packaging.stage_mod_library(self.source, self.root / 'output')

    def test_payload_path_is_fixed_by_hash(self):
        self.catalog['items'][0]['files'][0]['source'] = '../elsewhere.bundle'
        for path in (self.source / 'catalog.json', self.root / 'AstralParty-Hub/src-tauri/src/mod_library.generated.json'):
            path.write_text(json.dumps(self.catalog), encoding='utf-8')
        with self.assertRaises(ValueError):
            packaging.stage_mod_library(self.source, self.root / 'output')


if __name__ == '__main__':
    unittest.main()
