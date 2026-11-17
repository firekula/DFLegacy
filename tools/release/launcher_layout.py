#!/usr/bin/env python3
"""Package a client installation into the DFOLauncher layout.

The output archive contains the single visible launcher at its root and the
whole client installation under ``client/``.  The installation receipt's
embedded release manifest is the authority for the client files: every managed
file is checked for size and SHA-256 before the archive is written.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path, PurePosixPath
import sys
import zipfile

RECEIPT_PATH = Path('.dflegacy') / 'v4' / 'installed.json'
CHUNK = 1024 * 1024


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(CHUNK), b''):
            digest.update(chunk)
    return digest.hexdigest()


def load_manifest(client_dir):
    receipt_path = client_dir / RECEIPT_PATH
    if not receipt_path.is_file():
        raise ValueError(f'missing installation receipt: {receipt_path}')
    receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
    release = receipt.get('release')
    if not isinstance(release, dict):
        raise ValueError('installation receipt has no release record')
    encoded = release.get('manifestBytes')
    recorded = release.get('manifestSha256')
    if not isinstance(encoded, str) or not isinstance(recorded, str):
        raise ValueError('installation receipt has no embedded manifest')
    manifest_bytes = base64.b64decode(encoded)
    actual = sha256_file_bytes(manifest_bytes)
    if actual.lower() != recorded.lower():
        raise ValueError('embedded manifest SHA-256 does not match the receipt')
    manifest = json.loads(manifest_bytes)
    if not isinstance(manifest, dict) or not isinstance(manifest.get('files'), list):
        raise ValueError('embedded manifest has no file list')
    return manifest


def sha256_file_bytes(data):
    return hashlib.sha256(data).hexdigest()


def verify_files(client_dir, manifest):
    verified = 0
    for entry in manifest['files']:
        if not isinstance(entry, dict):
            raise ValueError('manifest file entry is not an object')
        raw = entry.get('path')
        size = entry.get('size')
        digest = entry.get('sha256')
        if not isinstance(raw, str) or not isinstance(size, int) or not isinstance(digest, str):
            raise ValueError('manifest file entry has invalid fields')
        relative = PurePosixPath(raw)
        if relative.is_absolute() or not relative.parts or any(part in ('..', '') for part in relative.parts):
            raise ValueError(f'manifest path escapes the client directory: {raw}')
        target = client_dir.joinpath(*relative.parts)
        if not target.is_file():
            raise ValueError(f'managed file is missing: {raw}')
        actual_size = target.stat().st_size
        if actual_size != size:
            raise ValueError(f'size mismatch for {raw}: expected {size}, got {actual_size}')
        actual_digest = sha256_file(target)
        if actual_digest.lower() != digest.lower():
            raise ValueError(f'SHA-256 mismatch for {raw}')
        verified += 1
    return verified


def package(client_dir, stub, output):
    client_dir = client_dir.resolve()
    stub = stub.resolve()
    output = output.resolve()
    if not client_dir.is_dir():
        raise ValueError(f'client directory does not exist: {client_dir}')
    if not stub.is_file():
        raise ValueError(f'launcher stub does not exist: {stub}')
    if output == client_dir or client_dir in output.parents:
        raise ValueError('output archive must be outside the client directory')
    manifest = load_manifest(client_dir)
    verified = verify_files(client_dir, manifest)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        archive.write(stub, 'DFOLauncher.exe')
        for path in sorted(client_dir.rglob('*')):
            if path.is_file():
                archive.write(path, 'client/' + path.relative_to(client_dir).as_posix())
    print(f"version={manifest.get('version')} sequence={manifest.get('sequence')} verified={verified}")
    print(f'stub_sha256={sha256_file(stub)}')
    print(f'zip_sha256={sha256_file(output)}')
    return output


def main(argv=None):
    parser = argparse.ArgumentParser(description='Package a client into the DFOLauncher layout')
    parser.add_argument('--client', required=True, help='existing client installation directory')
    parser.add_argument('--stub', required=True, help='DFOLauncher.exe')
    parser.add_argument('--output', required=True, help='output zip path')
    args = parser.parse_args(argv)
    try:
        package(Path(args.client), Path(args.stub), Path(args.output))
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(f'error: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
