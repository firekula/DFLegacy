from pathlib import Path
import hashlib, struct, re, json
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

ROOT = Path(__file__).resolve().parent
EXE = Path('G:/Game/DNF_ACT1/DF/DNF.exe')
data = EXE.read_bytes()
pe = pefile.PE(data=data)
BASE = pe.OPTIONAL_HEADER.ImageBase
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

def va(off):
    try: return BASE + pe.get_rva_from_offset(off)
    except Exception: return None

def read(addr, size):
    return pe.get_data(addr-BASE,size)

def dis(addr, size, out):
    for i in md.disasm(read(addr, size), addr):
        out.write(f'{i.address:08X} {i.bytes.hex():<24} {i.mnemonic:<8} {i.op_str}\n')

def direct_refs(addr):
    pattern=struct.pack('<I',addr)
    return [va(m.start()) for m in re.finditer(re.escape(pattern),data) if va(m.start())]

def calls(addr):
    result=[]
    for m in re.finditer(b'\xe8',data):
        off=m.start()
        if off+5>len(data): continue
        at=va(off)
        if at is not None and (at+5+struct.unpack_from('<i',data,off+1)[0])&0xffffffff==addr:
            result.append(at)
    return result

ascii_pat=re.compile(rb'[\x20-\x7e]{4,}')
keywords=('pickup','pick_up','pickitem','pick_item','routing','dice','bidding','epic','rare','unique','confirm','loot')
cnkeys=('拾取','捡取','捡起','分配','骰','确认','放弃','史诗','稀有','品质')
with (ROOT/'raw_probe.txt').open('w',encoding='utf-8') as out:
    out.write(f'FILE {EXE}\nSIZE {len(data)}\nSHA256 {hashlib.sha256(data).hexdigest()}\n')
    if getattr(pe,'VS_FIXEDFILEINFO',None):
        v=pe.VS_FIXEDFILEINFO[0]
        out.write(f'VERSION {(v.FileVersionMS>>16,v.FileVersionMS&65535,v.FileVersionLS>>16,v.FileVersionLS&65535)}\n')
    for s in pe.sections:
        out.write(f'SECTION {s.Name.decode(errors="replace")} VA={BASE+s.VirtualAddress:08X} VSZ={s.Misc_VirtualSize:X} RAW={s.PointerToRawData:X} RSZ={s.SizeOfRawData:X} FLAGS={s.Characteristics:X}\n')
    out.write('\nASCII RELEVANT STRINGS (literal pointer refs may be data or false positives)\n')
    for m in ascii_pat.finditer(data):
        text=m.group().decode()
        if any(k in text.lower() for k in keywords):
            addr=va(m.start())
            out.write(f'{addr:08X} {text}\nREFS '+', '.join(f'{x:08X}' for x in direct_refs(addr))+'\n')
    out.write('\nCHINESE RELEVANT NULL-DELIMITED STRINGS\n')
    last=0
    for m in re.finditer(b'\0',data):
        off=m.start(); chunk=data[last:off]
        if 4 <= len(chunk) <= 2000:
            for enc in ('gbk','utf-8'):
                try: text=chunk.decode(enc)
                except UnicodeError: continue
                if any(k in text for k in cnkeys):
                    addr=va(last)
                    out.write(f'{addr:08X} {enc} {text!r}\nREFS '+', '.join(f'{x:08X}' for x in direct_refs(addr))+'\n')
                    break
        last=off+1
    ranges=[(0x414090,0x1a0),(0x42252b,0x7d0),(0x427104,0x330),(0x40e290,0x80),(0x8b2650,0x180),(0x40d800,0x160),(0x417c40,0x40)]
    for addr,size in ranges:
        out.write(f'\nDIS {addr:08X} SIZE {size:X}\n'); dis(addr,size,out)
    for addr in (0x40e290,0x417c40,0x417c60):
        out.write(f'\nDIRECT CALLS TO {addr:08X}: '+', '.join(f'{x:08X}' for x in calls(addr))+'\n')
