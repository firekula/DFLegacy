from pathlib import Path
import collections,hashlib,json,re,struct,subprocess
T=Path('G:/Game/DNF_ACT1/DFLegacy/.tmp/u05-final-20261030')
r=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
j=r(T/'deployment.json');assert j['Status']=='stopped' and j['ObservedActiveChannelSessionsBeforeStop']==0
for name in ['replacement-backups.json','protected-files.json']:
    for row in r(T/'preflight'/name):assert sha(Path(row['Path']))==row['Sha256']
assert subprocess.run(['powershell','-NoProfile','-Command',
    'if (@(Get-Process -Name DNF,DFLegacy.Server,DFLegacy.Launcher -ErrorAction SilentlyContinue).Count) {exit 1}; exit 0'],capture_output=True).returncode==0
line=re.compile(r'^.*? T=\d+ TX TCP (\S+) -> (\S+) len=(\d+)(.*?) hex=([A-Fa-f0-9]*)$')
audit={}
for client in j['Clients']:
    path=T/'closed-resource-v1'/(client['Role']+'-netdump.log');streams=collections.defaultdict(bytearray)
    counts=collections.Counter();segments=frames=0
    for text in path.read_text(encoding='utf-8').splitlines():
        match=line.match(text)
        if not match:continue
        local,remote,length,extra,raw=match.groups()
        if remote.rsplit(':',1)[-1]!='47001':continue
        data=bytes.fromhex(raw);assert len(data)==int(length) and not extra.strip()
        pending=streams[(local,remote)];pending.extend(data);segments+=1
        while len(pending)>=10:
            kind,protocol=struct.unpack_from('<BB',pending);size=struct.unpack_from('<I',pending,2)[0]
            assert kind==1 and 10<=size<=4*1024*1024
            if len(pending)<size:break
            counts[protocol]+=1;frames+=1;del pending[:size]
    assert segments and all(not pending for pending in streams.values())
    forbidden={str(code):counts[code] for code in [29,44,50,63,64,97,98]}
    assert all(value==0 for value in forbidden.values()),(client['Role'],forbidden)
    audit[client['Role']]=dict(segments=segments,frames=frames,forbidden_counts=forbidden,netdump_sha256=sha(path))
before=r(Path(r(T/'setup-v1.json')['SourceClosedState']));after=r(T/'closed-resource-v1/state.json')
prior={x['Id']:x for x in before['Accounts']}
for account in after['Accounts']:
    for key in ['Cash','Friends','Blacklist']:assert prior[account['Id']][key]==account[key]
safe=r(T/'evidence-resource-v1/resource-safe-room-setup.json')
summary=dict(scope='Strict from-byte-zero TX TCP10-byte headers to private channel47001; encrypted bodies not decoded. No transfer/commit inference.',
    counts=audit,forbidden=dict(use_lottery_item=29,revive_coin_command=44,inventory_drop=50,add_friend=63,remove_friend=64,send_mail=97,extract_attachment=98),
    private_account_cash_friends_blacklist_unchanged=len(after['Accounts']),
    private_combat_metadata='Natural deaths, monster cleanup/EXP/weakness/last-seen may change; no claim that the whole private state is unchanged.',
    setup_monster_cleanup=[dict(monster=x['monster'],status=x['result'].get('status'),error=x['result'].get('error')) for x in safe['results']],
    restoration=r(T/'restoration-final.json'),owned_processes_running=0)
with (T/'closeout-audit.json').open('x',encoding='utf-8') as f:json.dump(summary,f,indent=2)
print(json.dumps(dict(header_audit=audit,accounts_unchanged=len(after['Accounts']),normal_save_sha256=summary['restoration']['normal_save_sha256'])))
