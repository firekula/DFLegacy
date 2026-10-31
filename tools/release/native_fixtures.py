#!/usr/bin/env python3
"""Generate exact native JSON exports and their source/binary provenance."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))
from tools.acceptance.native_fixtures import FIXTURES, verify_bundle
from tools.acceptance.motion_replay_fixture import load_fixture as load_motion_fixture


def sha256(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def find_cmake(cache, explicit=None):
    cached = next((line.split('=', 1)[1] for line in cache.splitlines() if line.startswith('CMAKE_COMMAND:INTERNAL=')), '')
    executable = explicit or os.environ.get('CMAKE_EXE') or cached or shutil.which('cmake')
    if not executable:
        raise ValueError('CMake is absent from the build cache; set CMAKE_EXE or provide --cmake')
    return executable


def generate(build_directory, output, cmake):
    if os.name != 'nt':
        raise ValueError('native fixture generation requires the Windows native toolchain')
    build = Path(build_directory).resolve()
    destination = Path(output).resolve()
    if destination.exists():
        raise FileExistsError('fixture output already exists: ' + str(destination))
    cache = (build / 'CMakeCache.txt').read_text(encoding='utf-8')
    source = next((line.split('=', 1)[1] for line in cache.splitlines() if line.startswith('CMAKE_HOME_DIRECTORY:INTERNAL=')), '')
    if not source or Path(source).resolve() != REPO / 'src/DFLegacy.Ijl15':
        raise ValueError('CMake build directory belongs to another checkout')
    executable = find_cmake(cache, cmake)
    subprocess.run([str(executable), '--build', str(build), '--config', 'Release', '--target', 'df_debug_tests', 'ijl15_tests'], cwd=REPO, check=True)
    native = build / 'Release'
    producer = native / 'df_debug_tests.exe'
    binary_hashes = {name: sha256(native / name) for name in ['df_debug_tests.exe', 'debug.dll', 'ijl15_tests.exe', 'ijl15.dll']}
    inputs = {}
    for folder in ['src/DFLegacy.Debug', 'src/DFLegacy.Ijl15']:
        for path in sorted((REPO / folder).rglob('*')):
            if path.is_file() and (path.suffix in {'.cpp', '.h', '.inc', '.def'} or path.name == 'CMakeLists.txt'):
                inputs[path.relative_to(REPO).as_posix()] = sha256(path)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip()
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='native-fixture-stage-', dir=destination.parent) as temporary:
        stage = Path(temporary) / 'bundle'
        stage.mkdir()
        installed_patch = Path(temporary) / 'installed-patch.bin'
        patch_environment = dict(os.environ, DFLEGACY_MOTION_HORIZON_PATCH=str(installed_patch))
        for other in FIXTURES.values():
            patch_environment.pop(other.environment, None)
        subprocess.run([str(native / 'ijl15_tests.exe'), '--motion-horizon-only'], cwd=native, env=patch_environment, timeout=60, check=True)
        if installed_patch.read_bytes() != load_motion_fixture().patch:
            raise ValueError('current native installed patch differs from the bounded replay fixture')
        files = {}
        for name, spec in FIXTURES.items():
            target = stage / spec.filename
            environment = dict(os.environ)
            for other in FIXTURES.values():
                environment.pop(other.environment, None)
            environment[spec.environment] = str(target)
            subprocess.run([str(producer), spec.mode], cwd=native, env=environment, timeout=60, check=True)
            files[name] = {'path': spec.filename, 'sha256': sha256(target), 'bytes': target.stat().st_size,
                           'mode': spec.mode, 'environment': spec.environment}
        manifest = {'schema': 'dfl-native-test-fixtures/v1', 'source_commit': commit, 'producer_binaries': binary_hashes,
                    'source_inputs': inputs, 'scope': 'Native test model inputs and real JSON exporters; no live game observations',
                    'verified_motion_patch': {'sha256': sha256(installed_patch), 'bytes': 3, 'mode': '--motion-horizon-only'}, 'files': files}
        (stage / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        verify_bundle(stage)
        stage.rename(destination)
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    child = commands.add_parser('generate')
    child.add_argument('--build-dir', required=True, type=Path)
    child.add_argument('--output', required=True, type=Path)
    child.add_argument('--cmake')
    child = commands.add_parser('verify')
    child.add_argument('directory', type=Path)
    args = parser.parse_args()
    try:
        if args.command == 'generate':
            print(generate(args.build_dir, args.output, args.cmake))
        else:
            print(json.dumps({name: str(path) for name, path in verify_bundle(args.directory).items()}))
        return 0
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        parser.exit(2, str(error) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
