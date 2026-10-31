"""Archived evidence verification only. Never import/run input harnesses or query processes."""
from pathlib import Path, PureWindowsPath
from datetime import datetime
import ast
import hashlib
import json
import math
import sys

ROOT = Path('D:/codex_worktree/party-chat-gold/DFLegacy')
X = Path('G:/Game/DNF_ACT1/DFLegacy/.tmp/native-town-route-20261029')
E = X/'evidence-native-ui-v2'
OUT = X/'runtime/review/native-ui-v2'
sys.path.insert(0, str(ROOT))
from tools.debug import client_state as sdk, client_native_route as api

checks = 0
def check(value, label):
    global checks
    checks += 1
    if not value: raise AssertionError(label)
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()
def ft(text):
    main, _, fraction = text.removesuffix('Z').partition('.')
    delta = datetime.fromisoformat(main) - datetime(1601, 1, 1)
    return (delta.days*86400+delta.seconds)*10000000+int((fraction+'0000000')[:7])
def area(s): return s['town']['town_id'], s['town']['area_id']
def identity(s):
    return (s['pid'], s['process_started_filetime'], s['me']['uid'], s['me']['user_token'],
            s['me']['actor_token'], s['town']['context_token'])
def valid(s):
    sdk._validate(s)
    check(s['valid'] and s['layout_supported'] and not s['truncated'] and s['scene']==1, 'complete supported Town SDK')
def close(a, b, tolerance=3): return all(abs(a[k]-b[k]) <= tolerance for k in ('x', 'y'))

journal = read(X/'running-v2-deployment.json')
journal_sha = sha(X/'running-v2-deployment.json')
check(journal['Status']=='running' and journal['Phase']=='native-ui-v2', 'frozen running journal phase')
check(journal_sha=='59C95948675A1AD0752D4CEB5BD3F7DAC14B15F3A6419D936DE031067D410017', 'frozen run bytes')
manifest = read(journal['BuildManifest']); manifest_sha = sha(journal['BuildManifest'])
check(manifest_sha==journal['BuildManifestSha256'], 'build manifest bound')
check(manifest['SourceCommit']=='f6a9eb93cfff8678d3b99ad0fe7a5a92871b551f', 'native source commit')
for key, expected in [('DebugSha256','5E5FF3F218E738C9C4AAEFACBB496B800A44BD5D44BA88E45BB00AFFB401836B'),
                      ('NativeSha256','0F2CB060C1DBAA09367C40E81B297951B5CC2883278B1B255EE4672258709225')]:
    check(manifest[key]==expected, key)
    check(sha(manifest['DebugArtifact' if key=='DebugSha256' else 'NativeArtifact'])==expected, key+' artifact')
clients = {c['Role']:c for c in journal['Clients']}
modules = read(X/'loaded-v2-modules.json')
for role, c in clients.items():
    for name, expected in [('debug.dll',manifest['DebugSha256']),('ijl15.dll',manifest['NativeSha256'])]:
        found = [m for m in modules if m['PID']==c['Id'] and PureWindowsPath(m['Path'])==PureWindowsPath(c['Path']).parent/name]
        check(len(found)==1 and found[0]['Sha256']==expected, 'recorded module '+role+'/'+name)

def bound(proof):
    check(proof['phase']=='native-ui-v2' and proof['journal_sha256']==journal_sha and
          proof['build_manifest_sha256']==manifest_sha, 'proof run/build binding')

projection = read(E/'A-map-projection-confirmed.json')
def inverse(step):
    px, py = step['x']-projection['shift'][0], step['y']-projection['shift'][1]
    hits = [r for r in projection['projects'] if r['rect'][0]<=px<r['rect'][0]+r['rect'][2]
            and r['rect'][1]<=py<r['rect'][1]+r['rect'][3]]
    check(len(hits)==1, 'unique recorded projection hit')
    p=hits[0]; r=next(r for r in projection['reals'] if (r['town'],r['area'])==(p['town'],p['area']))
    rnd=lambda v:math.floor(v+.5) if v>=0 else math.ceil(v-.5)
    return dict(town_id=p['town'],area_id=p['area'],
        x=rnd(r['rect'][0]+(px-p['rect'][0])*r['rect'][2]/p['rect'][2]),
        y=rnd(r['rect'][1]+(py-p['rect'][1])*r['rect'][3]/p['rect'][3]))

summaries=[]
for name in ['A-full-forward','A-full-reverse','B-full-forward','B-full-reverse','A-same-area','B-same-area']:
    p=read(E/(name+'.json'));bound(p);role=p['role'];c=clients[role]
    before,after,term,hist=p['before'],p['after'],p['terminal'],p['history']
    valid(before);valid(after)
    check(before['pid']==c['Id'] and before['process_started_filetime']==ft(c['StartTimeUtc']), name+' PID/start')
    check(identity(before)==identity(after), name+' fixed identity')
    check(p['native_route_only'] and p['expected_status']=='arrived', name+' declared route contract')
    check(hist and hist[-1]==term and term['status']=='arrived' and term['reason']=='final_position_observed', name+' terminal complete')
    lines=[json.loads(line) for line in (E/(name+'-route-updates.jsonl')).read_text(encoding='utf-8-sig').splitlines() if line.strip()]
    check(lines==hist, name+' original updates exact match')
    prior_tick=before['captured_tick_ms'];prior_step=0
    for row in hist:
        check(row['schema']=='dfl-client-route/v1' and row['pid']==c['Id'] and row['process_started_filetime']==ft(c['StartTimeUtc'])
              and row['uid']==before['me']['uid'] and row['source']=='minimap', name+' native identity/source')
        check(row['request_id']==term['request_id'] and row['goal']==term['goal'] and row['origin']==term['origin'], name+' one request/goal/origin')
        check(prior_tick<=row['tick_ms']<=after['captured_tick_ms'] and prior_step<=row['steps']<=14, name+' monotonic event/steps')
        prior_tick,prior_step=row['tick_ms'],row['steps']
        check(row['pvf_sha256']=='288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f', name+' PVF')
    for k in ('user_token','actor_token'):
        check(term['origin'][k]==before['me'][k], name+' original '+k)
    check(term['origin']['context_token']==before['town']['context_token'] and term['origin']['map_token']==before['me']['map_token'], name+' original map/context')
    check(after['captured_tick_ms']>term['tick_ms'] and after['sequence']>before['sequence'], name+' later SDK')
    goal=term['goal'];obs=term['observed']
    check(area(after)==(goal['town_id'],goal['area_id']) and (obs['town_id'],obs['area_id'])==area(after), name+' final area')
    check(close(after['town']['position'],goal) and close(obs['position'],goal) and
          after['town']['direction_raw']==obs['direction_raw']==dict(x=4,y=4), name+' physical final/idle')
    check(after['town']['destination']==obs['destination']=={k:goal[k] for k in ('x','y')}, name+' final destination')
    check(obs['map_token']==after['me']['map_token']==after['town']['map_token'], name+' final map')
    states=p['area_states'];path=[area(s) for s in states]
    expected=([(1,1),(1,0),(2,1),(2,0),(2,2)] if name.endswith('forward') else
              [(2,2),(2,0),(2,1),(1,0),(1,1)] if name.endswith('reverse') else [(1,1)])
    check(path==expected and term['steps']==(14 if 'full' in name else 1), name+' path and step count')
    previous=None
    for s in states:
        valid(s);check(identity(s)==identity(before), name+' area-state binding')
        if previous is not None:
            check(s['generation']>previous['generation'] and s['sequence']>previous['sequence'] and
                  s['captured_tick_ms']>previous['captured_tick_ms'] and s['town']['map_token']!=previous['town']['map_token'], name+' proved area change')
        previous=s
    inp=read(E/(name+'-input.json'))
    check(inp['pid']==c['Id'] and PureWindowsPath(inp['client_dir'])==PureWindowsPath(c['Path']).parent, name+' input PID/path')
    check(inp['success'] and inp['own_plan_armed'] and len(inp['steps'])==1 and inp['steps']==p['input_steps'] and inp['steps'][0]['op']=='rclick', name+' exactly one rclick')
    payload=json.dumps(dict(version=1,steps=inp['steps']),ensure_ascii=False).encode()
    check(hashlib.sha256(payload).hexdigest()==inp['plan_sha256'], name+' exact input payload hash')
    diag=(E/(name+'-input.diag.log')).read_text(encoding='utf-8-sig')
    check(sum(f"[{c['Id']}]" in line and 'plan armed (1 steps)' in line for line in diag.splitlines())==1, name+' own PID fresh one-step arm')
    check(inverse(inp['steps'][0])==goal, name+' inverse projection matches final goal')
    summaries.append(dict(case=name,pid=c['Id'],uid=term['uid'],request_id=term['request_id'],path=path,goal=goal,
        native_steps=term['steps'],event_rows=len(hist),seen_steps=sorted({r['steps'] for r in hist}),elapsed_ms=p['elapsed_ms'],
        sdk_after_terminal_ms=after['captured_tick_ms']-term['tick_ms'],sdk_export_delay_ms=(after['exported_filetime']-after['captured_filetime'])/10000,
        input_sha256=inp['plan_sha256'],proof_sha256=sha(E/(name+'.json')),updates_sha256=sha(E/(name+'-route-updates.jsonl'))))

# Manual cancellation and replacement are separate from the positive four runs.
p=read(E/'A-native-manual-interruption.json');bound(p)
chain=[p[k] for k in ('started','after_npc','before_key','terminal')]
check(all(r['request_id']==chain[0]['request_id'] and r['goal']==chain[0]['goal'] and r['origin']==chain[0]['origin'] for r in chain), 'manual route identity constant')
check(all(r['status']=='moving' for r in chain[:3]) and chain[-1]['status']=='interrupted' and chain[-1]['reason']=='native_auto_move_stopped', 'manual strong interruption')
closed=read(E/'A-manual-close-map.json');check(p['before_key']['tick_ms']>closed['after']['captured_tick_ms'], 'new Native moving observation after N closed')
check(not closed['after']['ui']['town_minimap']['visible'], 'map closed during continued movement')
for k in ('after_key','after_wait'):valid(p[k])
check(identity(p['after_key'])==identity(p['after_wait']) and p['wait_ms']==2250 and p['route_bytes_unchanged'] and
      close(p['after_key']['town']['position'],p['after_wait']['town']['position'],.01) and
      p['after_wait']['town']['direction_raw']==dict(x=4,y=4) and p['after_wait']['town']['auto_move_active'] is False, 'no resumed goal after manual input')
p=read(E/'A-valid-target-takeover.json');bound(p)
check(p['new_target']['request_id']>p['old_active']['request_id'] and p['old_active']['status']=='moving' and
      p['new_target']['goal']!=p['old_active']['goal'] and p['terminal']['request_id']==p['new_target']['request_id'] and
      p['terminal']['status']=='arrived', 'valid replacement takes over active route')
check(p['wait_ms']==2100 and p['route_bytes_unchanged'] and close(p['after']['town']['position'],p['after_wait']['town']['position'],.01)
      and p['after_wait']['town']['direction_raw']==dict(x=4,y=4) and p['after']['captured_tick_ms']>p['terminal']['tick_ms'], 'no old-goal resume after replacement')
for name in ['A-control-right-no-goal','A-outside-projection-no-goal','A-region-menu-right-no-goal','A-npc-menu-right-no-goal','A-hidden-map-right-no-route']:
    p=read(E/(name+'.json'));bound(p)
    check(p['exact_route_bytes_unchanged'] and p['before_route']==p['after_route'], name+' preserves route record')
    valid(p['before']);valid(p['after']);check(identity(p['before'])==identity(p['after']), name+' SDK binding')
for name,key in [('A-region-menu-open-state','0x1c'),('A-npc-menu-open-state','0x64')]:
    p=read(E/(name+'.json'));bound(p);check(p['menus'][key]['visible'], name+' Native menu open proof')
p=read(E/'A-reopened-map-no-replay.json');bound(p);check(p['unchanged'] and p['snapshot']['ui']['town_minimap']['visible'], 'reopen does not replay')

# Archived v2 API uses exactly the independent96-byte request and per-ID terminal.
p=read(E/'B-v2-native-api-cross.json');bound(p);before,after=p['before'],p['after'];valid(before);valid(after)
raw=(E/'B-v2-api-dfdebug-route-76432.bin').read_bytes();request=api.parse_request(raw)
check(len(raw)==96 and raw[:4]==b'DLRT' and p['one_final_command'] and p['python_waypoint_controller'] is False, 'new API single final command')
terminal=read(E/f"B-v2-api-dfdebug-route-76432-{request['request_id']}.result.json")
check(terminal==p['result']['native'] and p['result']['ok'] and p['result']['status']=='arrived', 'API raw terminal exact match')
api.validate_response(terminal,request,after['captured_tick_ms'])
check(request['pid']==clients['B']['Id'] and request['process_started_filetime']==ft(clients['B']['StartTimeUtc']) and
      request['uid']==before['me']['uid'] and identity(before)==identity(after), 'API process/actor lifetime')
check(terminal['steps']==14 and after['captured_tick_ms']>terminal['tick_ms'] and close(after['town']['position'],terminal['goal'])
      and area(after)==(request['town_id'],request['area_id']) and after['town']['direction_raw']==dict(x=4,y=4), 'API fresh physical endpoint')
api_summary=dict(request_id=request['request_id'],steps=terminal['steps'],elapsed_ms=p['elapsed_ms'],goal=terminal['goal'],
                 sdk_after_terminal_ms=after['captured_tick_ms']-terminal['tick_ms'],raw_sha256=sha(E/'B-v2-api-dfdebug-route-76432.bin'))

images=[]
for name in ['A-hendon-key-screenshot','B-hendon-key-screenshot','A-room-key-screenshot','B-room-key-screenshot']:
    p=read(E/(name+'.json'));bound(p);inp=read(E/(name+'-input.json'));source=Path(inp['screenshot_source']);copy=Path(inp['screenshot'])
    check(inp['success'] and sha(source)==sha(copy), name+' original image bytes')
    check(inp['pid']==clients[p['role']]['Id'] and identity(p['before'])==identity(p['after']), name+' image identity')
    check(area(p['after'])==((2,2) if 'hendon' in name else (1,1)), name+' scene evidence')
    images.append(dict(name=name,source=str(source),copy=str(copy),sha256=sha(copy),area=area(p['after'])))

# Closeout evidence is checked as files; no live PID queries after shutdown.
restored=read(X/'closeout-restoration.json')
check(restored['Verified'] and restored['NoNativeProcesses'] and restored['ProtectedInputCount']==15 and
      restored['NormalFileCount']==50 and restored['PreviousWFrozenFilesVerified']==146, 'declared closeout counts')
check(sha(X/'closed-native-ui-v2/deployment.json')==restored['StoppedJournalSha256'] and
      sha(X/'closed-native-ui-v2/manifest.json')==restored['ClosedManifestSha256'], 'closed journal/manifest binding')
for group,count in [('NormalConfigs',2),('RestoredInterfaces',8),('DebugTargets',4),('NativeTargets',4)]:
    check(len(restored[group])==count, 'closeout '+group+' count')
    for item in restored[group]:
        path=Path(item['Path'])
        if item.get('Exists',True):check(path.is_file() and sha(path)==item['Sha256'], 'restored hash '+group)
        else:check(not path.exists(), 'restored absence '+group)

harness=[]
for name in ['goaltest_v3.py','h.py','control.py','builtin_driver.py','manual_native.py','protection_native.py']:
    p=X/name;tree=ast.parse(p.read_text(encoding='utf-8-sig'))
    harness.append(dict(path=str(p),sha256=sha(p),calls=sorted({ast.unparse(n.func) for n in ast.walk(tree) if isinstance(n,ast.Call)})))
out=dict(checks=checks,failures=0,running_journal_sha256=journal_sha,build_manifest_sha256=manifest_sha,source_commit=manifest['SourceCommit'],
         debug_sha256=manifest['DebugSha256'],ijl_sha256=manifest['NativeSha256'],positive_cases=summaries,api=api_summary,images=images,harness=harness,
         manual_protection='verified as scoped; hidden map asserts no new map route only',
         closeout='journal/hash/count checks only; no live process query and no rerun of all146oldWfiles',
         scope='archived evidence only; no harness execution/native calls/process control/build/deployment')
(OUT/'acceptance-audit-results.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
print(json.dumps(dict(checks=checks,failures=0,cases=[{k:v for k,v in s.items() if k in ('case','native_steps','elapsed_ms','sdk_after_terminal_ms','path')} for s in summaries],api=api_summary),indent=2))
