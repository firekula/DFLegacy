"""Offline ELF crypto factory/roundtrip probe. Harness allocations are NOT live heap.
Original target functions execute under Unicorn; no syscalls, networking or target PID.
"""
import sys,io,struct,hashlib
from pathlib import Path
from elftools.elf.elffile import ELFFile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EAX,UC_X86_REG_ESP,UC_X86_REG_EIP
blob=Path(sys.argv[1]).read_bytes();assert hashlib.sha256(blob).hexdigest()=='486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043'
u=Uc(UC_ARCH_X86,UC_MODE_32)
for s in ELFFile(io.BytesIO(blob)).iter_segments():
 if s['p_type']=='PT_LOAD':
  a=s['p_vaddr'];lo=a&~4095;hi=(a+s['p_memsz']+4095)&~4095;u.mem_map(lo,hi-lo);u.mem_write(a,s.data())
BASE=0x20000000;STACK=0x30000000;STOP=BASE+0xff000;heap=BASE+0x700000
u.mem_map(BASE,0x1000000);u.mem_map(STACK,0x100000)
def rd(a):return struct.unpack('<I',u.mem_read(a,4))[0]
def hook(uc,a,size,data):
 global heap
 sp=uc.reg_read(UC_X86_REG_ESP);ret=None
 if a in (0x8724450,0x8722dc0,0x8725b50):
  n=rd(sp+4);assert n<0x100000;ret=heap;heap+=(n+15)&~15;assert heap<BASE+0xf00000
 elif a in (0x87244f0,0x8724bb0):ret=0
 elif a==0x807dca0:ret=0x1234 # constructor-only random material is overwritten by Initialize below
 elif a in (0x807d8a0,0x807dcc0,0x807dc90):
  dst,src,n=[rd(sp+i) for i in (4,8,12)];assert n<=0x100000
  if a==0x807dc90:
   x,y=bytes(uc.mem_read(dst,n)),bytes(uc.mem_read(src,n));ret=(x>y)-(x<y)
  else:
   if n:uc.mem_write(dst,bytes(uc.mem_read(src,n)) if a==0x807d8a0 else bytes([src&255])*n)
   ret=dst
 if ret is not None:
  uc.reg_write(UC_X86_REG_EAX,ret&0xffffffff);uc.reg_write(UC_X86_REG_ESP,sp+4);uc.reg_write(UC_X86_REG_EIP,rd(sp))
u.hook_add(UC_HOOK_CODE,hook)
def call(va,*args):
 sp=STACK+0xf0000;u.mem_write(sp,struct.pack('<'+'I'*(len(args)+1),STOP,*args));u.reg_write(UC_X86_REG_ESP,sp)
 try:u.emu_start(va,STOP,timeout=120000000,count=50000000)
 except Exception:
  print('FAILED_CALL='+hex(va)+' EIP='+hex(u.reg_read(UC_X86_REG_EIP)));raise
 assert u.reg_read(UC_X86_REG_EIP)==STOP,'instruction/time bound'
 return u.reg_read(UC_X86_REG_EAX)
names=['Shift','Rijndael','BlowFish','Rc6','TwoFish','Tea','Kasumi','XTea','Noekeon','Khazad','Cast5','Skipjack','Multi2','Anubis']
objects=[]
for i,name in enumerate(names):
 obj=call(0x809df90,i);assert obj
 vt=rd(obj);ks=call(rd(vt+0x20),obj);bs=call(rd(vt+0x24),obj)
 key=bytes((i*19+j*37)&255 for j in range(ks));u.mem_write(BASE,key)
 assert call(rd(vt+0xc),obj,BASE,ks)==0x6fffffff
 plain=bytes((j*13+i)&255 for j in range(bs*3));u.mem_write(BASE+0x40000,plain)
 assert call(rd(vt+0x18),obj,BASE+0x40000,len(plain),BASE+0x60000,4096)==0x6fffffff
 assert call(rd(vt+0x1c),obj,BASE+0x60000,len(plain),BASE+0x3000,4096)==0x6fffffff
 assert bytes(u.mem_read(BASE+0x3000,len(plain)))==plain
 objects.append((obj,vt,ks,bs))
 print(f'{i} {name} key={ks} block={bs} ELF_FACTORY_INIT_ENCRYPT_DECRYPT=PASS')
# Actual captured keys, all C frames: compare plaintext CRC with header low byte.
W=Path(__file__).resolve().parent
for folder,cp,sp in [('streams-live','2343-10013.bin','10013-2343.bin'),('streams-live2','14805-10013.bin','10013-14805.bin'),('streams-vm','13681-10013.bin','10013-13681.bin')]:
 s=(W/'captures'/folder/sp).read_bytes();sn=struct.unpack_from('<I',s,3)[0]
 key=bytes((((v<<6)|(v>>2))&255)^181 for v in s[15:sn])[:334];off=0
 for obj,vt,ks,bs in objects:
  u.mem_write(BASE,key[off:off+ks]);assert call(rd(vt+12),obj,BASE,ks)==0x6fffffff;off+=ks
 assert off==334
 b=(W/'captures'/folder/cp).read_bytes();p=0;total=0;used=set()
 while p<len(b):
  n=struct.unpack_from('<I',b,p+3)[0];f=b[p:p+n];assert n>=13 and len(f)==n
  op=struct.unpack_from('<H',f,1)[0];idx=op%14;obj,vt,ks,bs=objects[idx];cipher=f[13:];assert len(cipher)%bs==0
  u.mem_write(BASE+0x40000,cipher)
  assert call(rd(vt+28),obj,BASE+0x40000,len(cipher),BASE+0x60000,4096)==0x6fffffff
  plain=bytes(u.mem_read(BASE+0x60000,len(cipher)));u.mem_write(BASE+0x3000,f[11:13]+plain)
  assert call(0x848d4c8,BASE+0x3000,len(plain)+2)&255==f[7],(folder,total,op)
  used.add(idx);total+=1;p+=n
 print(folder,'CAPTURE_C_FRAMES='+str(total),'PLAINTEXT_CRC=PASS','ALGORITHMS='+str(sorted(used)))
 # All S frames: ciphertext CRC and decrypt/re-encrypt exact byte roundtrip.
 p=0;total=0;used=set()
 while p<len(s):
  n=struct.unpack_from('<I',s,p+3)[0];f=s[p:p+n];assert n>=15 and len(f)==n;op=struct.unpack_from('<H',f,1)[0];cipher=f[15:]
  u.mem_write(BASE+0x40000,cipher or b'\0');assert call(0x848d4c8,BASE+0x40000,len(cipher))&255==f[7]
  if cipher and not (f[0]==0 and op in (1,561)):
   idx=op%14;obj,vt,ks,bs=objects[idx]
   if len(cipher)%bs: print(folder,'S_FRAME_NONBLOCK_ALIGNED',total,op,len(cipher),bs);p+=n;total+=1;continue
   assert call(rd(vt+28),obj,BASE+0x40000,len(cipher),BASE+0x60000,0x10000)==0x6fffffff
   assert call(rd(vt+24),obj,BASE+0x60000,len(cipher),BASE+0x3000,0x10000)==0x6fffffff
   assert bytes(u.mem_read(BASE+0x3000,len(cipher)))==cipher
   used.add(idx)
  total+=1;p+=n
 print(folder,'CAPTURE_S_FRAMES='+str(total),'CIPHER_CRC_AND_ROUNDTRIP=PASS','ALGORITHMS='+str(sorted(used)))
print('LIMIT: same-binary roundtrip, not independent cipher known answers; deterministic constructor rand stub. CRC is only eight bits. No gameplay field semantics claimed.')
