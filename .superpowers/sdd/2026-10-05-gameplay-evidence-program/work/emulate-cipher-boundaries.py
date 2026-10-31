"""Bounded offline x86 execution of exact ELF functions; no host execution, syscalls or live target.
Usage: python emulate-cipher-boundaries.py <vm-elf>
Only memcpy/memset/strlen PLT are stubbed. Unexpected unmapped calls fail; max 2M instructions/call.
"""
from pathlib import Path
import sys,hashlib,struct,ast,io
from elftools.elf.elffile import ELFFile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EAX
W=Path(__file__).resolve().parent
blob=Path(sys.argv[1]).read_bytes();sha=hashlib.sha256(blob).hexdigest();assert sha=='486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043'
e=ELFFile(io.BytesIO(blob));u=Uc(UC_ARCH_X86,UC_MODE_32)
for seg in e.iter_segments():
 if seg['p_type']=='PT_LOAD':
  a=seg['p_vaddr'];lo=a&~4095;end=(a+seg['p_memsz']+4095)&~4095;u.mem_map(lo,end-lo);u.mem_write(a,seg.data())
BASE=0x20000000;STACK=0x30000000;STOP=BASE+0xff000
u.mem_map(BASE,0x100000);u.mem_map(STACK,0x100000)
def rd(a):return struct.unpack('<I',u.mem_read(a,4))[0]
def hook(uc,a,size,data):
 if a==0x807e3b0:
  sp=uc.reg_read(UC_X86_REG_ESP);p=rd(sp+4);n=0
  while bytes(uc.mem_read(p+n,1))!=b'\0':
   n+=1;assert n<4096
  uc.reg_write(UC_X86_REG_EAX,n);uc.reg_write(UC_X86_REG_ESP,sp+4);uc.reg_write(UC_X86_REG_EIP,rd(sp))
 if a in (0x807d8a0,0x807dcc0):
  sp=uc.reg_read(UC_X86_REG_ESP);dst,src,n=[rd(sp+i) for i in (4,8,12)];assert n<=4096
  uc.mem_write(dst,bytes(uc.mem_read(src,n)) if a==0x807d8a0 else bytes([src&255])*n);uc.reg_write(UC_X86_REG_EAX,dst);uc.reg_write(UC_X86_REG_ESP,sp+4);uc.reg_write(UC_X86_REG_EIP,rd(sp))
u.hook_add(UC_HOOK_CODE,hook)
def call(va,*args):
 sp=STACK+0xf0000;u.mem_write(sp,struct.pack('<'+'I'*(len(args)+1),STOP,*args));u.reg_write(UC_X86_REG_ESP,sp)
 u.emu_start(va,STOP,timeout=3000000,count=2000000)
 assert u.reg_read(UC_X86_REG_EIP)==STOP, 'emulation limit reached'
 return u.reg_read(UC_X86_REG_EAX)
def definitions(file):
 tree=ast.parse((W/file).read_text(encoding='utf-8'));ns={'M':0xffffffff}
 exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef)],type_ignores=[]),file,'exec'),ns);return ns
sf=definitions('verify-pi-boundary.py')['sf']
print('ELF_SHA256='+sha)
# Independent instruction execution versus reconstructed SuperFastHash across all remainder branches.
for n in range(1,260):
 d=bytes((i*197+n)&255 for i in range(n));u.mem_write(BASE,d);call(0x808c174,BASE,n,BASE+4096);assert rd(BASE+4096)==sf(d)
print('SUPERFAST_ELF_VS_PYTHON_259_LENGTHS=PASS')
# Exact in-place ordering for every possible byte, with zero state.
u.mem_write(BASE,bytes(range(256)));u.mem_write(BASE+4096,b'\0'*4);call(0x848da70,BASE,256,BASE+4096)
want=bytes((((x^181)>>6)|((x^181)<<2))&255 for x in range(256));assert bytes(u.mem_read(BASE,256))==want
print('NOTI1_XOR_THEN_ROR_ALL_BYTES=PASS')
# Execute recv PI function on actual ciphertext streams, retaining per-direction object state.
for rel in ['streams-live/2343-10013.bin','streams-live2/14805-10013.bin','streams-vm/13681-10013.bin']:
 b=(W/'captures'/rel).read_bytes();u.mem_write(BASE+8192,b'\0'*32);p=0;count=0
 while p<len(b):
  n=struct.unpack_from('<I',b,p+3)[0];f=b[p:p+n];assert n>=13 and len(f)==n
  u.mem_write(BASE,f[13:]);header=struct.unpack_from('<I',f,7)[0];reset=int(f[:3]==b'\1\1\0')
  assert call(0x808bf7e,BASE+8192,BASE,n-13,header>>8,reset)&255==1
  p+=n;count+=1
 print(rel,'ELF_PI_RECEIVER_FRAMES='+str(count)+' PASS')
from Crypto.Cipher import AES
obj=BASE+0x10000
call(0x80c32f4,obj)
assert call(0x80c339c,obj,0x8c179e7,16,0x8b23368,0,0)==0x6fffffff
u.mem_write(0x941f700,struct.pack('<I',obj))
s=(W/'captures/streams-live/10013-2343.bin').read_bytes()[:450]
k=bytes((((v<<6)|(v>>2))&255)^181 for v in s[15:])[:334]
c=(W/'captures/streams-live/2343-10013.bin').read_bytes()[:413]
plain=AES.new(k[8:24],AES.MODE_ECB).decrypt(c[13:])
u.mem_write(BASE,plain[365:373]);u.mem_write(BASE+4096,b'\0'*16)
assert call(0x826333a,BASE,BASE+4096,8)==1
mouse=bytes(u.mem_read(BASE+4096,8))
print('MOUSE_CTEA_ELF_DECRYPT=PASS OUTPUT_SHA256='+hashlib.sha256(mouse).hexdigest())
# Independent translation of exact block instructions, not selected by class name.
def xtea_dec(block,key):
 a,b=struct.unpack('>II',block);kw=struct.unpack('>IIII',key);s=0xc6ef3720;mask=0xffffffff
 for _ in range(32):
  b=(b-((((a<<4)^(a>>5))+a)^((s+kw[(s>>11)&3])&mask)))&mask
  s=(s-0x9e3779b9)&mask
  a=(a-((((b<<4)^(b>>5))+b)^((s+kw[s&3])&mask)))&mask
 return struct.pack('>II',a,b)
static_key=bytes(u.mem_read(0x8c179e7,16))
assert xtea_dec(plain[365:373],static_key)==mouse
for i in range(256):
 block=bytes((i+j*53)&255 for j in range(8));u.mem_write(BASE,block)
 call(0x80c3804,obj,BASE,BASE+4096)
 assert bytes(u.mem_read(BASE+4096,8))==xtea_dec(block,static_key)
 call(0x80c3724,obj,BASE+4096,BASE+8192)
 assert bytes(u.mem_read(BASE+8192,8))==block
print('CTEA_XTEA_FORM_BE32_ROUNDS32_PYTHON_VS_ELF_256_AND_ROUNDTRIP=PASS')
u.mem_write(BASE+4096,mouse+b'\0');assert call(0x82632ca,BASE+4096)==1
for text,want in [(b'',1),(b'123456',1),(b'aaaaaa',1),(b'1234567',0),(b'12b',0),(b'/',0),(b':',0)]:
 u.mem_write(BASE+4096,text+b'\0');assert call(0x82632ca,BASE+4096)==want
print('MOUSE_CAPTURE_VALIDATOR_AND_SEVEN_EDGE_CASES=PASS')
# Category1 predicate: construct only its vector layout, then execute original predicate.
policy=BASE+0x20000;vec=BASE+0x21000
u.mem_write(policy,b'\0'*64);u.mem_write(policy+12,struct.pack('<III',vec,vec+68,vec+68))
for idx in range(34):
 for value in [0,1,2,0xffff]:
  u.mem_write(vec+idx*2,struct.pack('<H',value))
  assert call(0x816d7f0,policy,idx)&255==int(value==1)
print('MOBILE_POLICY_34_INDICES_4_VALUES_ELF=PASS')
print('MOUSE_STATIC_KEY_SHA256='+hashlib.sha256(static_key).hexdigest())
print('LIMIT: ELF instruction replay is same target binary, not independent official truth or live singleton evidence.')
