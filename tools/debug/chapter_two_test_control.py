"""Semantic control for explicit isolated C2 test hosts; no process writes/injection."""
import argparse,ctypes,json,os,struct,time
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

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid',type=int,required=True)
    parser.add_argument('operation',choices=['status','kill'])
    parser.add_argument('--uid',type=int)
    args=parser.parse_args()
    s=read(args.pid)
    if args.operation=='status':
        print(json.dumps({k:v for k,v in s.items() if k not in ('nonce','processStarted','actor','map','stage')},indent=2));return
    if args.uid is None or not 0<=args.uid<=65535:raise RuntimeError('A single explicit monster UID is required')
    initial=(s['actor'],s['map'],s['stage'],s['uid'])
    for attempt in range(3):
        s=read(args.pid)
        if not all(s.get(k) for k in ('enabled','supported','valid')) or (s['actor'],s['map'],s['stage'],s['uid'])!=initial:raise RuntimeError('Fresh native dungeon context required')
        if args.uid not in [t['uid'] for t in s['targets']]:raise RuntimeError('Target is not a live locally owned monster')
        now=K.GetTickCount64();request_id=max(now,int(s['requestId'])+1)
        payload=struct.pack('<IIIIQQQQQIIIIII',0x43543244,1,args.pid,1,int(s['processStarted']),int(s['nonce']),request_id,now,int(s['snapshot']),s['actor'],s['map'],s['stage'],s['uid'],args.uid,0)
        assert len(payload)==80
        directory=ROOT/str(args.pid);temporary=directory/f'request.{os.getpid()}.pending'
        with temporary.open('xb') as f:f.write(payload)
        temporary.replace(directory/'request.bin')
        deadline=time.monotonic()+2
        while time.monotonic()<deadline:
            result=read(args.pid)
            if int(result['requestId'])==request_id:break
            time.sleep(.025)
        else:raise RuntimeError('Outcome unknown: no retry without an observed native acknowledgment')
        if result['applied']==1:
            print(json.dumps(dict(pid=args.pid,target=args.uid,applied=1,reason=result['reason'])));return
        if result['reason'] not in ('snapshot','expired'):raise RuntimeError('Native request refused: '+result['reason'])
    raise RuntimeError('No action applied: three fresh-context refusals retained')

if __name__=='__main__':main()
