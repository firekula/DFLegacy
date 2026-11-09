"""Resource scans must exclude credentials in config backups and atomic saves."""
import json
import os
from pathlib import Path
import shutil
import subprocess

import pytest

from test_update_v4 import v4


SHELLS = [name for name in ('powershell.exe', 'pwsh') if shutil.which(name)]


def options(root):
    return dict(root=root,version='v4-private-test-1',commit='a'*40,architecture='x86',
                flavor='normal',channel='gitea-test',sequence=1,includes=['**'])


@pytest.mark.skipif(os.name != 'nt', reason='Windows opt-in scripts')
@pytest.mark.parametrize('shell', SHELLS)
@pytest.mark.parametrize('provider', ['Gitee', 'Gitea'])
def test_actual_opt_in_config_backup_cannot_enter_broad_resource_scan(tmp_path, shell, provider):
    source=tmp_path/'client'; source.mkdir()
    (source/'client-package.json').write_text('{"flavor":"normal"}',encoding='utf-8')
    before=json.dumps({'Account':'private-account','Password':'PRIVATE-CREDENTIAL-MARKER'}).encode()
    (source/'launcher.json').write_bytes(before)
    (source/'app.exe').write_bytes(b'public app bytes')
    script=Path(__file__).resolve().parents[1]/f'Enable-{provider}-Test.ps1'
    result=subprocess.run([shell,'-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
                           '-File',str(script),'-ClientDirectory',str(source)],capture_output=True,timeout=30)
    assert result.returncode==0, result.stderr.decode(errors='replace')
    backup,=source.glob('launcher.json.backup-*')
    assert backup.read_bytes()==before
    nested=source/'nested'; nested.mkdir()
    shutil.copyfile(backup,nested/backup.name)
    preview=v4.plan_resources(**options(source))
    assert [row['path'] for row in preview['manifest']['files']]==['app.exe']
    assert {backup.name,'nested/'+backup.name}<={row['path'] for row in preview['excluded']}
    bundle=tmp_path/'bundle'
    v4.build(output=bundle,provider='gitea',repository='firekula/DFLegacy',**options(source))
    assert [path.read_bytes() for path in (bundle/'assets').iterdir()]==[b'public app bytes']


def test_known_config_derivatives_and_atomic_save_files_are_private_at_every_depth(tmp_path):
    private_names=['launcher.json.backup-20261108-abcdef','LaUnChEr.JsOn.enable-abcd.tmp',
                   'Config.ini.123456.tmp','Config.ini.bak','launcher.txt.old','launcher.json~',
                   'server.json.backup','manager.json.tmp','account.json.bak','accounts.json.old',
                   'credentials.json.copy','cookies.json.bak','client-package.json.old',
                   'client-update-v4.json.tmp','.launcher-save-0123456789abcdef.tmp']
    for parent in (tmp_path,tmp_path/'nested'/'deeper'):
        parent.mkdir(parents=True,exist_ok=True)
        for name in private_names: (parent/name).write_bytes(b'PRIVATE-CREDENTIAL-MARKER')
    for name in ('app.exe','launcher.jsonx','Config.inifile'):
        (tmp_path/name).write_bytes(b'public')
    preview=v4.plan_resources(**options(tmp_path))
    assert [row['path'] for row in preview['manifest']['files']]==['app.exe','Config.inifile','launcher.jsonx']
    assert len(preview['excluded'])==2*len(private_names)
    assert all(row['reason']=='protected/private' for row in preview['excluded'])
