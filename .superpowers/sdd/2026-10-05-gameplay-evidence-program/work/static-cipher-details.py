"""Read-only exact-boundary disassembly; ELF hash locked; PLT resolved by relocation."""
import sys, hashlib, struct
from pathlib import Path
from elfrecon import Elf
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM
e=Elf(sys.argv[1]); digest=hashlib.sha256(e.data).hexdigest()
assert digest=='486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043'
sy={s['value']:s for s in e.func_symbols() if s['value']}; rel={}
for sec in e.sections:
 if sec['type'] not in (4,9):continue
 tab=e.sections[sec['link']]; strings=e.sections[tab['link']]
 for o in range(sec['offset'],sec['offset']+sec['size'],sec['entsize'] or (8 if sec['type']==9 else 12)):
  got,info=struct.unpack_from('<II',e.data,o); no=struct.unpack_from('<I',e.data,tab['offset']+(info>>8)*tab['entsize'])[0]; p=strings['offset']+no
  rel[got]=e.data[p:e.data.index(b'\0',p)].decode('latin1')
def label(va):
 o=e.va_to_off(va)
 if o is not None and e.data[o:o+2]==b'\xff\x25':return rel.get(struct.unpack_from('<I',e.data,o+2)[0],'UNKNOWN_GOT')
 return sy.get(va,{}).get('name','UNKNOWN')
vas=[int(a,0) for a in sys.argv[2:]]
if not vas:
 vas=[0x848da70,0x848d3e4,0x848d472,0x848d4c8,0x808cc17,0x858d548,0x858d86a,0x864809c,0x86485ba,0x8647890,0x8097306,0x809751e,0x8097160,0x809bf08,0x809c18a,0x809c376,0x809c404,0x809c40e,0x80b8028,0x80b6220,0x858d2bc,0x81e8c78,0x81731f2,0x816c532]
 vas+= sorted(v for v,s in sy.items() if 'CNCrypto' in s['name'] and ('GetKeySize' in s['name'] or 'GetBlockSize' in s['name']))
print('ELF_SHA256='+digest)
md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
for va in dict.fromkeys(vas):
 s=sy[va];o=e.va_to_off(va);ins=list(md.disasm(e.data[o:o+s['size']],va));assert sum(i.size for i in ins)==s['size']
 print(f'\nFUNCTION {s["name"]} VA=0x{va:08x} SIZE={s["size"]}')
 for i in ins:
  note=' ; '+label(i.operands[0].imm) if i.mnemonic=='call' and i.operands[0].type==X86_OP_IMM else ''
  print(f'0x{i.address:08x} {i.mnemonic:8} {i.op_str}{note}'.rstrip())
