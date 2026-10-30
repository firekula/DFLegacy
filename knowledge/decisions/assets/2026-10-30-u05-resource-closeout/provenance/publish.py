from pathlib import Path
from datetime import datetime,timezone
import collections,hashlib,json,shutil,subprocess

R=Path(__file__).resolve().parents[2]
T=Path('G:/Game/DNF_ACT1/DFLegacy/.tmp/u05-final-20261030');E=T/'evidence-resource-v1'
S=R/'.tmp/u05-final-20261030';V=S/'review'
A=R/'knowledge/decisions/assets/2026-10-30-u05-resource-closeout'
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
review=read(V/'review-manifest.json');sources=[]
for row in review['source_and_evidence_hashes']:
    if row['path'].startswith(('src/','tools/')):
        assert sha(R/row['path'])==row['sha256'].upper(),row['path']
        sources.append(row)
assert len(sources)>=8
debug='2D443011161A943B79BE4ED7C02EACAFE02F120DC5B0688C142A313D789BBF83'
ijl='0103C7DCE1FA8AEA5233D7A02C6E1115F83598A8E3A02EF85AC8834F24B2871A'
for name,folder,digest in [('debug.dll','debug',debug),('ijl15.dll','ijl15',ijl)]:
    assert sha(T/'native-v1'/name)==sha(R/f'artifacts/native/{folder}/Release/{name}')==digest
deployed=read(T/'deployed-files.json')
for role in 'ABCD':
    for name,digest in [('debug.dll',debug),('ijl15.dll',ijl)]:
        rows=[x for x in deployed if x['Path'].replace('\\','/').endswith(f'Loot4_{role}/{name}')]
        assert len(rows)==1 and rows[0]['Sha256']==digest
j=read(T/'deployment.json');assert j['Status']=='stopped' and j['ObservedActiveChannelSessionsBeforeStop']==0
for name in ['replacement-backups.json','protected-files.json']:
    for row in read(T/'preflight'/name):assert sha(Path(row['Path']))==row['Sha256']
assert subprocess.run(['powershell','-NoProfile','-Command',
    'if (@(Get-Process -Name DNF,DFLegacy.Server,DFLegacy.Launcher -ErrorAction SilentlyContinue).Count) {exit 1}; exit 0'],capture_output=True).returncode==0
initial=read(E/'resource-four-dungeon-initial.json')['snapshots']
live=read(E/'resource-four-live-cli.json')['results'];window=read(E/'resource-twelve-second-window.json')
raw=read(E/'resource-raw-witnesses.json')['results'];identities={}
count=0
for role in 'ABCD':
    s=initial[role];assert s['me']['dead'] is False
    identities[role]=dict(pid=s['pid'],process_started_filetime=s['process_started_filetime'],
        actor_token=s['me']['actor_token'],uid=s['me']['uid'])
    for sample in [live[role]['snapshot'],window['before'][role]]:
        assert sample['pid']==s['pid'] and sample['process_started_filetime']==s['process_started_filetime']
        assert sample['me']['uid']==s['me']['uid'] and sample['me']['actor_token']==s['me']['actor_token']
    assert raw[role]['pid']==s['pid'] and raw[role]['process_started_filetime']==s['process_started_filetime']
    assert raw[role]['actor_token']==s['me']['actor_token'] and raw[role]['uid']==s['me']['uid']
    for value in live[role]['cli'].values():assert value['status']=='matched' and value['ok'];count+=1
assert count==13
assert len(window['rows'])==len({(x['role'],x['sequence']) for x in window['rows']})==240
assert collections.Counter(x['role'] for x in window['rows'])==dict.fromkeys('ABCD',60)
assert all(x['status']=='timeout' for x in window['global_unknown_checks'].values())
assert all(x['exit_code']==0 for x in read(S/'native-final-results.json'))
assert '620 passed, 569 subtests passed' in (S/'python-final.log').read_text(encoding='utf-8-sig')
audit=read(T/'closeout-audit.json')
audit['forbidden']['revive_coin_command']=audit['forbidden'].pop('buy_revive_coin')
A.mkdir(parents=True,exist_ok=False)
def put(name,value):
    with (A/name).open('x',encoding='utf-8') as f:json.dump(value,f,indent=2,ensure_ascii=False)
def copy(source,name):
    p=A/name;assert not p.exists();p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,p)
for name in ['resource-four-dungeon-initial.json','resource-four-live-cli.json','resource-raw-witnesses.json',
             'resource-twelve-second-window.json','resource-four-final-state.json']:
    copy(E/name,name)
for role in 'ACD':copy(E/f'resource-{role}-final-key-image.jpg',f'{role}-final-scene.jpg')
copy(E/'resource-B-skill51-hover.jpg','B-dead-coin-prompt.jpg')
for name in ['native-resource.asm','password-constructor.asm','native-resource-red.log','native-red-heap.log',
             'native-review-red.log','native-review-green.log','python-resources-red.log','python-resources-green.log',
             'native-final.log','native-final-results.json','python-final.log','test-crash-stack.txt']:
    copy(S/name,name)
for name in ['native_oracle.py','native-oracle.json','counter_repro.cpp','counter-repro.log','review-manifest.json',
             'native-client-state-green.log','python-client-state-green.log','final-evidence-review.json']:
    copy(V/name,'review/'+name)
for name in ['resource_witness.py','native_sampler.py','observe_live.py','closeout_audit.py','publish.py']:
    copy(S/name,'provenance/'+name)
put('closeout-audit.json',audit)
copy(T/'restoration-final.json','restoration-final.json')
put('identity-ledger.json',dict(phase=j['Phase'],status=j['Status'],clients=j['Clients'],server=j['Server'],
    started_at_utc=j['StartedAtUtc'],stopped_at_utc=j['StoppedAtUtc'],bindings=identities,
    source_files=sources,debug_sha256=debug,ijl_sha256=ijl,
    build_manifest_sha256=sha(T/'build-manifest-v1.json'),
    private_seed_state_sha256=read(T/'setup-v1.json')['SourceSha256']))
put('verification.json',dict(verified_at_utc=datetime.now(timezone.utc).isoformat(),
    python=dict(tests=620,subtests=569,skipped=0,exit_code=0,
        diagnostic='Existing Unicorn.mem_map Windows faulthandler diagnostic stacks retained in the raw log; suite completed exit0.'),
    native=read(S/'native-final-results.json'),independent_original_byte_oracle_cases=24,
    final_binary_loaded_on_all_four=True,source_unchanged_since_live=True,
    live_cli_matches=count,global_unknown_timeouts=4,unique_sdk_rows=240,rows_per_role=60,
    skill_observations=sum(len(x['skills']) for x in window['rows']),
    capture_duration_us=dict(minimum=min(x['capture_duration_us'] for x in window['rows']),maximum=max(x['capture_duration_us'] for x in window['rows'])),
    timing_scope='Reader duration in this window only, not whole-frame timing or an U08 conclusion.',
    health=dict(initial_alive=list('ABCD'),live_cli_and_window_alive=['A'],natural_deaths=['B','C','D']),
    window_complete_snapshot_note="The raw window JSON's 'before' key was captured at the end of the loop; it is not the start snapshot.",
    tested_live=dict(A='skills27/55 require item3037 x1; native inventory no matching slot; CLI validates skill27 cost94/limit0/items=false',
        B='skill51 cost10/limit5/count0/items=true',C='skill46 cost6/limit0/items=true',D='skill4 cost4/limit0/items=true'),
    limited_to_fixtures_and_oracle=['positive/exhausted use counts','nonzero item cost sufficient first stack and duplicate-stack shortage'],
    unsupported=['global input boolean','password56 live: existing PvP business rejects password rooms',
        'type1 level1/flag90 positive live routes','nonempty equipment modifiers and unproved x87 boundaries'],
    ijl_compatibility='Reused prior complete suite at identical0103 SHA, no IJL source change or repeated manual matrix.',
    fixture_stack_fix='26 large legacy nested test snapshots moved to heap after exact Win32 debugger stack confirmation; native stack size and game behavior unchanged.',
    restoration=read(T/'restoration-final.json'),daily_package_deployed=False,held=['U07/GP42','U08']))
files=[dict(path=p.relative_to(A).as_posix(),bytes=p.stat().st_size,sha256=sha(p)) for p in sorted(A.rglob('*')) if p.is_file()]
put('manifest.json',dict(files=files,excludes=['raw normal/private state and accounts','launcher credentials','raw journal','raw encrypted network dumps','raw thread memory']))
print(json.dumps(dict(published_files=len(files)+1,assets=str(A),live_cli=count,sdk_rows=240,protected_files=8,restored_files=14)))
