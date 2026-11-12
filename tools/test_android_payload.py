"""Keep cache paths and accepted upgrades bound to their audited Android resources."""
from pathlib import Path
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


if __name__ == '__main__':
    unittest.main(verbosity=2)
