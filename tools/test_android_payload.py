"""Keep cache paths and accepted upgrades bound to their audited Android resources."""
from pathlib import Path
import json
import tempfile
import unittest
from unittest.mock import patch

import android_payload as payload


class AndroidBaselineTests(unittest.TestCase):
    def manifest(self, digest):
        return {'originalSha256': digest, **payload.verified_baseline(digest),
                'candidateSha256': 'a' * 64}

    def test_unknown_or_mismatched_baseline_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Unknown Android baseline'):
            payload.verified_baseline('0' * 64)
        manifests = [self.manifest(digest) for digest in payload.BASELINES]
        manifests[1]['bundleRelativePath'] = manifests[0]['bundleRelativePath']
        with self.assertRaisesRegex(ValueError, 'does not match'):
            payload.validate_baseline_manifest(manifests[1])
        wrong = self.manifest(next(iter(payload.BASELINES)))
        wrong['resourceVersion'] = '150'
        with self.assertRaisesRegex(ValueError, 'does not match'):
            payload.validate_baseline_manifest(wrong)

    def test_old_plugin_hash_is_never_accepted_for_later_resources(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(payload, 'ROOT', Path(directory)):
            for digest in payload.BASELINES:
                manifest = self.manifest(digest)
                expected = 1 if manifest['resourceVersion'] == '026' else 0
                self.assertEqual(len(payload.previous_hashes(manifest)), expected)

    def test_resource_paths_cannot_be_mixed_across_baselines(self):
        for digest, baseline in payload.BASELINES.items():
            self.assertEqual(payload.validate_baseline_manifest(self.manifest(digest)), baseline)
            for other_digest, other in payload.BASELINES.items():
                if digest == other_digest:
                    continue
                wrong = {**self.manifest(digest), 'bundleRelativePath': other['bundleRelativePath']}
                with self.assertRaisesRegex(ValueError, 'does not match'):
                    payload.validate_baseline_manifest(wrong)

    def test_explicit_local_predecessor_requires_intact_payload_and_same_baseline(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(payload, 'ROOT', Path(directory)):
            root = Path(directory)
            original = next(digest for digest, row in payload.BASELINES.items() if row['resourceVersion'] == '152')
            current = {**self.manifest(original), 'platform': 'ANDROID_CN', 'package': 'com.feimo.astralparty', 'appVersion': '3.3.0'}
            candidate = root / '__data'
            candidate.write_bytes(b'verified local predecessor')
            old = {**current, 'candidate': '__data', 'candidateSha256': payload.sha(candidate)}
            path = root / 'candidate.json'
            path.write_text(json.dumps(old), encoding='utf8')
            self.assertIn(old['candidateSha256'], payload.previous_hashes(current, [path]))
            candidate.write_bytes(b'corrupt')
            with self.assertRaisesRegex(ValueError, 'missing or corrupt'):
                payload.previous_hashes(current, [path])
            old['package'] = 'another.game'
            path.write_text(json.dumps(old), encoding='utf8')
            with self.assertRaisesRegex(ValueError, 'different game baseline'):
                payload.previous_hashes(current, [path])
            old = {**current, 'candidate': '../escape', 'candidateSha256': 'b' * 64}
            path.write_text(json.dumps(old), encoding='utf8')
            with self.assertRaisesRegex(ValueError, 'escapes'):
                payload.previous_hashes(current, [path])


if __name__ == '__main__':
    unittest.main(verbosity=2)
