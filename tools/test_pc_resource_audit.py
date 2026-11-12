import base64
import copy
import struct
import unittest

from pc_resource_audit import managed_bundle_id


def catalog(bundle='1234567890abcdef1234567890abcdef'):
    names = ['AstralParty.Runtime.dll', bundle + '.bundle']
    key_data, offsets = bytearray(), []
    for name in names:
        offsets.append(len(key_data))
        raw = name.encode('ascii')
        key_data.extend(b'\0' + struct.pack('<i', len(raw)) + raw)
    buckets = struct.pack('<7i', 2, offsets[0], 1, 0, offsets[1], 1, 1)
    entries = struct.pack('<15i', 2, 0, 1, 1, 0, -1, 0, 0, 1, 0, -1, 0, -1, 1, 0)
    return {'m_KeyDataString': base64.b64encode(key_data).decode(),
            'm_BucketDataString': base64.b64encode(buckets).decode(),
            'm_EntryDataString': base64.b64encode(entries).decode(),
            'm_InternalIds': ['runtime-address', '{App.WebServerConfig.Path}\\' + bundle + '.bundle']}


def change_int(source, table, position, value):
    updated = copy.deepcopy(source)
    raw = bytearray(base64.b64decode(updated[table]))
    struct.pack_into('<i', raw, position, value)
    updated[table] = base64.b64encode(raw).decode()
    return updated


class ResourceCatalogTests(unittest.TestCase):
    def test_arbitrary_resource_bundle_is_resolved_without_pinned_id(self):
        for bundle in ('a' * 32, 'f' * 32, '536196fba89e10e449fc9b864bfdbd51'):
            self.assertEqual(managed_bundle_id(catalog(bundle)), bundle)

    def test_runtime_key_is_required(self):
        sample = catalog()
        raw = base64.b64decode(sample['m_KeyDataString']).replace(b'AstralParty.Runtime.dll', b'AnotherGame.Runtime.dll')
        sample['m_KeyDataString'] = base64.b64encode(raw).decode()
        with self.assertRaises(ValueError):
            managed_bundle_id(sample)

    def test_negative_bucket_count_is_rejected(self):
        with self.assertRaises(ValueError):
            managed_bundle_id(change_int(catalog(), 'm_BucketDataString', 0, -1))

    def test_out_of_range_entry_is_rejected(self):
        with self.assertRaises(ValueError):
            managed_bundle_id(change_int(catalog(), 'm_BucketDataString', 12, 2))

    def test_out_of_range_key_is_rejected(self):
        with self.assertRaises(ValueError):
            managed_bundle_id(change_int(catalog(), 'm_BucketDataString', 4, 99999))

    def test_out_of_range_dependency_is_rejected(self):
        with self.assertRaises(ValueError):
            managed_bundle_id(change_int(catalog(), 'm_EntryDataString', 12, 2))

    def test_truncated_entry_table_is_rejected(self):
        sample = catalog()
        sample['m_EntryDataString'] = base64.b64encode(base64.b64decode(sample['m_EntryDataString'])[:-1]).decode()
        with self.assertRaises(ValueError):
            managed_bundle_id(sample)

    def test_dependency_cycle_cannot_select_a_random_bundle(self):
        with self.assertRaises(ValueError):
            managed_bundle_id(change_int(catalog(), 'm_EntryDataString', 12, 0))

    def test_external_url_cannot_become_a_cache_target(self):
        sample = catalog()
        sample['m_InternalIds'][1] = 'https://other.example/' + 'a' * 32 + '.bundle'
        with self.assertRaises(ValueError):
            managed_bundle_id(sample)


if __name__ == '__main__':
    unittest.main()
