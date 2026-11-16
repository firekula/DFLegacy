"""Pinned public Chapter One bridge and Chapter Two resource packages.

Only explicit game resources are selected. Starter settings are generated here,
never copied from the supplied game directory. This module does not publish.
"""
import base64
import copy
import importlib.util
from pathlib import Path
import zipfile

import bootstrap_package
import update_v4 as v4

_local_path = Path(__file__).resolve().parents[2] / 'deploy/chapter-two/Build-LocalChapterChannel.py'
_spec = importlib.util.spec_from_file_location('chapter_inventory', _local_path)
inventory = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(inventory)

REPOSITORY = 'firekula/DFLegacy'
CHANNEL = 'chapter-two'


def chapter_image_resources(root):
    # The original C2 client uses distinct resources with identical basenames
    # in ImagePacks2 and ImagePacks4. A previously stripped stage is not complete.
    groups = {group: {p.relative_to(root).as_posix() for p in (root/group).rglob('*')
                      if p.is_file() and p.suffix.casefold() == '.npk'}
              for group in ('ImagePacks2', 'ImagePacks3', 'ImagePacks4')}
    for group in ('ImagePacks2', 'ImagePacks4'):
        v4.require(bool(groups[group]), 'Chapter Two source is missing resources: '+group)
    return set().union(*groups.values())


def _manifest(value):
    return v4.validate_manifest(v4.load_json(value) if isinstance(value, (str, Path)) else copy.deepcopy(value))


def build(root, output, *, chapter, architecture, flavor, version, commit, sequence, baseline=None,
          previous=None, provider='github', seed_provider=False, startup_bridge=None):
    root, output = Path(root).absolute(), Path(output).absolute()
    v4.require(chapter in ('chapter1', 'chapter2'), 'invalid chapter')
    v4.require(provider == 'github' or (provider == 'gitcode' and chapter == 'chapter2'),
               'invalid provider for the public chapter')
    v4.require(not seed_provider or provider == 'gitcode', 'provider seed requires GitCode')
    v4.require(architecture in ('x86', 'x64') and flavor in ('normal', 'test'), 'invalid variant')
    required = inventory.required_files(root, chapter) | inventory.FIXED_COMPONENTS
    old = _manifest(baseline) if baseline is not None else None
    prior = _manifest(previous) if previous is not None else None
    bridge = _manifest(startup_bridge) if startup_bridge is not None else None
    if bridge is not None:
        v4.require(chapter == 'chapter2' and not seed_provider and prior is not None
                   and (prior['version'], prior['sequence']) == ('v1.0.11', 16)
                   and (bridge['version'], bridge['sequence']) == ('v1.0.12-bridge', 17)
                   and (version, sequence) == ('v1.0.12', 18)
                   and (bridge['architecture'], bridge['flavor'], bridge['channel'], bridge['commit']) ==
                       (architecture, flavor, CHANNEL, commit), 'invalid startup migration bridge')
    v4.require(prior is None or chapter=='chapter2' and
               (prior['architecture'], prior['flavor'], prior['channel']) ==
               (architecture, flavor, CHANNEL), 'incompatible previous Chapter Two release')
    v4.require(old is None or (old['architecture'], old['flavor'], old['channel']) ==
               (architecture, flavor, 'stable'), 'incompatible baseline variant/source')
    v4.require(chapter != 'chapter2' or old is not None, 'Chapter Two requires a verified full bridge baseline')
    if chapter == 'chapter2':
        with (root/'Script.pvf').open('rb') as pvf:
            v4.require(pvf.read(15) != b'DNF_SCRIPT_PACK',
                       'Chapter Two client cannot use a server-format PVF; export the matching native client view')
        required.update(chapter_image_resources(root))
        v4.require(not any((root/name).exists() for name in ('DFLegacy.Client.dll','DFLegacy.Debug.dll','debug.dll')),
                   'Second Chapter contains a First Chapter module')
        v4.require('DFLegacy.Chapter.json' in {f['path'] for f in old['files']}, 'baseline has no chapter descriptor')
    versioned_startup = chapter == 'chapter2' and prior is not None and prior['minimumSequence'] >= 17
    entry_baseline = prior if versioned_startup else old
    entry_path = bootstrap_package.ENTRY
    if entry_baseline is not None:
        entry = next((f for f in entry_baseline['files'] if f['path'] == entry_path), None)
        v4.require(entry is not None, 'fixed entry missing from baseline')
        unchanged = v4.file_hash(root/entry_path) == (entry['sha256'], entry['size'])
        if bridge is None:
            v4.require(unchanged, 'fixed entry changed')
        else:
            bridge_entry = next((f for f in bridge['files'] if f['path'] == entry_path), None)
            v4.require(bridge_entry is not None and not unchanged
                       and (bridge_entry['sha256'], bridge_entry['size']) == (entry['sha256'], entry['size']),
                       'startup migration must replace the approved fixed entry')
    includes = inventory.COMMON + (inventory.FIRST if chapter=='chapter1' else inventory.SECOND)
    excludes = ['debug.dll','DFLegacy.Debug.dll'] if chapter=='chapter1' and flavor=='normal' else []
    if bridge is not None:
        minimum_sequence = 17
    elif versioned_startup:
        minimum_sequence = prior['minimumSequence']
    elif chapter == 'chapter2':
        minimum_sequence = old['sequence']
    else:
        minimum_sequence = old['minimumSequence'] if old is not None else 0
    # Preview provides an exact file set for retirement decisions, before output is created.
    options = dict(root=str(root),version=version,commit=commit,architecture=architecture,flavor=flavor,
        channel='stable' if chapter=='chapter1' else CHANNEL,sequence=sequence,
        minimum_sequence=minimum_sequence,
        includes=includes,excludes=excludes)
    if prior is not None:
        v4.require(sequence > prior['sequence'] and version != prior['version']
                   and options['minimum_sequence'] >= prior['minimumSequence'], 'previous chapter version does not advance')
        if seed_provider:
            options['migrations'] = prior['migrations']
        else:
            options['previous'] = [prior]
    planned = v4.plan_resources(**options)
    names = {f['path'] for f in planned['manifest']['files']}
    folded = {name.casefold() for name in names}
    v4.require(required <= names, 'required chapter resource missing')
    history = prior if prior is not None else old
    retired = {r['path'].casefold():r for r in (history['retired'] if history else []) if r['path'].casefold() not in folded}
    if history:
        for file in history['files']:
            if file['path'].casefold() not in folded:
                retired.setdefault(file['path'].casefold(),dict(path=file['path'],action='delete',sequence=sequence))
    options['retired'] = list(retired.values())
    if bridge is not None:
        def other_files(manifest):
            return {file['path']: (file['sha256'], file['size']) for file in manifest['files'] if file['path'] != entry_path}
        v4.require(other_files(planned['manifest']) == other_files(bridge)
                   and options['retired'] == bridge['retired']
                   and planned['manifest']['migrations'] == bridge['migrations'],
                   'startup migration may only replace the fixed entry')
    if old and chapter=='chapter1': options['previous'] = [old]
    return v4.build(output, provider, REPOSITORY, **options)


def public_configuration(flavor, provider='github'):
    return dict(ClientPath='DNF.exe',StartServer=False,ChapterTwoServerHost='games.firekula.site',
        ChapterTwoDebugEnabled=False,ChapterTwoTestMuteEnabled=False,DisableGitHubUpdates=False,
        ChapterTwoPostProcess='none',
        UpdateProvider=provider,UpdateRepository=REPOSITORY,UpdateChannel=CHANNEL,
        UpdateFlavor=flavor,UpdateServiceUrl='',Relay=dict(Enabled=True,Preference='ipv6'))


def create_starter(output, root, bundle):
    output, root, bundle = Path(output).absolute(), Path(root).absolute(), Path(bundle).absolute()
    receipt = v4.verify_local(bundle); manifest = receipt['manifest']
    v4.require(receipt['provider'] in ('github', 'gitcode')
               and receipt['repository']==REPOSITORY and manifest['channel']==CHANNEL,
               'starter is only for the pinned Chapter Two public source')
    inventory.required_files(root,'chapter2')
    rows = {f['path']:f for f in manifest['files']}
    v4.require(chapter_image_resources(root) <= rows.keys(),
               'Chapter Two image resource missing from manifest; rebuild from the complete source')
    v4.require(not {'DFLegacy.Client.dll','DFLegacy.Debug.dll','debug.dll'} & rows.keys(), 'First Chapter module in starter')
    v4.require(output!=root and root not in output.parents and output!=bundle and bundle not in output.parents,
               'starter output must be outside source/bundle')
    v4.no_links(output); v4.require(not output.exists(),'starter output already exists')
    raw = (bundle/receipt['manifestAsset']).read_bytes()
    source = dict(provider=receipt['provider'],repository=REPOSITORY,channel=CHANNEL,
                  architecture=manifest['architecture'],flavor=manifest['flavor'])
    installed = dict(release=dict(source=source,tag=manifest['version'],manifestSha256=receipt['manifestSha256'],
                                 manifestBytes=base64.b64encode(raw).decode('ascii')),
                     managedHistory=sorted(rows,key=str.casefold))
    output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(output,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        # ImagePacks3 is empty in the original client, but retain its directory.
        if (root/'ImagePacks3').is_dir():
            archive.writestr('ImagePacks3/', b'')
        for name,row in sorted(rows.items(),key=lambda p:p[0].casefold()):
            bootstrap_package._write_zip_file(archive,root/name,name,(row['sha256'],row['size']))
        archive.writestr('.dflegacy/v4/installed.json',v4.json_bytes(installed))
        archive.writestr('launcher.chapter2.json',v4.json_bytes(public_configuration(manifest['flavor'], receipt['provider'])))
        archive.writestr('.dflegacy/bootstrap/recovery.ini',bootstrap_package._descriptor(manifest['architecture'],
            rows['DFLegacy.Launcher.App.exe']['sha256'],rows['DFLegacy.Updater.exe']['sha256']))
        updater = rows['DFLegacy.Updater.exe']
        bootstrap_package._write_zip_file(archive,root/'DFLegacy.Updater.exe',
            '.dflegacy/bootstrap/'+updater['sha256']+'.exe',(updater['sha256'],updater['size']))
    digest,size = v4.file_hash(output)
    return dict(path=str(output),sha256=digest,size=size,source=source,version=manifest['version'])
