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


def _manifest(value):
    return v4.validate_manifest(v4.load_json(value) if isinstance(value, (str, Path)) else copy.deepcopy(value))


def build(root, output, *, chapter, architecture, flavor, version, commit, sequence, baseline=None):
    root, output = Path(root).absolute(), Path(output).absolute()
    v4.require(chapter in ('chapter1', 'chapter2'), 'invalid chapter')
    v4.require(architecture in ('x86', 'x64') and flavor in ('normal', 'test'), 'invalid variant')
    required = inventory.required_files(root, chapter) | inventory.FIXED_COMPONENTS
    old = _manifest(baseline) if baseline is not None else None
    v4.require(old is None or (old['architecture'], old['flavor'], old['channel']) ==
               (architecture, flavor, 'stable'), 'incompatible baseline variant/source')
    v4.require(chapter != 'chapter2' or old is not None, 'Chapter Two requires a verified full bridge baseline')
    if chapter == 'chapter2':
        v4.require(not any((root/name).exists() for name in ('DFLegacy.Client.dll','DFLegacy.Debug.dll','debug.dll')),
                   'Second Chapter contains a First Chapter module')
        v4.require('DFLegacy.Chapter.json' in {f['path'] for f in old['files']}, 'baseline has no chapter descriptor')
    if old is not None:
        entry = next((f for f in old['files'] if f['path']=='DFLegacy.Launcher.exe'), None)
        v4.require(entry is not None and v4.file_hash(root/'DFLegacy.Launcher.exe') == (entry['sha256'], entry['size']),
                   'fixed entry changed')
    includes = inventory.COMMON + (inventory.FIRST if chapter=='chapter1' else inventory.SECOND)
    excludes = ['debug.dll','DFLegacy.Debug.dll'] if chapter=='chapter1' and flavor=='normal' else []
    # Preview provides an exact file set for retirement decisions, before output is created.
    options = dict(root=str(root),version=version,commit=commit,architecture=architecture,flavor=flavor,
        channel='stable' if chapter=='chapter1' else CHANNEL,sequence=sequence,
        minimum_sequence=old['sequence'] if chapter=='chapter2' else (old['minimumSequence'] if old else 0),includes=includes,excludes=excludes)
    planned = v4.plan_resources(**options)
    names = {f['path'] for f in planned['manifest']['files']}
    folded = {name.casefold() for name in names}
    v4.require(required <= names, 'required chapter resource missing')
    retired = {r['path'].casefold():r for r in (old['retired'] if old else []) if r['path'].casefold() not in folded}
    if old:
        for file in old['files']:
            if file['path'].casefold() not in folded:
                retired.setdefault(file['path'].casefold(),dict(path=file['path'],action='delete',sequence=sequence))
    options['retired'] = list(retired.values())
    if old and chapter=='chapter1': options['previous'] = [old]
    return v4.build(output, 'github', REPOSITORY, **options)


def public_configuration(flavor):
    return dict(ClientPath='DNF.exe',StartServer=False,ChapterTwoServerHost='games.firekula.site',
        ChapterTwoDebugEnabled=False,ChapterTwoTestMuteEnabled=False,DisableGitHubUpdates=False,
        UpdateProvider='github',UpdateRepository=REPOSITORY,UpdateChannel=CHANNEL,
        UpdateFlavor=flavor,UpdateServiceUrl='',Relay=dict(Enabled=True,Preference='ipv6'))


def create_starter(output, root, bundle):
    output, root, bundle = Path(output).absolute(), Path(root).absolute(), Path(bundle).absolute()
    receipt = v4.verify_local(bundle); manifest = receipt['manifest']
    v4.require(receipt['provider']=='github' and receipt['repository']==REPOSITORY and manifest['channel']==CHANNEL,
               'starter is only for the pinned Chapter Two public source')
    inventory.required_files(root,'chapter2')
    rows = {f['path']:f for f in manifest['files']}
    v4.require(not {'DFLegacy.Client.dll','DFLegacy.Debug.dll','debug.dll'} & rows.keys(), 'First Chapter module in starter')
    v4.require(output!=root and root not in output.parents and output!=bundle and bundle not in output.parents,
               'starter output must be outside source/bundle')
    v4.no_links(output); v4.require(not output.exists(),'starter output already exists')
    raw = (bundle/receipt['manifestAsset']).read_bytes()
    source = dict(provider='github',repository=REPOSITORY,channel=CHANNEL,
                  architecture=manifest['architecture'],flavor=manifest['flavor'])
    installed = dict(release=dict(source=source,tag=manifest['version'],manifestSha256=receipt['manifestSha256'],
                                 manifestBytes=base64.b64encode(raw).decode('ascii')),
                     managedHistory=sorted(rows,key=str.casefold))
    output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(output,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for name,row in sorted(rows.items(),key=lambda p:p[0].casefold()):
            bootstrap_package._write_zip_file(archive,root/name,name,(row['sha256'],row['size']))
        archive.writestr('.dflegacy/v4/installed.json',v4.json_bytes(installed))
        archive.writestr('launcher.chapter2.json',v4.json_bytes(public_configuration(manifest['flavor'])))
        archive.writestr('.dflegacy/bootstrap/recovery.ini',bootstrap_package._descriptor(manifest['architecture'],
            rows['DFLegacy.Launcher.App.exe']['sha256'],rows['DFLegacy.Updater.exe']['sha256']))
        updater = rows['DFLegacy.Updater.exe']
        bootstrap_package._write_zip_file(archive,root/'DFLegacy.Updater.exe',
            '.dflegacy/bootstrap/'+updater['sha256']+'.exe',(updater['sha256'],updater['size']))
    digest,size = v4.file_hash(output)
    return dict(path=str(output),sha256=digest,size=size,source=source,version=manifest['version'])
