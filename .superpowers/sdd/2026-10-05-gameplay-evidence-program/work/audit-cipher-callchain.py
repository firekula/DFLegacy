#!/usr/bin/env python3
"""Offline evidence audit; resolves ELF32 PLT calls by GOT relocation, not nearest symbol.
Usage: python audit-cipher-callchain.py <vm-elf>
Does not modify binaries or contact live servers.
"""
import hashlib
import struct
import sys
from pathlib import Path
from elfrecon import Elf
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM

e = Elf(sys.argv[1])
print('binary_sha256=' + hashlib.sha256(e.data).hexdigest())
print('binary_size=' + str(len(e.data)))

def cstr(off):
    return e.data[off:e.data.index(b'\0', off)].decode('latin1')

relocs = {}
for sec in e.sections:
    if sec['type'] not in (9, 4):
        continue
    symtab = e.sections[sec['link']]
    strings = e.sections[symtab['link']]
    stride = sec['entsize'] or (8 if sec['type'] == 9 else 12)
    for off in range(sec['offset'], sec['offset'] + sec['size'], stride):
        got, info = struct.unpack_from('<II', e.data, off)
        sym_off = symtab['offset'] + (info >> 8) * symtab['entsize']
        name_index = struct.unpack_from('<I', e.data, sym_off)[0]
        relocs[got] = cstr(strings['offset'] + name_index)

def plt(va):
    off = e.va_to_off(va)
    if off is not None and e.data[off:off+2] == b'\xff\x25':
        got = struct.unpack_from('<I', e.data, off+2)[0]
        return relocs.get(got, 'UNRESOLVED_GOT_%08x' % got)
    return None

exact = {s['value']: s['name'] for s in e.func_symbols() if s['value']}
print('\n[PLT via dynamic relocations]')
for va in (0x0807e360, 0x0807e3b0, 0x0807d8a0, 0x0807dcc0, 0x0807dca0):
    print('0x%08x -> %s' % (va, plt(va)))

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True
ranges = [('Cipher::Decrypt', 0x0809751e, 182),
          ('Cipher::Initialize', 0x08097306, 185),
          ('G_Cipher', 0x082a713a, 13),
          ('GlobalInstance<Cipher>::inst_ptr', 0x082ab9da, 18),
          ('InitCipherKeyString timer tail', 0x082a03f3, 102),
          ('GetCryptoType', 0x08097160, 53),
          ('GlobalData::Init', 0x08299fa0, 13464)]
for needle in ('InitOnAccept', 'encrypt_packet', 'GetOriginalKey', 'COpensslRSA7Decrypt'):
    candidates = {s['value']: s for s in e.func_symbols() if s['value'] and needle in s['name']}
    for va, sym in candidates.items():
        ranges.append((sym['name'], va, sym['size']))
for label, va, n in ranges:
    print('\n[' + label + ']')
    off = e.va_to_off(va)
    if off is None:
        raise ValueError('unmapped range')
    for ins in md.disasm(e.data[off:off+n], va):
        note = ''
        if ins.mnemonic == 'call' and ins.operands[0].type == X86_OP_IMM:
            dest = ins.operands[0].imm
            note = ' ; ' + (plt(dest) or exact.get(dest, 'UNRESOLVED'))
        print('0x%08x %-7s %s%s' % (ins.address, ins.mnemonic, ins.op_str, note))
