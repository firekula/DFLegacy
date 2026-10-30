"""Bounded native UI observation. QUERY/VM_READ only, no native calls or writes."""
from pathlib import Path
import ctypes as c, hashlib, json, struct, sys
import pefile
import h
K=c.WinDLL('kernel32',use_last_error=True)
K.OpenProcess.argtypes=[c.c_ulong,c.c_int,c.c_ulong];K.OpenProcess.restype=c.c_void_p
K.CloseHandle.argtypes=[c.c_void_p]
K.ReadProcessMemory.argtypes=[c.c_void_p,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
class UI:
 def __init__(self,role):
  self.role=role;self.client,self.started=h.identity(role);self.handle=K.OpenProcess(0x410,False,self.client['Id']);assert self.handle
  self.image=pefile.PE(self.client['Path'],fast_load=True);self.base=self.image.OPTIONAL_HEADER.ImageBase;self.types={}
  assert hashlib.sha256(Path(self.client['Path']).read_bytes()).hexdigest().upper()=='6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5'
 def close(self):K.CloseHandle(self.handle)
 def raw(self,p,n):
  if p<0x10000 or n<0 or n>4096 or p+n>0x100000000:raise ValueError('bounded read')
  b=c.create_string_buffer(n);got=c.c_size_t()
  if not K.ReadProcessMemory(self.handle,p,b,n,c.byref(got)) or got.value!=n:raise ValueError('unreadable')
  return b.raw
 def u(self,p):return struct.unpack('<I',self.raw(p,4))[0]
 def typename(self,vt):
  if vt in self.types:return self.types[vt]
  result=None
  try:
   col=struct.unpack('<I',self.image.get_data(vt-self.base-4,4))[0]
   td=struct.unpack('<I',self.image.get_data(col-self.base+12,4))[0]
   name=self.image.get_data(td-self.base+8,160).split(b'\0')[0]
   if name.startswith((b'.?AV',b'.?AU')):result=name.decode('ascii')
  except (ValueError,struct.error,UnicodeError,pefile.PEFormatError):pass
  self.types[vt]=result;return result
 def control(self,p):
  b=self.raw(p,36);vt,unknown,kind,ident=struct.unpack_from('<IIII',b);name=self.typename(vt)
  x,y,w,z=struct.unpack_from('<iiii',b,20)
  if not name or b[16]>1 or b[17]>1 or not (0<w<=4096 and 0<z<=4096 and -640<=x<=1280 and -480<=y<=960):raise ValueError('not supported control')
  return dict(pointer=p,type=name,kind=kind,id=ident,visible=bool(b[16]),enabled=bool(b[17]),rect=[x,y,w,z],center=[x+w//2,y+z//2])
 def tree(self,root):
  rows=[];seen=set();pending=[(root,0)]
  while pending:
   p,depth=pending.pop()
   if p in seen or depth>8 or len(seen)>=256:raise ValueError('control tree bound')
   seen.add(p);row=self.control(p);row['depth']=depth;rows.append(row)
   head,count=struct.unpack('<II',self.raw(p+0x64,8))
   if count>128:raise ValueError('control count bound')
   if not head:
    if count:raise ValueError('null children')
    continue
   n=self.u(head);visited=set()
   while n!=head:
    if n in visited or len(visited)>=count:raise ValueError('child cycle/count')
    visited.add(n);nxt,prior,child=struct.unpack('<III',self.raw(n,12))
    if child:pending.append((child,depth+1))
    n=nxt
   if len(visited)!=count:raise ValueError('child count changed')
  return rows
 def inspect(self):
  h.identity(self.role);im=self.u(0xB5DCE8);block=self.raw(im,0x1000);objects=[];roots={};candidates=[]
  for offset in range(0,len(block),4):
   p=struct.unpack_from('<I',block,offset)[0]
   if p<0x10000:continue
   try:typ=self.typename(self.u(p))
   except ValueError:continue
   if not typ:continue
   objects.append(dict(offset=hex(offset),pointer=p,type=typ))
   if any(v in typ.lower() for v in ('window','message','request','box','menu')):candidates.append((hex(offset),p,typ))
  # Original799E20/799EF0: per-class16-byte vector, begin IM+D8+class*16.
  for cls in range(0xE2):
   try:
    begin,end,cap=struct.unpack('<III',self.raw(im+0xD8+cls*16,12))
    if not begin or end<begin or cap<end or (end-begin)%4 or (end-begin)//4>32:continue
    for j in range((end-begin)//4):
     p=self.u(begin+4*j);typ=self.typename(self.u(p))
     if typ:candidates.append(('class:'+hex(cls),p,typ))
   except ValueError:continue
  for offset,p,typ in candidates:
   for field in ([0x58] if 'PopupMenu' in typ else ([8] if offset.startswith('class:') else [4])):
    try:
     root=self.u(p+field)
     if root in roots:continue
     panel=self.control(root)
     if not panel['visible'] or not any(x in panel['type'] for x in ('Frame','IControl')):continue
     controls=self.tree(root)
     roots[root]=dict(owner_type=typ,owner_pointer=p,owner_offset=offset,panel_field=hex(field),panel=panel,controls=controls)
    except ValueError:continue
  h.identity(self.role)
  if self.u(0xB5DCE8)!=im:raise ValueError('UI manager changed')
  return dict(role=self.role,pid=self.client['Id'],tick_ms=h.state.now_tick_ms(),manager=im,objects=objects,visible_panels=list(roots.values()))
if __name__=='__main__':
 role,label=sys.argv[1:3];u=UI(role)
 try:out=u.inspect();h.save(label,out)
 finally:u.close()
 print(json.dumps(dict(role=role,panels=[dict(owner=x['owner_type'],slot=x['owner_offset'],panel=x['panel'],buttons=[c for c in x['controls'] if 'Button' in c['type'] and c['visible'] and c['enabled']]) for x in out['visible_panels']]),ensure_ascii=True))
