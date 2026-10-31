"""Portable ACT1 town scripts must be present, verified, and actually parsed."""
import importlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import unittest

import pytest


ROOT = Path(__file__).resolve().parents[3] / 'tests/fixtures/act1-town-navigation'


def fixture_api():
    return importlib.import_module('tools.acceptance.town_fixture')


def test_checked_town_fixture_runs_original_parser_without_game_install(monkeypatch):
    # Removing the packaged scripts must fail, even on a machine with no G: drive.
    assert (ROOT / 'catalog.pvf').is_file(), 'portable town PVF fixture is missing'
    monkeypatch.delenv('DFLEGACY_TOWN_ORIGINAL_PVF', raising=False)
    monkeypatch.delenv('DFLEGACY_TOWN_FIXTURE_ROOT', raising=False)
    fixture = fixture_api().load_fixture_catalog()
    assert fixture.source_pvf_sha256 == '288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f'
    assert fixture.catalog.pvf_sha256 != fixture.source_pvf_sha256
    assert fixture.catalog.pvf_sha256 == fixture.catalog_pvf_sha256
    assert fixture.catalog.area((1, 0)).map_path == 'map/elvengard/elvengard.map'
    assert fixture.catalog.area((1, 2)).kind == 'dungeon_gate'
    assert len(fixture.catalog._areas) == 29
    assert [(p.source, p.target) for p in fixture.catalog.route((1, 0), (3, 1))] == [
        ((1, 0), (2, 1)), ((2, 1), (2, 0)), ((2, 0), (3, 0)), ((3, 0), (3, 1))]


def test_both_existing_town_regressions_run_with_original_path_absent(monkeypatch):
    from tools.debug import test_town_navigation as regression
    monkeypatch.setattr(regression, 'ACTUAL_PVF', Path('Z:/absent-game/Script.pvf'))
    monkeypatch.delenv('DFLEGACY_TOWN_ORIGINAL_PVF', raising=False)
    monkeypatch.delenv('DFLEGACY_TOWN_FIXTURE_ROOT', raising=False)
    names = [
        'test_actual_pvf_tolerant_replanning_avoids_unselected_doors',
        'test_actual_pvf_sha_portal_points_and_multitown_forward_reverse_regression',
    ]
    result = unittest.TestResult()
    for name in names:
        regression.TownNavigationTests(name).run(result)
    assert result.testsRun == 2
    assert not result.skipped
    assert not result.errors, result.errors
    assert not result.failures, result.failures


@pytest.mark.parametrize('change', [
    'missing-manifest', 'missing-pvf', 'payload-bytes', 'derived-hash',
    'source-hash', 'unsafe-path', 'payload-hash', 'oversize-pvf',
    'oversize-manifest', 'unexpected-script',
])
def test_missing_or_tampered_town_fixture_fails(tmp_path, change):
    api = fixture_api()
    root = tmp_path / 'town'
    shutil.copytree(ROOT, root)
    manifest_path = root / 'manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    pvf = root / 'catalog.pvf'
    if change == 'missing-manifest':
        manifest_path.unlink()
    elif change == 'missing-pvf':
        pvf.unlink()
    elif change == 'payload-bytes':
        raw = bytearray(pvf.read_bytes())
        raw[-1] ^= 1
        pvf.write_bytes(raw)
    elif change == 'derived-hash':
        manifest['derived']['sha256'] = '0' * 64
    elif change == 'source-hash':
        manifest['source']['sha256'] = '0' * 64
    elif change == 'unsafe-path':
        manifest['derived']['path'] = '../outside.pvf'
    elif change == 'payload-hash':
        manifest['scripts']['town/town.lst']['sha256'] = '0' * 64
    elif change == 'oversize-pvf':
        pvf.write_bytes(b'x' * (256 * 1024 + 1))
    elif change == 'oversize-manifest':
        manifest_path.write_bytes(b' ' * (256 * 1024 + 1))
    elif change == 'unexpected-script':
        manifest['scripts']['../outside.map'] = manifest['scripts']['town/town.lst']
    if change not in ('missing-manifest', 'oversize-manifest'):
        manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
    with pytest.raises((api.FixtureError, FileNotFoundError)):
        api.load_fixture_catalog(root)


def test_explicit_fixture_root_is_verified_and_missing_root_fails(tmp_path, monkeypatch):
    api = fixture_api()
    monkeypatch.delenv('DFLEGACY_TOWN_ORIGINAL_PVF', raising=False)
    root = tmp_path / 'town'
    shutil.copytree(ROOT, root)
    monkeypatch.setenv('DFLEGACY_TOWN_FIXTURE_ROOT', str(root))
    assert api.load_fixture_catalog().catalog.area((1, 0)).portals
    monkeypatch.setenv('DFLEGACY_TOWN_FIXTURE_ROOT', str(tmp_path / 'absent'))
    with pytest.raises((api.FixtureError, FileNotFoundError)):
        api.load_fixture_catalog()


def test_explicit_original_pvf_override_requires_exact_original_sha(tmp_path, monkeypatch):
    api = fixture_api()
    wrong = tmp_path / 'Script.pvf'
    wrong.write_bytes((ROOT / 'catalog.pvf').read_bytes())
    monkeypatch.setenv('DFLEGACY_TOWN_ORIGINAL_PVF', str(wrong))
    with pytest.raises(api.FixtureError, match='original PVF SHA256'):
        api.load_fixture_catalog()
    monkeypatch.setenv('DFLEGACY_TOWN_ORIGINAL_PVF', str(tmp_path / 'absent.pvf'))
    with pytest.raises((api.FixtureError, FileNotFoundError)):
        api.load_fixture_catalog()


def test_generator_rejects_wrong_source_before_writing(tmp_path):
    api = fixture_api()
    source = tmp_path / 'Script.pvf'
    source.write_bytes(b'not the original game PVF')
    destination = tmp_path / 'generated'
    with pytest.raises(api.FixtureError, match='original PVF SHA256'):
        api.generate_fixture(source, destination)
    assert not destination.exists()
    assert sorted(path.name for path in tmp_path.iterdir()) == ['Script.pvf']


@pytest.mark.parametrize('populated', [False, True])
def test_generator_rejects_existing_destination_before_reading_source(tmp_path, populated):
    api = fixture_api()
    destination = tmp_path / 'existing'
    if populated:
        shutil.copytree(ROOT, destination)
    else:
        destination.mkdir()
    before = {path.name: path.read_bytes() for path in destination.iterdir()}
    with pytest.raises(api.FixtureError, match='fresh'):
        api.generate_fixture(tmp_path / 'source-must-not-be-read.pvf', destination)
    assert {path.name: path.read_bytes() for path in destination.iterdir()} == before


def test_generator_cli_requires_explicit_fresh_output(tmp_path):
    result = subprocess.run(
        [sys.executable, '-m', 'tools.acceptance.town_fixture',
         '--original-pvf', str(tmp_path / 'absent.pvf')],
        cwd=ROOT.parents[2], capture_output=True, text=True, check=False)
    assert result.returncode == 2
    assert '--output' in result.stderr


def portable_generator_source(api, tmp_path, monkeypatch):
    # Exercise the real pack/parser pipeline without distributing the original
    # 66 MB source: explicitly trust this test's existing bounded input only.
    source = tmp_path / 'source.pvf'
    source.write_bytes((ROOT / 'catalog.pvf').read_bytes())
    monkeypatch.setattr(api, 'SOURCE_SHA256', api.DERIVED_SHA256)
    monkeypatch.setattr(api, 'SOURCE_BYTES', source.stat().st_size)
    return source


def test_generator_stages_complete_bundle_then_publishes(tmp_path, monkeypatch):
    api = fixture_api()
    source = portable_generator_source(api, tmp_path, monkeypatch)
    destination = tmp_path / 'generated'
    api.generate_fixture(source, destination)
    assert (destination / 'catalog.pvf').read_bytes() == source.read_bytes()
    assert len(api.load_fixture_catalog(destination).catalog._areas) == 29
    assert sorted(path.name for path in tmp_path.iterdir()) == ['generated', 'source.pvf']


def test_generator_write_failure_leaves_no_partial_destination(tmp_path, monkeypatch):
    api = fixture_api()
    source = portable_generator_source(api, tmp_path, monkeypatch)
    original_bytes = source.read_bytes()
    write_bytes = Path.write_bytes

    def fail_second_write(path, body):
        if path.name == 'catalog.pvf':
            raise OSError('injected fixture write failure')
        return write_bytes(path, body)

    monkeypatch.setattr(Path, 'write_bytes', fail_second_write)
    destination = tmp_path / 'generated'
    with pytest.raises(OSError, match='injected fixture write failure'):
        api.generate_fixture(source, destination)
    assert not destination.exists()
    assert source.read_bytes() == original_bytes
    assert sorted(path.name for path in tmp_path.iterdir()) == ['source.pvf']
