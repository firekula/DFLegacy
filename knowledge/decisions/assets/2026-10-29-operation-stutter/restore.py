from pathlib import Path
import json,hashlib,shutil
Q=Path(r'G:\Game\DNF_ACT1\DFLegacy\.tmp\active-operation-stutter-20261029');Z=Q.parent/'town-return-perf-20261029';load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'));sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper();assert load(Q/'deployment.json')['Status']=='stopped'
configs=[]
for v in load(Q/'config-variants.json'):
 p=Path(v['Path']);assert sha(p)==v['PlannedSha256'];assert sha(Path(v['Backup']))==v['OriginalSha256'];shutil.copyfile(v['Backup'],p);assert sha(p)==v['OriginalSha256'];configs.append(dict(path=str(p),sha256=sha(p)))
interfaces=[]
for v in load(Q/'native-fixture.json')['interface_files']:
 p=Path(v['path'])
 if v['existed']:
  assert sha(Path(v['backup']))==v['sha256'];shutil.copyfile(v['backup'],p);assert sha(p)==v['sha256']
 elif p.exists():p.unlink()
 interfaces.append(dict(path=str(p),exists=p.exists(),sha256=sha(p) if p.exists() else None))
manifest=load(Z/'build-manifest-return-green.json');normal=Path(r'G:\Game\DNF_ACT1\DF\town-test');files=[]
for f in manifest['Files']:
 if f['File']=='server.json':continue
 assert sha(normal/f['File'])==f['Sha256'];files.append(f)
for v in load(Z/'debug-deployment.json')['Targets']:assert sha(Path(v['Path']))==manifest['DebugSha256']
for v in load(Q/'native-drag-deployment.json')['Targets']:assert sha(Path(v['Path']))==manifest['NativeSha256']
frozen=load(Z/'frozen-manifest.json');assert sha(Z/'frozen-manifest.json')=='03E96277E8BA5040DD6BD54272CEE8976406FFD593C25E51A567AA552D47BF64'
for v in frozen['Files']:assert sha(Z/v['File'])==v['Sha256'],v['File']
out=dict(verified=True,restored_configs=configs,restored_interfaces=interfaces,normal_runtime_file_count=len(files),normal_runtime_sha_unchanged=True,debug_sha256=manifest['DebugSha256'],native_sha256=manifest['NativeSha256'],prior_frozen_namespace=str(Z),prior_frozen_files=len(frozen['Files']),prior_manifest_sha256=sha(Z/'frozen-manifest.json'),original_saves_copied=False,closed_manifest_sha256=sha(Q/'closed-operation-baseline/manifest.json'))
with (Q/'closeout-restoration.json').open('x',encoding='utf-8') as f:json.dump(out,f,indent=2)
print(json.dumps(dict(verified=True,configs=len(configs),interfaces=len(interfaces),unchanged_normal_runtime_files=len(files),old_frozen_files=len(frozen['Files']))))
