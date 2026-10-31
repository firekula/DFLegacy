"""Verified, bounded original town scripts in a portable derived PVF.

This test utility never modifies a source PVF or a game installation. The
production parser reads both the derived archive and an explicit original
override; provenance is separate from the archive's actual SHA256.
"""
from dataclasses import dataclass
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import tempfile
import zlib

from tools.debug import town_navigation as navigation


SOURCE_SHA256 = '288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f'
SOURCE_BYTES = 65951563
DERIVED_SHA256 = 'd2f0a13f4d702bcfcca38dde080e197134252029f31c5a4df50ec86851cfd01e'
DEFAULT_ROOT = Path(__file__).resolve().parents[2] / 'tests/fixtures/act1-town-navigation'
MAX_BYTES = 256 * 1024
MAX_MANIFEST_BYTES = 32 * 1024
SCRIPT_COUNT = 35


class FixtureError(ValueError):
    """Missing, oversized, or mismatched evidence is a test failure, never a skip."""


@dataclass(frozen=True)
class FixtureCatalog:
    catalog: navigation.Catalog
    source_pvf_sha256: str
    catalog_pvf_sha256: str


def _sha256(raw):
    return hashlib.sha256(raw).hexdigest()


def _read_bounded(path, limit, *, minimum=1):
    if path.is_symlink() or not path.is_file():
        raise FileNotFoundError(f'fixture must be a regular file: {path}')
    if not minimum <= path.stat().st_size <= limit:
        raise FixtureError(f'fixture size outside bounds: {path.name}')
    with path.open('rb') as stream:
        raw = stream.read(limit + 1)
    if not minimum <= len(raw) <= limit:
        raise FixtureError(f'fixture read outside bounds: {path.name}')
    return raw


def _payload(archive, path):
    offset, size, key = archive.entries[path]
    start = archive.body + offset
    return navigation._decode(archive.raw[start:start + ((size + 3) & ~3)], key)[:size]


def _original_archive(path):
    path = Path(path)
    # Hash before parsing and keep the source read-only. An arbitrary small
    # file also gets a clear identity error rather than a parser diagnostic.
    raw = _read_bounded(path, 256 * 1024 * 1024)
    if _sha256(raw) != SOURCE_SHA256 or len(raw) != SOURCE_BYTES:
        raise FixtureError('original PVF SHA256 does not match the verified ACT1 source')
    archive = navigation._Pvf(path)
    if archive.sha256 != SOURCE_SHA256:
        raise FixtureError('original PVF SHA256 changed while reading')
    return archive


def _contained_file(root, name):
    path = root / name
    if path.is_symlink() or path.resolve().parent != root:
        raise FixtureError(f'fixture path escapes bundle: {name}')
    return path


def load_fixture_catalog(directory=None, *, original_pvf=None):
    """Use checked scripts by default; an explicit original must match exactly.

    DFLEGACY_TOWN_ORIGINAL_PVF requests the original-file integration path.
    DFLEGACY_TOWN_FIXTURE_ROOT selects an alternate verified bundle. Neither
    option is inferred from game installation paths or file availability.
    """
    original = original_pvf or os.environ.get('DFLEGACY_TOWN_ORIGINAL_PVF')
    if original:
        archive = _original_archive(original)
        catalog = navigation.load_catalog(original)
        if catalog.pvf_sha256 != archive.sha256:
            raise FixtureError('original PVF SHA256 changed during catalog parsing')
        return FixtureCatalog(catalog, SOURCE_SHA256, catalog.pvf_sha256)
    root = Path(directory or os.environ.get('DFLEGACY_TOWN_FIXTURE_ROOT', DEFAULT_ROOT)).resolve()
    manifest_raw = _read_bounded(_contained_file(root, 'manifest.json'), MAX_MANIFEST_BYTES)
    try:
        manifest = json.loads(manifest_raw)
    except (UnicodeError, ValueError) as error:
        raise FixtureError('invalid town fixture manifest JSON') from error
    if (not isinstance(manifest, dict)
            or manifest.get('schema') != 'dfl-act1-town-navigation/v1'
            or manifest.get('source') != {'name': 'Script.pvf', 'bytes': SOURCE_BYTES,
                                          'sha256': SOURCE_SHA256}):
        raise FixtureError('unexpected town fixture source identity or schema')
    derived, scripts = manifest.get('derived'), manifest.get('scripts')
    if (not isinstance(derived, dict) or derived.get('path') != 'catalog.pvf'
            or derived.get('sha256') != DERIVED_SHA256
            or type(derived.get('bytes')) is not int
            or not 27 <= derived['bytes'] <= MAX_BYTES
            or not isinstance(scripts, dict) or len(scripts) != SCRIPT_COUNT):
        raise FixtureError('unexpected derived PVF identity or script count')
    path = _contained_file(root, 'catalog.pvf')
    raw = _read_bounded(path, MAX_BYTES, minimum=27)
    if (len(raw) != derived['bytes'] or len(raw) + len(manifest_raw) > MAX_BYTES
            or _sha256(raw) != DERIVED_SHA256):
        raise FixtureError('derived town PVF size or SHA256 mismatch')
    archive = navigation._Pvf(path)
    if archive.sha256 != DERIVED_SHA256 or set(archive.entries) != set(scripts):
        raise FixtureError('derived PVF changed or script paths differ from manifest')
    for virtual_path, entry in scripts.items():
        # These are archive paths, never filesystem extraction destinations.
        if (not isinstance(entry, dict) or navigation._path(virtual_path) != virtual_path
                or type(entry.get('bytes')) is not int
                or not 1 <= entry['bytes'] <= MAX_BYTES
                or not re.fullmatch('[a-f0-9]{64}', str(entry.get('sha256', '')))
                or type(entry.get('source_offset')) is not int
                or not 27 <= entry['source_offset'] <= SOURCE_BYTES - entry['bytes']):
            raise FixtureError(f'invalid script provenance: {virtual_path}')
        payload = _payload(archive, virtual_path)
        if len(payload) != entry['bytes'] or _sha256(payload) != entry['sha256']:
            raise FixtureError(f'original script payload SHA256 mismatch: {virtual_path}')
    catalog = navigation.load_catalog(path)
    if catalog.pvf_sha256 != DERIVED_SHA256:
        raise FixtureError('derived town PVF changed during catalog parsing')
    return FixtureCatalog(catalog, SOURCE_SHA256, DERIVED_SHA256)


def _encrypt(raw, key):
    raw += b'\0' * (-len(raw) % 4)
    return b''.join(struct.pack('<I', (((word << 6) | (word >> 26)) & 0xFFFFFFFF) ^ key)
                    for (word,) in struct.iter_unpack('<I', raw))


def _pack(entries):
    tree, body = bytearray(), bytearray()
    for number, (path, payload) in enumerate(entries):
        name, key = path.encode('cp949'), zlib.crc32(payload)
        tree.extend(struct.pack('<IIiIi', number, len(body), len(payload), key, len(name)))
        tree.extend(name)
        body.extend(_encrypt(payload, key))
    key = 0x1234A5B6
    encrypted = _encrypt(bytes(tree), key)
    return b'DNF_SCRIPT_PACK' + struct.pack('<iII', len(encrypted), key, len(entries)) + encrypted + body


def generate_fixture(original_pvf, destination):
    """Extract only exact town-list/town/map payloads from the pinned source.

    This is an explicit offline regeneration command, never invoked by tests.
    Paths are a fixed verified virtual set; no archive entry is extracted to
    the filesystem. The output must be fresh; a complete staged bundle is
    renamed into place only after both files have been written.
    """
    requested = Path(destination)
    root = requested.resolve()
    if requested.is_symlink() or root.exists():
        raise FixtureError('town fixture output must be a fresh destination')
    archive = _original_archive(original_pvf)
    catalog = navigation.load_catalog(original_pvf)
    if catalog.pvf_sha256 != SOURCE_SHA256:
        raise FixtureError('original PVF SHA256 changed during extraction')
    town_paths = {'town/' + match for match in re.findall(
        r'`([^`]+)`', archive.text('town/town.lst'))}
    paths = sorted({'town/town.lst'} | {navigation._path(p) for p in town_paths}
                   | {area.map_path for area in catalog._areas.values()})
    if len(paths) != SCRIPT_COUNT:
        raise FixtureError('unexpected original town script set')
    entries = [(path, _payload(archive, path)) for path in paths]
    raw = _pack(entries)
    if _sha256(raw) != DERIVED_SHA256 or len(raw) > MAX_BYTES:
        raise FixtureError('regenerated PVF differs from pinned original script payloads')
    manifest = {
        'schema': 'dfl-act1-town-navigation/v1',
        'source': {'name': 'Script.pvf', 'bytes': SOURCE_BYTES, 'sha256': SOURCE_SHA256},
        'derived': {'path': 'catalog.pvf', 'bytes': len(raw), 'sha256': _sha256(raw)},
        'scripts': {path: {'bytes': len(payload), 'sha256': _sha256(payload),
                           'source_offset': archive.body + archive.entries[path][0]}
                    for path, payload in entries},
    }
    manifest_raw = (json.dumps(manifest, indent=2, sort_keys=True) + '\n').encode('utf-8')
    if len(manifest_raw) > MAX_MANIFEST_BYTES or len(manifest_raw) + len(raw) > MAX_BYTES:
        raise FixtureError('regenerated town fixture exceeds bundle size limit')
    root.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='town-fixture-stage-', dir=root.parent) as temporary:
        stage = Path(temporary) / 'bundle'
        stage.mkdir()
        (stage / 'manifest.json').write_bytes(manifest_raw)
        (stage / 'catalog.pvf').write_bytes(raw)
        if root.exists() or root.is_symlink():
            raise FixtureError('town fixture output must remain a fresh destination')
        stage.rename(root)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original-pvf', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    manifest = generate_fixture(args.original_pvf, args.output)
    print(json.dumps(manifest['derived'], sort_keys=True))


if __name__ == '__main__':
    main()
