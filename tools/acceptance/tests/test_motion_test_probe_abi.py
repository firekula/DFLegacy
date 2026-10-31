"""Execute actual built motion-test call and return markers; deliberately clobber observer state."""
import os
from pathlib import Path
import re
import struct
import unittest


class MotionTestProbeAbiTests(unittest.TestCase):
    def test_exact_v94_argument_stack_return_and_fp_simd_survive_observer(self):
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
            entry=match.start()-2 if point==101 and raw[match.start()-2:match.start()]==b'\x6a\x00' else match.start()
            if point in (100,101):probes[point]=(base+entry,int(code[call].op_str,0))
        self.assertEqual(set(probes),{100,101},'actual motion call and return markers must exist')
        stops={100:0x4B3C30,101:0x40000000}
        for point,(start,helper) in probes.items():
            with self.subTest(point=point):
                u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(base,(len(raw)+4095)//4096*4096);u.mem_write(base,raw)
                for address,size in [(0x4B3000,0x2000),(0x792000,0x2000),(0xB5D000,0x2000),(0xBF1000,0x1000),(0x30000000,0x10000),(0x40000000,0x1000)]:u.mem_map(address,size)
                stack=0x30009000;sentinel=0x40000000;u.mem_write(stack,struct.pack('<III',sentinel,0xA1B2C3D4,0x11224488))
                for address in (0xB5DCE0,0xB5DCE4,0xB5DCE8,0xBF1638):u.mem_write(address,struct.pack('<I',address+0x1234))
                regs={UC_X86_REG_EAX:0xB5DCE0,UC_X86_REG_EBX:0x3344,UC_X86_REG_ECX:0x5566,UC_X86_REG_EDX:0x7788,
                    UC_X86_REG_ESI:0x99AA,UC_X86_REG_EDI:0xBBCC,UC_X86_REG_EBP:0x30007000,UC_X86_REG_ESP:stack,UC_X86_REG_EFLAGS:0x246,
                    UC_X86_REG_FPCW:0x37F,UC_X86_REG_FPSW:0,UC_X86_REG_FPTAG:0xFFFC,UC_X86_REG_FP0:(0x8000000000000000,0x3FFF),
                    UC_X86_REG_MXCSR:0x1F80,UC_X86_REG_XMM0:0x112233445566778899AABBCCDDEEFF00,UC_X86_REG_XMM7:0x123456789ABCDEF}
                for register,value in regs.items():u.reg_write(register,value)
                u.mem_write(0xB5DCE0+0x94,struct.pack('<I',0x4B3C30))
                seen=[]
                def hook(cpu,address,size,unused):
                    if address==helper:
                        sp=cpu.reg_read(UC_X86_REG_ESP);ret,observed,slot,saved=struct.unpack('<IIII',bytes(cpu.mem_read(sp,16)))
                        self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(saved+28,4)))[0],regs[UC_X86_REG_EAX])
                        self.assertEqual(observed,point);seen.append(point)
                        if point==100:
                            self.assertEqual(slot,stack);self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(slot,4)))[0],sentinel)
                            cpu.mem_write(slot,struct.pack('<I',probes[101][0]))
                        if point==101:
                            self.assertEqual(slot,stack-4);cpu.mem_write(slot,struct.pack('<I',sentinel))
                        for register in regs:
                            if register in (UC_X86_REG_ESP,UC_X86_REG_FPSW,UC_X86_REG_FPTAG):continue
                            value=(0,0) if register==UC_X86_REG_FP0 else (0x27F if register==UC_X86_REG_FPCW else 0x1F80 if register==UC_X86_REG_MXCSR else 0x202 if register==UC_X86_REG_EFLAGS else 0)
                            cpu.reg_write(register,value)
                        cpu.reg_write(UC_X86_REG_ESP,sp+16);cpu.reg_write(UC_X86_REG_EIP,ret)
                    elif address==stops[point]:cpu.emu_stop()
                u.hook_add(UC_HOOK_CODE,hook);u.emu_start(start,0x40000001,count=200)
                self.assertEqual(seen,[point]);self.assertEqual(u.reg_read(UC_X86_REG_EIP),stops[point])
                expected=dict(regs)
                for register,value in expected.items():self.assertEqual(u.reg_read(register),value,hex(register))
                self.assertEqual(bytes(u.mem_read(stack+4,8)),struct.pack('<II',0xA1B2C3D4,0x11224488))

        # Exercise both real compiled markers as one complete thiscall. The
        # native callee, rather than the observer, pops all three arguments.
        u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(base,(len(raw)+4095)//4096*4096);u.mem_write(base,raw)
        for address,size in [(0x4B3000,0x2000),(0xB5D000,0x2000),(0x30000000,0x10000),(0x40000000,0x1000)]:u.mem_map(address,size)
        stack=0x30009000;sentinel=0x40000000;arguments=(0xffffffff,252,0x30008000)
        u.mem_write(stack,struct.pack('<4I',sentinel,*arguments));u.mem_write(0xB5DCE0+0x94,struct.pack('<I',0x4B3C30))
        u.mem_write(0x4B3C30,b'\xb8\x00\xee\xdd\xcc\xc2\x0c\x00')
        u.reg_write(UC_X86_REG_ESP,stack);u.reg_write(UC_X86_REG_EAX,0xB5DCE0);u.reg_write(UC_X86_REG_ECX,0x123456)
        u.reg_write(UC_X86_REG_EFLAGS,0x246);targets=[];flow=[]
        def complete_call(cpu,address,size,unused):
            if address in {x[1] for x in probes.values()}:
                sp=cpu.reg_read(UC_X86_REG_ESP);ret,point,slot,saved=struct.unpack('<IIII',bytes(cpu.mem_read(sp,16)))
                flow.append(point)
                if point==100:
                    self.assertEqual(slot,stack);targets.append(struct.unpack('<I',bytes(cpu.mem_read(slot,4)))[0])
                    self.assertEqual(struct.unpack('<3I',bytes(cpu.mem_read(slot+4,12))),arguments)
                    cpu.mem_write(slot,struct.pack('<I',probes[101][0]))
                else:
                    self.assertEqual(point,101);self.assertEqual(slot,stack+12)
                    self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(saved+28,4)))[0],0xCCDDEE00)
                    cpu.mem_write(slot,struct.pack('<I',targets.pop()))
                cpu.reg_write(UC_X86_REG_ESP,sp+16);cpu.reg_write(UC_X86_REG_EIP,ret)
            elif address==0x4B3C30:
                sp=cpu.reg_read(UC_X86_REG_ESP)
                self.assertEqual(struct.unpack('<4I',bytes(cpu.mem_read(sp,16))),(probes[101][0],*arguments))
                self.assertEqual(cpu.reg_read(UC_X86_REG_ECX),0x123456)
            elif address==sentinel:cpu.emu_stop()
        u.hook_add(UC_HOOK_CODE,complete_call);u.emu_start(probes[100][0],sentinel+1,count=300)
        self.assertEqual(flow,[100,101]);self.assertEqual(u.reg_read(UC_X86_REG_ESP),stack+16)
        self.assertEqual(u.reg_read(UC_X86_REG_EAX),0xCCDDEE00);self.assertEqual(u.reg_read(UC_X86_REG_EFLAGS),0x246)


if __name__=='__main__':unittest.main()
