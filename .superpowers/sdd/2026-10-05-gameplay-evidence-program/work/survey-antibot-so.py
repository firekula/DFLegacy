"""Read-only candidate SO identity, relocated vtable and exact factory disassembly.
Addresses are ELF virtual offsets, not runtime addresses. Never loads native library.
"""
import sys,io,hashlib,struct
from pathlib import Path
from elftools.elf.elffile import ELFFile
from elftools.elf.relocation import RelocationSection
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
p=Path(sys.argv[1]);b=p.read_bytes();e=ELFFile(io.BytesIO(b))
print('SOURCE='+str(p));print('SHA256='+hashlib.sha256(b).hexdigest());print('SIZE='+str(len(b)));print('ELF_CLASS='+str(e.elfclass),'MACHINE='+e['e_machine'])
assert hashlib.sha256(b).hexdigest()=='8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7'
for t in e.get_section_by_name('.dynamic').iter_tags():
 if t.entry.d_tag=='DT_NEEDED':print('NEEDED='+t.needed)
sy={s.name:s for s in e.get_section_by_name('.symtab').iter_symbols()}
byva={s['st_value']:s.name for s in sy.values() if s['st_value']}
def off(va):
 for seg in e.iter_segments():
  if seg['p_type']=='PT_LOAD' and seg['p_vaddr']<=va<seg['p_vaddr']+seg['p_filesz']:return seg['p_offset']+va-seg['p_vaddr']
 raise ValueError(hex(va))
rel={}
for sec in e.iter_sections():
 if isinstance(sec,RelocationSection):
  st=e.get_section(sec['sh_link'])
  for r in sec.iter_relocations():
   rs=st.get_symbol(r['r_info_sym']);rel[r['r_offset']]=(r['r_info_type'],rs.name,rs['st_value'])
s=sy['_ZTV16AntibotInterface'];base=s['st_value']
for pos in range(0,s['st_size'],4):
 va=base+pos;raw=struct.unpack_from('<I',b,off(va))[0];r=rel.get(va)
 print('VTABLE_STORAGE+%02x ADDRESS_POINT_SLOT=%+d RAW=%08x RELOC=%s RAW_EXACT=%s'%(pos,pos-8,raw,r,byva.get(raw,'')))
md=Cs(CS_ARCH_X86,CS_MODE_32)
for name in ['GetAntiBotInterfaceAll','_ZN16AntibotInterface20RecvBufferForEncryptEjPhjS0_Rj']:
 s=sy[name];a=s['st_value'];n=s['st_size'];print('FUNCTION',name,hex(a),n)
 ins=list(md.disasm(b[off(a):off(a)+n],a));assert sum(i.size for i in ins)==n
 for i in ins:print(('%08x %-8s %s'%(i.address,i.mnemonic,i.op_str)).rstrip())
print('LIMIT: candidate build asset only; VM library hash not established. Raw calls not guessed via nearest symbols.')
