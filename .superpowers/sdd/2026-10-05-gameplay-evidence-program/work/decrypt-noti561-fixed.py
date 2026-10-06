"""Bounded offline SO execution with ELF relocations, no native dlopen/network.
Use fixed header seed/choice, then verify full CRC32 and re-encryption. Fail on unknown imports.
"""
import io,sys,struct,hashlib,zlib
from pathlib import Path
from elftools.elf.elffile import ELFFile
from elftools.elf.relocation import RelocationSection
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EAX
b=Path(sys.argv[1]).read_bytes();assert hashlib.sha256(b).hexdigest()=='8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7'
e=ELFFile(io.BytesIO(b));u=Uc(UC_ARCH_X86,UC_MODE_32)
for s in e.iter_segments():
 if s['p_type']=='PT_LOAD':
  a=s['p_vaddr'];lo=a&~4095;u.mem_map(lo,((a+s['p_memsz']+4095)&~4095)-lo);u.mem_write(a,s.data())
BASE=0x20000000;STACK=0x30000000;STOP=BASE+0xff000;heap=BASE+0x100000
u.mem_map(BASE,0x1000000);u.mem_map(STACK,0x200000);u.mem_map(0x40000000,0x100000)
imports={};named={}
def rd(a):return int.from_bytes(u.mem_read(a,4),'little')
for sec in e.iter_sections():
 if not isinstance(sec,RelocationSection):continue
 st=e.get_section(sec['sh_link'])
 for r in sec.iter_relocations():
  a=r['r_offset'];t=r['r_info_type'];sym=st.get_symbol(r['r_info_sym']);sv=sym['st_value']
  if t==8:continue # load bias zero
  if t not in (1,6,7):raise ValueError(('relocation',t))
  if sym['st_shndx']=='SHN_UNDEF' and sym.name:
   if sym.name not in named:
    addr=0x40000000+len(named)*16;named[sym.name]=addr;imports[addr]=sym.name
   sv=named[sym.name]
  v=sv+(rd(a) if t==1 else 0);u.mem_write(a,struct.pack('<I',v&0xffffffff))
def hook(uc,a,size,data):
 global heap
 if a not in imports:return
 name=imports[a];sp=uc.reg_read(UC_X86_REG_ESP);arg=lambda i:rd(sp+4*i);ret=0
 if name in ('_Znaj','_Znwj','malloc','calloc'):
  n=arg(1)*(arg(2) if name=='calloc' else 1);assert n<0x200000;ret=heap;heap+=(n+31)&~15;assert heap<BASE+0xf00000
 elif name in ('_ZdaPv','_ZdlPv','free'):pass
 elif name in ('memcpy','memmove','memset'):
  dst,src,n=arg(1),arg(2),arg(3);assert n<0x200000
  if n:uc.mem_write(dst,bytes([src&255])*n if name=='memset' else bytes(uc.mem_read(src,n)))
  ret=dst
 else:raise RuntimeError('unsupported import '+name)
 uc.reg_write(UC_X86_REG_EAX,ret);uc.reg_write(UC_X86_REG_ESP,sp+4);uc.reg_write(UC_X86_REG_EIP,rd(sp))
u.hook_add(UC_HOOK_CODE,hook)
def call(va,*args):
 sp=STACK+0x1f0000;u.mem_write(sp,struct.pack('<'+'I'*(len(args)+1),STOP,*args));u.reg_write(UC_X86_REG_ESP,sp)
 try:u.emu_start(va,STOP,timeout=120000000,count=30000000)
 except Exception:
  print('FAIL_EIP='+hex(u.reg_read(UC_X86_REG_EIP)));raise
 assert u.reg_read(UC_X86_REG_EIP)==STOP,'instruction bound'
 return u.reg_read(UC_X86_REG_EAX)
W=Path(__file__).resolve().parent
print('GENKEY input order checked independently: seedLE then choiceLE')
for choice in range(1,20):
 assert call(0x193420,choice,0x12345678,BASE+0x80000)==1
 key=bytes(u.mem_read(rd(BASE+0x80000),32));assert key==b''.join(hashlib.md5(struct.pack('<II',0x12345678,choice+i)).digest() for i in range(2))
print('GENKEY_19_CHOICES_ELF_VS_HASHLIB=PASS')
for rel in ['streams-live/10013-2343.bin','streams-live2/10013-14805.bin','streams-vm/10013-13681.bin']:
 raw=(W/'captures'/rel).read_bytes();p=0;count=0
 while p<len(raw):
  n=struct.unpack_from('<I',raw,p+3)[0];f=raw[p:p+n];p+=n
  if f[:3]!=b'\0\x31\x02':continue
  d=f[15:];outer=int.from_bytes(d[:4],'big');typ=int.from_bytes(d[6:8],'big')
  if typ!=17:print(rel,'TYPE_NOT_17='+str(typ));continue
  seed,choice,crc=struct.unpack_from('>III',d,9);cipher=d[21:outer];assert 1<=choice<=19
  u.mem_write(BASE,cipher);assert call(0x1934c0,choice,BASE,len(cipher),seed,0)==1
  plain=bytes(u.mem_read(BASE,len(cipher)));actual=call(0x19dc80,BASE,len(cipher))
  assert actual==crc,(rel,count,choice,hex(actual),hex(crc))
  assert zlib.crc32(plain)==crc
  assert call(0x1934c0,choice,BASE,len(cipher),seed,1)==1
  assert bytes(u.mem_read(BASE,len(cipher)))==cipher
  print(rel,'choice='+str(choice),'plain_len='+str(len(plain)),'CRC32_AND_ROUNDTRIP=PASS','plain_sha256='+hashlib.sha256(plain).hexdigest());count+=1
 print(rel,'DECRYPTED_TYPE17='+str(count))
