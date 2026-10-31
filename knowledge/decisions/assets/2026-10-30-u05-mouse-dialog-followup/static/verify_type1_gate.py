"""Offline original ACT1 type1 physical-check contract. No process access."""
import hashlib, itertools, json, struct
from pathlib import Path
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_ESP
path = Path("G:/Game/DNF_ACT1/Loot4_A/DNF.exe")
sha = hashlib.sha256(path.read_bytes()).hexdigest()
assert sha == "6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5"
pe = pefile.PE(str(path))
code = pe.get_data(0x8BA7F0 - pe.OPTIONAL_HEADER.ImageBase, 0x109)
cases = 0
for button, left, right, lock, flag, mark in itertools.product([0,1,7], [0,1], [0,1], [0,1,2], [0,1], [0,1]):
    machine = Uc(UC_ARCH_X86, UC_MODE_32)
    for address, size in [(0x8BA000,0x1000), (0xCFD000,0x1000), (0x100000,0x3000), (0x2000000,0x2000), (0x3000000,0x1000)]:
        machine.mem_map(address, size)
    machine.mem_write(0x8BA7F0, code)
    machine.mem_write(0x100068, struct.pack("<I", lock))
    machine.mem_write(0x100090, bytes([flag]))
    machine.mem_write(0x101000, struct.pack("<IIB", 1, button, mark))
    machine.mem_write(0xCFD200, bytes([left]))
    machine.mem_write(0xCFD202, bytes([right]))
    machine.mem_write(0x2001000, struct.pack("<II", 0x3000000, 0x101000))
    machine.reg_write(UC_X86_REG_ESP, 0x2001000)
    machine.reg_write(UC_X86_REG_ECX, 0x100000)
    machine.emu_start(0x8BA7F0, 0x3000000, count=100)
    actual = (machine.reg_read(UC_X86_REG_EAX)&255, machine.mem_read(0x101008,1)[0], machine.reg_read(UC_X86_REG_ESP))
    down = left if button == 0 else right
    expected = (0,1,0x2001008) if lock else (0,0,0x2001008) if flag or not down else (0,mark,0x2001008) if mark else (1,0,0x2001008)
    assert actual == expected, (button,left,right,lock,flag,mark,actual,expected)
    cases += 1
print(json.dumps({"original_sha256":sha, "cases":cases, "failures":0}))
