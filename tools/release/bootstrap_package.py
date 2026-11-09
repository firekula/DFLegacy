#!/usr/bin/env python3
"""Build Gitea bootstrap bundles/starters from explicitly pinned public build files.

No client-directory discovery, configuration copying, uploads or legacy package
policy changes. ``build_release`` takes input records with path/source/sha256/size,
stages the selected public files and a generated descriptor, and delegates V4
history/content-addressing to update_v4. ``create_starter`` accepts that source
and bundle and generates a compatible installed receipt and recovery copy.
The starter intentionally contains no launcher.json or launcher.txt; callers
handle any opt-in source/configuration migration separately.
"""
import base64
import copy
import hashlib
import os
from pathlib import Path
import shutil
import struct
import zipfile

import update_v4 as v4

ENTRY = 'DFLegacy.Launcher.exe'
LAUNCHER = 'DFLegacy.Launcher.App.exe'
UPDATER = 'DFLegacy.Updater.exe'
DESCRIPTOR = 'DFLegacy.Bootstrap.ini'
CLIENT = 'DFLegacy.Client.dll'
DIAGNOSTICS = 'DFLegacy.Diagnostics.exe'
CAPTURE = 'DFLegacy.CrashCapture.exe'
BASE_REQUIRED = {ENTRY, LAUNCHER, UPDATER, DESCRIPTOR, CLIENT}
REQUIRED = BASE_REQUIRED | {DIAGNOSTICS, CAPTURE}


def _source(architecture, flavor, provider, repository, channel, origin):
    allowed_source = (provider, channel, origin) in (
        ('gitea', 'gitea-test', v4.GITEA_ORIGIN),
        ('github', 'stable', 'https://github.com'),
    )
    v4.require(repository == 'firekula/DFLegacy' and allowed_source,
               'bootstrap packaging requires a pinned release source')
    v4.require(architecture in ('x86', 'x64') and flavor in ('normal', 'test'),
               'invalid bootstrap architecture or flavor')
    return dict(provider=provider, repository=repository, channel=channel,
                architecture=architecture, flavor=flavor)


def _public_path(path):
    v4.validate_path(path)
    v4.require(not v4.private_path(path), 'private/configuration/cache input is forbidden')


def _identity(path, expected):
    v4.no_links(path)
    v4.require(Path(path).is_file(), 'required public file is missing')
    v4.require(v4.file_hash(path) == expected, 'public file hash/size identity changed')


def _pe_architecture(path, architecture):
    v4.no_links(path)
    with Path(path).open('rb') as stream:
        size = os.fstat(stream.fileno()).st_size
        header = stream.read(64)
        v4.require(len(header) == 64 and header[:2] == b'MZ', 'invalid public PE header')
        offset = struct.unpack_from('<I', header, 60)[0]
        v4.require(64 <= offset <= size - 24, 'invalid public PE header offset')
        stream.seek(offset)
        coff = stream.read(24)
        v4.require(len(coff) == 24 and coff[:4] == b'PE\0\0', 'invalid public PE signature')
        v4.require(struct.unpack_from('<H', coff, 4)[0] == {'x86': 0x14c, 'x64': 0x8664}[architecture],
                   'public PE architecture differs: ' + Path(path).name)


def _descriptor(architecture, launcher_hash, updater_hash):
    v4.require(architecture in ('x86', 'x64') and v4.checksum(launcher_hash) and v4.checksum(updater_hash),
               'invalid bootstrap descriptor identity')
    return (f'[Bootstrap]\nSchemaVersion=1\nArchitecture={architecture}\n'
            f'LauncherSha256={launcher_hash}\nUpdaterSha256={updater_hash}\n').encode('ascii')


def _inputs(inputs, architecture):
    v4.require(isinstance(inputs, (list, tuple)) and inputs, 'explicit public input records are required')
    rows = {}; names = set()
    for row in inputs:
        v4.require(isinstance(row, dict) and set(row) == {'path', 'source', 'sha256', 'size'},
                   'public inputs require exactly path/source/sha256/size')
        relative = row['path']; _public_path(relative)
        v4.require(relative.casefold() not in names and relative.casefold() != DESCRIPTOR.casefold(),
                   'duplicate public input or reserved generated descriptor')
        v4.require(v4.checksum(row['sha256']) and v4.integer(row['size']), 'invalid public input hash/size')
        v4.require(isinstance(row['source'], (str, Path)), 'invalid public source path')
        source = Path(row['source']).absolute()
        # Do not allow explicitly selected secrets/cache files to be renamed into
        # apparently public resources. Unselected siblings are never traversed.
        v4.require(not v4.private_path('/'.join(source.parts[1:])), 'private/cache source is forbidden')
        _identity(source, (row['sha256'], row['size']))
        names.add(relative.casefold())
        rows[relative] = dict(source=source, sha256=row['sha256'], size=row['size'])
    v4.require(REQUIRED - {DESCRIPTOR} <= rows.keys(), 'missing fixed bootstrap layout')
    for name in names:
        parts = name.split('/')
        v4.require(not any('/'.join(parts[:i]) in names for i in range(1, len(parts))),
                   'public file/directory path collision')
    for name in (ENTRY, LAUNCHER, UPDATER, DIAGNOSTICS):
        _pe_architecture(rows[name]['source'], architecture)
    _pe_architecture(rows[CLIENT]['source'], 'x86')
    _pe_architecture(rows[CAPTURE]['source'], 'x86')
    if 'debug.dll' in rows:
        _pe_architecture(rows['debug.dll']['source'], 'x86')
    return rows


def _manifest(value):
    return v4.validate_manifest(v4.load_json(value) if isinstance(value, (str, Path)) else copy.deepcopy(value))


def _layout(manifest, historical=False):
    rows = {row['path']: row for row in manifest['files']}
    v4.require((BASE_REQUIRED if historical else REQUIRED) <= rows.keys(), 'manifest is missing the fixed bootstrap layout')
    for path in rows: _public_path(path)
    return rows


def assert_stable_entry(previous_manifest, current_manifest):
    """Require identical fixed-entry bytes for two bootstrap release manifests."""
    previous = _manifest(previous_manifest); current = _manifest(current_manifest)
    old = _layout(previous, historical=True); new = _layout(current)
    v4.require(all(previous[key] == current[key] for key in ('architecture', 'flavor', 'channel')),
               'fixed entry comparison requires matching variant/source')
    v4.require((old[ENTRY]['sha256'], old[ENTRY]['size']) == (new[ENTRY]['sha256'], new[ENTRY]['size']),
               'fixed bootstrap entry changed between versions')


def _verify_tree(root, manifest):
    root = Path(root).absolute(); v4.no_links(root)
    v4.require(root.is_dir(), 'bootstrap source root is missing')
    rows = _layout(manifest)
    actual = set()
    for directory, dirs, files in os.walk(root, followlinks=False):
        for name in dirs:
            child = Path(directory) / name; v4.no_links(child)
            _public_path(child.relative_to(root).as_posix())
        for name in files:
            path = Path(directory) / name; relative = path.relative_to(root).as_posix()
            _public_path(relative); v4.no_links(path)
            v4.require(relative in rows, 'unexpected file outside explicit public inventory')
            _identity(path, (rows[relative]['sha256'], rows[relative]['size']))
            actual.add(relative)
    v4.require(actual == set(rows), 'bootstrap source is missing managed files')
    for name in (ENTRY, LAUNCHER, UPDATER, DIAGNOSTICS): _pe_architecture(root / name, manifest['architecture'])
    _pe_architecture(root / CLIENT, 'x86')
    _pe_architecture(root / CAPTURE, 'x86')
    if 'debug.dll' in rows: _pe_architecture(root / 'debug.dll', 'x86')
    expected = _descriptor(manifest['architecture'], rows[LAUNCHER]['sha256'], rows[UPDATER]['sha256'])
    v4.require((root / DESCRIPTOR).read_bytes() == expected, 'bootstrap descriptor hash/architecture layout differs')
    return rows


def build_release(output, *, inputs, architecture, flavor, version, commit, sequence,
                  previous=None, reuse=None, migrations=None, retired=None, minimum_sequence=0,
                  provider='gitea', repository='firekula/DFLegacy', channel='gitea-test',
                  origin=v4.GITEA_ORIGIN):
    """Create ``output/source`` and ``output/bundle`` without touching built inputs.

    Each input is {path: destination, source: built file, sha256: expected, size:
    expected}. The caller pins these values when selecting public build outputs;
    a later mutation cannot silently change the package. Paths are exact, never
    globs. Existing outputs are refused. Returns the standard V4 build result
    plus absolute sourceRoot/bundleRoot paths (not added to the V4 receipt).
    """
    _source(architecture, flavor, provider, repository, channel, origin)
    rows = _inputs(inputs, architecture)
    histories = [_manifest(value) for value in previous or []]
    output = Path(output).absolute(); v4.no_links(output)
    v4.require(not output.exists(), 'bootstrap output already exists')
    output.mkdir(parents=True)
    root = output / 'source'; root.mkdir()
    for name, row in rows.items():
        target = root / name; target.parent.mkdir(parents=True, exist_ok=True)
        _identity(row['source'], (row['sha256'], row['size']))
        shutil.copyfile(row['source'], target)
        _identity(target, (row['sha256'], row['size']))
        _identity(row['source'], (row['sha256'], row['size']))
    (root / DESCRIPTOR).write_bytes(_descriptor(architecture, rows[LAUNCHER]['sha256'], rows[UPDATER]['sha256']))
    options = dict(root=root, version=version, commit=commit, architecture=architecture, flavor=flavor,
                   channel=channel, sequence=sequence, minimum_sequence=minimum_sequence,
                   includes=[name.replace('[', '[[]') for name in sorted([*rows, DESCRIPTOR])],
                   previous=histories, reuse=reuse,
                   retired=retired, migrations=migrations)
    # Validate history/layout/entry stability before creating attachment bytes.
    manifest = v4.build_manifest(**options)
    for old in histories:
        old_paths = {row['path'] for row in old['files']}
        if {LAUNCHER, UPDATER, DESCRIPTOR} & old_paths:
            assert_stable_entry(old, manifest)
    _verify_tree(root, manifest)
    bundle = output / 'bundle'
    result = v4.build(bundle, provider=provider, repository=repository, **options)
    v4.verify_local(bundle)
    v4.require(result['manifest'] == manifest, 'public inputs changed while building bundle')
    _verify_tree(root, manifest)
    for row in rows.values(): _identity(row['source'], (row['sha256'], row['size']))
    return result | dict(sourceRoot=str(root), bundleRoot=str(bundle))


def _write_zip_file(archive, source, name, expected):
    """Verify the actual bytes written, including the duplicated recovery copy."""
    v4.no_links(source)
    digest = hashlib.sha256(); size = 0
    with Path(source).open('rb') as stream, archive.open(name, 'w', force_zip64=True) as target:
        while chunk := stream.read(1024 * 1024):
            target.write(chunk); digest.update(chunk); size += len(chunk)
    v4.require((digest.hexdigest(), size) == expected, 'public input changed while creating starter')


def create_starter(output, *, root, bundle, architecture, flavor, provider='gitea',
                   repository='firekula/DFLegacy', channel='gitea-test', origin=v4.GITEA_ORIGIN):
    """Create a ZIP from a built V4 source and its matching verified bundle.

    Recovery and installed receipts are generated, never read from user state.
    The manifest is embedded as its exact base64 bytes in InstalledReceipt, as
    required by UpdateCore. No configuration is shipped or rewritten. Existing
    config migrations remain pending until the updater checks/applies them.
    """
    source = _source(architecture, flavor, provider, repository, channel, origin)
    bundle = Path(bundle).absolute(); root = Path(root).absolute()
    receipt = v4.verify_local(bundle); manifest = receipt['manifest']
    v4.require(receipt['provider'] == provider and receipt['repository'] == repository
               and all(manifest[key] == source[key] for key in ('channel', 'architecture', 'flavor')),
               'starter source/architecture/flavor differs from built bundle')
    rows = _verify_tree(root, manifest)
    raw = (bundle / receipt['manifestAsset']).read_bytes()
    installed = dict(release=dict(source=source, tag=manifest['version'],
                                 manifestSha256=receipt['manifestSha256'],
                                 manifestBytes=base64.b64encode(raw).decode('ascii')),
                     managedHistory=sorted(rows, key=str.casefold))
    output = Path(output).absolute(); v4.no_links(output)
    v4.require(output != root and root not in output.parents and output != bundle and bundle not in output.parents,
               'starter output must be outside public source and bundle')
    v4.require(not output.exists(), 'starter output already exists')
    output.parent.mkdir(parents=True, exist_ok=True)
    created = False
    try:
        with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            created = True
            for name, row in sorted(rows.items(), key=lambda pair: pair[0].casefold()):
                _write_zip_file(archive, root / name, name, (row['sha256'], row['size']))
            archive.writestr('.dflegacy/v4/installed.json', v4.json_bytes(installed))
            archive.writestr('.dflegacy/bootstrap/recovery.ini',
                             _descriptor(architecture, rows[LAUNCHER]['sha256'], rows[UPDATER]['sha256']))
            updater = rows[UPDATER]
            _write_zip_file(archive, root / UPDATER, '.dflegacy/bootstrap/' + updater['sha256'] + '.exe',
                            (updater['sha256'], updater['size']))
        _verify_tree(root, manifest)
        v4.verify_local(bundle)
    except Exception:
        if created: output.unlink()
        raise
    digest, size = v4.file_hash(output)
    return dict(path=str(output), sha256=digest, size=size, source=source,
                version=manifest['version'], manifestSha256=receipt['manifestSha256'])
