"""Verify the AI research against raw ACT1 PE bytes; no process control or writes to game files."""
import hashlib
import json
import struct
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = next(x for x in HERE.parents if (x / '.git').exists())
raw = (ROOT.parent / 'DF/DNF.exe').read_bytes()
pe = struct.unpack_from('<I', raw, 0x3c)[0]
base = struct.unpack_from('<I', raw, pe + 52)[0]
opt = struct.unpack_from('<H', raw, pe + 20)[0]
sections = [struct.unpack_from('<IIII', raw, pe + 24 + opt + i * 40 + 8)
            for i in range(struct.unpack_from('<H', raw, pe + 6)[0])]

def read(at, count):
    for _, rva, size, offset in sections:
        if base + rva <= at and at + count <= base + rva + size:
            start = offset + at - base - rva
            return raw[start:start + count]
    raise ValueError(hex(at))

def u32(at):
    return struct.unpack('<I', read(at, 4))[0]

checks = []
def check(label, actual, expected):
    assert actual == expected, (label, actual, expected)
    checks.append({'label': label, 'actual': actual})

def call(at, target, label):
    instruction = read(at, 5)
    check(label + ' opcode', instruction[0], 0xe8)
    check(label + ' target', f'{at + 5 + struct.unpack("<i", instruction[1:])[0]:08X}', f'{target:08X}')

check('ACT1 executable hash', hashlib.sha256(raw).hexdigest(),
      '6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5')
for job, table in [('swordman', 0xa038dc), ('fighter', 0xa03efc), ('gunner', 0xa0451c),
                   ('mage', 0xa04b3c), ('priest', 0xa0515c)]:
    check(job + ' primary AI flag vtable slot', f'{u32(table + 0x5dc):08X}', '00493870')
check('ordinary actor AI flag is constant false', read(0x493870, 3).hex(), '32c0c3')
check('AI state allocation size push', read(0x48f7c0, 5).hex(), '6808090000')
call(0x48f7df, 0x492020, 'AI state constructor')
call(0x48fc49, 0x71bc00, 'AI character script loader')
call(0x48f685, 0x48e770, 'AI target selection')
call(0x48f68c, 0x48d760, 'AI battle observation fill')
call(0x48f6a3, 0x4e2a40, 'AI action pattern evaluation')
call(0x48f6ad, 0x48dad0, 'AI key stream activation')
check('network AI creation still defaults to return', f'{u32(0x79213c + 40 * 4):08X}', '00792127')
result = {'scope': 'Static native AI decision/execution and normal-player gate evidence; no live combat proof.',
          'passed': len(checks), 'failures': 0, 'checks': checks}
(HERE / 'ai-battle-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({'passed': len(checks), 'failures': 0, 'scope': result['scope']}, ensure_ascii=False))
