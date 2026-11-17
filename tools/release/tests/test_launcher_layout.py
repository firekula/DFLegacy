import base64
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile

SCRIPT = Path(__file__).resolve().parents[1] / 'launcher_layout.py'


def write_client(tmp_path, files=None, manifest_files=None, manifest_sha=None):
    client = tmp_path / 'client'
    (client / '.dflegacy' / 'v4').mkdir(parents=True)
    contents = files if files is not None else {'DNF.exe': b'game', 'DFLegacy.Chapter.json': b'{}'}
    entries = []
    for name, data in contents.items():
        (client / name).write_bytes(data)
        entries.append({'path': name, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest(), 'parts': []})
    manifest = {
        'schemaVersion': 4,
        'version': 'v9.9.9-test',
        'sequence': 1,
        'minimumSequence': 1,
        'architecture': 'x64',
        'flavor': 'normal',
        'channel': 'chapter-two',
        'files': entries if manifest_files is None else manifest_files,
        'retired': [],
        'migrations': [],
    }
    manifest_bytes = json.dumps(manifest).encode('utf-8')
    receipt = {
        'release': {
            'source': {'provider': 'test'},
            'tag': 'v9.9.9-test',
            'manifestSha256': hashlib.sha256(manifest_bytes).hexdigest() if manifest_sha is None else manifest_sha,
            'manifestBytes': base64.b64encode(manifest_bytes).decode('ascii'),
        },
        'managedHistory': list(contents),
    }
    (client / '.dflegacy' / 'v4' / 'installed.json').write_text(json.dumps(receipt), encoding='utf-8')
    stub = tmp_path / 'DFOLauncher.exe'
    stub.write_bytes(b'stub')
    return client, stub


def run(client, stub, output):
    return subprocess.run(
        [sys.executable, str(SCRIPT), '--client', str(client), '--stub', str(stub), '--output', str(output)],
        capture_output=True, text=True, timeout=120)


def test_packages_client_under_client_directory(tmp_path):
    client, stub = write_client(tmp_path)
    output = tmp_path / 'package.zip'
    result = run(client, stub, output)
    assert result.returncode == 0, result.stderr
    with zipfile.ZipFile(output) as archive:
        names = set(archive.namelist())
    assert 'DFOLauncher.exe' in names
    assert 'client/DNF.exe' in names
    assert 'client/DFLegacy.Chapter.json' in names
    assert 'client/.dflegacy/v4/installed.json' in names
    assert 'verified=2' in result.stdout
    assert 'zip_sha256=' in result.stdout


def test_rejects_corrupt_client_file(tmp_path):
    client, stub = write_client(tmp_path)
    (client / 'DNF.exe').write_bytes(b'changed')
    result = run(client, stub, tmp_path / 'package.zip')
    assert result.returncode != 0
    assert 'size mismatch' in result.stderr or 'SHA-256 mismatch' in result.stderr


def test_rejects_missing_client_file(tmp_path):
    client, stub = write_client(tmp_path)
    (client / 'DFLegacy.Chapter.json').unlink()
    result = run(client, stub, tmp_path / 'package.zip')
    assert result.returncode != 0
    assert 'missing' in result.stderr


def test_rejects_manifest_hash_mismatch(tmp_path):
    client, stub = write_client(tmp_path, manifest_sha='0' * 64)
    result = run(client, stub, tmp_path / 'package.zip')
    assert result.returncode != 0
    assert 'manifest SHA-256' in result.stderr


def test_rejects_manifest_path_traversal(tmp_path):
    client, stub = write_client(tmp_path)
    receipt_path = client / '.dflegacy' / 'v4' / 'installed.json'
    receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
    manifest = json.loads(base64.b64decode(receipt['release']['manifestBytes']))
    manifest['files'] = [{'path': '../escape.exe', 'size': 1, 'sha256': '0' * 64, 'parts': []}]
    manifest_bytes = json.dumps(manifest).encode('utf-8')
    receipt['release']['manifestBytes'] = base64.b64encode(manifest_bytes).decode('ascii')
    receipt['release']['manifestSha256'] = hashlib.sha256(manifest_bytes).hexdigest()
    receipt_path.write_text(json.dumps(receipt), encoding='utf-8')
    result = run(client, stub, tmp_path / 'package.zip')
    assert result.returncode != 0
    assert 'escapes' in result.stderr
