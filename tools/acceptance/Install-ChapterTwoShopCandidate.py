"""Install this task's verified candidate into the stopped, backed-up local runtime."""
import hashlib
import json
import pathlib
import shutil
import subprocess

root = pathlib.Path(__file__).resolve().parents[2]
evidence = root / 'dist/c2-shop-restoration-20261112'
runtime = root / 'dist/c2-win-20261111-loopback2/manual-restore'
processes = json.loads(subprocess.check_output([
    'powershell', '-NoProfile', '-Command',
    'Get-CimInstance Win32_Process | Select-Object Name,ExecutablePath | ConvertTo-Json -Compress'
], text=True))
prefix = str(runtime.resolve()).casefold() + '\\'
assert not any((p.get('ExecutablePath') or '').casefold().startswith(prefix)
               and p['Name'] != 'DFLegacy.Manager.exe' for p in processes), 'Stop isolated game and service first'
baseline = json.loads((evidence / 'baseline.json').read_text(encoding='utf-8-sig'))
def hash_file(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def git(*args):
    return subprocess.check_output(['git', *args], cwd=root, text=True).strip()
assert git('branch','--show-current') == 'codex/chapter-two'
assert git('rev-parse','main') == baseline['Main']
assert git('rev-parse','HEAD') == baseline['Head']
live = runtime / 'server/data/game/state.json'
assert hash_file(live) == baseline['SaveHash'], 'Live save changed; do not overwrite'
old = json.loads(live.read_text(encoding='utf-8-sig'))
new = json.loads((evidence / 'fixture02/prepared-state.json').read_text(encoding='utf-8-sig'))
assert new['Accounts'][:-1] == old['Accounts']
assert new['Accounts'][-1]['UserName'] == 'shopqa'
for key in old:
    if key not in ('Accounts','NextCharacterNo','NextNormalAccountUid','NextTestAccountUid'):
        assert old[key] == new[key], key
files = [
    ('server-build/DFLegacy.Server.exe', 'server/runtime/server/DFLegacy.Server.exe'),
    ('launcher/package/DFLegacy.Chapter2.Launcher.exe', 'DNFS1C2/DFLegacy.Chapter2.Launcher.exe'),
    ('combined-resources/Script.chapter-two.server.pvf','server/resources/Script.pvf'),
    ('combined-resources/Script.chapter-two.native.pvf','DNFS1C2/Script.pvf'),
    ('fixture02/prepared-state.json','server/data/game/state.json'),
]
for src, dst in files:
    assert (evidence / src).is_file(), src
    assert (runtime / dst).resolve().is_relative_to(runtime.resolve())
    assert (runtime / dst).is_file(), dst
    assert not (runtime / dst).is_symlink()
config_path = runtime / 'DNFS1C2/launcher.chapter2.json'
config = json.loads(config_path.read_text(encoding='utf-8-sig'))
login = json.loads((evidence / 'fixture02/qa-login.json').read_text(encoding='utf-8-sig'))
assert not any(a['Account'] == 'shopqa' for a in config['Accounts'])
config['Accounts'].append({'Account': 'shopqa', 'Password': login['Password'],
                          'DisplayName':'商店与礼包验收', 'RememberPassword':True})
config['AccountIndex'] = len(config['Accounts']) - 1
manifest = []
for src,dst in files:
    target = runtime / dst
    before = hash_file(target)
    shutil.copy2(evidence / src, target)
    assert hash_file(target) == hash_file(evidence / src)
    manifest.append({'Path':str(target),'Before':before,'After':hash_file(target)})
config_path.write_text(json.dumps(config,ensure_ascii=False,indent=2),encoding='utf-8')
(evidence / 'installed.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print('Installed verified candidate and QA account; original accounts and ledgers preserved.')
