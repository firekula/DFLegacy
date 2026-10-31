#!/usr/bin/env python3
"""Offline cross-check of previously captured runtime bytes; no live process access.
Usage: python verify-cipher-snapshot.py <peek-key2.txt>
The snapshot is one pool entry, NOT automatically G_Cipher().
"""
import re
import struct
import sys
from pathlib import Path
mem = {}
for line in Path(sys.argv[1]).read_text(encoding='utf-8').splitlines():
    match = re.match(r'\s+([0-9a-f]{8})\s+((?:[0-9a-f]{2} ){1,16})', line)
    if match:
        start = int(match[1], 16)
        for i, value in enumerate(bytes.fromhex(match[2])):
            if start+i in mem and mem[start+i] != value:
                raise ValueError('conflicting snapshot bytes')
            mem[start+i] = value

def take(addr, size):
    return bytes(mem[addr+i] for i in range(size))

length, begin, end, capacity = struct.unpack('<IIII', take(0x2311c7a0, 16))
key = take(0x2311c7b0, length)
unit = b'MapObject/PathGate/Act2/DEDownNormal.obj'
expected = (unit * ((length + len(unit)-1)//len(unit)))[:length]
assert length == 334
assert (end-begin) == 14*4 and end == capacity
assert key == expected
print('snapshot object=0x2311c7a0 length=%d vector_count=%d' % (length, (end-begin)//4))
print('repeat_unit_length=%d key_repeat_exact=True' % len(unit))
print('PASS: captured pool[0] object layout and repeated key bytes agree')
print('LIMIT: pool[0] is not the G_Cipher singleton at global slot 0x0941f8a4')
