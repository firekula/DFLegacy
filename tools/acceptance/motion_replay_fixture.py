"""Bounded ACT1 x86 arithmetic replay; never load or execute a game process.

The checked-in fixture contains original-image instruction/constant bytes
observed by the four MotionHorizonNativeTests and declared grid/installer audit
ranges. Synthetic vectors and stack state are supplied by the tests, not captured
from a player or a running client.
"""
from bisect import bisect_right
from dataclasses import dataclass
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest


ORIGINAL_SHA256 = '6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5'
IMAGE_BASE = 0x400000
PATCH_ADDRESS = 0x4C99EF
ORIGINAL_PATCH = b'\x8d\x57\x1c'
INSTALLED_PATCH = b'\x8b\x53\xf4'
MAX_FRAGMENT_BYTES = 16384
MAX_FRAGMENTS = 512
DEFAULT_FIXTURE = Path(__file__).resolve().parents[2] / 'tests' / 'fixtures' / 'act1-motion-horizon'
SUPPLEMENTAL_RANGES = (
    (0x4B3C30, 0x1EB, 'grid division, control-flow disassembly and four installer sites'),
    (0x8A8FE0, 0x76, 'original grid function including ret16'),
    (0x42C990, 0x10, 'original no-argument v18C getter'),
    (0x4C8EBC, 6, 'remaining DflMotionProbeSites motion-call installer site'),
    (0x78EE76, 5, 'original provider result marker entry for register and floating-point ABI replay'),
)


class FixtureError(ValueError):
    """The evidence is invalid or replay left its recorded original-image boundary."""


def sha256(data):
    return hashlib.sha256(data).hexdigest()


@dataclass(frozen=True)
class ReplayImage:
    base: int
    image_size: int
    fragments: tuple
    patch: bytes
    source_sha256: str = ORIGINAL_SHA256

    @property
    def byte_count(self):
        return sum(len(data) for _, data in self.fragments)

    def _contains(self, address, size):
        starts = [start for start, _ in self.fragments]
        index = bisect_right(starts, address) - 1
        return index >= 0 and address + size <= starts[index] + len(self.fragments[index][1])

    def original_bytes(self, address, size):
        if not self._contains(address, size):
            raise FixtureError(f'Uncaptured original bytes at {address:08X}+{size}')
        index = bisect_right([start for start, _ in self.fragments], address) - 1
        start, data = self.fragments[index]
        return data[address - start:address - start + size]

    def get_data(self, rva, length):
        """The bounded subset of pefile.PE.get_data used by original-code tests."""
        if type(rva) is not int or type(length) is not int or rva < 0 or length <= 0:
            raise FixtureError('Original image reads require a positive, bounded RVA and length')
        return self.original_bytes(self.base + rva, length)

    def install(self, u, *, fixed, recorder=None):
        from unicorn import UC_HOOK_CODE, UC_HOOK_MEM_READ, UC_HOOK_MEM_WRITE
        pages = sorted({page for start, data in self.fragments
                        for page in range(start & ~4095, (start + len(data) + 4095) & ~4095, 4096)})
        runs = []
        for page in pages:
            if runs and runs[-1][0] + runs[-1][1] == page:
                runs[-1][1] += 4096
            else:
                runs.append([page, 4096])
        for start, size in runs:
            u.mem_map(start, size)
        for start, data in self.fragments:
            u.mem_write(start, data)
        if fixed:
            u.mem_write(PATCH_ADDRESS, self.patch)

        def code(cpu, address, size, unused):
            if self.base <= address < self.base + self.image_size:
                if not self._contains(address, size):
                    raise FixtureError(f'Uncaptured original instruction at {address:08X}+{size}')
                if recorder is not None:
                    recorder.record(address, size, 'instruction')

        def read(cpu, access, address, size, value, unused):
            if not self._contains(address, size):
                raise FixtureError(f'Uncaptured original data read at {address:08X}+{size}')
            if recorder is not None:
                recorder.record(address, size, 'data')

        def write(cpu, access, address, size, value, unused):
            raise FixtureError(f'Unexpected original-image write at {address:08X}+{size}')

        u.hook_add(UC_HOOK_CODE, code)
        u.hook_add(UC_HOOK_MEM_READ, read, begin=self.base, end=self.base + self.image_size - 1)
        u.hook_add(UC_HOOK_MEM_WRITE, write, begin=self.base, end=self.base + self.image_size - 1)


def _read(path):
    try:
        return Path(path).read_bytes()
    except OSError as error:
        raise FixtureError(f'Missing or unreadable motion fixture: {path}') from error


def load_fixture(directory=DEFAULT_FIXTURE):
    directory = Path(directory)
    try:
        manifest = json.loads(_read(directory / 'manifest.json'))
        if manifest['schema'] != 1 or manifest['kind'] != 'act1-motion-horizon-instruction-replay':
            raise FixtureError('Unsupported motion fixture schema')
        source = manifest['source']
        if source['sha256'] != ORIGINAL_SHA256 or source['image_base'] != IMAGE_BASE:
            raise FixtureError('Unsupported original ACT1 identity')
        image_size = source['image_size']
        if type(image_size) is not int or not 0 < image_size <= 64 * 1024 * 1024:
            raise FixtureError('Invalid original image size')
        data = _read(directory / 'fragments.bin')
        if not 0 < len(data) <= MAX_FRAGMENT_BYTES or len(data) != manifest['fragment_bytes']:
            raise FixtureError('Missing, oversized, or truncated motion fragments')
        if sha256(data) != manifest['fragments_sha256']:
            raise FixtureError('Motion fragments SHA256 mismatch')
        records = manifest['fragments']
        if not 0 < len(records) <= MAX_FRAGMENTS:
            raise FixtureError('Invalid motion fragment count')
        fragments = []
        previous_end = IMAGE_BASE
        expected_offset = 0
        for record in records:
            address, size, offset = record['address'], record['size'], record['offset']
            if any(type(value) is not int for value in (address, size, offset)):
                raise FixtureError('Motion fragment addresses, sizes and offsets must be integers')
            if not 0 < size <= MAX_FRAGMENT_BYTES or offset != expected_offset:
                raise FixtureError('Invalid motion fragment size or offset')
            if address < previous_end or address + size > IMAGE_BASE + image_size:
                raise FixtureError('Overlapping or out-of-image motion fragments')
            fragment = data[offset:offset + size]
            if len(fragment) != size or sha256(fragment) != record['sha256']:
                raise FixtureError('Motion fragment SHA256 or size mismatch')
            fragments.append((address, fragment))
            previous_end = address + size
            expected_offset += size
        if expected_offset != len(data):
            raise FixtureError('Unaccounted motion fragment bytes')
        patch = _read(directory / 'installed-patch.bin')
        evidence = manifest['patch']
        if (patch != INSTALLED_PATCH or evidence['address'] != PATCH_ADDRESS
                or evidence['size'] != 3 or evidence['sha256'] != sha256(patch)
                or evidence['original_hex'] != ORIGINAL_PATCH.hex()):
            raise FixtureError('Unexpected installed motion horizon patch')
        image = ReplayImage(IMAGE_BASE, image_size, tuple(fragments), patch)
        if image.original_bytes(PATCH_ADDRESS, 3) != ORIGINAL_PATCH:
            raise FixtureError('Original motion patch site mismatch')
        return image
    except (KeyError, TypeError, json.JSONDecodeError) as error:
        raise FixtureError('Malformed motion fixture manifest') from error


def load_motion_image(*, executable=None, patch=None, directory=DEFAULT_FIXTURE):
    if executable is None and patch is None:
        return load_fixture(directory)
    if executable is None or patch is None:
        raise FixtureError('Explicit original executable and installed patch overrides must be supplied together')
    image = load_original_image(executable=executable)
    installed = _read(patch)
    if installed != INSTALLED_PATCH:
        raise FixtureError('Expected the three actual native installed instruction bytes')
    return image


def load_original_image(*, executable=None, directory=DEFAULT_FIXTURE):
    if executable is None:
        return load_fixture(directory)
    original = _read(executable)
    if sha256(original) != ORIGINAL_SHA256:
        raise FixtureError('Unsupported original ACT1 executable')
    import pefile
    pe = pefile.PE(data=original)
    raw = pe.get_memory_mapped_image()
    if pe.OPTIONAL_HEADER.ImageBase != IMAGE_BASE:
        raise FixtureError('Unsupported original ACT1 image base')
    image = ReplayImage(IMAGE_BASE, len(raw), ((IMAGE_BASE, raw),), INSTALLED_PATCH)
    if image.original_bytes(PATCH_ADDRESS, 3) != ORIGINAL_PATCH:
        raise FixtureError('Original ACT1 patch-site bytes mismatch')
    return image


def run_motion_case(image, fixed, *, accels=(0, 0, 0), durations=(18836, -1, -1),
                    current=(159.87469482421875, 226., 0.), target=(244.00990295410156, 226., 0.),
                    terminal=(0., 0., 0.), integrate_ms=0, full=False, recorder=None):
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
    from unicorn.x86_const import (UC_X86_REG_ESI, UC_X86_REG_EBP, UC_X86_REG_ESP,
                                   UC_X86_REG_ECX, UC_X86_REG_EBX, UC_X86_REG_EIP)
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    image.install(u, fixed=fixed, recorder=recorder)
    for start, size in [(0x20000000, 0x10000), (0x30000000, 0x10000), (0x40000000, 0x1000)]:
        u.mem_map(start, size)
    vt = 0x20000000
    cur, dest, controller, frame, sentinel = vt + 0x1000, vt + 0x2000, vt + 0x3000, 0x30008000, 0x40000000
    # Original plain-vector getters/setters and ftol code execute unchanged.
    u.mem_write(vt, struct.pack('<IIII', 0x4736A0, 0x4736B0, 0x4736D0, 0x4736E0))
    for address, position in [(cur, current), (dest, target)]:
        u.mem_write(address, struct.pack('<IffffffiiiiiiB', vt, *position, *terminal, *accels, *durations, 0))
    u.mem_write(controller, struct.pack('<II', cur, dest))
    u.mem_write(frame, struct.pack('<III', frame + 0x1000, sentinel, 0))
    u.mem_write(frame - 1, b'\0')
    for register, value in [(UC_X86_REG_ESI, controller), (UC_X86_REG_EBP, frame), (UC_X86_REG_ESP, frame - 0x30)]:
        u.reg_write(register, value)
    u.reg_write(UC_X86_REG_EBX, 1 if full else 0)
    u.hook_add(UC_HOOK_CODE, lambda cpu, address, size, data: cpu.emu_stop() if address == sentinel else None)

    def execute(entry):
        u.emu_start(entry, sentinel + 1, count=10000)
        if u.reg_read(UC_X86_REG_EIP) != sentinel:
            raise FixtureError('Original motion replay did not reach its return sentinel')

    execute(0x4C9919)
    reconciled = bytes(u.mem_read(cur, 53))
    remaining = integrate_ms
    while remaining:
        delta = min(16, remaining)
        remaining -= delta
        for axis in range(3):
            stack = 0x30009000
            u.mem_write(stack, struct.pack('<III', sentinel, axis, delta))
            u.reg_write(UC_X86_REG_ECX, controller)
            u.reg_write(UC_X86_REG_ESP, stack)
            execute(0x4C8C00)
    return reconciled, bytes(u.mem_read(cur, 53)), bytes(u.mem_read(dest, 53))


class TraceCollector:
    def __init__(self, image):
        self.image = image
        self.addresses = set()
        self.access_bytes = {'instruction': set(), 'data': set(), 'audit_range': set()}

    def record(self, address, size, kind):
        self.image.original_bytes(address, size)
        addresses = set(range(address, address + size))
        self.addresses.update(addresses)
        self.access_bytes[kind].update(addresses)
        if len(self.addresses) > MAX_FRAGMENT_BYTES:
            raise FixtureError('Motion replay exceeded its bounded byte budget')

    def fragments(self):
        runs = []
        for address in sorted(self.addresses):
            if runs and runs[-1][0] + runs[-1][1] == address:
                runs[-1][1] += 1
            else:
                runs.append([address, 1])
        if len(runs) > MAX_FRAGMENTS:
            raise FixtureError('Motion replay exceeded its bounded fragment count')
        return [(start, self.image.original_bytes(start, size)) for start, size in runs]


def regenerate(executable, native_tests, output):
    """Read the verified original PE, run native tests, capture all four replays."""
    output = Path(output)
    if output.exists():
        raise FixtureError('Regeneration requires a fresh output directory')
    native_tests = Path(native_tests).resolve()
    native_hash = sha256(_read(native_tests))
    # Verify the original identity before launching the supplied native test tool.
    if sha256(_read(executable)) != ORIGINAL_SHA256:
        raise FixtureError('Unsupported original ACT1 executable')
    with tempfile.TemporaryDirectory(prefix='dfl-motion-patch-') as temporary:
        patch = Path(temporary) / 'installed-patch.bin'
        env = dict(os.environ, DFLEGACY_MOTION_HORIZON_PATCH=str(patch))
        native = subprocess.run([str(native_tests), '--motion-horizon-only'], env=env,
                                capture_output=True, text=True, timeout=60, check=False)
        if native.returncode != 0 or 'Motion horizon tests: 0 failures' not in native.stdout:
            raise FixtureError(f'Actual native motion horizon tests failed: {native.stdout}\n{native.stderr}')
        image = load_motion_image(executable=executable, patch=patch)
    collector = TraceCollector(image)
    from tools.acceptance.tests.test_motion_horizon_native import MotionHorizonNativeTests

    class CapturedMotionHorizonNativeTests(MotionHorizonNativeTests):
        @classmethod
        def setUpClass(cls):
            cls.image = image
            cls.recorder = collector

    suite = unittest.defaultTestLoader.loadTestsFromTestCase(CapturedMotionHorizonNativeTests)
    names = unittest.defaultTestLoader.getTestCaseNames(CapturedMotionHorizonNativeTests)
    log = io.StringIO()
    result = unittest.TextTestRunner(stream=log, verbosity=2).run(suite)
    if result.testsRun != 4 or not result.wasSuccessful() or result.skipped:
        raise FixtureError(f'Original-vs-patched capture tests failed:\n{log.getvalue()}')
    for address, size, _ in SUPPLEMENTAL_RANGES:
        collector.record(address, size, 'audit_range')
    fragments = collector.fragments()
    data = b''.join(fragment for _, fragment in fragments)
    records, offset = [], 0
    for address, fragment in fragments:
        records.append(dict(address=address, size=len(fragment), offset=offset, sha256=sha256(fragment)))
        offset += len(fragment)
    root = Path(__file__).resolve().parents[2]
    inputs = ['src/DFLegacy.Ijl15/src/motion_horizon.cpp', 'src/DFLegacy.Ijl15/tests/motion_horizon_tests.cpp',
              'src/DFLegacy.Ijl15/src/motion_test_probe.cpp', 'src/DFLegacy.Ijl15/src/provider_probe.cpp']
    manifest = {
        'schema': 1, 'kind': 'act1-motion-horizon-instruction-replay',
        'source': {'name': 'DNF.exe', 'sha256': ORIGINAL_SHA256,
                   'image_base': IMAGE_BASE, 'image_size': image.image_size},
        'fragment_bytes': len(data), 'fragments_sha256': sha256(data), 'fragments': records,
        'patch': {'address': PATCH_ADDRESS, 'size': 3, 'sha256': sha256(image.patch),
                  'original_hex': ORIGINAL_PATCH.hex(), 'origin': 'actual native ApplyAt installed bytes',
                  'native_tests_sha256': native_hash, 'command': 'ijl15_tests.exe --motion-horizon-only',
                  'native_stdout': native.stdout.strip(),
                  'source_inputs': {name: sha256(_read(root / name)) for name in inputs}},
        'capture': {'tests': names, 'tests_run': result.testsRun,
                    'instruction_bytes': len(collector.access_bytes['instruction']),
                    'data_bytes': len(collector.access_bytes['data']),
                    'audit_range_bytes': len(collector.access_bytes['audit_range']),
                    'audit_ranges': [dict(address=address, size=size, purpose=purpose)
                                     for address, size, purpose in SUPPLEMENTAL_RANGES],
                    'method': 'Unicorn code and original-image read hooks across every original and repaired test case',
                    'excluded': ['full PE', 'synthetic heap and stack', 'player state', 'game assets']},
    }
    output.mkdir(parents=True)
    (output / 'fragments.bin').write_bytes(data)
    (output / 'installed-patch.bin').write_bytes(image.patch)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    (output / 'README.md').write_text(
        '# ACT1 motion horizon arithmetic replay\n\n'
        f'Contains **{len(data)} original instruction/constant bytes in {len(records)} fragments** '
        'and **3 actual native installed repair bytes**. Each fragment has an absolute virtual address, '
        'byte count and SHA256 in `manifest.json`. The manifest also records the full original PE SHA256, '
        'native test executable hash, native source hashes and successful export command.\n\n'
        'Instruction fetches and original-image data reads observed while running all four '
        '`MotionHorizonNativeTests`, both original and repaired, were extracted. Five additional '
        'bounded original-code ranges support `MotionGridNativeTests` and '
        'the control-flow, branch and provider probe tests; each range and its purpose is listed '
        'under `capture.audit_ranges`. They contain the grid division/control-flow block, grid routine, '
        'v18C getter, remaining motion installer site and provider result marker entry. '
        'The probe tests still inspect and execute probes from the actual compiled DLL. '
        'No full executable, '
        'game resources, mutable player state, heap or stack was captured. The tests create their '
        'own synthetic vectors, vtable, controller and stack. The 500ms integration path uses original '
        'getter/setter, arithmetic and float-to-integer instructions. No Python motion approximation '
        'replaces client instructions. `load_original_image().get_data(rva, length)` supplies a '
        'verified bounded adapter for the grid and probe tests; reads outside recorded fragments fail.\n\n'
        'This covers decoded-packet reconciliation at `0x4C9919` and per-axis integration at `0x4C8C00`, '
        'including existing duration/acceleration, terminal velocity, unaffected axes and full-target '
        'publication cases. It does not cover packet decoding, live maps, actor collision, server '
        'authority, network delivery or real gameplay. Missing/corrupt/overlapping/oversized fragments '
        'and unexpected repair bytes fail validation. In the horizon replay, uncaptured '
        'original-image reads/instructions and any original-image write fail rather than using '
        'mapped-page zero padding.\n\n'
        'Regenerate into a **new directory** from the exact verified original PE and the actual '
        'native test binary built from the recorded native source:\n\n'
        '```powershell\npython -m tools.acceptance.motion_replay_fixture '
        '--original-exe <path-to-original-DNF.exe> --native-tests <path-to-ijl15_tests.exe> '
        '--output <new-directory>\n```\n\n'
        'The command reads the original PE without launching or modifying it. It executes only the '
        'native unit-test executable to export a fresh installed patch, then executes the bounded '
        'instruction replays under Unicorn. The default original-code input needs no private '
        'client or environment variables. The probe tests still require '
        '`DFLEGACY_NATIVE_LOOP_DLL` pointing to the actual built DLL. For full-image confirmation, '
        'supply both `DFLEGACY_ACT1_CLIENT_EXE` and '
        '`DFLEGACY_MOTION_HORIZON_PATCH`; the grid and probe tests accept `DFLEGACY_DNF_EXE` for '
        'full-image confirmation. Wrong original SHA256 or unexpected patch bytes fail.\n',
        encoding='utf-8')
    load_fixture(output)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original-exe', required=True)
    parser.add_argument('--native-tests', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    manifest = regenerate(args.original_exe, args.native_tests, args.output)
    print(f"Captured {manifest['fragment_bytes']} bytes in {len(manifest['fragments'])} fragments; 4 replay tests passed")


if __name__ == '__main__':
    main()
