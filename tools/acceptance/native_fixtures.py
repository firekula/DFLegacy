"""Verified native producer snapshots for portable cross-language tests."""
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import re


@dataclass(frozen=True)
class NativeFixture:
    filename: str
    environment: str
    mode: str


FIXTURES = {
    'observer': NativeFixture('observer.json', 'DFLEGACY_TEST_CONTROL_OBSERVER_FIXTURE', '--client-test-control'),
    'frame-profile': NativeFixture('frame-profile.json', 'DFLEGACY_FRAME_PROFILE_FIXTURE', '--frame-profile'),
    'gameplay-trace': NativeFixture('gameplay-trace.json', 'DFLEGACY_GAMEPLAY_TRACE_FIXTURE', '--gameplay-trace'),
    'native-loop': NativeFixture('native-loop.json', 'DFLEGACY_NATIVE_LOOP_FIXTURE', '--native-loop'),
}
DEFAULT_ROOT = Path(__file__).resolve().parents[2] / 'tests/fixtures/native-debug'
MAX_BYTES = 1024 * 1024


def verify_bundle(directory):
    """Return the four fixed paths only after checking their exact export bytes."""
    root = Path(directory).resolve()
    manifest_path = root / 'manifest.json'
    if manifest_path.stat().st_size > MAX_BYTES:
        raise ValueError('native fixture manifest is too large')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    if manifest.get('schema') != 'dfl-native-test-fixtures/v1' or set(manifest.get('files', {})) != set(FIXTURES):
        raise ValueError('unexpected native fixture manifest layout')
    result = {}
    for name, spec in FIXTURES.items():
        entry = manifest['files'][name]
        if entry.get('path') != spec.filename or not re.fullmatch('[a-f0-9]{64}', entry.get('sha256', '')):
            raise ValueError('unexpected native fixture file identity: ' + name)
        path = root / spec.filename
        if path.is_symlink() or not path.is_file():
            raise FileNotFoundError('native fixture must be a regular file: ' + str(path))
        size = path.stat().st_size
        if not 1 <= size <= MAX_BYTES or size != entry.get('bytes'):
            raise ValueError('invalid native fixture size: ' + name)
        body = path.read_bytes()
        if hashlib.sha256(body).hexdigest() != entry['sha256']:
            raise ValueError('native fixture digest mismatch: ' + name)
        result[name] = path
    return result


def fixture_path(name):
    """Preserve explicit producer overrides; otherwise require a checked bundle."""
    spec = FIXTURES[name]
    explicit = os.environ.get(spec.environment)
    if explicit:
        path = Path(explicit).resolve(strict=True)
        if not path.is_file() or not 1 <= path.stat().st_size <= MAX_BYTES:
            raise ValueError('invalid explicit native fixture: ' + str(path))
        return path
    root = Path(os.environ.get('DFLEGACY_NATIVE_FIXTURE_ROOT', str(DEFAULT_ROOT)))
    return verify_bundle(root)[name]
