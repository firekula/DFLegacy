"""Bounded read-only research of this run's native gates, separate from SDK data."""
import ctypes as c,hashlib,json,threading,time,sys
from pathlib import Path
sys.path.insert(0,'G:/Game/DNF_ACT1/DFLegacy/.tmp/u05-final-20261030');import h
K=c.WinDLL('kernel32',use_last_error=True)
K.OpenProcess.argtypes=[c.c_ulong,c.c_int,c.c_ulong];K.OpenProcess.restype=c.c_void_p
K.ReadProcessMemory.argtypes=[c.c_void_p,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
K.CloseHandle.argtypes=[c.c_void_p]

class Sampler:
 def __init__(self,role,seconds=25,period_ms=10):
  assert role in 'ABCD' and 0<seconds<=40 and 2<=period_ms<=20
  self.period_ms=period_ms;self.role=role;self.seconds=seconds;self.events=[];self.errors=[];self.stop=threading.Event();self.ready=threading.Event()
  self.client,self.start=h.identity(role)
  assert hashlib.sha256(Path(self.client['Path']).read_bytes()).hexdigest()=='6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5'
  self.handle=K.OpenProcess(0x1010,False,self.client['Id']);assert self.handle
  self.calls=0;self.samples=0;self.last=None
 def read(self,a,n=4):
  self.calls+=1
  if not a or a<0x10000 or n>4096 or a+n>0x100000000:return None
  out=c.create_string_buffer(n);got=c.c_size_t()
  return out.raw if K.ReadProcessMemory(self.handle,c.c_void_p(a),out,n,c.byref(got)) and got.value==n else None
 def u(self,a,n=4):
  b=self.read(a,n);return int.from_bytes(b,'little') if b is not None else None
 def secure(self,a):
  key=self.u(0xCFD45C);manager=self.u(0xCFD458);enc=self.u(a);idx=self.u(a+4)
  if None in (key,manager,enc,idx) or not manager:return None
  count=self.u(manager+4)
  if count is None or idx>=count or idx>0x1000000 or ((enc^key)-a)&0xffffffff!=idx:return None
  one=self.u(manager+0x44);two=self.u(manager+0x48)
  if not one or not two:return None
  a1=one+idx*4;a2=two+idx*4;v1=self.u(a1);v2=self.u(a2)
  if v1 is None or v2 is None:return None
  v1^=a1^key;v2^=a2^key
  return v1 if v1==v2 else None
 def state(self):
  deferred=self.u(0xCFD208,1);game=self.u(0xB5D6F0);scene=self.secure(game+0x10) if game else None
  actor=self.u(0xB75A14)
  row=dict(scene=scene,mouse_button_deferred=bool(deferred) if deferred in (0,1) else None,actor_token=hex(actor) if actor else None,command=None)
  if not actor:return row
  table=self.u(actor);kind=self.u(actor+0xD0);category=self.u(actor+0xCC);uid=self.secure(actor+0xD4)
  if not table or self.u(table+0x5D4)!=0x493770 or category!=0x111 or kind not in (0,0x111) or uid is None or uid>65535:return row
  raw=self.u(actor+0x2384)
  if raw is None:return row
  checker=raw or 0xB5DE68
  slots={0x28:0x8BA0C0,0x30:0x8BA7F0,0x40:0x8BA030,0x44:0x43BC30}
  if self.u(checker)!=0x99A88C or any(self.u(0x99A88C+off)!=fn for off,fn in slots.items()):return row
  keyboard=self.u(checker+0x64);mouse=self.u(checker+0x68);flag=self.u(checker+0x90,1)
  if keyboard is None or mouse is None or flag not in (0,1):return row
  if self.u(0xB75A14)!=actor or self.u(actor+0x2384)!=raw or self.secure(actor+0xD4)!=uid:return row
  if self.u(checker+0x64)!=keyboard or self.u(checker+0x68)!=mouse or self.u(checker+0x90,1)!=flag:return row
  row['command']=dict(uid=uid,checker_token=hex(checker),keyboard_lock_level=keyboard,mouse_lock_level=mouse,
      checker_flag90_raw=bool(flag),physical_excluded=bool(mouse or flag),sdk_uid_bound=uid!=0)
  return row
 def run(self):
  try:
   assert self.read(0x8FCAE8,5)==bytes.fromhex('a008d2cf00')
   assert self.read(0x7993C4,7)==bytes.fromhex('c60508d2cf0001')
   assert self.read(0x8FCC9D,7)==bytes.fromhex('c60508d2cf0000')
   self.ready.set();began=time.monotonic();last_identity=began;last_saved=0
   while not self.stop.is_set() and time.monotonic()-began<self.seconds and self.samples<4000:
    now=time.monotonic()
    if now-last_identity>.5:h.identity(self.role);last_identity=now
    row=self.state();self.samples+=1
    if row!=self.last or now-last_saved>.1:
     self.events.append(dict(tick_ms=h.state.now_tick_ms(),**row));self.last=row;last_saved=now
    self.stop.wait(self.period_ms/1000)
  except Exception as error:self.errors.append(repr(error));self.ready.set()
  finally:K.CloseHandle(self.handle)
 def start_thread(self):
  self.thread=threading.Thread(target=self.run);self.thread.start();assert self.ready.wait(2);assert not self.errors;return self
 def finish(self,label):
  self.stop.set();self.thread.join();h.identity(self.role)
  proof=dict(role=self.role,pid=self.client['Id'],process_started_filetime=self.start,sampling='read-only owned-process research; not SDK history',
      period_ms=self.period_ms,max_seconds=self.seconds,samples=self.samples,read_calls=self.calls,events=self.events,errors=self.errors)
  h.save(label,proof)
  return proof
