"""Semantic control for explicit isolated C2 test hosts; no process writes/injection."""
import argparse,ctypes,json,math,os,struct,time
from pathlib import Path

GAME=Path('G:/Project/DNF2-native-fulltest-20261109/DNFS1C2').resolve()
ROOT=Path(os.environ['USERPROFILE'])/'AppData/LocalLow/DFLegacy/ChapterTwoTestControl-20261110'
K=ctypes.WinDLL('kernel32',use_last_error=True)
K.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_int,ctypes.c_ulong];K.OpenProcess.restype=ctypes.c_void_p
K.QueryFullProcessImageNameW.argtypes=[ctypes.c_void_p,ctypes.c_ulong,ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_ulong)]
K.GetProcessTimes.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p]
K.GetTickCount64.restype=ctypes.c_ulonglong
K.CloseHandle.argtypes=[ctypes.c_void_p]

def owned(pid,started):
    h=K.OpenProcess(0x1000,False,pid)
    if not h:raise RuntimeError('The exact isolated test host is unavailable')
    try:
        path=ctypes.create_unicode_buffer(32768);length=ctypes.c_ulong(len(path))
        if not K.QueryFullProcessImageNameW(h,0,path,ctypes.byref(length)):raise RuntimeError('Cannot verify host path')
        p=Path(path.value).resolve()
        if p.parent!=GAME or not p.name.startswith('s1c2_chapter2_testcontrol-'):raise RuntimeError('Refused non-test-control process')
        times=[ctypes.c_ulonglong() for _ in range(4)]
        if not K.GetProcessTimes(h,*(ctypes.byref(t) for t in times)) or times[0].value!=started:raise RuntimeError('Process identity changed')
    finally:K.CloseHandle(h)

def read(pid):
    # Windows may briefly deny opening the destination while the native worker
    # atomically replaces it. Retry the read only; never repeat an action here.
    for attempt in range(5):
        try:
            d=json.loads((ROOT/str(pid)/'state.json').read_text())
            break
        except (PermissionError,FileNotFoundError):
            if attempt==4:raise
            time.sleep(.02)
    if d['pid']!=pid or not d['testOnly']:raise RuntimeError('Invalid isolated state identity')
    owned(pid,int(d['processStarted']))
    return d

def require(condition, reason):
    if not condition:raise RuntimeError(reason)

def identity(s):
    return tuple(int(s[k]) for k in ('pid','processStarted','nonce','actor','map','stage','uid'))

def fresh(s, now):
    require(s.get('testOnly') is True and s.get('controlVersion')==2, 'Current private control version required')
    require(all(s.get(k) is True for k in ('enabled','supported','valid')), 'Fresh native dungeon context required')
    require(0<int(s['tick'])<=now and now-int(s['tick'])<=750, 'Expired native snapshot')
    require(0<int(s['uid'])<=65535 and int(s['snapshot'])>0, 'Invalid self identity')

def encode_request(s, operation, target, x, y, now, request_id):
    fresh(s, now)
    require(operation in (1,2,3,4,5) and type(target) is int and 0<=target<=65535, 'Invalid semantic target')
    require((operation==3)==(s.get('scene',3)==1), 'Semantic scene mismatch')
    require(type(x) is int and type(y) is int and -32768<=x<=32767 and -32768<=y<=32767, 'Coordinate overflow')
    if operation==1:
        require(x==y==0, 'Kill cannot carry coordinates')
        require(target in [t['uid'] for t in s['targets']], 'Target is not a live local monster')
    elif operation in (2,3):require(target==s['uid'], 'Position is self only')
    else:
        c=s.get('cards',{});kind='free' if operation==4 else 'paid';unset=4294967295
        require(target<4 and x==y==0 and c.get('available') is True and c.get('count')==4
                and type(c.get('slot')) is int and 0<=c['slot']<4, 'Native card screen required')
        columns=c.get('columns',[])
        require(len(columns)==4 and c.get(kind+'Selected')==c.get(kind+'Pending')==unset,
                'Native card already selected or pending')
        require(columns[target].get(kind+'Visible') is True and columns[target].get(kind+'Owner')==unset
                and all(row.get(kind+'Owner')!=c['slot'] for row in columns), 'Card is hidden or already owned')
    return struct.pack('<IIIIQQQQQIIIIIIii',0x43543244,2,s['pid'],operation,
        int(s['processStarted']),int(s['nonce']),request_id,now,int(s['snapshot']),
        s['actor'],s['map'],s['stage'],s['uid'],target,0,x,y)

def submit(pid, operation, target, x, y, initial):
    """Retry only an explicit no-mutation stale-snapshot refusal."""
    for attempt in range(3):
        s=read(pid)
        require(identity(s)==identity(initial), 'Native context changed before action')
        now=K.GetTickCount64();request_id=max(now,int(s['requestId'])+1)
        payload=encode_request(s,operation,target,x,y,now,request_id)
        directory=ROOT/str(pid);temporary=directory/f'request.{os.getpid()}.pending'
        with temporary.open('xb') as f:f.write(payload)
        temporary.replace(directory/'request.bin')
        deadline=time.monotonic()+2
        while time.monotonic()<deadline:
            result=read(pid)
            if int(result['requestId'])==request_id:break
            time.sleep(.025)
        else:raise RuntimeError('Outcome unknown: no retry without an observed native acknowledgment')
        if result['applied'] or result['reason'] not in ('snapshot','expired'):return result
    raise RuntimeError('No action applied: three fresh-context refusals retained')

def observe_outcome(before, after, operation, target, x, y, minimum_tick=0):
    observed={}
    for pid, prior in before.items():
        current=after.get(pid);same=False;matched=False
        if current:
            same=(identity(current)==identity(prior) and current.get('valid') is True
                  and int(current['snapshot'])>int(prior['snapshot']) and int(current['tick'])>int(prior['tick'])
                  and int(current['tick'])>=minimum_tick)
        if same:
            if operation==2:
                players=[p for p in current['players'] if p['uid']==target]
                position=players[0].get('position') if len(players)==1 else None
                matched=bool(position and all(type(position.get(axis)) in (int,float)
                    and math.isfinite(position[axis]) and abs(position[axis]-value)<=2
                    for axis,value in (('x',x),('y',y))))
            else:
                enemies=[e for e in current['enemies'] if e['uid']==target]
                matched=not enemies or (len(enemies)==1 and enemies[0].get('dead') is True)
        observed[str(pid)]=dict(contextMatched=same,matched=matched)
    return dict(complete=bool(before) and all(r['matched'] for r in observed.values()),clients=observed)

def run_action(pid, operation, *, target=None, x=0, y=0, peers=(), timeout=3):
    pids=[pid,*peers]
    require(1<=len(pids)<=4 and len(set(pids))==len(pids), 'One to four distinct private processes required')
    before={p:read(p) for p in pids};uids=[s['uid'] for s in before.values()]
    require(len(set(uids))==len(uids), 'Distinct player identities required')
    for s in before.values():
        fresh(s,K.GetTickCount64())
        if peers:require(sorted(p['uid'] for p in s['players'])==sorted(uids), 'Every same-room party observer is required')
    selected=pid
    if operation=='kill':
        require(type(target) is int and 0<=target<=65535, 'A single explicit monster UID is required')
        for s in before.values():
            require(any(e['uid']==target and not e['dead'] and e['hp']>0 for e in s['enemies']), 'Live monster required in every observer')
        owners=[p for p,s in before.items() if target in [t['uid'] for t in s['targets']]]
        require(len(owners)==1, 'Exactly one current local monster owner required')
        selected=owners[0];code=1
    elif operation in ('teleport','walk'):
        require(operation!='walk' or not peers, 'Town navigation uses one private observer')
        target=before[pid]['uid'];code=2 if operation=='teleport' else 3
    elif operation in ('free-card','paid-card'):
        require(not peers, 'Card selection invokes one exact private owner')
        code=4 if operation=='free-card' else 5
    else:raise RuntimeError('Unknown semantic operation')
    native=submit(selected,code,target,x,y,before[selected])
    result=dict(pid=selected,target=target,operation=operation,applied=native['applied'],reason=native['reason'],
                status='rejected',synchronization=dict(complete=False,clients={}))
    if not native['applied']:return result
    if code>=4:
        result['status']='native_selection_pending' if native['reason']=='applied_native_card_selection' else 'partial'
        return result
    if code==3:
        result['status']='native_navigation_started' if native['reason']=='applied_native_town_walk' else 'partial'
        return result
    if native['reason']!=('applied_native_setter' if code==1 else 'applied_native_position'):
        result['status']='partial';return result
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        try:after={p:read(p) for p in pids}
        except (OSError,RuntimeError,ValueError,KeyError) as error:
            result.update(status='partial',error=str(error));return result
        result['synchronization']=observe_outcome(before,after,code,target,x,y,int(native.get('tick',0)))
        if result['synchronization']['complete']:
            result['status']='synchronized' if peers else 'native_observed';return result
        if any(identity(after[p])!=identity(before[p]) for p in pids):break
        time.sleep(.05)
    result['status']='partial';return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid',type=int,required=True)
    parser.add_argument('operation',choices=['status','kill','teleport','walk','free-card','paid-card'])
    parser.add_argument('--uid',type=int)
    parser.add_argument('--x',type=int);parser.add_argument('--y',type=int)
    parser.add_argument('--peers',type=int,nargs='*',default=[])
    args=parser.parse_args()
    if args.operation in ('teleport','walk') and (args.x is None or args.y is None):parser.error('Explicit --x and --y coordinates required')
    if args.operation=='status':
        s=read(args.pid)
        print(json.dumps({k:v for k,v in s.items() if k not in ('nonce','processStarted','actor','map','stage')},indent=2));return
    result=run_action(args.pid,args.operation,target=args.uid,x=args.x or 0,y=args.y or 0,peers=args.peers)
    print(json.dumps(result))
    if result['status'] in ('partial','rejected'):raise SystemExit(1)

if __name__=='__main__':main()
