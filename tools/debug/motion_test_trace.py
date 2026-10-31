"""Read the default-off ACT1 XY refusal ring without remote calls or writes."""
import argparse
import ctypes as c
import json
from pathlib import Path
import struct
from . import client_state as state

SIZE=64+256*64
class MotionTraceError(ValueError):pass
def require(ok,code):
    if not ok:raise MotionTraceError(code)
def decode_block(raw,*,pid,started,now):
    require(len(raw)==SIZE,'bad_size')
    magic,version,header,event_size,capacity,owner,created,revision,enabled,serial,drops,overflow=struct.unpack_from('<6IQ2I3Q',raw)
    require(version in (1,2) and (magic,header,event_size,capacity)==(0x4D543031,64,64,256),'bad_header')
    require(owner==pid and created==started,'identity_mismatch')
    require(not revision%2 and enabled in (0,1),'torn_or_bad_header')
    events=[];previous=0
    for n in range(max(1,serial-255),serial+1):
        e=struct.unpack_from('<QQ4I2i6I',raw,64+(n-1)%256*64)
        s,t,thread,actor,controller,method,x,y,result,kind,map_,encoded,index,axis=e
        require(s==n and previous<=t<=now and thread>0 and actor>=0x10000 and controller>=0x10000
            and method==0x4B3C30 and result==0 and kind==529 and map_>=0x10000 and axis in (0,1),'bad_event')
        if version==2:require(encoded in (0,1,2,3,4) and (encoded==4 and index!=5 or encoded!=4 and index==0),'bad_native_path')
        path={0:'unknown_exit',1:'independent_bounds',2:'first_grid_query',3:'second_grid_query',4:'state_gate'}.get(encoded) if version==2 else None
        previous=t;events.append(dict(serial=s,tick_ms=t,thread=thread,actor_token=actor,controller_token=controller,
            native_method=method,x=x,y=y,result=False,object_kind=kind,map_token=map_,uid_encoded=encoded,uid_index=index,axis='xy'[axis]))
        events[-1].update(native_path=path,native_state_value=index if path=='state_gate' else None)
        if version==2:events[-1].pop('uid_encoded');events[-1].pop('uid_index')
    return dict(schema=f'dfl-motion-test-trace/v{version}',pid=pid,process_started_filetime=started,enabled=bool(enabled),serial=serial,
        lock_drops=drops,overflows=overflow,overwrites=max(0,serial-256),events=events,latest_age_ms=now-previous if events else None,
        scope='Original 4C8EBC XY v94 call with actual step candidate and original false return. Bound mapped monsters using 4B3C30 only. No Z/v90 events. V2 records the original state/grid/bounds exit, not the individual grid-cell predicate. No provider/GP42 branch is inferred.')
def new_events(value,after):return [e for e in value['events'] if e['serial']>after]

def grid_cells(x,y):
    """Candidate cell coordinates; caller must supply observed v94 arguments."""
    def divide(n,d):return n//d if n>=0 else -((-n)//d)
    plus=((y+1+0x80000000)&0xffffffff)-0x80000000
    return divide(x,16),divide(y,12),divide(plus,12)

def select_patch_module(modules,exe):
    directory=Path(exe).resolve().parent
    candidates={'dflegacy.client.dll':[],'ijl15.dll':[]}
    for base,path in modules:
        path=Path(path).resolve();name=path.name.lower()
        if name not in candidates:continue
        require(path.parent==directory,'foreign_patch_module')
        candidates[name].append(base)
    for name in ('dflegacy.client.dll','ijl15.dll'):
        require(len(candidates[name])<=1,'ambiguous_patch_module')
    selected=candidates['dflegacy.client.dll'] or candidates['ijl15.dll']
    require(len(selected)==1,'unique_patch_required')
    return selected[0]

class Reader:
    def __init__(self,pid,started,exe,*,symbol=b'DflMotionTestTrace',block_size=SIZE):
        self.pid=pid;self.started=started;self.handle=None
        self.symbol=symbol;self.block_size=block_size
        require(state.get_process_started_filetime(pid)==started,'identity_mismatch')
        self.k=c.WinDLL('kernel32',use_last_error=True)
        self.k.OpenProcess.argtypes=[c.c_ulong,c.c_int,c.c_ulong];self.k.OpenProcess.restype=c.c_void_p
        self.k.CloseHandle.argtypes=[c.c_void_p]
        self.k.ReadProcessMemory.argtypes=[c.c_void_p,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
        self.k.QueryFullProcessImageNameW.argtypes=[c.c_void_p,c.c_ulong,c.c_wchar_p,c.POINTER(c.c_ulong)]
        self.k.K32EnumProcessModulesEx.argtypes=[c.c_void_p,c.c_void_p,c.c_ulong,c.POINTER(c.c_ulong),c.c_ulong]
        self.k.K32GetModuleFileNameExW.argtypes=[c.c_void_p,c.c_void_p,c.c_wchar_p,c.c_ulong]
        self.handle=self.k.OpenProcess(0x410,False,pid);require(self.handle,'open_process_failed')
        try:
            name=c.create_unicode_buffer(32768);size=c.c_ulong(len(name))
            require(self.k.QueryFullProcessImageNameW(self.handle,0,name,c.byref(size)),'query_image_failed')
            require(Path(name.value).resolve()==Path(exe).resolve(),'image_mismatch')
            modules=(c.c_void_p*1024)();needed=c.c_ulong()
            require(self.k.K32EnumProcessModulesEx(self.handle,modules,c.sizeof(modules),c.byref(needed),3)
                and needed.value<=c.sizeof(modules),'module_list_failed')
            candidates=[]
            for base in modules[:needed.value//c.sizeof(c.c_void_p)]:
                require(self.k.K32GetModuleFileNameExW(self.handle,c.c_void_p(base),name,len(name)),'module_name_failed')
                candidates.append((base,name.value))
            self.base=select_patch_module(candidates,exe);self.address=self._export()
        except BaseException:self.close();raise
    def close(self):
        if self.handle:self.k.CloseHandle(self.handle);self.handle=None
    def read(self,address,n):
        require(0x10000<=address<0x100000000 and 0<n<=max(SIZE,self.block_size) and address+n<=0x100000000,'bad_read')
        b=c.create_string_buffer(n);got=c.c_size_t()
        require(self.k.ReadProcessMemory(self.handle,c.c_void_p(address),b,n,c.byref(got)) and got.value==n,'read_failed')
        return b.raw
    def _export(self):
        base=self.base;dos=self.read(base,64);require(dos[:2]==b'MZ','bad_pe')
        nt=struct.unpack_from('<I',dos,60)[0];require(0x40<=nt<4096,'bad_pe')
        pe=self.read(base+nt,128);require(pe[:4]==b'PE\0\0' and struct.unpack_from('<H',pe,24)[0]==0x10B,'bad_pe')
        image=struct.unpack_from('<I',pe,80)[0];rva,size=struct.unpack_from('<II',pe,120)
        require(0<rva<image and 40<=size<=image-rva,'bad_export')
        def remote(off,n):
            require(0<off<image and n<=image-off,'bad_export');return self.read(base+off,n)
        e=remote(rva,40);functions,names,fa,na,oa=struct.unpack_from('<5I',e,20)
        require(0<names<=functions<=512,'bad_export')
        name_rvas=struct.unpack('<'+'I'*names,remote(na,names*4));matches=[]
        for i,n in enumerate(name_rvas):
            text=remote(n,min(128,image-n)).split(b'\0',1)[0]
            if text.lstrip(b'_')==self.symbol:
                ordinal=struct.unpack_from('<H',remote(oa+i*2,2))[0];require(ordinal<functions,'bad_export')
                target=struct.unpack_from('<I',remote(fa+ordinal*4,4))[0]
                require(not rva<=target<rva+size and 0<target<=image-self.block_size,'forwarded_or_small_export');matches.append(base+target)
        require(len(matches)==1,'unsupported_ijl');return matches[0]
    def copy_stable(self):
        require(state.get_process_started_filetime(self.pid)==self.started,'identity_mismatch')
        for _ in range(6):
            before=self.read(self.address+32,4);raw=self.read(self.address,self.block_size);after=self.read(self.address+32,4)
            if before==raw[32:36]==after and not struct.unpack('<I',before)[0]%2:
                return raw
        raise MotionTraceError('concurrent_write')
    def snapshot(self):return decode_block(self.copy_stable(),pid=self.pid,started=self.started,now=state.now_tick_ms())

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--pid',type=int,required=True);p.add_argument('--exe',required=True);p.add_argument('--after-serial',type=int,default=0);a=p.parse_args(argv);reader=None
    try:
        reader=Reader(a.pid,state.get_process_started_filetime(a.pid),a.exe);v=reader.snapshot();v['events']=new_events(v,a.after_serial)
        print(json.dumps(dict(ok=True,**v)));return 0
    except (MotionTraceError,state.SnapshotError,OSError) as e:print(json.dumps(dict(ok=False,error=str(e))));return 1
    finally:
        if reader:reader.close()
if __name__=='__main__':raise SystemExit(main())
