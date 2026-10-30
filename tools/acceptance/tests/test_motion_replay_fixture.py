"""Integrity and fail-closed boundaries for the portable ACT1 arithmetic replay."""
from pathlib import Path
from dataclasses import replace
import json
import shutil
import struct
import tempfile
import unittest


FIXTURE = Path(__file__).resolve().parents[3] / 'tests' / 'fixtures' / 'act1-motion-horizon'


class MotionReplayFixtureTests(unittest.TestCase):
    def test_committed_fixture_runs_without_a_private_client(self):
        self.assertTrue((FIXTURE / 'manifest.json').is_file(),
                        'Portable ACT1 motion instruction fixture is required')
        from tools.acceptance.motion_replay_fixture import load_fixture, run_motion_case
        image = load_fixture()
        state, end, _ = run_motion_case(image, True, integrate_ms=500)
        self.assertEqual(struct.unpack_from('<i', state, 40)[0], 500)
        self.assertAlmostEqual(struct.unpack_from('<f', end, 4)[0], 244.00990295410156, delta=.1)

    def test_missing_or_changed_fragment_data_is_rejected(self):
        from tools.acceptance.motion_replay_fixture import FixtureError, load_fixture
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary) / 'fixture'
            shutil.copytree(FIXTURE, fixture)
            binary = fixture / 'fragments.bin'
            data = binary.read_bytes()
            for changed in (data[:-1], bytes([data[0] ^ 1]) + data[1:]):
                with self.subTest(length=len(changed)):
                    binary.write_bytes(changed)
                    with self.assertRaises(FixtureError):
                        load_fixture(fixture)
            binary.unlink()
            with self.assertRaises(FixtureError):
                load_fixture(fixture)

    def test_original_image_adapter_supplies_grid_and_installer_audit_ranges(self):
        from tools.acceptance.motion_replay_fixture import FixtureError, load_original_image
        image = load_original_image()
        self.assertEqual(image.get_data(0x4C8EBC - 0x400000, 6), b'\xff\x90\x94\0\0\0')
        self.assertEqual(len(image.get_data(0x4B3C30 - 0x400000, 0x1EB)), 0x1EB)
        self.assertEqual(len(image.get_data(0x8A8FE0 - 0x400000, 0x76)), 0x76)
        self.assertEqual(len(image.get_data(0x42C990 - 0x400000, 0x10)), 0x10)
        self.assertEqual(image.get_data(0x78EE76 - 0x400000, 5), b'\x85\xc0\x0f\x95\xc0')
        for rva, length in ((-1, 1), (0, 0), (0, 1), (0x4B3C30 - 0x400000, 0x1EC)):
            with self.subTest(rva=rva, length=length), self.assertRaises(FixtureError):
                image.get_data(rva, length)

    def test_bad_identity_patch_overlap_and_size_are_rejected(self):
        from tools.acceptance.motion_replay_fixture import FixtureError, load_fixture
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary) / 'fixture'
            shutil.copytree(FIXTURE, fixture)
            manifest_path = fixture / 'manifest.json'
            original = json.loads(manifest_path.read_text(encoding='utf-8'))
            for failure in ('identity', 'patch', 'overlap', 'oversize', 'outside_image', 'offset', 'fragment_hash'):
                with self.subTest(failure=failure):
                    manifest = json.loads(json.dumps(original))
                    if failure == 'identity':
                        manifest['source']['sha256'] = '0' * 64
                    elif failure == 'patch':
                        (fixture / 'installed-patch.bin').write_bytes(b'\x8d\x57\x1c')
                    elif failure == 'overlap':
                        manifest['fragments'][1]['address'] = manifest['fragments'][0]['address']
                    elif failure == 'oversize':
                        manifest['fragments'][0]['size'] = 1000000
                    elif failure == 'outside_image':
                        manifest['fragments'][0]['address'] = 0x30008000
                    elif failure == 'offset':
                        manifest['fragments'][1]['offset'] = 0
                    else:
                        manifest['fragments'][0]['sha256'] = '0' * 64
                    manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
                    with self.assertRaises(FixtureError):
                        load_fixture(fixture)
                    (fixture / 'installed-patch.bin').write_bytes(b'\x8b\x53\xf4')

    def test_uncaptured_original_image_reads_execution_and_writes_fail_closed(self):
        from tools.acceptance.motion_replay_fixture import FixtureError, load_fixture
        from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
        image = load_fixture()
        # The page is mapped for recorded instructions, but 4C9000 was not captured.
        for access in ('execute', 'read', 'write'):
            with self.subTest(access=access):
                u = Uc(UC_ARCH_X86, UC_MODE_32)
                image.install(u, fixed=False)
                u.mem_map(0x20000000, 0x1000)
                if access == 'execute':
                    entry = 0x4C9000
                elif access == 'read':
                    entry = 0x20000000
                    u.mem_write(entry, b'\xa1' + struct.pack('<I', 0x4C9000))
                else:
                    entry = 0x20000000
                    u.mem_write(entry, b'\xc6\x05' + struct.pack('<I', 0x4C9919) + b'\x00')
                with self.assertRaises(FixtureError):
                    u.emu_start(entry, 0, count=1)

    def test_reverting_the_patch_fails_the_existing_convergence_assertion(self):
        from tools.acceptance.motion_replay_fixture import load_fixture
        from tools.acceptance.tests.test_motion_horizon_native import MotionHorizonNativeTests
        image = replace(load_fixture(), patch=b'\x8d\x57\x1c')
        case = MotionHorizonNativeTests('test_captured_zero_acceleration_stale_duration_must_converge')
        case.image = image
        result = unittest.TestResult()
        case.run(result)
        self.assertEqual(len(result.failures), 1)
        self.assertFalse(result.errors)
        self.assertIn('not greater than', result.failures[0][1])

    def test_explicit_original_and_patch_overrides_must_be_a_verified_pair(self):
        from tools.acceptance.motion_replay_fixture import FixtureError, load_motion_image, load_original_image
        with tempfile.TemporaryDirectory() as temporary:
            bad = Path(temporary) / 'wrong.exe'
            bad.write_bytes(b'not the original client')
            with self.assertRaises(FixtureError):
                load_motion_image(executable=bad)
            with self.assertRaises(FixtureError):
                load_motion_image(patch=FIXTURE / 'installed-patch.bin')
            with self.assertRaisesRegex(FixtureError, 'Unsupported original ACT1'):
                load_motion_image(executable=bad, patch=FIXTURE / 'installed-patch.bin')
            with self.assertRaisesRegex(FixtureError, 'Unsupported original ACT1'):
                load_original_image(executable=bad)

    def test_regeneration_rejects_unverified_original_and_existing_destinations(self):
        from tools.acceptance.motion_replay_fixture import FixtureError, regenerate
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            bad = directory / 'untrusted.exe'
            bad.write_bytes(b'not an original client or a native test binary')
            output = directory / 'new-fixture'
            with self.assertRaisesRegex(FixtureError, 'Unsupported original ACT1'):
                regenerate(bad, bad, output)
            self.assertFalse(output.exists())
            with self.assertRaisesRegex(FixtureError, 'fresh output'):
                regenerate(bad, bad, directory)
            self.assertEqual(bad.read_bytes(), b'not an original client or a native test binary')


if __name__ == '__main__':
    unittest.main()
