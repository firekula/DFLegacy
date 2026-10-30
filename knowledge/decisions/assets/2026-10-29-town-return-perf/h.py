"""Root-owned native acceptance: file API + original IJL input, no external writes."""
from pathlib import Path
import ctypes as c, datetime, hashlib, json, re, subprocess, sys, time
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,'D:/codex_worktree/party-chat-gold/DFLegacy')
from tools.debug import client_state as state, client_navigation as nav
JOURNAL=(ROOT/'deployment.json').read_bytes()
J=json.loads(JOURNAL.decode('utf-8-sig'))
assert J['Status']=='running'
DIRECTORY=ROOT/('snapshots-'+J['Phase'])
EVIDENCE=ROOT/('evidence-'+J['Phase'])
EVIDENCE.mkdir(exist_ok=True)
def expected_start(text):
    main,_,fraction=text.removesuffix('Z').partition('.')
    stamp=datetime.datetime.fromisoformat(main)
    delta=stamp-datetime.datetime(1601,1,1)
    return (delta.days*86400+delta.seconds)*10000000+int((fraction+'0000000')[:7])
def identity(role):
    assert (ROOT/'deployment.json').read_bytes()==JOURNAL
    client=next(v for v in J['Clients'] if v['Role']==role)
    expected=expected_start(client['StartTimeUtc'])
    assert state.get_process_started_filetime(client['Id'])==expected
    kernel=c.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.argtypes=[c.c_ulong,c.c_int,c.c_ulong];kernel.OpenProcess.restype=c.c_void_p
    kernel.QueryFullProcessImageNameW.argtypes=[c.c_void_p,c.c_ulong,c.c_wchar_p,c.POINTER(c.c_ulong)]
    kernel.CloseHandle.argtypes=[c.c_void_p]
    handle=kernel.OpenProcess(0x1000,False,client['Id'])
    assert handle
    try:
        name=c.create_unicode_buffer(32768);size=c.c_ulong(len(name))
        assert kernel.QueryFullProcessImageNameW(handle,0,name,c.byref(size))
        assert Path(name.value)==Path(client['Path'])
    finally:kernel.CloseHandle(handle)
    assert state.get_process_started_filetime(client['Id'])==expected
    return client,expected
def snapshot(role):
    for attempt in range(6):
        client,start=identity(role)
        try:return state.load_snapshot(DIRECTORY/f"dfdebug-client-{client['Id']}.json",pid=client['Id'],process_started_filetime=start,now_tick_ms=state.now_tick_ms())
        except state.SnapshotError as error:
            if error.code!='unreadable_json' or attempt==5:raise
            time.sleep(.035)
def save(label,proof):
    assert re.fullmatch('[A-Za-z0-9_-]{1,80}',label)
    proof.update(phase=J['Phase'],build_manifest_sha256=J['BuildManifestSha256'],journal_sha256=hashlib.sha256(JOURNAL).hexdigest().upper())
    with (EVIDENCE/(label+'.json')).open('x',encoding='utf-8') as file:json.dump(proof,file,indent=2)
def response_witness(role,label):
    client,_=identity(role);path=DIRECTORY/f"dfdebug-navigation-{client['Id']}.json"
    if not path.exists():return None
    for attempt in range(6):
        try:raw=path.read_bytes();value=json.loads(raw);break
        except (OSError,ValueError):
            if attempt==5:raise
            time.sleep(.035)
    dest=EVIDENCE/(label+'-native-response.json')
    with dest.open('xb') as file:file.write(raw)
    return dict(file=dest.name,sha256=hashlib.sha256(raw).hexdigest().upper(),response=value)
def move(role,label,x,y,wait='arrived'):
    before=snapshot(role);client,start=identity(role)
    began=state.now_tick_ms()
    try:result=nav.execute(DIRECTORY,pid=client['Id'],uid=before['me']['uid'],started=start,x=x,y=y,wait=wait,timeout_seconds=8)
    except nav.NavigationError as error:result=dict(ok=False,status='consumer_error',reason=error.code)
    after=snapshot(role);proof=dict(role=role,before=before,result=result,after=after,elapsed_ms=state.now_tick_ms()-began,native_response=response_witness(role,label))
    save(label,proof);print(json.dumps(dict(role=role,result=result,elapsed_ms=proof['elapsed_ms'],town=after['town'])))
    return proof
def keys(role,label,steps,shot=False):
    before=snapshot(role);identity(role)
    assert before['scene'] in (1,3) and before['me']
    cmd=[sys.executable,str(ROOT/'control.py'),role,label+'-input',json.dumps(steps)]
    if not shot:cmd.append('--no-shot')
    subprocess.run(cmd,check=True)
    acknowledged=state.now_tick_ms();deadline=time.monotonic()+5
    while True:
        try:after=snapshot(role)
        except state.SnapshotError as error:
            if error.code not in ('unknown_scene','stale','invalid_capture','unreadable_json') or time.monotonic()>deadline:raise
            time.sleep(.04);continue
        if after['captured_tick_ms']>acknowledged:break
        if time.monotonic()>deadline:raise RuntimeError('No fresh post-input capture')
        time.sleep(.025)
    save(label,dict(role=role,before=before,after=after,input_label=label+'-input',acknowledged_tick_ms=acknowledged,native_response=response_witness(role,label)))
    print(json.dumps(dict(role=role,town=after['town'],ui=after['ui'])))
if __name__=='__main__':
    mode=sys.argv[1]
    if mode=='move':move(sys.argv[2],sys.argv[3],int(sys.argv[4]),int(sys.argv[5]),sys.argv[6] if len(sys.argv)>6 else 'arrived')
    elif mode=='keys':keys(sys.argv[2],sys.argv[3],json.loads(sys.argv[4]),len(sys.argv)>5 and sys.argv[5]=='shot')
    elif mode=='read':
        rows={role:snapshot(role) for role in ('A','B')};save(sys.argv[2],dict(snapshots=rows))
        print(json.dumps({role:dict(town=v['town'],ui=v['ui']) for role,v in rows.items()}))
    else:raise ValueError('Unsupported action')
