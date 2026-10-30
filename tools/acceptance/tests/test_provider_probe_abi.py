import os,unittest,re,struct
from tools.acceptance.motion_replay_fixture import load_original_image
class ProviderProbeAbiTests(unittest.TestCase):
 def test_native_provider_result_marker_exist_in_actual_dll(self):
  path=os.environ.get('DFLEGACY_NATIVE_LOOP_DLL')
  if not path:self.skipTest('Explicit built IJL required')
  executable=os.environ.get('DFLEGACY_DNF_EXE')
  import pefile,capstone
  p=pefile.PE(path);raw=p.get_memory_mapped_image();base=p.OPTIONAL_HEADER.ImageBase;md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32);points={}
  for m in re.finditer(rb'\x9c\x60\x81\xec\x20\x02\x00\x00',raw):
   a=list(md.disasm(raw[m.start():m.start()+100],base+m.start()));i=next(i for i,z in enumerate(a) if z.mnemonic=='call');point=int(a[i-1].op_str,0)
   if point==200:points[point]=(base+m.start(),int(a[i].op_str,0))
  self.assertEqual(set(points),{200})
  from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
  from unicorn.x86_const import (UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,
   UC_X86_REG_EBP,UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EFLAGS,UC_X86_REG_FPCW,UC_X86_REG_FP0,UC_X86_REG_FPSW,UC_X86_REG_FPTAG,UC_X86_REG_MXCSR,UC_X86_REG_XMM0,UC_X86_REG_XMM7)
  original=load_original_image(executable=executable)
  entries={200:0x78EE76}
  for point,(probe,helper) in points.items():
   for value in (0,1,0x80000000,0xffffffff):
    with self.subTest(point=point,native_return=value):
     states=[];target=0x78EE7B
     registers={UC_X86_REG_EAX:value,UC_X86_REG_EBX:0x3344,UC_X86_REG_ECX:0x5566,UC_X86_REG_EDX:0x7788,
      UC_X86_REG_ESI:0x99AA,UC_X86_REG_EDI:0xBBCC,UC_X86_REG_EBP:0x30007000,UC_X86_REG_ESP:0x30009000,UC_X86_REG_EFLAGS:0x246,
      UC_X86_REG_FPCW:0x37F,UC_X86_REG_FPSW:0,UC_X86_REG_FPTAG:0xFFFC,UC_X86_REG_FP0:(0x8000000000000000,0x3FFF),
      UC_X86_REG_MXCSR:0x1F80,UC_X86_REG_XMM0:0x112233445566778899AABBCCDDEEFF00,UC_X86_REG_XMM7:0x123456789ABCDEF}
     for instrumented in (False,True):
      u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(base,(len(raw)+4095)//4096*4096);u.mem_write(base,raw)
      u.mem_map(0x78E000,0x2000);u.mem_map(0x30000000,0x10000)
      u.mem_write(entries[point],original.get_data(entries[point]-0x400000,5))
      u.mem_write(0x30009000,struct.pack('<3I',0xAABBCCDD,0x11224488,0x55667788));u.mem_write(0x3000700C,struct.pack('<2I',0xABCDEF,0xFEDCBA))
      for reg,val in registers.items():u.reg_write(reg,val)
      observed=[]
      def hook(cpu,address,size,unused):
       if instrumented and address==helper:
        sp=cpu.reg_read(UC_X86_REG_ESP);ret,seen,slot,saved=struct.unpack('<4I',bytes(cpu.mem_read(sp,16)))
        self.assertEqual(seen,point);self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(saved+28,4)))[0],value);observed.append(seen)
        for reg in registers:
         if reg in (UC_X86_REG_ESP,UC_X86_REG_FPSW,UC_X86_REG_FPTAG):continue
         cpu.reg_write(reg,(0,0) if reg==UC_X86_REG_FP0 else 0x27F if reg==UC_X86_REG_FPCW else 0x1F80 if reg==UC_X86_REG_MXCSR else 0x202 if reg==UC_X86_REG_EFLAGS else 0)
        cpu.reg_write(UC_X86_REG_ESP,sp+16);cpu.reg_write(UC_X86_REG_EIP,ret)
       elif address==target:cpu.emu_stop()
      u.hook_add(UC_HOOK_CODE,hook);u.emu_start(probe if instrumented else entries[point],0x790000,count=200)
      self.assertEqual(u.reg_read(UC_X86_REG_EIP),target);self.assertEqual(observed,[point] if instrumented else [])
      states.append({reg:u.reg_read(reg) for reg in [*registers,UC_X86_REG_EIP]})
     self.assertEqual(states[0],states[1])
if __name__=='__main__':unittest.main()
