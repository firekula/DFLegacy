"""Replay original ACT1 reconciliation/integration, with actual installed repair bytes.

The native packet has already been decoded at 4C9919. This isolates arithmetic;
it does not emulate a live map, actor collision, authority, or network delivery.
"""
import hashlib
import os
from pathlib import Path
import struct
import unittest


class MotionHorizonNativeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        executable=os.environ.get('DFLEGACY_ACT1_CLIENT_EXE')
        fixture=os.environ.get('DFLEGACY_MOTION_HORIZON_PATCH')
        if not executable or not fixture:raise unittest.SkipTest('Explicit original ACT1 executable and installed native patch fixture required')
        import pefile
        cls.assert_original=hashlib.sha256(Path(executable).read_bytes()).hexdigest()
        if cls.assert_original!='6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5':
            raise AssertionError('Unsupported original ACT1 executable')
        pe=pefile.PE(str(executable));cls.raw=pe.get_memory_mapped_image();cls.base=pe.OPTIONAL_HEADER.ImageBase
        cls.patch=Path(fixture).read_bytes()
        if len(cls.patch)!=3:raise AssertionError('Expected three actual native installed instruction bytes')

    def run_native(self,fixed,*,accels=(0,0,0),durations=(18836,-1,-1),
                   current=(159.87469482421875,226.,0.),target=(244.00990295410156,226.,0.),
                   terminal=(0.,0.,0.),integrate_ms=0,full=False):
        from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
        from unicorn.x86_const import UC_X86_REG_ESI,UC_X86_REG_EBP,UC_X86_REG_ESP,UC_X86_REG_ECX,UC_X86_REG_EBX
        u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(self.base,(len(self.raw)+4095)//4096*4096);u.mem_write(self.base,self.raw)
        if fixed:u.mem_write(0x4C99EF,self.patch)
        for start,size in [(0x20000000,0x10000),(0x30000000,0x10000),(0x40000000,0x1000)]:u.mem_map(start,size)
        vt=0x20000000;cur=vt+0x1000;dest=vt+0x2000;controller=vt+0x3000;frame=0x30008000;sentinel=0x40000000
        # Original plain-vector getters/setters and ftol code execute unchanged.
        u.mem_write(vt,struct.pack('<IIII',0x4736A0,0x4736B0,0x4736D0,0x4736E0))
        for address,position in [(cur,current),(dest,target)]:
            u.mem_write(address,struct.pack('<IffffffiiiiiiB',vt,*position,*terminal,*accels,*durations,0))
        u.mem_write(controller,struct.pack('<II',cur,dest));u.mem_write(frame,struct.pack('<III',frame+0x1000,sentinel,0));u.mem_write(frame-1,b'\0')
        for register,value in [(UC_X86_REG_ESI,controller),(UC_X86_REG_EBP,frame),(UC_X86_REG_ESP,frame-0x30)]:u.reg_write(register,value)
        u.reg_write(UC_X86_REG_EBX,1 if full else 0)
        u.hook_add(UC_HOOK_CODE,lambda cpu,address,size,data:cpu.emu_stop() if address==sentinel else None)
        u.emu_start(0x4C9919,sentinel+1,count=10000)
        reconciled=bytes(u.mem_read(cur,53))
        remaining=integrate_ms
        while remaining:
            delta=min(16,remaining);remaining-=delta
            for axis in range(3):
                stack=0x30009000;u.mem_write(stack,struct.pack('<III',sentinel,axis,delta))
                u.reg_write(UC_X86_REG_ECX,controller);u.reg_write(UC_X86_REG_ESP,stack)
                u.emu_start(0x4C8C00,sentinel+1,count=10000)
        return reconciled,bytes(u.mem_read(cur,53)),bytes(u.mem_read(dest,53))

    def test_captured_zero_acceleration_stale_duration_must_converge(self):
        old,old_end,_=self.run_native(False,integrate_ms=500)
        repaired,end,_=self.run_native(True,integrate_ms=500)
        self.assertEqual(struct.unpack_from('<f',old,16)[0],0.)
        self.assertEqual(struct.unpack_from('<i',old,40)[0],18836)
        self.assertEqual(old,old_end,'original zero velocity/acceleration is a fixed point')
        self.assertGreater(struct.unpack_from('<f',repaired,16)[0],100.)
        self.assertEqual(struct.unpack_from('<i',repaired,40)[0],500)
        self.assertAlmostEqual(struct.unpack_from('<f',end,4)[0],244.00990295410156,delta=.1)

    def test_other_axes_active_acceleration_and_native_default_remain_identical(self):
        cases=[dict(accels=(a,0,0),durations=(t,-1,-1)) for a in (-1500,-100,100) for t in (-1,0,100,18836)]
        cases.extend(dict(durations=(t,-1,-1)) for t in (-1,0))
        cases.append(dict(current=(244.00990295410156,226.,0.)))
        for case in cases:
            with self.subTest(case=case):self.assertEqual(self.run_native(False,**case),self.run_native(True,**case))
        old=self.run_native(False,accels=(0,100,-1500),durations=(18836,1000,-1),target=(244.,250.,40.))[0]
        repaired=self.run_native(True,accels=(0,100,-1500),durations=(18836,1000,-1),target=(244.,250.,40.))[0]
        for offset in (8,12,20,24,32,36,44,48):self.assertEqual(old[offset:offset+4],repaired[offset:offset+4])

    def test_zero_acceleration_small_positive_duration_and_terminal_velocity(self):
        for duration in (1,100,500,18836):
            with self.subTest(duration=duration):
                state,_,_=self.run_native(True,durations=(duration,-1,-1),terminal=(50.,0.,0.))
                self.assertEqual(struct.unpack_from('<i',state,40)[0],500)
                expected=self.run_native(False,durations=(0,-1,-1),terminal=(50.,0.,0.))[0]
                self.assertEqual(state,expected)

    def test_native_force_publication_still_copies_the_complete_target(self):
        old=self.run_native(False,full=True)
        repaired=self.run_native(True,full=True)
        self.assertEqual(old,repaired)
        self.assertEqual(repaired[0],repaired[2])


if __name__=='__main__':unittest.main()
