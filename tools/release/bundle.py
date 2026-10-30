"""Read .NET 6+ single-file manifests without extracting executable content.

Format: dotnet/runtime v10.0.0 src/installer/managed/Microsoft.NET.HostModel/Bundle/{Manifest,FileEntry}.cs.
CoreCLR/hostfxr can be statically linked into singlefilehost; native deps in the
embedded .deps.json, rather than loose coreclr filenames, are authoritative.
"""
import mmap
from pathlib import Path, PurePosixPath
import struct
import zlib

SIGNATURE = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")


class Bundle:
    def __init__(self, path, entries):
        self.path, self.entries = Path(path), entries

    def read(self, name):
        offset, size, compressed, _ = self.entries[name]
        if max(size, compressed) > 16 * 1024 * 1024:
            raise ValueError("embedded metadata exceeds 16 MiB")
        with self.path.open("rb") as source:
            source.seek(offset); data = source.read(compressed or size)
        if len(data) != (compressed or size):
            raise ValueError("truncated embedded file")
        if compressed:
            decoder = zlib.decompressobj(-15)
            try:
                data = decoder.decompress(data, size + 1)
            except zlib.error as error:
                raise ValueError("invalid compressed bundle entry") from error
            if len(data) != size or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
                raise ValueError("compressed entry length mismatch")
        return data


def _architecture(data, rid):
    if rid.startswith("win-"):
        pe = struct.unpack_from("<I", data, 0x3c)[0] if data[:2] == b"MZ" else 0
        return pe > 0 and pe + 6 <= len(data) and data[pe:pe + 4] == b"PE\0\0" and struct.unpack_from("<H", data, pe + 4)[0] == 0x8664 and rid == "win-x64"
    if rid.startswith("linux-"):
        machine = 62 if rid == "linux-x64" else 183 if rid == "linux-arm64" else -1
        return data[:6] == b"\x7fELF\x02\x01" and struct.unpack_from("<H", data, 18)[0] == machine
    if rid.startswith("osx-"):
        machine = 0x1000007 if rid == "osx-x64" else 0x100000c if rid == "osx-arm64" else -1
        return data[:4] == b"\xcf\xfa\xed\xfe" and struct.unpack_from("<I", data, 4)[0] == machine
    return False


def read_bundle(path, rid):
    path = Path(path)
    with path.open("rb") as source:
        if path.stat().st_size < 512:
            raise ValueError("missing single-file executable")
        with mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as data:
            if not _architecture(data, rid):
                raise ValueError("executable platform/architecture differs from RID")
            signature = data.find(SIGNATURE)
            if signature < 8:
                raise ValueError("missing .NET bundle signature")
            header = struct.unpack_from("<q", data, signature - 8)[0]
            if header < signature + len(SIGNATURE) or header >= len(data):
                raise ValueError("apphost stub has no valid bundle manifest")
            position = header

            def number(fmt):
                nonlocal position
                count = struct.calcsize(fmt)
                if position + count > len(data):
                    raise ValueError("truncated bundle manifest")
                result = struct.unpack_from(fmt, data, position); position += count
                return result

            def string():
                nonlocal position
                length = 0
                for shift in range(0, 35, 7):
                    value, = number("<B"); length |= (value & 127) << shift
                    if value < 128:
                        break
                else:
                    raise ValueError("invalid bundle string length")
                if length > 4096 or position + length > len(data):
                    raise ValueError("invalid bundle string bounds")
                result = data[position:position + length].decode("utf-8"); position += length
                return result

            major, minor, count = number("<IIi")
            if major != 6 or minor != 0 or not 0 < count <= 20000:
                raise ValueError("unsupported or invalid bundle manifest")
            if not string():
                raise ValueError("missing bundle identity")
            deps_offset, deps_size, config_offset, config_size, flags = number("<qqqqQ")
            if flags & ~1:
                raise ValueError("unsupported bundle flags")
            entries, folded, intervals = {}, set(), []
            for _ in range(count):
                offset, size, compressed, kind = number("<qqqB"); name = string()
                parts = PurePosixPath(name).parts
                if not name or "\\" in name or ":" in name or name.startswith("/") or any(ord(c) < 32 for c in name) or any(p in ("", ".", "..") for p in name.split("/")) or not parts:
                    raise ValueError("unsafe embedded file path")
                if name.casefold() in folded or kind > 5:
                    raise ValueError("duplicate or unknown embedded file")
                if offset < signature + len(SIGNATURE) or size < 0 or compressed < 0 or offset + (compressed or size) > header:
                    raise ValueError("embedded file exceeds bundle bounds")
                entries[name] = (offset, size, compressed, kind); folded.add(name.casefold())
                intervals.append((offset, offset + (compressed or size)))
            intervals.sort()
            if any(right[0] < left[1] for left, right in zip(intervals, intervals[1:])):
                raise ValueError("overlapping embedded files")
            for kind, offset, size in ((3, deps_offset, deps_size), (4, config_offset, config_size)):
                matches = [entry for entry in entries.values() if entry[3] == kind]
                if len(matches) != 1 or matches[0][:2] != (offset, size):
                    raise ValueError("bundle configuration location does not match manifest")
            return Bundle(path, entries)
