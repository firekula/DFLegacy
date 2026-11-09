"""Read-only raw PE RTTI/vtable mapping, independent of IDA offset annotations."""
from pathlib import Path
import json
import re
import struct
import pefile

OUT = Path(__file__).resolve().parent
ROOT = next(x for x in OUT.parents if (x / '.git').exists())
p = pefile.PE(str(ROOT.parent / 'DF' / 'DNF.exe'))
raw = p.__data__
base = p.OPTIONAL_HEADER.ImageBase

def va(at):
    try:
        return base + p.get_rva_from_offset(at)
    except pefile.PEFormatError:
        return None

def read(at, count=4):
    return p.get_data(at - base, count)

def u32(at):
    return struct.unpack('<I', read(at))[0]

def refs(target):
    return [va(m.start()) for m in re.finditer(re.escape(struct.pack('<I', target)), raw)
            if va(m.start()) is not None]

def code(at):
    sec = p.get_section_by_rva(at - base)
    return sec is not None and bool(sec.Characteristics & 0x20000000)

types = []
for match in re.finditer(rb'\.\?A[VU][A-Za-z0-9_@$?]+@@', raw):
    name = match.group().decode()
    if not re.search(r'AIThink|AICharacter|CNMage@@|CNFighter@@|CNGunner@@|CNPriest@@|CNSword[Mm]an@@', name):
        continue
    td = va(match.start()) - 8
    entry = {'name': name, 'descriptor': f'{td:08X}', 'descriptor_refs': [f'{x:08X}' for x in refs(td)],
             'vtables': []}
    for reference in refs(td):
        col = reference - 12
        if u32(col) != 0:
            continue
        for reference_col in refs(col):
            table = reference_col + 4
            if not code(u32(table)):
                continue
            method_count = 0
            while method_count < 1024 and code(u32(table + method_count * 4)):
                method_count += 1
            # offset zero is the actor's primary vtable; secondary tables are recorded too.
            methods = {}
            for offset in (0, 8, 84, 912, 1492, 1496, 1500, 1508, 1512, 1560):
                if offset >= method_count * 4:
                    continue
                target = u32(table + offset)
                if code(target):
                    methods[f'{offset:X}'] = f'{target:08X}'
            entry['vtables'].append({'table': f'{table:08X}', 'object_offset': u32(col + 4),
                                     'method_count': method_count, 'methods': methods})
    types.append(entry)
(OUT / 'ai-vtables.json').write_text(json.dumps(types, ensure_ascii=False, indent=2), encoding='utf-8')
for item in types:
    print(item['name'], item['descriptor'], 'descriptor refs', len(item['descriptor_refs']))
    for table in item['vtables']:
        if table['object_offset'] == 0:
            print(json.dumps(table))
