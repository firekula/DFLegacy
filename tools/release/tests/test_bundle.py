"""Bundle format tests use controlled bytes; a separate real SDK publish test proves interoperability."""
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

SOURCE = Path(__file__).parents[1] / "bundle.py"
spec = importlib.util.spec_from_file_location("dotnet_bundle_tested", SOURCE)
bundle = importlib.util.module_from_spec(spec) if SOURCE.exists() else None
if bundle:
    spec.loader.exec_module(bundle)
SIGNATURE = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")


def string(value):
    b = value.encode(); length = len(b); prefix = bytearray()
    while length >= 128:
        prefix.append((length & 127) | 128); length >>= 7
    return bytes(prefix) + bytes([length]) + b


def fixture(path, names=None, compressed=False, rid="win-x64"):
    contents = names or {"Fixture.dll": b"assembly", "Fixture.deps.json": b'{"runtimeTarget":{"name":"net10.0/win-x64"}}', "Fixture.runtimeconfig.json": b'{}'}
    data = bytearray(4096)
    if rid.startswith("win"):
        data[:2] = b"MZ"; struct.pack_into("<I", data, 0x3c, 128); data[128:132] = b"PE\0\0"; struct.pack_into("<H", data, 132, 0x8664)
    elif rid.startswith("linux"):
        data[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", data, 18, 62 if rid.endswith("x64") else 183)
    else:
        data[:4] = b"\xcf\xfa\xed\xfe"; struct.pack_into("<I", data, 4, 0x1000007 if rid.endswith("x64") else 0x100000c)
    data[256:288] = SIGNATURE
    entries = []; locations = {}
    for name, body in contents.items():
        offset = len(data)
        encoded = zlib.compress(body)[2:-4] if compressed else body
        data.extend(encoded)
        kind = 3 if name.endswith(".deps.json") else 4 if name.endswith(".runtimeconfig.json") else 1
        entries.append(struct.pack("<qqqB", offset, len(body), len(encoded) if compressed else 0, kind) + string(name))
        locations[kind] = (offset, len(body))
    offset = len(data); struct.pack_into("<q", data, 248, offset)
    data.extend(struct.pack("<IIi", 6, 0, len(entries)) + string("test-bundle") + struct.pack("<qqqqQ", *locations.get(3, (0, 0)), *locations.get(4, (0, 0)), 0) + b"".join(entries))
    path.write_bytes(data); return path


class Bundles(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup); self.path = Path(self.tmp.name) / "Fixture.exe"

    def test_reads_actual_manifest_format_and_raw_deflate_entries(self):
        for compressed in (False, True):
            fixture(self.path, compressed=compressed)
            parsed = bundle.read_bundle(self.path, "win-x64")
            self.assertEqual(parsed.read("Fixture.dll"), b"assembly")
            self.assertEqual(json.loads(parsed.read("Fixture.deps.json"))["runtimeTarget"]["name"], "net10.0/win-x64")

    def test_rejects_apphost_stub_wrong_rid_and_escaping_paths(self):
        self.path.write_bytes(b"not a bundled program")
        with self.assertRaises(ValueError): bundle.read_bundle(self.path, "win-x64")
        fixture(self.path)
        with self.assertRaises(ValueError): bundle.read_bundle(self.path, "linux-arm64")
        fixture(self.path, {"../escape.dll": b"bad"})
        with self.assertRaises(ValueError): bundle.read_bundle(self.path, "win-x64")

    def test_rejects_duplicate_overlapping_and_out_of_bounds_entries(self):
        fixture(self.path)
        data = bytearray(self.path.read_bytes()); offset = struct.unpack_from("<q", data, 248)[0]
        first = offset + 12 + len(string("test-bundle")) + 40
        for position, value in ((first, offset + 100), (first + 8, 10_000_000), (first + 16, 10_000_000)):
            changed = bytearray(data); struct.pack_into("<q", changed, position, value); self.path.write_bytes(changed)
            with self.assertRaises(ValueError): bundle.read_bundle(self.path, "win-x64")
        second = first + 25 + len(string("Fixture.dll"))
        changed = bytearray(data); struct.pack_into("<q", changed, second, 4096); self.path.write_bytes(changed)
        with self.assertRaisesRegex(ValueError, "overlapping"): bundle.read_bundle(self.path, "win-x64")
        changed = bytearray(data); duplicate = string("Fixture.dll"); changed[second + 25:second + 25 + len(duplicate)] = duplicate; self.path.write_bytes(changed)
        with self.assertRaisesRegex(ValueError, "duplicate"): bundle.read_bundle(self.path, "win-x64")

    def test_declared_compressed_size_cannot_hide_a_larger_payload(self):
        fixture(self.path, compressed=True)
        data = bytearray(self.path.read_bytes()); offset = struct.unpack_from("<q", data, 248)[0]
        first = offset + 12 + len(string("test-bundle")) + 40
        struct.pack_into("<q", data, first + 8, 1); self.path.write_bytes(data)
        parsed = bundle.read_bundle(self.path, "win-x64")
        with self.assertRaisesRegex(ValueError, "length mismatch"): parsed.read("Fixture.dll")


if __name__ == "__main__": unittest.main()
