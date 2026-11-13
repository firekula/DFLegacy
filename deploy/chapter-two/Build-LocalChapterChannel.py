"""Build a loopback-only full client channel. Never uploads or reads account files."""
import argparse
import importlib.util
import json
import hashlib
from pathlib import Path
import tempfile

MODULE = Path(__file__).resolve().parents[2] / 'tools/release/update_v4.py'
spec = importlib.util.spec_from_file_location('update_v4', MODULE)
v4 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v4)

# Explicit runtime resources only. Logs, credentials and generated test hosts are absent.
COMMON = ['DNF.exe', 'Script.pvf', 'audio.xml', 'DFighter.ini', 'Tenparty.dat',
          'BugTrap.dll', 'dbghelp.dll', 'DumpReport.dll', 'fmod.dll', 'ijl15.dll',
          'msvcp71.dll', 'msvcr71.dll', 'RS.dll', 'TerSafe.dll',
          'ImagePacks2/*', 'Music/*', 'SoundPacks/*', 'Plugin/*',
          'DFLegacy.Launcher.exe', 'DFLegacy.Launcher.App.exe', 'DFLegacy.Updater.exe',
          'DFLegacy.Bootstrap.ini', 'DFLegacy.Chapter.json', 'DFLegacy.Diagnostics.exe',
          'DFLegacy.CrashCapture.exe', 'Assets/Skin/fwtk.html']
FIRST = ['DFLegacy.Client.dll', 'DFLegacy.Debug.dll', 'debug.dll', 'ijl15.original.dll']
SECOND = ['TenFactTS.dll', 'TenioTS.ini', 'TenSLU.dll', 'TenSLX.dat', 'TenSLX.dll',
          'TenSM.dll', 'TenTC.dll', 'TesNet.dll', 'TC0001.DAT', 'localpackage.lst']

DESCRIPTOR = 'DFLegacy.Chapter.json'
ENTRY = 'DFLegacy.Launcher.exe'
CHANNELS = {'chapter1': 'c1-bridge', 'chapter2': 'c2-local'}
FIXED_COMPONENTS = {'DFLegacy.Launcher.App.exe', 'DFLegacy.Updater.exe', 'DFLegacy.Bootstrap.ini'}


def load_input(path, limit=v4.MAX_METADATA):
    path = Path(path).absolute()
    v4.no_links(path)
    v4.require(path.is_file() and 0 < path.stat().st_size <= limit, 'invalid or oversized channel metadata')
    return v4.load_json(path)


def required_files(root, chapter):
    descriptor = load_input(root / DESCRIPTOR, 64 * 1024)
    v4.require(isinstance(descriptor, dict) and set(descriptor) == {'schema', 'chapter', 'entry', 'requiredFiles'}
               and type(descriptor['schema']) is int and descriptor['schema'] == 1 and descriptor['chapter'] == chapter,
               'Chapter identity differs or has invalid fields.')
    required = descriptor['requiredFiles']
    v4.require(descriptor['entry'] == ENTRY and isinstance(required, list) and 0 < len(required) <= 128,
               'invalid chapter entry or required files')
    folded = set()
    for name in required:
        v4.validate_path(name)
        v4.require(name.casefold() not in folded, 'duplicate chapter required file')
        folded.add(name.casefold())
    mandatory = {ENTRY, 'Script.pvf', 'DFLegacy.Client.dll' if chapter == 'chapter1' else 'DNF.exe'}
    v4.require(mandatory <= set(required), 'chapter descriptor omits required startup files')
    return {*required, 'DNF.exe', DESCRIPTOR}


def build(root, output, chapter, version, commit, previous=None, sequence=1, minimum_sequence=0, migrations=None):
    root, output = Path(root).absolute(), Path(output).absolute()
    v4.no_links(root); v4.no_links(output)
    root, output = root.resolve(), output.resolve()
    v4.require(root.is_dir() and root != output and root not in output.parents, 'channel output must be outside the input stage')
    v4.require(not output.exists(), 'Channel output already exists; use a new directory.')
    v4.require(chapter in CHANNELS and v4.name(version) and v4.checksum(commit, 40), 'invalid chapter channel identity')
    v4.validate_path(version)
    v4.require(v4.integer(sequence, 1) and v4.integer(minimum_sequence, 0, sequence), 'invalid release sequence')
    channel = CHANNELS[chapter]
    required = required_files(root, chapter)
    if chapter == 'chapter2' and any(path.name.casefold() in ('dflegacy.client.dll','dflegacy.debug.dll','debug.dll')
                                    for path in root.iterdir()):
        raise ValueError('Chapter Two stage contains a Chapter One module.')
    old = None
    if previous:
        old = v4.validate_manifest(load_input(previous))
        v4.require(old['architecture'] == 'x64' and old['flavor'] == 'normal'
                   and (old['channel'] == channel or chapter == 'chapter2' and old['channel'] == CHANNELS['chapter1']),
                   'previous manifest does not belong to a compatible chapter channel')
        if old['channel'] == channel:
            v4.require(sequence > old['sequence'] and minimum_sequence >= old['minimumSequence'],
                       'same-chapter sequence must increase without lowering the minimum sequence')
            v4.require(version != old['version'], 'same-chapter version must be new')
    changes = load_input(migrations) if migrations else []
    v4.require(isinstance(changes, list) and (chapter == 'chapter1' or not changes),
               'Chapter Two cannot carry a configuration migration')
    migrations_by_id = {}
    for migration in ([*old['migrations'], *changes] if old and old['channel'] == channel else changes):
        v4.migration_valid(migration)
        v4.require(migration['id'] not in migrations_by_id or migration == migrations_by_id[migration['id']],
                   'a historical migration identity cannot change')
        if migration['operation'] == 'bindLocalBridge':
            source = migration['value']
            v4.require((source['channel'], source['architecture'], source['flavor']) == (channel, 'x64', 'normal'),
                       'bridge migration source does not match the channel')
        migrations_by_id[migration['id']] = migration
    v4.require(sum(m['operation'] == 'bindLocalBridge' for m in migrations_by_id.values()) <= 1,
               'channel has more than one bridge migration')

    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.dflegacy-channel-', dir=output.parent) as temporary:
        prepared = Path(temporary) / 'channel'
        resources = prepared / 'releases' / version
        resources.mkdir(parents=True)
        selected = COMMON + (FIRST if chapter == 'chapter1' else SECOND)
        planned = v4.plan_resources(root=str(root), version=version, commit=commit, architecture='x64',
            flavor='normal', channel=channel, sequence=sequence, minimum_sequence=minimum_sequence,
            includes=selected, _asset_output=resources, migrations=list(migrations_by_id.values()))
        manifest = planned['manifest']
        files = {file['path']: file for file in manifest['files']}
        if FIXED_COMPONENTS & files.keys():
            required.update(FIXED_COMPONENTS)
        for name in sorted(required):
            v4.require(name in files and files[name]['size'] > 0, 'missing required managed resource: ' + name)
        v4.require(required_files(root, chapter) <= files.keys()
                   and hashlib.sha256((root / DESCRIPTOR).read_bytes()).hexdigest() == files[DESCRIPTOR]['sha256'],
                   'chapter descriptor changed during packaging')
        if old:
            current = {name.casefold() for name in files}
            existing = {row['path'].casefold(): row for row in old['retired'] if row['path'].casefold() not in current}
            for file in old['files']:
                if file['path'].casefold() not in current:
                    existing.setdefault(file['path'].casefold(), dict(path=file['path'], action='delete', sequence=sequence))
            manifest['retired'] = list(existing.values())
        v4.validate_manifest(manifest)
        raw = v4.json_bytes(manifest)
        asset = v4.manifest_name(manifest)
        (prepared / asset).write_bytes(raw)
        summary = dict(version=version, chapter=chapter, files=len(files), bytes=planned['totalBytes'],
            parts=len(planned['assets']), retired=[row['path'] for row in manifest['retired']],
            manifest=asset, sha256=hashlib.sha256(raw).hexdigest())
        (prepared / 'build-summary.json').write_bytes(v4.json_bytes(summary))
        v4.no_links(output)
        v4.require(not output.exists(), 'Channel output already exists; use a new directory.')
        prepared.rename(output)
    return summary

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for flag in ('root','output','chapter','version','commit'): parser.add_argument('--'+flag,required=True)
    parser.add_argument('--previous')
    parser.add_argument('--sequence',type=int,default=1)
    parser.add_argument('--minimum-sequence',type=int,default=0)
    parser.add_argument('--migrations')
    print(json.dumps(build(**vars(parser.parse_args())),ensure_ascii=True))
