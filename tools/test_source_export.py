"""Regression checks for document migration without deleting checkout edits."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import export_source_repo as export
from project_paths import project_document


class DocumentationExportTests(unittest.TestCase):
    def test_packaging_reads_archived_guides(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            legacy = root / 'note.md'; legacy.write_text('local', encoding='utf8')
            self.assertEqual(project_document(root, 'note.md'), legacy)
            archived = root / 'docs/guides/note.md'
            archived.parent.mkdir(parents=True)
            archived.write_text('public', encoding='utf8')
            self.assertEqual(project_document(root, 'note.md'), archived)
            legacy.unlink()
            self.assertEqual(project_document(root, 'note.md'), archived)

    def test_links_follow_moved_documents_and_keep_fragments(self):
        source = '[guide](ANDROID_PHONE_INSTALL.md#phone) [code](tools/build.py)'
        result = export.rewrite_doc_links(source, 'GAME_UI_R7.md', 'docs/records/GAME_UI_R7.md')
        self.assertEqual(result, '[guide](../guides/ANDROID_PHONE_INSTALL.md#phone) [code](../../tools/build.py)')

    def test_external_links_and_same_document_anchors_remain(self):
        source = '[web](https://example.com/file.md) [section](#one)'
        self.assertEqual(export.rewrite_doc_links(source, 'old.md', 'docs/old.md'), source)

    def test_readme_link_rewrite_is_idempotent(self):
        source = '[records](GAME_UI_R7.md)'
        result = export.rewrite_doc_links(source, 'README.md', 'README.md')
        self.assertEqual(result, '[records](docs/records/GAME_UI_R7.md)')
        self.assertEqual(export.rewrite_doc_links(result, 'README.md', 'README.md'), result)

    def test_unknown_checkout_edits_are_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            root, checkout = Path(temp) / 'working', Path(temp) / 'checkout'
            root.mkdir(); checkout.mkdir(); (checkout / 'docs').mkdir()
            (root / 'note.md').write_text('original', encoding='utf8')
            old = checkout / 'note.md'; old.write_text('user edit', encoding='utf8')
            with patch.object(export, 'ROOT', root), patch.object(export, 'DOC_PATHS', {'note.md': 'docs/records/note.md'}):
                with self.assertRaises(SystemExit): export.migrate_root_documents(checkout)
            self.assertEqual(old.read_text('utf8'), 'user edit')


if __name__ == '__main__':
    unittest.main()
