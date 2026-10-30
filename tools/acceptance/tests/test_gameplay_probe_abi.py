"""Execute the actual x86 probe from an explicitly supplied IJL DLL offline."""
import os
from pathlib import Path
import re
import struct
import unittest


class GameplayProbeAbiTests(unittest.TestCase):
    def test_original_cdecl_stack_registers_and_payload_survive_observer(self):
        self.check_probe(False)

    def test_gp4_motion_thiscall_stack_registers_survive_observer(self):
        self.check_probe(True)

    def check_probe(self,movement):
        path=os.environ.get('DFLEGACY_GAMEPLAY_PROBE_DLL')
        if not path:self.skipTest('Explicit built native probe DLL required')
        import pefile
        from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
        from unicorn.x86_const import (UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,
            UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,
            UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EFLAGS)
        pe=pefile.PE(str(Path(path)));raw=pe.get_memory_mapped_image();base=pe.OPTIONAL_HEADER.ImageBase
        pattern=(rb'\x9c\x60\x8b\x44\x24\x28\x55\x50\x51\x57\xe8....\x61\x9d\x68\x70\x92\x4c\x00\xc3' if movement else rb'\x9c\x60\x8b\x45\x10\x83\xc0\x07\x55\x57\x50\x53\xe8....\x61\x9d\x68\xa0\x27\x79\x00\xc3')
        matches=list(re.finditer(pattern,raw,re.DOTALL))
        self.assertEqual(len(matches),1,'the built DLL must contain exactly one accepted-dispatch probe')
        start=base+matches[0].start();offset=11 if movement else 13;helper=start+offset+4+struct.unpack_from('<i',raw,matches[0].start()+offset)[0];original=0x4C9270 if movement else 0x7927A0
        u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(base,(len(raw)+4095)//4096*4096);u.mem_write(base,raw)
        for address,size in [(0x4C9000,0x1000),(0x790000,0x4000),(0x20000000,0x2000),(0x30000000,0x10000),(0x40000000,0x1000)]:u.mem_map(address,size)
        stack=0x30009000;frame=0x30008000;packet=0x20000100;sentinel=0x40000000;context=0x21000000
        payload=bytes(range(39));u.mem_write(packet,payload);u.mem_write(stack,struct.pack('<III',sentinel,16 if movement else 32,4));u.mem_write(frame+16,struct.pack('<I',32))
        regs={UC_X86_REG_EAX:0x230000 if movement else 4,UC_X86_REG_EBX:context,UC_X86_REG_ECX:0x230000 if movement else 32,UC_X86_REG_EDX:0x11223344,
            UC_X86_REG_ESI:0x55667788,UC_X86_REG_EDI:0x220000 if movement else packet,UC_X86_REG_EBP:frame,UC_X86_REG_ESP:stack,UC_X86_REG_EFLAGS:0x246}
        for register,value in regs.items():u.reg_write(register,value)
        seen=[]
        def observe(cpu,address,size,unused):
            sp=cpu.reg_read(UC_X86_REG_ESP)
            if address==helper:
                ret,*observed=struct.unpack('<IIIII',bytes(cpu.mem_read(sp,20)))
                self.assertEqual(observed,[0x220000,0x230000,16,frame] if movement else [context,39,packet,frame]);seen.append('observer')
                for register in (UC_X86_REG_EAX,UC_X86_REG_ECX,UC_X86_REG_EDX):cpu.reg_write(register,0xDEADBEEF)
                cpu.reg_write(UC_X86_REG_EFLAGS,0x202);cpu.reg_write(UC_X86_REG_ESP,sp+20);cpu.reg_write(UC_X86_REG_EIP,ret)
            elif address==original:
                self.assertEqual(struct.unpack('<III',bytes(cpu.mem_read(sp,12))),(sentinel,16 if movement else 32,4))
                for register,value in regs.items():self.assertEqual(cpu.reg_read(register),value)
                seen.append('original');cpu.reg_write(UC_X86_REG_EAX,0x55FF);cpu.reg_write(UC_X86_REG_ESP,sp+(8 if movement else 4));cpu.reg_write(UC_X86_REG_EIP,sentinel)
            elif address==sentinel:cpu.emu_stop()
        u.hook_add(UC_HOOK_CODE,observe);u.emu_start(start,sentinel+1,count=100)
        self.assertEqual(seen,['observer','original']);self.assertEqual(u.reg_read(UC_X86_REG_EAX),0x55FF)
        self.assertEqual(bytes(u.mem_read(packet,len(payload))),payload)


if __name__=='__main__':unittest.main()
