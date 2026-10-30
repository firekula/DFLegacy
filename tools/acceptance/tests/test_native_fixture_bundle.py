"""Use real producer exports, and reject stale or malformed fixture files."""
import json
import shutil

import pytest

from tools.acceptance.native_fixtures import FIXTURES, DEFAULT_ROOT, fixture_path, verify_bundle


def test_all_checked_native_exports_are_present_and_verified():
    result = verify_bundle(DEFAULT_ROOT)
    assert set(result) == set(FIXTURES)
    for name, path in result.items():
        value = json.loads(path.read_bytes())
        assert value['schema'].startswith('dfl-')
        assert fixture_path(name).is_file()


def test_explicit_native_export_override_is_preserved(tmp_path, monkeypatch):
    path = tmp_path / 'actual-native-output.json'
    path.write_text('{"producer":"explicit"}', encoding='utf-8')
    monkeypatch.setenv('DFLEGACY_FRAME_PROFILE_FIXTURE', str(path))
    assert fixture_path('frame-profile') == path


def test_missing_explicit_export_fails_instead_of_skipping(tmp_path, monkeypatch):
    monkeypatch.setenv('DFLEGACY_FRAME_PROFILE_FIXTURE', str(tmp_path / 'missing'))
    with pytest.raises((FileNotFoundError, ValueError)):
        fixture_path('frame-profile')


def test_generation_reuses_visual_studio_cmake_from_verified_cache(monkeypatch):
    from tools.release import native_fixtures as generator
    monkeypatch.delenv('CMAKE_EXE', raising=False)
    monkeypatch.setattr(generator.shutil, 'which', lambda name: None)
    cache = 'CMAKE_COMMAND:INTERNAL=C:/Program Files/VS/CMake/bin/cmake.exe\n'
    assert generator.find_cmake(cache) == 'C:/Program Files/VS/CMake/bin/cmake.exe'


@pytest.mark.parametrize('change', ['missing', 'bytes', 'path', 'hash', 'oversize'])
def test_checked_bundle_rejects_missing_and_tampered_files(tmp_path, change):
    root = tmp_path / 'bundle'
    shutil.copytree(DEFAULT_ROOT, root)
    manifest = root / 'manifest.json'
    data = json.loads(manifest.read_text(encoding='utf-8'))
    frame = root / 'frame-profile.json'
    if change == 'missing':
        frame.unlink()
    elif change == 'bytes':
        frame.write_bytes(frame.read_bytes() + b'\n')
    elif change == 'path':
        data['files']['frame-profile']['path'] = '../private.json'
    elif change == 'hash':
        data['files']['frame-profile']['sha256'] = '0' * 64
    else:
        frame.write_bytes(b'x' * (1024 * 1024 + 1))
    manifest.write_text(json.dumps(data), encoding='utf-8')
    with pytest.raises((ValueError, FileNotFoundError)):
        verify_bundle(root)
