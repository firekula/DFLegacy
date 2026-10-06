#!/usr/bin/env python3
"""Independent pyelftools verification of PLT imports and selector arithmetic.
Usage: python verify-cipher-audit.py <vm-elf>
"""
import struct
import sys
from elftools.elf.elffile import ELFFile
with open(sys.argv[1], 'rb') as stream:
    elf = ELFFile(stream)
    rel = elf.get_section_by_name('.rel.plt')
    dyn = elf.get_section(rel['sh_link'])
    got_names = {r['r_offset']: dyn.get_symbol(r['r_info_sym']).name for r in rel.iter_relocations()}
    expected = {0x0807e360:'localtime_r', 0x0807e3b0:'strlen', 0x0807d8a0:'memcpy',
                0x0807dcc0:'memset', 0x0807dca0:'rand'}
    for va, name in expected.items():
        segments = [s for s in elf.iter_segments() if s['p_type']=='PT_LOAD'
                    and s['p_vaddr'] <= va < s['p_vaddr']+s['p_filesz']]
        assert len(segments)==1
        seg=segments[0]
        stream.seek(seg['p_offset'] + va-seg['p_vaddr'])
        code=stream.read(6)
        assert code[:2]==b'\xff\x25'
        actual=got_names[struct.unpack('<I',code[2:])[0]]
        assert actual==name, (actual,name)
        print('PLT 0x%08x %s PASS' % (va,name))

def signed32(n):
    n &= 0xffffffff
    return n if n<0x80000000 else n-0x100000000

def selector(n):
    # GetCryptoType @08097160: signed multiply/high-word, shift, quotient*14 subtraction.
    high=signed32((signed32(n)*signed32(0x92492493)) >> 32)
    quotient=(signed32(high+n)>>3) - (signed32(n)>>31)
    return signed32(n-14*quotient)

assert all(selector(n)==n%14 for n in range(65536))
print('GetCryptoType arithmetic: all 65536 uint16 inputs equal n % 14 PASS')
print('LIMIT: this verifies selector arithmetic, not any cipher algorithm or packet plaintext')
