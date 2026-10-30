"""Execute actual built native-loop markers; deliberately clobber observer state."""
import os
from pathlib import Path
import re
import struct
import unittest


class NativeLoopProbeAbiTests(unittest.TestCase):
    def test_original_instructions_return_stack_and_fp_simd_survive_every_marker(self):
        path=os.environ.get('DFLEGACY_NATIVE_LOOP_DLL')
        if not path:self.skipTest('Explicit built native-loop IJL required')
        import pefile,capstone
        from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
        from unicorn.x86_const import (UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,
            UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EFLAGS,
            UC_X86_REG_FPCW,UC_X86_REG_FPSW,UC_X86_REG_FPTAG,UC_X86_REG_FP0,UC_X86_REG_MXCSR,
            UC_X86_REG_XMM0,UC_X86_REG_XMM7)
        pe=pefile.PE(str(Path(path)));raw=pe.get_memory_mapped_image();base=pe.OPTIONAL_HEADER.ImageBase
        c=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32);probes={}
        for match in re.finditer(rb'\x9c\x60\x81\xec\x20\x02\x00\x00',raw):
            code=list(c.disasm(raw[match.start():match.start()+100],base+match.start()));call=next(i for i,x in enumerate(code) if x.mnemonic=='call')
            point=int(code[call-1].op_str,0)
            entry=match.start()-2 if point==7 and raw[match.start()-2:match.start()]==b'\x6a\x00' else match.start()
            if point<8:probes[point]=(base+entry,int(code[call].op_str,0))
        self.assertEqual(set(probes),set(range(8)),'all eight actual compiled loop markers must exist')
        stops={0:0x433B05,1:0x433B3C,2:0x433B47,3:0x433B52,4:0x433B5D,5:0x433660,6:0x792A70,7:0x40000000}
        for point,(start,helper) in probes.items():
            with self.subTest(point=point):
                u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(base,(len(raw)+4095)//4096*4096);u.mem_write(base,raw)
                for address,size in [(0x433000,0x2000),(0x792000,0x2000),(0xB5D000,0x2000),(0xBF1000,0x1000),(0x30000000,0x10000),(0x40000000,0x1000)]:u.mem_map(address,size)
                stack=0x30009000;sentinel=0x40000000;u.mem_write(stack,struct.pack('<III',sentinel,0xA1B2C3D4,0x11224488))
                for address in (0xB5DCE0,0xB5DCE4,0xB5DCE8,0xBF1638):u.mem_write(address,struct.pack('<I',address+0x1234))
                regs={UC_X86_REG_EAX:0x1122,UC_X86_REG_EBX:0x3344,UC_X86_REG_ECX:0x5566,UC_X86_REG_EDX:0x7788,
                    UC_X86_REG_ESI:0x99AA,UC_X86_REG_EDI:0xBBCC,UC_X86_REG_EBP:0x30007000,UC_X86_REG_ESP:stack,UC_X86_REG_EFLAGS:0x246,
                    UC_X86_REG_FPCW:0x37F,UC_X86_REG_FPSW:0,UC_X86_REG_FPTAG:0xFFFC,UC_X86_REG_FP0:(0x8000000000000000,0x3FFF),
                    UC_X86_REG_MXCSR:0x1F80,UC_X86_REG_XMM0:0x112233445566778899AABBCCDDEEFF00,UC_X86_REG_XMM7:0x123456789ABCDEF}
                for register,value in regs.items():u.reg_write(register,value)
                seen=[]
                def hook(cpu,address,size,unused):
                    if address==helper:
                        sp=cpu.reg_read(UC_X86_REG_ESP);ret,observed,slot=struct.unpack('<III',bytes(cpu.mem_read(sp,12)))
                        self.assertEqual(observed,point);seen.append(point)
                        if point==0:
                            self.assertEqual(slot,stack);self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(slot,4)))[0],sentinel)
                            cpu.mem_write(slot,struct.pack('<I',probes[7][0]))
                        if point==7:
                            self.assertEqual(slot,stack-4);cpu.mem_write(slot,struct.pack('<I',sentinel))
                        for register in regs:
                            if register in (UC_X86_REG_ESP,UC_X86_REG_FPSW,UC_X86_REG_FPTAG):continue
                            value=(0,0) if register==UC_X86_REG_FP0 else (0x27F if register==UC_X86_REG_FPCW else 0x1F80 if register==UC_X86_REG_MXCSR else 0x202 if register==UC_X86_REG_EFLAGS else 0)
                            cpu.reg_write(register,value)
                        cpu.reg_write(UC_X86_REG_ESP,sp+12);cpu.reg_write(UC_X86_REG_EIP,ret)
                    elif address==stops[point]:cpu.emu_stop()
                u.hook_add(UC_HOOK_CODE,hook);u.emu_start(start,0x40000001,count=200)
                self.assertEqual(seen,[point]);self.assertEqual(u.reg_read(UC_X86_REG_EIP),stops[point])
                expected=dict(regs)
                if point in (1,2,3,4):expected[UC_X86_REG_ECX]=(0xB5DCE0,0xB5DCE4,0xB5DCE8,0xBF1638)[point-1]+0x1234
                if point in (0,5):expected[UC_X86_REG_ESP]=stack-4
                for register,value in expected.items():self.assertEqual(u.reg_read(register),value,hex(register))
                self.assertEqual(bytes(u.mem_read(stack+4,8)),struct.pack('<II',0xA1B2C3D4,0x11224488))
                if point==0:self.assertEqual(struct.unpack('<I',bytes(u.mem_read(stack-4,4)))[0],0xB5D9E8)
                if point==5:self.assertEqual(struct.unpack('<I',bytes(u.mem_read(stack-4,4)))[0],0x433C12)


if __name__=='__main__':unittest.main()
