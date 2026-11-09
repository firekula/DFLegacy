"""Independently check the research's important native claims against PE bytes."""
import hashlib
import json
import struct
from pathlib import Path

ROOT = next(p for p in Path(__file__).resolve().parents if (p / '.git').exists())
OUT = Path(__file__).resolve().parent
DEST = ROOT / 'knowledge/decisions/assets/2026-11-01-social-world'
DEST.mkdir(parents=True, exist_ok=True)
raw = (ROOT.parent / 'DF/DNF.exe').read_bytes()
pe = struct.unpack_from('<I', raw, 0x3c)[0]
base = struct.unpack_from('<I', raw, pe + 52)[0]
opt = struct.unpack_from('<H', raw, pe + 20)[0]
sections = [struct.unpack_from('<IIII', raw, pe + 24 + opt + i * 40 + 8)
            for i in range(struct.unpack_from('<H', raw, pe + 6)[0])]

def read(va, count):
    for _, rva, size, start in sections:
        if base + rva <= va and va + count <= base + rva + size:
            at = start + va - base - rva
            return raw[at:at + count]
    raise ValueError(hex(va))

def u32(va):
    return struct.unpack('<I', read(va, 4))[0]

def cstr(va):
    return read(va, 160).split(b'\0', 1)[0].decode('ascii')

checks = []
def check(name, actual, expected):
    assert actual == expected, (name, actual, expected)
    checks.append({'name': name, 'actual': actual})

check('ACT1 client SHA256', hashlib.sha256(raw).hexdigest(),
      '6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5')
check('client PVF SHA256', hashlib.sha256((ROOT.parent / 'DF/Script.pvf').read_bytes()).hexdigest(),
      '288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f')
check('server PVF SHA256', hashlib.sha256((ROOT / 'DNF2008/Script.pvf').read_bytes()).hexdigest(),
      'bc397d1463f1f0f749bbd169400b640d0c8a5d1f6e6e97cfda797099f995ac27')
check('native disjoint command', cstr(u32(0xb54430 + 28 * 4)), 'ENUM_CMDPACKET_DISJOINT_ITEM')
check('native UDP AI creation name', cstr(u32(0xb51c08 + 40 * 4)), 'UDPPACKET_CREATE_AI_CHARACTER')
switches = json.loads((OUT / 'udp-switches.json').read_text(encoding='utf-8'))
switch = next(s for s in switches['switches'] if s['at'] == '0078EF40')
targets = {case: entry['target'] for entry in switch['cases'] for case in entry['values']}
check('IDA default AI creation target', targets[40], switch['default'])
check('PE jump table AI creation default', f'{u32(0x79213c + 40 * 4):08X}', '00792127')
check('PE default epilogue with no object creation call', read(0x792127, 19).hex(),
      '8b4df45f5e64890d000000005b8be55dc20c00')
for case in (4, 5, 12, 13, 19, 20, 24, 25, 30):
    check(f'PE jump table ordinary actor operation {case}',
          f'{u32(0x79213c + case * 4):08X}', targets[case])
assert all(targets[case] != targets[40] for case in (4, 5, 12, 13, 19, 20, 24, 25, 30))
checks.append({'name': 'ordinary movement, state, HP, MP, hit, damage, item, skill and coin targets are non-default',
               'actual': True})
names = [cstr(u32(0xb54430 + i * 4)) for i in range(163)]
check('enchant command names in this ACT1 name table', [n for n in names if 'ENCHANT' in n], [])
catalog = json.loads((OUT / 'pvf-catalog.json').read_text(encoding='utf-8'))
check('client AI definition files', [p for p in catalog[0]['ai_entries'] if p.endswith('.aic')],
      ['aicharacter/cuwaki/cuwaki.aic'])
check('OperatingValue file in client archive', 'aicharacter/operatingvalue.lst' in catalog[0]['ai_entries'], False)
report = {'scope': 'Static integrity and control-flow checks only. No live companion, UI or combat validation.',
          'checks': checks, 'passed': len(checks), 'failures': 0,
          'native_switch': switch,
          'client_pvf_ai': catalog[0]['ai_entries'],
          'source_idb_used': False}
(DEST / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
(DEST / 'binary-inventory.json').write_text((OUT / 'binary-inventory.json').read_text(encoding='utf-8'), encoding='utf-8')
(DEST / 'pvf-catalog.json').write_text((OUT / 'pvf-catalog.json').read_text(encoding='utf-8'), encoding='utf-8')
(DEST / 'udp-switches.json').write_text((OUT / 'udp-switches.json').read_text(encoding='utf-8'), encoding='utf-8')
(DEST / 'verify_evidence.py').write_text(Path(__file__).read_text(encoding='utf-8'), encoding='utf-8')

parts = []
for source, addresses in [('native-core.txt', ('004601B0',)),
                          ('native-ai-entry.txt', ('0071BC00',))]:
    if not (OUT / source).exists():
        continue
    content = (OUT / source).read_text(encoding='utf-8')
    for block in content.split('// ===== '):
        if any(('"va": "' + addr + '"') in block[:180] for addr in addresses):
            parts.append('// ===== ' + block)
if parts:
    (DEST / 'native-excerpts.txt').write_text('\n'.join(parts).rstrip() + '\n', encoding='utf-8')
print(json.dumps({'checks_passed': len(checks), 'failures': 0,
                  'scope': report['scope'], 'output': str(DEST)}, ensure_ascii=False, indent=2))
