from pathlib import Path
import json,hashlib,shutil,subprocess
R=Path(r'D:\codex_worktree\party-chat-gold\DFLegacy'); P=Path(r'G:\Game\DNF_ACT1\DFLegacy\.tmp');Y=P/'active-drop-native-20261029';Z=P/'town-return-perf-20261029';X=P/'native-town-route-20261029'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
write=lambda p,v:p.write_text(json.dumps(v,indent=2),encoding='utf-8')
assert load(Y/'deployment.json')['Status']=='stopped'
# Restore prior run's exact normal configurations and original input interface files.
for v in load(Y/'config-variants.json'):
 p=Path(v['Path']);assert sha(p)==v['PlannedSha256'];assert sha(Path(v['Backup']))==v['OriginalSha256'];shutil.copyfile(v['Backup'],p)
for v in load(Y/'native-fixture.json')['interface_files']:
 p=Path(v['path'])
 if v['existed']:
  assert sha(Path(v['backup']))==v['sha256'];shutil.copyfile(v['backup'],p)
 elif p.exists():p.unlink()
pre=Z/'preflight';pre.mkdir(exist_ok=True)
for name in ['protected-files.json','05-server.json']:shutil.copyfile(Y/'preflight'/name,pre/name)
for name in ['native-drag-deployment.json','native-fixture.json','h.py','control.py','builtin_driver.py','ui.py','enter-dungeon.py','dungeon.py','setup-party-gate.py']:shutil.copyfile(Y/name,Z/name)
for name in ['mapread.py','goaltest_v3.py']:shutil.copyfile(X/name,Z/name)
for name in ['native-common.ps1','start-drop-green.ps1']:
 s=(Y/name).read_text(encoding='utf-8-sig').replace(str(Y),str(Z)).replace('drop-green','return-green')
 if name.startswith('start'):
  fixture_sha=sha(Y/'closed-drop-green/state.json');s=s.replace('A3E6922E6C58B20DD787BEBA3B82586C192AFA23612C7ED8B9CDEECB6046FE8C',fixture_sha)
 (Z/name.replace('drop-green','return-green')).write_text(s,encoding='utf-8-sig')
shutil.copyfile(Y/'closed-drop-green/state.json',Z/'fixture-return-green.json')
s=(Y/'login-native.py').read_text().replace("rows['town']['town_id']==1 and rows['town']['area_id']==1","rows['town']['town_id']>0").replace('initial Seria room','persisted town')
(Z/'login-native.py').write_text(s)
variants=load(Y/'config-variants.json')
for v in variants:
 p=Path(v['Path']);assert sha(p)==v['OriginalSha256'];backup=pre/(v['Role']+'-normal-Config.ini');shutil.copyfile(p,backup)
 data=p.read_bytes();line=next(l for l in data.splitlines() if l.startswith(b'SnapshotDirectory='));planned=pre/(v['Role']+'-planned-Config.ini');planned.write_bytes(data.replace(line,('SnapshotDirectory='+str(Z/'snapshots-return-green')).encode()))
 v.update(Backup=str(backup),Planned=str(planned),PlannedSha256=sha(planned));shutil.copyfile(planned,p)
write(Z/'config-variants.json',variants)
old=load(Y/'build-manifest-drop-green.json');build=Z/'build-return-green';build.mkdir()
for f in old['Files']:
 src=Path(old['Build'])/f['File'];assert sha(src)==f['Sha256'];dst=build/f['File'];dst.parent.mkdir(exist_ok=True,parents=True);shutil.copyfile(src,dst)
dll=R/'artifacts/native/debug/Release/debug.dll';assert sha(dll)=='C3ED35E1008D74536D96307A9478C636AAD092186DEF3C9EE99CF8F3652D14AD';shutil.copyfile(dll,build/'debug.dll')
commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,text=True).strip()
new=dict(old,Build=str(build),SourceCommit=commit,DebugSourceCommit=commit,DebugSha256=sha(dll),DebugArtifact=str(build/'debug.dll'),NativeArtifact=str(build/'ijl15.dll'),PreviousBuildManifest=str(Y/'build-manifest-drop-green.json'),PreviousBuildManifestSha256=sha(Y/'build-manifest-drop-green.json'))
new['Files']=[dict(File=str(p.relative_to(build)),Bytes=p.stat().st_size,Sha256=sha(p)) for p in sorted(build.rglob('*')) if p.is_file()];write(Z/'build-manifest-return-green.json',new)
dep=load(X/'debug-v2-deployment.json')
for i,t in enumerate(dep['Targets']):
 p=Path(t['Path']);assert sha(p)==dep['DebugSha256'];backup=pre/f'debug-before-{i}.dll';shutil.copyfile(p,backup);t.update(BeforeSha256=sha(p),Backup=str(backup),Sha256=sha(dll));shutil.copyfile(dll,p)
dep.update(BuildManifest=str(Z/'build-manifest-return-green.json'),BuildManifestSha256=sha(Z/'build-manifest-return-green.json'),DebugSha256=sha(dll));write(Z/'debug-deployment.json',dep)
print(json.dumps(dict(manifest=dep['BuildManifest'],sha=dep['BuildManifestSha256'],fixture_sha=sha(Z/'fixture-return-green.json')),indent=2))
