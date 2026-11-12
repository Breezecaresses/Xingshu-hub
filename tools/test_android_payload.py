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

    def test_old_plugin_hash_is_never_accepted_for_resource_150(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(payload, 'ROOT', Path(directory)):
            old, current = [self.manifest(digest) for digest in payload.BASELINES]
            self.assertEqual(len(payload.previous_hashes(old)), 1)
            self.assertEqual(payload.previous_hashes(current), [])


if __name__ == '__main__':
    unittest.main(verbosity=2)
