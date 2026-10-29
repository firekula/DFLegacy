from pathlib import Path
import json,shutil,hashlib
P=Path(r'G:\Game\DNF_ACT1\DFLegacy\.tmp');Z=P/'town-return-perf-20261029';X=P/'native-town-route-20261029'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'));sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
assert load(Z/'deployment.json')['Status']=='stopped'
restored=[]
for v in load(Z/'config-variants.json'):
 p=Path(v['Path']);assert sha(p)==v['PlannedSha256'];assert sha(Path(v['Backup']))==v['OriginalSha256'];shutil.copyfile(v['Backup'],p);assert sha(p)==v['OriginalSha256'];restored.append(dict(path=str(p),sha256=sha(p)))
interfaces=[]
for v in load(Z/'native-fixture.json')['interface_files']:
 p=Path(v['path'])
 if v['existed']:
  assert sha(Path(v['backup']))==v['sha256'];shutil.copyfile(v['backup'],p);assert sha(p)==v['sha256']
 elif p.exists():p.unlink()
 interfaces.append(dict(path=str(p),exists=p.exists(),sha256=sha(p) if p.exists() else None))
old=load(X/'build-manifest-native-ui-v2.json');new=load(Z/'build-manifest-return-green.json');olds={f['File']:f for f in old['Files']};normal=Path(r'G:\Game\DNF_ACT1\DF\town-test');backup=Z/'normal-before';backup.mkdir();targets=[]
# Validate ALL normal server runtime files before any replacement. server.json is protected.
for f in new['Files']:
 if f['File']=='server.json':continue
 p=normal/f['File'];expected=new['DebugSha256'] if f['File']=='debug.dll' else olds[f['File']]['Sha256'];assert sha(p)==expected,(f['File'],sha(p),expected)
 src=Path(new['Build'])/f['File'];assert sha(src)==f['Sha256'];dest=backup/f['File'];dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,dest);targets.append(dict(file=f['File'],before_sha256=sha(p),sha256=f['Sha256']))
for t in targets:
 shutil.copyfile(Path(new['Build'])/t['file'],normal/t['file']);assert sha(normal/t['file'])==t['sha256']
# Check frozen earlier evidence without modifying it.
frozen=[]
for folder,expected in [('debug-cross-area-20261029','FE04B5CA5E8FEC90375F943B864DFB46D2EB35E03C22FD94924BC8DA27DFC573'),('native-town-route-20261029','A8D03B79FBA6327FCFB5D978BD45F14EB9C3C7117DC77DBD3D176422A0F19E78')]:
 base=P/folder;p=base/'frozen-manifest.json';assert sha(p)==expected;mf=load(p)
 for f in mf['Files']:assert sha(base/f['File'])==f['Sha256'],f['File']
 frozen.append(dict(namespace=folder,files=len(mf['Files']),manifest_sha256=sha(p)))
for t in load(Z/'debug-deployment.json')['Targets']:assert sha(Path(t['Path']))==new['DebugSha256']
for t in load(Z/'native-drag-deployment.json')['Targets']:assert sha(Path(t['Path']))==new['NativeSha256']
out=dict(verified=True,restored_configs=restored,restored_interfaces=interfaces,normal_runtime=str(normal),normal_files=targets,debug_sha256=new['DebugSha256'],native_sha256=new['NativeSha256'],frozen_evidence=frozen,original_saves_copied=False)
(Z/'closeout-restoration.json').write_text(json.dumps(out,indent=2),encoding='utf-8');print(json.dumps(dict(verified=True,normal_files=len(targets),frozen=frozen)))
