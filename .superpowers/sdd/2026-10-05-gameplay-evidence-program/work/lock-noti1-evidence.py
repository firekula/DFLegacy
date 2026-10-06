#!/usr/bin/env python3
"""Freeze NOTI1 offline evidence: exact function bounds, two captures, positive/negative replay."""
from __future__ import annotations
import hashlib, struct, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from elfrecon import Elf
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM

ROOT = Path(__file__).resolve().parent
CAP = ROOT / 'captures'
OUT = ROOT / 'out' / 'noti1-evidence-locked.txt'
ELF = Path(sys.argv[1])

def rotl8(x, n): return ((x << n) | (x >> (8-n))) & 0xff

def inverse_zero_state(frame):
    # Encryption::Encrypt zero-state branch: key byte 0xb5, rotate-left 6 is inverse of ROR 6.
    return bytes(rotl8(x, 6) ^ 0xb5 for x in frame[15:450])

def static_facts():
    e = Elf(str(ELF)); sy = {s['value']: s for s in e.func_symbols() if s['value']}
    lines = [f'ELF_SHA256={hashlib.sha256(e.data).hexdigest()}', f'ELF_SIZE={len(e.data)}']
    for va, label in [(0x848da70,'Encryption::Encrypt'),(0x848d51a,'Encryption::SeedRand'),(0x848d3e4,'Encryption::make_crc_table'),(0x848d472,'Encryption::crc32'),(0x848d4c8,'Encryption::crc32NE'),(0x858d548,'PacketBuf::finalize'),(0x858d86a,'PacketBuf::encrypt_packet')]:
        s=sy.get(va); assert s, hex(va)
        lines.append(f'SYMBOL {label} VA=0x{va:08x} SIZE={s["size"]} NAME={s["name"]}')
    # exact bytes prove fixed zero-state branch has no SeedRand call in its loop.
    s=sy[0x848da70]; off=e.va_to_off(s['value']); blob=e.data[off:off+s['size']]
    md=Cs(CS_ARCH_X86,CS_MODE_32); md.detail=True
    ins=list(md.disasm(blob,s['value']))
    lines.append('ENCRYPT_CALLS=' + ','.join(f'0x{i.address:08x}->0x{i.operands[0].imm:08x}' for i in ins if i.mnemonic=='call' and i.operands[0].type==X86_OP_IMM))
    lines += ['ENCRYPT_DISASSEMBLY (exact function boundary; raw call addresses, no guessed labels)']
    lines += [f'0x{i.address:08x} {i.bytes.hex()} {i.mnemonic} {i.op_str}' for i in ins]
    assert hashlib.sha256(e.data).hexdigest() == '486239c75293731c090b448921ccab522b9fade46ad966adc44b0566fff91043'
    return lines

def replay():
    lines=[]
    for rel in [('streams-live','10013-2343.bin'),('streams-live2','10013-14805.bin')]:
        p=CAP/rel[0]/rel[1]; b=p.read_bytes(); sha=hashlib.sha256(b).hexdigest(); f=b[:450]
        assert f[:3]==b'\x00\x01\x00' and struct.unpack_from('<I',f,3)[0]==450
        plain=inverse_zero_state(f)
        key=plain[:334]
        pos=struct.unpack_from('<I',plain,334)[0]
        positive=(pos==5 and plain[338:343]==b'ch.11')
        # negative: wrong rotation must not reproduce the validated channel marker.
        wrong=bytes(rotl8(x, 5) ^ 0xb5 for x in f[15:450])
        negative=not (struct.unpack_from('<I',wrong,334)[0]==5 and wrong[338:343]==b'ch.11')
        assert positive and negative, 'positive/negative fixture failed'
        # Independent inverse: exhaustive forward lookup, not inverse rotation expression.
        forward = {((v ^ 0xb5) >> 6 | ((v ^ 0xb5) << 2)) & 255: v for v in range(256)}
        assert bytes(forward[x] for x in f[15:450]) == plain
        assert bytes(((x ^ 0xb5) >> 6 | ((x ^ 0xb5) << 2)) & 255 for x in plain) == f[15:450]
        lines.append('FORWARD_LOOKUP_INVERSE_AND_WIRE_ROUNDTRIP=PASS (same hypothesis, not singleton validation)')
        lines.append(f'CAPTURE {rel[0]}/{rel[1]} SIZE={len(b)} SHA256={sha}')
        lines.append(f'REPLAY {rel[0]} FRAME_SIZE=450 BODY_AFTER_15={len(plain)} POS_U32={pos} CHANNEL={plain[338:343]!r} KEY_SHA256={hashlib.sha256(key).hexdigest()} POSITIVE={positive} WRONG_ROTATION_NEGATIVE={negative}')
    return lines

lines=static_facts()+replay()+['VERDICT=EVIDENCE_LOCKED_NOT_KEY_CLOSED']
OUT.write_text('\n'.join(lines)+'\n',encoding='utf-8'); print('\n'.join(lines))
