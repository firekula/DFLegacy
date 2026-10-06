"""Exact SO symbols; i386 PIC PLT resolved using DT_PLTGOT+displacement relocations."""
import sys,io,hashlib,struct
from pathlib import Path
from elftools.elf.elffile import ELFFile
from elftools.elf.relocation import RelocationSection
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
from capstone.x86 import X86_OP_IMM
b=Path(sys.argv[1]).read_bytes();sha=hashlib.sha256(b).hexdigest();assert sha=='8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7'
e=ELFFile(io.BytesIO(b));sy={s['st_value']:s for s in e.get_section_by_name('.symtab').iter_symbols() if s['st_value'] and s['st_info']['type']=='STT_FUNC'}
got=next(t['d_ptr'] for t in e.get_section_by_name('.dynamic').iter_tags() if t['d_tag']=='DT_PLTGOT');rel={}
for sec in e.iter_sections():
 if isinstance(sec,RelocationSection):
  st=e.get_section(sec['sh_link'])
  for r in sec.iter_relocations():rel[r['r_offset']]=st.get_symbol(r['r_info_sym']).name
def off(va):
 for s in e.iter_segments():
  if s['p_type']=='PT_LOAD' and s['p_vaddr']<=va<s['p_vaddr']+s['p_filesz']:return s['p_offset']+va-s['p_vaddr']
 raise ValueError(hex(va))
def name(a):
 p=off(a);x=b[p:p+6]
 if x[:2]==b'\xff\xa3':return rel.get(got+struct.unpack('<i',x[2:])[0],'UNKNOWN_PIC_PLT')
 if x[:2]==b'\xff\x25':return rel.get(struct.unpack('<I',x[2:])[0],'UNKNOWN_PLT')
 return sy[a].name if a in sy else 'UNKNOWN_EXACT'
md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
print('SHA256='+sha+' DT_PLTGOT='+hex(got))
for a in [int(x,0) for x in sys.argv[2:]]:
 s=sy[a];ins=list(md.disasm(b[off(a):off(a)+s['st_size']],a));assert sum(i.size for i in ins)==s['st_size']
 print('\nFUNCTION',s.name,hex(a),s['st_size'])
 for i in ins:
  note=''
  if i.mnemonic=='call':note=' ; '+(name(i.operands[0].imm) if i.operands[0].type==X86_OP_IMM else 'INDIRECT_RUNTIME_TARGET')
  print(('%08x %-8s %s%s'%(i.address,i.mnemonic,i.op_str,note)).rstrip())
